using BattlePvp.Combat;
using System.Collections;
using Mirror;
using UnityEngine;

public sealed class BowAttackController : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private BowAttackSettings _settings;

    [Header("Scene References")]
    [SerializeField] private Transform _arrowSpawnPoint;
    [SerializeField] private GameObject _handArrowVisual;

    [Header("Overrides")]
    [SerializeField] private BowArrowProjectile _projectilePrefabOverride;
    [SerializeField] private string _drawAnimationStateNameOverride;
    [SerializeField] private string _aimHoldAnimationStateNameOverride;
    [SerializeField] private string _releaseTriggerNameOverride;
    [SerializeField] private int _animationLayerOverride = -1;
    [SerializeField] private float _projectileSpeedOverride = -1f;
    [SerializeField] private float _projectileLifeSecondsOverride = -1f;
    [SerializeField] private float _releaseInputLockFallbackSecondsOverride = -1f;

    private PlayerCombat _playerCombat;
    private PlayerManager _playerManager;
    private Animator _animator;
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
    }

    private void OnDisable()
    {
        CancelCharge();
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
            PlayBowAnimationNetworked(DrawAnimationStateName);
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
    }

    public void OnBowDrawReady()
    {
        if (!_isVisuallyCharging)
            return;

        PlayBowAnimationLocal(AimHoldAnimationStateName);
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
        _pendingDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
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
    private void CmdPlayBowAnimation(string stateName)
    {
        RpcPlayBowAnimation(stateName);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlayBowAnimation(string stateName)
    {
        PlayBowAnimationLocal(stateName);
    }

    private void PlayBowAnimationNetworked(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
            return;

        PlayBowAnimationLocal(stateName);

        if (isClient && isLocalPlayer && !isServer)
            CmdPlayBowAnimation(stateName);
        else if (NetworkServer.active)
            RpcPlayBowAnimation(stateName);
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

    private void PlayBowAnimationLocal(string stateName)
    {
        if (_animator == null || string.IsNullOrWhiteSpace(stateName))
            return;

        if (stateName == DrawAnimationStateName)
        {
            _isVisuallyCharging = true;
            SetHandArrowVisible(false);
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
        StopReleaseLockFallback();
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
