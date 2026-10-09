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
    [SerializeField] private bool _applyBowCameraOffset = false;
    [SerializeField] private Vector3 _bowCameraOffset = new Vector3(0.35f, 0.3f, -0.7f);
    [SerializeField] private Vector3 _bowCameraRotationOffset;
    [SerializeField] private bool _showCrosshair = true;
    [SerializeField] private Color _chargeRingColor = new Color(1f, 1f, 1f, 0.75f);
    [Range(1f, 5f)] [SerializeField] private float _chargeRingMaximumScale = 2.2f;
    [Range(0.5f, 8f)] [SerializeField] private float _chargeRingThickness = 2f;

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
    private bool _hasPendingShot;
    private float _offlineShotMultiplier;
    private bool _hasPendingAimPoint;
    private bool _captureAimPointPending;
    private bool _releaseArrowEventPending;
    private bool _releaseFinishedPending;
    private bool _isVisuallyCharging;
    private bool _isAimHoldReady;
    private bool _isReleaseLocked;
    private bool _chargeRingVisible;
    private Coroutine _releaseLockFallbackRoutine;
    private readonly BowShotAuthority _serverShotAuthority = new BowShotAuthority();
    // A draw keeps one cadence through release, even if a lobby selection changes meanwhile.
    private float _chargeSpeedMultiplier = 1f;
    private float _serverChargeSpeedMultiplier = 1f;
    private float _serverReleaseLockSeconds;
    private bool _serverOwnsTauntVisual;
    private float _drawDuration = 3f;
    private float _drawReadyAt = float.PositiveInfinity;
    private Transform _bowHand, _stringHand;
    private float _handArrowTailLocal = .29609093f;

    public bool IsCharging => _chargeStartedAt >= 0d;
    public bool IsBusy => IsCharging || _isVisuallyCharging || _isReleaseLocked;
    public bool ControlsAimPose => IsBusy || (_bowAimRigTarget != null && _bowAimRigTarget.IsPosing);
    private bool UsesLocalAim => (isLocalPlayer || (!NetworkClient.active && !NetworkServer.active)) &&
        !_serverOwnsTauntVisual && (_playerCombat == null || !_playerCombat.IsServerTaunted);

    [Server]
    public void ServerTickTauntAttack(Vector3 direction)
    {
        if (_playerCombat == null || !_playerCombat.IsServerTaunted) return;
        ServerTickAutomatedAttack(direction);
    }

    [Server]
    internal void ServerTickAutomatedAttack(Vector3 direction)
    {
        if (_playerCombat == null || !_playerCombat.CanServerUseBow) { CancelCharge(); return; }
        JobSkillData data = _playerCombat.ServerBowData;
        if (data == null) return;
        if (!IsBusy) HandleAttackInput(true, data, direction);
        else if (IsCharging && (SkillTime - _chargeStartedAt) * _chargeSpeedMultiplier >= data.MinimumBowChargeSeconds)
            HandleAttackInput(false, data, direction);
    }

    [Server]
    public void ServerEndTauntAttack()
    {
        CancelCharge();
        RpcEndTauntBowVisual();
    }

    [ClientRpc]
    private void RpcEndTauntBowVisual()
    {
        if (isServer) return;
        _serverOwnsTauntVisual = true;
        CancelCharge();
        _serverOwnsTauntVisual = false;
    }

    private double SkillTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;

    private void Awake()
    {
        ResolveReferences();
        ApplyBowPlaybackSpeed();
        _serverReleaseLockSeconds = ResolveServerReleaseLockSeconds();
        if (_animator != null && _animator.runtimeAnimatorController != null)
            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
                if (clip.name == DrawAnimationStateName) { _drawDuration = Mathf.Clamp(clip.length / BaseAnimationPlaybackSpeed, .1f, 5f); break; }
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

        // Clips may lose events at a transition boundary or when animation is culled.
        if (_isVisuallyCharging && !_isAimHoldReady && Time.time >= _drawReadyAt)
            OnBowDrawReady();

        UpdateNockedArrowPose();

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
            _isAimHoldReady = false;
            _isVisuallyCharging = true;
            _playerManager?.SetMovementEffect(CombatEffectSources.BowCharge, bowData.BowChargeMoveMultiplier, 86400f);
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
        _playerManager?.RemoveMovementEffect(CombatEffectSources.BowCharge);
        if (isClient && isLocalPlayer && !isServer)
            CmdPrepareBowShot();
        else if (NetworkServer.active && !PrepareServerShot())
        {
            CancelCharge();
            return;
        }
        QueueShot(bowData, chargeSeconds, aimDirection);
    }

    public void CancelCharge()
    {
        if (!_serverOwnsTauntVisual && isClient && isLocalPlayer && !isServer && NetworkClient.ready && (IsBusy || _hasPendingShot))
            CmdCancelBowCharge();
        _serverShotAuthority.Cancel();
        _playerManager?.RemoveMovementEffect(CombatEffectSources.BowCharge);

        _chargeStartedAt = -1d;
        _activeChargeData = null;
        _hasPendingShot = false;
        _hasPendingAimPoint = false;
        _captureAimPointPending = false;
        _releaseArrowEventPending = false;
        _releaseFinishedPending = false;
        _isAimHoldReady = false;
        _isReleaseLocked = false;
        _isVisuallyCharging = false;
        _drawReadyAt = float.PositiveInfinity;
        StopReleaseLockFallback();
        SetHandArrowVisible(false);
        SetBowAimRigActive(false);
        SetChargeRingVisible(false);
        ClearBowAnimationLayer();
    }

    public void OnBowDrawReady()
    {
        if (!_isVisuallyCharging || _isAimHoldReady)
            return;

        PlayBowAnimationLocal(AimHoldAnimationStateName, Vector3.zero);
        _isAimHoldReady = true;

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
        if (!_isReleaseLocked || !_hasPendingShot || _releaseArrowEventPending)
            return;

        _releaseArrowEventPending = true;
        _captureAimPointPending = UsesLocalAim;
    }

    public void OnBowReleaseFinished()
    {
        if (!_isReleaseLocked) return;
        if (_hasPendingShot) OnBowReleaseArrow();
        if (_releaseArrowEventPending)
            _releaseFinishedPending = true;
        else
            UnlockReleaseInput();
    }

    private void QueueShot(JobSkillData bowData, float chargeSeconds, Vector3 direction)
    {
        // Release intent freezes damage now. LateUpdate spawns after this frame's camera/rig pose.
        _pendingDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        _hasPendingAimPoint = false;
        _captureAimPointPending = false;
        float progress = Mathf.Clamp01((chargeSeconds * _chargeSpeedMultiplier - bowData.MinimumBowChargeSeconds) /
            Mathf.Max(.001f, bowData.MaximumBowDamageChargeSeconds - bowData.MinimumBowChargeSeconds));
        _offlineShotMultiplier = Mathf.Lerp(bowData.MinimumBowDamageMultiplier, bowData.MaximumBowDamageMultiplier, progress);
        _hasPendingShot = true;
        TriggerBowReleaseNetworked();
    }

    [Command]
    private void CmdPlayBowAnimation(string stateName, Vector3 aimDirection)
    {
        if (_playerCombat != null && _playerCombat.IsServerTaunted) return;
        if (stateName != DrawAnimationStateName || !TryBeginServerCharge(aimDirection))
        {
            TargetRejectBowAction(connectionToClient);
            return;
        }
        RpcPlayBowAnimation(stateName, aimDirection);
    }

    private bool TryBeginServerCharge(Vector3 aimDirection)
    {
        bool accepted = _playerCombat != null && _playerCombat.CanServerUseBow &&
            CombatValidation.IsFinite(aimDirection) && aimDirection.sqrMagnitude > 0.001f &&
            _serverShotAuthority.TryBegin(NetworkTime.time);
        if (accepted)
            _serverChargeSpeedMultiplier = ResolveCharacterAttackSpeed();
        if (accepted)
            GetComponent<ExpandedSkillController>()?.NotifyAttackStarted();
        if (accepted)
            _playerManager?.SetMovementEffect(CombatEffectSources.BowCharge,
                _playerCombat.ServerBowData.BowChargeMoveMultiplier, 86400f);
        return accepted;
    }

    [Command]
    private void CmdPrepareBowShot()
    {
        if (_playerCombat != null && _playerCombat.IsServerTaunted) return;
        if (!PrepareServerShot()) TargetRejectBowAction(connectionToClient);
    }

    private bool PrepareServerShot()
    {
        if (_playerCombat == null || !_playerCombat.CanServerUseBow)
        {
            _serverShotAuthority.Cancel();
            _playerManager?.RemoveMovementEffect(CombatEffectSources.BowCharge);
            return false;
        }
        JobSkillData data = _playerCombat.ServerBowData;
        bool accepted = ReleaseServerShot(data, NetworkTime.time);
        if (accepted) _playerManager?.RemoveMovementEffect(CombatEffectSources.BowCharge);
        return accepted;
    }

    private bool ReleaseServerShot(JobSkillData data, double now) =>
        _serverShotAuthority.TryRelease(now, data.MinimumBowChargeSeconds / _serverChargeSpeedMultiplier,
            data.MaximumBowDamageChargeSeconds / _serverChargeSpeedMultiplier, data.MinimumBowDamageMultiplier,
            data.MaximumBowDamageMultiplier, _serverReleaseLockSeconds / _serverChargeSpeedMultiplier);

    private float ResolveServerReleaseLockSeconds()
    {
        // Preserve the authored release lock; the fallback only applies when no event exists.
        if (_animator != null && _animator.runtimeAnimatorController != null)
            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
                foreach (AnimationEvent animationEvent in clip.events)
                    if (animationEvent.functionName == nameof(OnBowReleaseFinished))
                        return Mathf.Max(0.1f, Mathf.Min(animationEvent.time / BaseAnimationPlaybackSpeed, ReleaseInputLockFallbackSeconds));
        return Mathf.Max(0.1f, ReleaseInputLockFallbackSeconds);
    }

    [Command]
    private void CmdCancelBowCharge()
    {
        if (_playerCombat != null && _playerCombat.IsServerTaunted) return;
        _serverShotAuthority.Cancel();
        _playerManager?.RemoveMovementEffect(CombatEffectSources.BowCharge);
    }

    [TargetRpc]
    private void TargetRejectBowAction(NetworkConnectionToClient target) => CancelCharge();

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
        else if (NetworkServer.active && TryBeginServerCharge(aimDirection))
        {
            RpcPlayBowAnimation(stateName, aimDirection);
            if (_playerCombat.IsServerTaunted && connectionToClient != null)
                TargetTauntBowVisual(connectionToClient, false, aimDirection);
        }
    }

    [Command]
    private void CmdTriggerBowRelease()
    {
        if (_playerCombat != null && _playerCombat.IsServerTaunted) return;
        if (_playerCombat == null || !_playerCombat.CanServerUseBow ||
            !_serverShotAuthority.HasPendingShot(NetworkTime.time))
            return;
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
        {
            RpcTriggerBowRelease();
            if (_playerCombat != null && _playerCombat.IsServerTaunted && connectionToClient != null)
                TargetTauntBowVisual(connectionToClient, true, _pendingDirection);
        }
    }

    [TargetRpc]
    private void TargetTauntBowVisual(NetworkConnectionToClient target, bool release, Vector3 direction)
    {
        if (isServer) return;
        _serverOwnsTauntVisual = true;
        if (release) TriggerBowReleaseLocal();
        else PlayBowAnimationLocal(DrawAnimationStateName, direction);
    }

    private void TriggerBowReleaseLocal()
    {
        _isVisuallyCharging = false;
        _isReleaseLocked = true;
        _drawReadyAt = float.PositiveInfinity;
        OnBowReleaseArrow();
        RestartReleaseLockFallback();
        if (_animator == null) return;
        ApplyBowPlaybackSpeed();
        _animator.speed = 1f;
        if (!string.IsNullOrWhiteSpace(ReleaseTriggerName)) _animator.ResetTrigger(ReleaseTriggerName);
        int layer = Mathf.Clamp(AnimationLayer, 0, _animator.layerCount - 1);
        string state = _settings != null ? _settings.ReleaseAnimationStateName : "Bow_Release";
        if (_animator.HasState(layer, Animator.StringToHash(state)))
            _animator.CrossFadeInFixedTime(state, .08f / _chargeSpeedMultiplier, layer, 0f);
        else if (!string.IsNullOrWhiteSpace(ReleaseTriggerName)) _animator.SetTrigger(ReleaseTriggerName);
    }

    private void PlayBowAnimationLocal(string stateName, Vector3 aimDirection)
    {
        if (string.IsNullOrWhiteSpace(stateName))
            return;

        if (stateName == DrawAnimationStateName)
        {
            _chargeSpeedMultiplier = ResolveCharacterAttackSpeed();
            _isVisuallyCharging = true;
            _isAimHoldReady = false;
            _drawReadyAt = Time.time + (_drawDuration + .05f) / _chargeSpeedMultiplier;
            SetHandArrowVisible(false);
            ApplyBowAimDirection(aimDirection);
            SetBowAimRigActive(true);
        }
        if (_animator == null) return;
        ApplyBowPlaybackSpeed();
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
        Vector3 aimPoint = _hasPendingAimPoint
            ? _pendingAimPoint
            : spawnPosition + _pendingDirection.normalized * Mathf.Max(1f, _aimDistance);
        Vector3 direction = aimPoint - spawnPosition;
        if (direction.sqrMagnitude <= 0.001f)
            direction = transform.forward;

        _hasPendingShot = false;
        _hasPendingAimPoint = false;

        if (isClient && isLocalPlayer && !isServer)
            CmdSpawnBowArrow(spawnPosition, aimPoint);
        else if (NetworkServer.active)
            SpawnBowArrow(spawnPosition, aimPoint);
        else if (!NetworkClient.active && ProjectilePrefab != null && _playerCombat != null &&
            CombatValidation.HasClearPath(transform.position + Vector3.up, spawnPosition, transform, transform))
        {
            var arrow = Instantiate(ProjectilePrefab, spawnPosition, Quaternion.LookRotation(direction.normalized));
            arrow.InitializeOffline(_playerCombat, direction.normalized, ProjectileSpeed, ProjectileLifeSeconds,
                _offlineShotMultiplier * _playerCombat.ConsumeNextAttackDamageMultiplier());
        }
    }

    [Command]
    private void CmdSpawnBowArrow(Vector3 spawnPosition, Vector3 aimPoint)
    {
        if (_playerCombat != null && _playerCombat.IsServerTaunted) return;
        SpawnBowArrow(spawnPosition, aimPoint);
    }

    private void SpawnBowArrow(Vector3 requestedSpawnPosition, Vector3 aimPoint)
    {
        BowArrowProjectile projectilePrefab = ProjectilePrefab;
        if (!NetworkServer.active || projectilePrefab == null || _playerCombat == null ||
            !_playerCombat.CanServerUseBow || !CombatValidation.IsFinite(aimPoint) ||
            !IsValidRequestedSpawnPosition(requestedSpawnPosition))
            return;

        Transform spawnPoint = _arrowSpawnPoint != null ? _arrowSpawnPoint : transform;
        Vector3 spawnPosition = spawnPoint.position;
        Vector3 direction = aimPoint - spawnPosition;
        if (direction.sqrMagnitude <= .001f || direction.sqrMagnitude > Mathf.Pow(Mathf.Max(1f, _aimDistance) + 8f, 2f)) return;
        if (!CombatValidation.HasClearPath(transform.position + Vector3.up, spawnPosition, transform, transform) ||
            !_serverShotAuthority.TryConsume(NetworkTime.time, out float damageMultiplier))
            return;
        damageMultiplier *= _playerCombat.ConsumeNextAttackDamageMultiplier();
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
        if (_animator != null && _animator.isHuman)
        {
            if (_bowHand == null) _bowHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (_stringHand == null) _stringHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
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

    private void UpdateNockedArrowPose()
    {
        if (_bowHand == null || _stringHand == null || _handArrowVisual == null ||
            (!_handArrowVisual.activeSelf && !_releaseArrowEventPending)) return;
        Vector3 direction = _bowHand.position - _stringHand.position;
        if (direction.sqrMagnitude < .001f) return;
        direction.Normalize();
        Transform arrow = _handArrowVisual.transform;
        // This asset's arrowhead is -Z and its nock is +Z. Keep the nock at the string hand.
        arrow.rotation = Quaternion.LookRotation(-direction, transform.up);
        arrow.position = _stringHand.position + direction * (_handArrowTailLocal * Mathf.Abs(arrow.lossyScale.z));
        if (_arrowSpawnPoint != null)
            _arrowSpawnPoint.SetPositionAndRotation(arrow.position, Quaternion.LookRotation(direction, transform.up));
    }

    private bool TryResolveCenterScreenAimPoint(out Vector3 aimPoint)
    {
        aimPoint = Vector3.zero;
        if (UsesLocalAim)
        {
            if (_followCamera == null)
                _followCamera = FindFirstObjectByType<FollowCamera>();

            if (_followCamera != null && _followCamera.Target == transform)
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
        RaycastHit[] hits = Physics.RaycastAll(aimRay, Mathf.Max(1f, _aimDistance), _aimHitMask, QueryTriggerInteraction.Collide);
        float closestDistance = float.PositiveInfinity;
        Vector3 aimPoint = aimRay.origin + aimRay.direction * Mathf.Max(1f, _aimDistance);

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;
            // Use the projectile's body-part rules, including trigger hitboxes, for the crosshair target.
            CombatHitTargets.Resolve(hit.collider, out var receiver, out var stats, out var bodyPart);
            if (receiver is HealthSystem && bodyPart == null) continue;
            if (hit.collider.isTrigger && (receiver == null || stats == null ||
                (receiver is Behaviour behaviour && !behaviour.isActiveAndEnabled))) continue;

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

        if (!UsesLocalAim) return;
        if (_followCamera == null)
            _followCamera = FindFirstObjectByType<FollowCamera>();

        if (_followCamera != null && _followCamera.Target == transform)
            _followCamera.SetTemporaryOffset(active, _bowCameraOffset, _bowCameraRotationOffset);
    }

    private void ResolveBowRigComponent()
    {
        if (_bowAimRigObject == null) return;
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
        // The calibrated pose driver preserves the authored arms; the old chest constraint must not add a second rotation.
        SetBowRigWeight(0f);
        if (_bowAimRigTarget != null) _bowAimRigTarget.enabled = true;
    }

    private void ApplyBowAimDirection(Vector3 aimDirection)
    {
        if (_bowAimRigTarget == null)
            return;

        if (UsesLocalAim)
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
        if (!IsCharging || _activeChargeData == null || !UsesLocalAim)
        {
            SetChargeRingVisible(false);
            return;
        }

        ResolveReticleView();
        if (_reticleView == null)
            return;

        float elapsed = Mathf.Max(0f, (float)(SkillTime - _chargeStartedAt)) * _chargeSpeedMultiplier;
        float denominator = Mathf.Max(0.001f,
            _activeChargeData.MaximumBowDamageChargeSeconds - _activeChargeData.MinimumBowChargeSeconds);
        float damageProgress = elapsed < _activeChargeData.MinimumBowChargeSeconds
            ? 0f
            : Mathf.Clamp01((elapsed - _activeChargeData.MinimumBowChargeSeconds) / denominator);

        if (_chargeRingMaximumScale <= 1.001f || damageProgress >= 0.999f)
        {
            SetChargeRingVisible(false);
            return;
        }

        _reticleView.SetCharge(damageProgress, _chargeRingMaximumScale, _chargeRingColor, _chargeRingThickness);
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
        float seconds = ReleaseInputLockFallbackSeconds / _chargeSpeedMultiplier;
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
        if (_animator == null || _animator.layerCount == 0 || string.IsNullOrWhiteSpace(ResetAnimationStateName))
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

    private float ResolveCharacterAttackSpeed() =>
        GetComponent<BattlePvp.Stats.StatManager>()?.CharacterModifiers.AttackSpeed ?? 1f;

    private void ApplyBowPlaybackSpeed()
    {
        if (_animator == null) return;
        foreach (var parameter in _animator.parameters)
            if (parameter.name == "BowPlaybackSpeed" && parameter.type == AnimatorControllerParameterType.Float)
            { _animator.SetFloat(parameter.nameHash, AnimationPlaybackSpeed); break; }
    }

    private float BaseAnimationPlaybackSpeed => _settings != null ? _settings.AnimationPlaybackSpeed : 3f;
    private float AnimationPlaybackSpeed => BaseAnimationPlaybackSpeed * _chargeSpeedMultiplier;

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
