using BattlePvp.Combat;
using System.Collections;
using System.Reflection;
using BattlePvp.CameraLogic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

public sealed class BowAttackController : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private BowAttackSettings _settings;

    [Header("Scene References")]
    [SerializeField] private Transform _arrowSpawnPoint;
    [SerializeField] private GameObject _handArrowVisual;
    [SerializeField] private GameObject _bowAimRigObject;
    [SerializeField] private BowAimRigTarget _bowAimRigTarget;

    [Header("Overrides")]
    [SerializeField] private BowArrowProjectile _projectilePrefabOverride;
    [SerializeField] private string _drawAnimationStateNameOverride;
    [SerializeField] private string _aimHoldAnimationStateNameOverride;
    [SerializeField] private string _resetAnimationStateNameOverride;
    [SerializeField] private string _releaseTriggerNameOverride;
    [SerializeField] private int _animationLayerOverride = -1;
    [SerializeField] private float _projectileSpeedOverride = -1f;
    [SerializeField] private float _projectileLifeSecondsOverride = -1f;
    [SerializeField] private float _releaseInputLockFallbackSecondsOverride = -1f;

    [Header("Aim")]
    [SerializeField] private float _aimDistance = 1000f;
    [SerializeField] private LayerMask _aimHitMask = ~0;
    [SerializeField] private bool _applyBowCameraOffset = true;
    [SerializeField] private Vector3 _bowCameraOffset = new Vector3(0.35f, 0.3f, -0.7f);
    [SerializeField] private Vector3 _bowCameraRotationOffset;
    [SerializeField] private bool _showCrosshair = true;
    [SerializeField] private Sprite _crosshairSprite;
    [SerializeField] private Color _crosshairColor = Color.white;
    [SerializeField] private Vector2 _crosshairSize = new Vector2(26f, 26f);
    [SerializeField] private float _crosshairThickness = 3f;
    [SerializeField] private float _crosshairGap = 5f;

    private PlayerCombat _playerCombat;
    private PlayerManager _playerManager;
    private Animator _animator;
    private FollowCamera _followCamera;
    private Component _bowRigComponent;
    private PropertyInfo _bowRigWeightProperty;
    private GameObject _crosshairRoot;
    private double _chargeStartedAt = -1d;
    private Vector3 _pendingDirection;
    private float _pendingDamageMultiplier;
    private bool _hasPendingShot;
    private bool _isVisuallyCharging;
    private bool _isAimHoldReady;
    private bool _releaseQueued;
    private bool _isReleaseLocked;
    private Coroutine _releaseLockFallbackRoutine;

    public bool IsCharging => _chargeStartedAt >= 0d;
    public bool IsBusy => IsCharging || _isVisuallyCharging || _releaseQueued || _isReleaseLocked;

    private double SkillTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;

    private void Awake()
    {
        ResolveReferences();
        SetHandArrowVisible(false);
        SetBowAimRigActive(false);
    }

    private void OnDisable()
    {
        CancelCharge();
        SetCrosshairVisible(false);
    }

    private void OnDestroy()
    {
        if (_crosshairRoot != null)
            Destroy(_crosshairRoot.transform.root.gameObject);
    }

    public void SetCrosshairVisible(bool visible)
    {
        if (!isLocalPlayer)
            visible = false;

        if (!_showCrosshair)
            visible = false;

        if (visible)
            EnsureCrosshair();

        if (_crosshairRoot != null && _crosshairRoot.activeSelf != visible)
            _crosshairRoot.SetActive(visible);
    }

    public void HandleAttackInput(bool pressed, JobSkillData bowData, Vector3 aimDirection)
    {
        if (bowData == null)
            return;

        ResolveReferences();

        if (pressed)
        {
            if (IsBusy)
                return;

            _chargeStartedAt = SkillTime;
            _hasPendingShot = false;
            _releaseQueued = false;
            _isAimHoldReady = false;
            _isVisuallyCharging = true;
            _playerManager?.ApplySkillMoveMultiplier(bowData.BowChargeMoveMultiplier, 86400f);
            SetHandArrowVisible(false);
            ApplyBowAimDirection(aimDirection);
            SetBowAimRigActive(true);
            PlayBowAnimationNetworked(DrawAnimationStateName, aimDirection);
            return;
        }

        if (!IsCharging)
            return;

        float chargeSeconds = Mathf.Max(0f, (float)(SkillTime - _chargeStartedAt));
        _chargeStartedAt = -1d;
        _playerManager?.ApplySkillMoveMultiplier(1f, 0f);
        QueueShot(bowData, chargeSeconds, aimDirection);
    }

    public void CancelCharge()
    {
        if (IsCharging)
            _playerManager?.ApplySkillMoveMultiplier(1f, 0f);

        _chargeStartedAt = -1d;
        _hasPendingShot = false;
        _releaseQueued = false;
        _isAimHoldReady = false;
        _isReleaseLocked = false;
        _isVisuallyCharging = false;
        StopReleaseLockFallback();
        SetHandArrowVisible(false);
        SetBowAimRigActive(false);
        ClearBowAnimationLayer();
    }

    public void OnBowDrawReady()
    {
        if (!_isVisuallyCharging)
            return;

        PlayBowAnimationLocal(AimHoldAnimationStateName, Vector3.zero);
        _isAimHoldReady = true;

        if (_releaseQueued)
        {
            _releaseQueued = false;
            TriggerBowReleaseNetworked();
        }
    }

    public void OnBowNockArrow()
    {
        if (!_isVisuallyCharging && !IsCharging)
            return;

        SetHandArrowVisible(true);
    }

    public void OnBowReleaseArrow()
    {
        SetHandArrowVisible(false);
        if (!_hasPendingShot)
            return;

        Vector3 direction = _pendingDirection.sqrMagnitude > 0.001f ? _pendingDirection.normalized : transform.forward;
        float damageMultiplier = _pendingDamageMultiplier;
        _hasPendingShot = false;

        if (isClient && isLocalPlayer && !isServer)
            CmdSpawnBowArrow(direction, damageMultiplier);
        else if (NetworkServer.active)
            SpawnBowArrow(direction, damageMultiplier);
    }

    public void OnBowReleaseFinished()
    {
        UnlockReleaseInput();
    }

    private void QueueShot(JobSkillData bowData, float chargeSeconds, Vector3 direction)
    {
        // Capture the local camera ray when the player releases the input. Animation events can
        // reach a remote client several frames later, where recalculating this ray is unreliable.
        _pendingDirection = ResolveCenterScreenDirection(direction);
        float denominator = Mathf.Max(0.001f, bowData.MaximumBowDamageChargeSeconds - bowData.MinimumBowChargeSeconds);
        float t = chargeSeconds < bowData.MinimumBowChargeSeconds
            ? 0f
            : Mathf.Clamp01((chargeSeconds - bowData.MinimumBowChargeSeconds) / denominator);
        float multiplier = Mathf.Lerp(bowData.MinimumBowDamageMultiplier, bowData.MaximumBowDamageMultiplier, t);

        _pendingDamageMultiplier = multiplier * (_playerCombat != null ? _playerCombat.ConsumeNextAttackDamageMultiplier() : 1f);
        _hasPendingShot = true;
        if (!_isAimHoldReady)
        {
            _releaseQueued = true;
            return;
        }

        TriggerBowReleaseNetworked();
    }

    [Command]
    private void CmdPlayBowAnimation(string stateName, Vector3 aimDirection)
    {
        RpcPlayBowAnimation(stateName, aimDirection);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlayBowAnimation(string stateName, Vector3 aimDirection)
    {
        PlayBowAnimationLocal(stateName, aimDirection);
    }

    private void PlayBowAnimationNetworked(string stateName, Vector3 aimDirection)
    {
        if (string.IsNullOrWhiteSpace(stateName))
            return;

        PlayBowAnimationLocal(stateName, aimDirection);

        if (isClient && isLocalPlayer && !isServer)
            CmdPlayBowAnimation(stateName, aimDirection);
        else if (NetworkServer.active)
            RpcPlayBowAnimation(stateName, aimDirection);
    }

    [Command]
    private void CmdTriggerBowRelease()
    {
        RpcTriggerBowRelease();
    }

    [ClientRpc(includeOwner = false)]
    private void RpcTriggerBowRelease()
    {
        TriggerBowReleaseLocal();
    }

    private void TriggerBowReleaseNetworked()
    {
        TriggerBowReleaseLocal();

        if (isClient && isLocalPlayer && !isServer)
            CmdTriggerBowRelease();
        else if (NetworkServer.active)
            RpcTriggerBowRelease();
    }

    private void TriggerBowReleaseLocal()
    {
        if (_animator == null || string.IsNullOrWhiteSpace(ReleaseTriggerName))
            return;

        _isVisuallyCharging = false;
        _releaseQueued = false;
        _isReleaseLocked = true;
        RestartReleaseLockFallback();
        _animator.speed = 1f;
        _animator.ResetTrigger(ReleaseTriggerName);
        _animator.SetTrigger(ReleaseTriggerName);
    }

    private void PlayBowAnimationLocal(string stateName, Vector3 aimDirection)
    {
        if (_animator == null || string.IsNullOrWhiteSpace(stateName))
            return;

        if (stateName == DrawAnimationStateName)
        {
            _isVisuallyCharging = true;
            SetHandArrowVisible(false);
            ApplyBowAimDirection(aimDirection);
            SetBowAimRigActive(true);
        }
        int safeLayer = Mathf.Clamp(AnimationLayer, 0, _animator.layerCount - 1);
        int stateHash = Animator.StringToHash(stateName);
        if (!_animator.HasState(safeLayer, stateHash))
        {
            Debug.LogWarning($"[BowAttackController] Animator state '{stateName}' was not found on layer {safeLayer}.", this);
            return;
        }

        _animator.speed = 1f;
        _animator.Play(stateName, safeLayer, 0f);
    }

    [Command]
    private void CmdSpawnBowArrow(Vector3 direction, float damageMultiplier)
    {
        SpawnBowArrow(direction, damageMultiplier);
    }

    private void SpawnBowArrow(Vector3 direction, float damageMultiplier)
    {
        BowArrowProjectile projectilePrefab = ProjectilePrefab;
        if (!NetworkServer.active || projectilePrefab == null || damageMultiplier <= 0f)
            return;

        Transform spawnPoint = _arrowSpawnPoint != null ? _arrowSpawnPoint : transform;
        Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        Quaternion rotation = Quaternion.LookRotation(normalizedDirection, Vector3.up);
        BowArrowProjectile arrow = Instantiate(projectilePrefab, spawnPoint.position, rotation);
        arrow.Initialize(netId, normalizedDirection, ProjectileSpeed, ProjectileLifeSeconds, damageMultiplier);
        NetworkServer.Spawn(arrow.gameObject);
    }

    private void ResolveReferences()
    {
        if (_playerCombat == null)
            _playerCombat = GetComponent<PlayerCombat>();
        if (_playerManager == null)
            _playerManager = GetComponent<PlayerManager>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();
        if (_arrowSpawnPoint == null)
            _arrowSpawnPoint = FindChildTransform("ArrowSpawnPoint");
        if (_handArrowVisual == null)
            _handArrowVisual = FindChildGameObject("Arrow_hand");
        if (_bowAimRigObject == null)
            _bowAimRigObject = FindChildGameObject("BowRig");
        if (_bowAimRigObject != null && !_bowAimRigObject.activeSelf)
            _bowAimRigObject.SetActive(true);
        if (_bowRigComponent == null && _bowAimRigObject != null)
            ResolveBowRigComponent();
        if (_bowAimRigTarget == null)
            _bowAimRigTarget = GetComponent<BowAimRigTarget>();
    }

    private Vector3 ResolveCenterScreenDirection(Vector3 fallback)
    {
        if (isLocalPlayer)
        {
            if (_followCamera == null)
                _followCamera = FindFirstObjectByType<FollowCamera>();

            if (_followCamera != null)
            {
                Transform spawnPoint = _arrowSpawnPoint != null ? _arrowSpawnPoint : transform;
                Ray aimRay = _followCamera.GetAimRay();
                Vector3 aimPoint = ResolveAimPoint(aimRay);
                Vector3 direction = aimPoint - spawnPoint.position;
                if (direction.sqrMagnitude > 0.001f)
                    return direction.normalized;
            }
        }

        return fallback.sqrMagnitude > 0.001f ? fallback.normalized : transform.forward;
    }

    private Vector3 ResolveAimPoint(Ray aimRay)
    {
        RaycastHit[] hits = Physics.RaycastAll(aimRay, Mathf.Max(1f, _aimDistance), _aimHitMask, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 aimPoint = aimRay.origin + aimRay.direction * Mathf.Max(1f, _aimDistance);

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            aimPoint = hit.point;
        }

        return aimPoint;
    }

    private void SetBowCameraOffsetActive(bool active)
    {
        if (!_applyBowCameraOffset)
            active = false;

        if (_followCamera == null && isLocalPlayer)
            _followCamera = FindFirstObjectByType<FollowCamera>();

        if (_followCamera != null)
            _followCamera.SetTemporaryOffset(active, _bowCameraOffset, _bowCameraRotationOffset);
    }

    private void ResolveBowRigComponent()
    {
        Component[] components = _bowAimRigObject.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null || component.GetType().Name != "Rig")
                continue;

            PropertyInfo weightProperty = component.GetType().GetProperty("weight", BindingFlags.Instance | BindingFlags.Public);
            if (weightProperty == null || !weightProperty.CanWrite)
                continue;

            _bowRigComponent = component;
            _bowRigWeightProperty = weightProperty;
            return;
        }
    }

    private GameObject FindChildGameObject(string childName)
    {
        Transform child = FindChildTransform(childName);
        return child != null ? child.gameObject : null;
    }

    private Transform FindChildTransform(string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
            return null;

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child != transform && child.name == childName)
                return child;
        }

        return null;
    }

    private void SetHandArrowVisible(bool visible)
    {
        if (_handArrowVisual != null && _handArrowVisual.activeSelf != visible)
            _handArrowVisual.SetActive(visible);
    }

    private void SetBowAimRigActive(bool active)
    {
        SetBowCameraOffsetActive(active);
        if (_bowAimRigTarget != null)
            _bowAimRigTarget.SetYawOffsetActive(active);
        SetBowRigWeight(active ? 1f : 0f);
        if (_bowAimRigTarget != null && _bowAimRigTarget.enabled != active)
            _bowAimRigTarget.enabled = active;
    }

    private void ApplyBowAimDirection(Vector3 aimDirection)
    {
        if (_bowAimRigTarget == null)
            return;

        if (isLocalPlayer)
        {
            _bowAimRigTarget.ClearNetworkAimDirection();
            return;
        }

        _bowAimRigTarget.SetNetworkAimDirection(aimDirection);
    }

    private void SetBowRigWeight(float weight)
    {
        if (_bowRigComponent == null || _bowRigWeightProperty == null)
            ResolveBowRigComponent();

        if (_bowRigComponent == null || _bowRigWeightProperty == null)
            return;

        _bowRigWeightProperty.SetValue(_bowRigComponent, Mathf.Clamp01(weight));
    }

    private void EnsureCrosshair()
    {
        if (_crosshairRoot != null)
            return;

        GameObject canvasObject = new GameObject("BowCrosshairCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvasObject.AddComponent<CanvasScaler>();

        _crosshairRoot = new GameObject("BowCrosshair");
        RectTransform rootRect = _crosshairRoot.AddComponent<RectTransform>();
        _crosshairRoot.transform.SetParent(canvas.transform, false);
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = _crosshairSize;

        if (_crosshairSprite != null)
        {
            Image icon = _crosshairRoot.AddComponent<Image>();
            icon.sprite = _crosshairSprite;
            icon.color = _crosshairColor;
            icon.raycastTarget = false;
            return;
        }

        AddCrosshairLine("Left", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-_crosshairGap, 0f), new Vector2((_crosshairSize.x * 0.5f) - _crosshairGap, _crosshairThickness));
        AddCrosshairLine("Right", new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(_crosshairGap, 0f), new Vector2((_crosshairSize.x * 0.5f) - _crosshairGap, _crosshairThickness));
        AddCrosshairLine("Top", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, _crosshairGap), new Vector2(_crosshairThickness, (_crosshairSize.y * 0.5f) - _crosshairGap));
        AddCrosshairLine("Bottom", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, -_crosshairGap), new Vector2(_crosshairThickness, (_crosshairSize.y * 0.5f) - _crosshairGap));
    }

    private void AddCrosshairLine(string lineName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject line = new GameObject(lineName);
        RectTransform rect = line.AddComponent<RectTransform>();
        line.transform.SetParent(_crosshairRoot.transform, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(Mathf.Max(1f, sizeDelta.x), Mathf.Max(1f, sizeDelta.y));

        Image image = line.AddComponent<Image>();
        image.color = _crosshairColor;
        image.raycastTarget = false;
    }

    private void RestartReleaseLockFallback()
    {
        StopReleaseLockFallback();
        float seconds = ReleaseInputLockFallbackSeconds;
        if (seconds > 0f)
            _releaseLockFallbackRoutine = StartCoroutine(CoReleaseLockFallback(seconds));
    }

    private void StopReleaseLockFallback()
    {
        if (_releaseLockFallbackRoutine == null)
            return;

        StopCoroutine(_releaseLockFallbackRoutine);
        _releaseLockFallbackRoutine = null;
    }

    private IEnumerator CoReleaseLockFallback(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _releaseLockFallbackRoutine = null;
        UnlockReleaseInput();
    }

    private void UnlockReleaseInput()
    {
        _isReleaseLocked = false;
        SetBowAimRigActive(false);
        ClearBowAnimationLayer();
        StopReleaseLockFallback();
    }

    private void ClearBowAnimationLayer()
    {
        if (_animator == null || string.IsNullOrWhiteSpace(ResetAnimationStateName))
            return;

        int safeLayer = Mathf.Clamp(AnimationLayer, 0, _animator.layerCount - 1);
        int stateHash = Animator.StringToHash(ResetAnimationStateName);
        if (!_animator.HasState(safeLayer, stateHash))
        {
            Debug.LogWarning($"[BowAttackController] Animator reset state '{ResetAnimationStateName}' was not found on layer {safeLayer}.", this);
            return;
        }

        if (!string.IsNullOrWhiteSpace(ReleaseTriggerName))
            _animator.ResetTrigger(ReleaseTriggerName);

        _animator.Play(stateHash, safeLayer, 0f);
    }

    private BowArrowProjectile ProjectilePrefab
    {
        get
        {
            if (_projectilePrefabOverride != null)
                return _projectilePrefabOverride;

            return _settings != null ? _settings.ProjectilePrefab : null;
        }
    }

    private string DrawAnimationStateName => !string.IsNullOrWhiteSpace(_drawAnimationStateNameOverride)
        ? _drawAnimationStateNameOverride
        : _settings != null ? _settings.DrawAnimationStateName : "Bow_Draw";

    private string AimHoldAnimationStateName => !string.IsNullOrWhiteSpace(_aimHoldAnimationStateNameOverride)
        ? _aimHoldAnimationStateNameOverride
        : _settings != null ? _settings.AimHoldAnimationStateName : "Bow_AimHold";

    private string ResetAnimationStateName => !string.IsNullOrWhiteSpace(_resetAnimationStateNameOverride)
        ? _resetAnimationStateNameOverride
        : _settings != null ? _settings.ResetAnimationStateName : "New State";

    private string ReleaseTriggerName => !string.IsNullOrWhiteSpace(_releaseTriggerNameOverride)
        ? _releaseTriggerNameOverride
        : _settings != null ? _settings.ReleaseTriggerName : "BowRelease";

    private int AnimationLayer => _animationLayerOverride >= 0
        ? _animationLayerOverride
        : _settings != null ? _settings.AnimationLayer : 1;

    private float ProjectileSpeed => _projectileSpeedOverride > 0f
        ? _projectileSpeedOverride
        : _settings != null ? _settings.ProjectileSpeed : 28f;

    private float ProjectileLifeSeconds => _projectileLifeSecondsOverride > 0f
        ? _projectileLifeSecondsOverride
        : _settings != null ? _settings.ProjectileLifeSeconds : 4f;

    private float ReleaseInputLockFallbackSeconds => _releaseInputLockFallbackSecondsOverride > 0f
        ? _releaseInputLockFallbackSecondsOverride
        : _settings != null ? _settings.ReleaseInputLockFallbackSeconds : 1f;
}
