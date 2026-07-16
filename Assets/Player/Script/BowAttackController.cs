using BattlePvp.Combat;
using System.Collections;
using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.UI;
using Mirror;
using UnityEngine;

[DefaultExecutionOrder(100)]
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
    [SerializeField] private Color _chargeRingColor = new Color(1f, 1f, 1f, 0.75f);
    [Min(1f)] [SerializeField] private float _chargeRingMaximumScale = 2.2f;

    private PlayerCombat _playerCombat;
    private PlayerManager _playerManager;
    private Animator _animator;
    private FollowCamera _followCamera;
    private Component _bowRigComponent;
    private PropertyInfo _bowRigWeightProperty;
    private CombatReticleView _reticleView;
    private double _chargeStartedAt = -1d;
    private JobSkillData _activeChargeData;
    private Vector3 _pendingDirection;
    private Vector3 _pendingAimPoint;
    private float _pendingDamageMultiplier;
    private bool _hasPendingShot;
    private bool _hasPendingAimPoint;
    private bool _captureAimPointPending;
    private bool _releaseArrowEventPending;
    private bool _releaseFinishedPending;
    private bool _isVisuallyCharging;
    private bool _isAimHoldReady;
    private bool _releaseQueued;
    private bool _isReleaseLocked;
    private bool _chargeRingVisible;
    private Coroutine _releaseLockFallbackRoutine;

    public bool IsCharging => _chargeStartedAt >= 0d;
    public bool IsBusy => IsCharging || _isVisuallyCharging || _releaseQueued || _isReleaseLocked;

    private double SkillTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;

    private void Awake()
    {
        ResolveReferences();
        SetHandArrowVisible(false);
        SetBowAimRigActive(false);
        if (!NetworkClient.active && !NetworkServer.active)
            SetCrosshairVisible(true);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        SetCrosshairVisible(true);
    }

    private void OnEnable()
    {
        if (ShouldShowLocalCrosshair)
            SetCrosshairVisible(true);
    }

    private void OnDisable()
    {
        CancelCharge();
        SetCrosshairVisible(false);
    }

    private void LateUpdate()
    {
        UpdateChargeReticle();

        if (_captureAimPointPending)
        {
            _captureAimPointPending = false;
            _hasPendingAimPoint = TryResolveCenterScreenAimPoint(out _pendingAimPoint);
        }

        if (_releaseArrowEventPending)
            SpawnPendingArrowFromReleasePose();

        if (_releaseFinishedPending && !_releaseArrowEventPending)
        {
            _releaseFinishedPending = false;
            UnlockReleaseInput();
        }
    }

    public void SetCrosshairVisible(bool visible)
    {
        if (!ShouldShowLocalCrosshair)
            return;

        ResolveReticleView();
        _reticleView?.SetBaseVisible(visible);
        if (!visible)
            SetChargeRingVisible(false);
    }

    private bool ShouldShowLocalCrosshair =>
        _showCrosshair && (isLocalPlayer || (!NetworkClient.active && !NetworkServer.active));

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
            _activeChargeData = bowData;
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
        _activeChargeData = null;
        SetChargeRingVisible(false);
        _playerManager?.ApplySkillMoveMultiplier(1f, 0f);
        QueueShot(bowData, chargeSeconds, aimDirection);
    }

    public void CancelCharge()
    {
        if (IsCharging)
            _playerManager?.ApplySkillMoveMultiplier(1f, 0f);

        _chargeStartedAt = -1d;
        _activeChargeData = null;
        _hasPendingShot = false;
        _hasPendingAimPoint = false;
        _captureAimPointPending = false;
        _releaseArrowEventPending = false;
        _releaseFinishedPending = false;
        _releaseQueued = false;
        _isAimHoldReady = false;
        _isReleaseLocked = false;
        _isVisuallyCharging = false;
        StopReleaseLockFallback();
        SetHandArrowVisible(false);
        SetBowAimRigActive(false);
        SetChargeRingVisible(false);
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
        if (!_hasPendingShot || _releaseArrowEventPending)
            return;

        _releaseArrowEventPending = true;
    }

    public void OnBowReleaseFinished()
    {
        if (_releaseArrowEventPending)
            _releaseFinishedPending = true;
        else
            UnlockReleaseInput();
    }

    private void QueueShot(JobSkillData bowData, float chargeSeconds, Vector3 direction)
    {
        // LateUpdate captures the release-frame camera ray after FollowCamera applies mouse input.
        _pendingDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        _hasPendingAimPoint = false;
        _captureAimPointPending = isLocalPlayer;
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

    private void SpawnPendingArrowFromReleasePose()
    {
        _releaseArrowEventPending = false;
        if (!_hasPendingShot)
            return;

        ResolveReferences();
        Transform spawnPoint = _arrowSpawnPoint != null ? _arrowSpawnPoint : transform;
        Vector3 spawnPosition = spawnPoint.position;
        Vector3 direction = _hasPendingAimPoint
            ? _pendingAimPoint - spawnPosition
            : _pendingDirection;
        if (direction.sqrMagnitude <= 0.001f)
            direction = transform.forward;

        float damageMultiplier = _pendingDamageMultiplier;
        _hasPendingShot = false;
        _hasPendingAimPoint = false;

        if (isClient && isLocalPlayer && !isServer)
            CmdSpawnBowArrow(spawnPosition, direction.normalized, damageMultiplier);
        else if (NetworkServer.active)
            SpawnBowArrow(spawnPosition, direction.normalized, damageMultiplier);
    }

    [Command]
    private void CmdSpawnBowArrow(Vector3 spawnPosition, Vector3 direction, float damageMultiplier)
    {
        SpawnBowArrow(spawnPosition, direction, damageMultiplier);
    }

    private void SpawnBowArrow(Vector3 requestedSpawnPosition, Vector3 direction, float damageMultiplier)
    {
        BowArrowProjectile projectilePrefab = ProjectilePrefab;
        if (!NetworkServer.active || projectilePrefab == null || damageMultiplier <= 0f)
            return;

        Transform spawnPoint = _arrowSpawnPoint != null ? _arrowSpawnPoint : transform;
        Vector3 spawnPosition = IsValidRequestedSpawnPosition(requestedSpawnPosition)
            ? requestedSpawnPosition
            : spawnPoint.position;
        Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        Quaternion rotation = Quaternion.LookRotation(normalizedDirection, Vector3.up);
        BowArrowProjectile arrow = Instantiate(projectilePrefab, spawnPosition, rotation);
        arrow.Initialize(netId, normalizedDirection, ProjectileSpeed, ProjectileLifeSeconds, damageMultiplier);
        NetworkServer.Spawn(arrow.gameObject);
    }

    private bool IsValidRequestedSpawnPosition(Vector3 position)
    {
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            return false;

        const float maxDistanceFromOwner = 4f;
        return (position - transform.position).sqrMagnitude <= maxDistanceFromOwner * maxDistanceFromOwner;
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
            _arrowSpawnPoint = FindChildTransform("Arrow_Point") ?? FindChildTransform("ArrowSpawnPoint");
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

    private bool TryResolveCenterScreenAimPoint(out Vector3 aimPoint)
    {
        aimPoint = Vector3.zero;
        if (isLocalPlayer)
        {
            if (_followCamera == null)
                _followCamera = FindFirstObjectByType<FollowCamera>();

            if (_followCamera != null)
            {
                Ray aimRay = _followCamera.GetAimRay();
                aimPoint = ResolveAimPoint(aimRay);
                return true;
            }
        }

        return false;
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

    private void ResolveReticleView()
    {
        if (_reticleView == null)
            _reticleView = GetComponent<CombatReticleView>();
        if (_reticleView != null && ShouldShowLocalCrosshair)
            _reticleView.InitializeForLocalPlayer(transform);
    }

    private void UpdateChargeReticle()
    {
        if (!IsCharging || _activeChargeData == null || !isLocalPlayer)
        {
            SetChargeRingVisible(false);
            return;
        }

        ResolveReticleView();
        if (_reticleView == null)
            return;

        float elapsed = Mathf.Max(0f, (float)(SkillTime - _chargeStartedAt));
        float denominator = Mathf.Max(0.001f,
            _activeChargeData.MaximumBowDamageChargeSeconds - _activeChargeData.MinimumBowChargeSeconds);
        float damageProgress = elapsed < _activeChargeData.MinimumBowChargeSeconds
            ? 0f
            : Mathf.Clamp01((elapsed - _activeChargeData.MinimumBowChargeSeconds) / denominator);
        _reticleView.SetCharge(damageProgress, _chargeRingMaximumScale, _chargeRingColor);
        _chargeRingVisible = true;
    }

    private void SetChargeRingVisible(bool visible)
    {
        if (_chargeRingVisible == visible)
            return;

        _chargeRingVisible = visible;
        ResolveReticleView();
        _reticleView?.SetChargeVisible(visible);
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

        if (_hasPendingShot)
        {
            _releaseArrowEventPending = true;
            _releaseFinishedPending = true;
            yield break;
        }

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
