using UnityEngine;
using UnityEngine.InputSystem; // 신형 시스템 네임스페이스
using BattlePvp.Stats;
using BattlePvp.Combat;
using BattlePvp.UI;
using System;
using System.Collections;
using Mirror;
using UnityEngine.SceneManagement;
using BattlePvp.Logic; // 추가

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerManager : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private StatManager _statManager;
    [SerializeField] private float moveSpeed = 5.0f; // 기본 이동 속도
    [SerializeField] private float gravity = 22f; // 일정한 가속도, 0.8m 점프의 체공 시간 약 0.54초
    [SerializeField] private float jumpHeight = 0.8f;
    [SerializeField] private float rotationSpeed = 10.0f; // 회전 속도
    [SerializeField] private float _transformSyncInterval = 0.033f;
    [SerializeField] private float _rotationOnlyTransformSyncInterval = 0.1f;
    [SerializeField, Range(0.1f, 0.5f)] private float _reliableTransformKeyframeInterval = 0.25f;
    [SerializeField] private float _jumpBufferSeconds = 0.15f;
    [SerializeField] private float _coyoteTimeSeconds = 0.08f;
    [SerializeField] private float _groundSnapDistance = 0.2f;

    [Header("Remote Movement Smoothing")]
    [SerializeField, Range(0.03f, 0.25f)] private float _remoteInterpolationBackTime = 0.1f;
    [SerializeField, Range(0.1f, 0.6f)] private float _remoteMaxInterpolationBackTime = 0.35f;
    [SerializeField, Range(0f, 0.1f)] private float _remoteJitterMargin = 0.03f;
    [SerializeField, Range(4, 64)] private int _remoteSnapshotBufferSize = 32;
    [SerializeField, Range(0f, 0.25f)] private float _remoteExtrapolationLimit = 0.1f;
    [SerializeField] private float _remoteSnapDistance = 3f;

    [Header("Locomotion Network Sync")]
    [SerializeField, Range(1f, 30f)] private float _locomotionSyncRate = 10f;
    [SerializeField, Range(1, 32)] private int _locomotionQuantizedChangeThreshold = 8;
    [SerializeField, Range(0f, 0.5f)] private float _locomotionInputDeadzone = 0.05f;
    [SerializeField, Min(0f)] private float _localLocomotionDampTime = 0.12f;
    [SerializeField, Min(0f)] private float _remoteLocomotionDampTime = 0.08f;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchSpeedMultiplier = 0.7f;
    [SerializeField] private float crouchControllerHeightMultiplier = 0.55f;
    [Tooltip("Idle와 Crouch Walk의 머리 높이 차이(캐릭터 기본 크기 기준). 충돌체 축소량과 별도로 적용합니다.")]
    [SerializeField] private float crouchCameraHeightDrop = 0.35f;

    [Header("Emotes")]
    [SerializeField] private EmoteData[] _emotes;
    [SerializeField] private int _defaultEmoteIndex = 0;

    [Header("Death Overlay Text")]
    [SerializeField] private string _respawnPromptText = "Press Space to Respawn";
    [SerializeField] private Color _deathOverlayTextColor = new Color(0.75f, 0.2f, 1f, 1f);

    private CharacterController controller;
    private Animator animator;
    private AudioSource _audioSource;
    private Rigidbody rb; // Rigidbody 참조 추가 (요청사항 반영)
    private BattlePvp.CameraLogic.FollowCamera followCamera; // 카메라 참조 추가

    private PlayerInput _playerInput;

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private Vector2 inputVector; // 신형 시스템에서 받을 Vector2 값
    [SerializeField] private float velocityY;
    [SerializeField] private bool isAttacking = false; // 현재 공격 중인지 여부
    [SerializeField] private bool isDead = false; // 사망 여부
    [SerializeField] private bool _matchEndLocked = false;
    [SyncVar(hook = nameof(OnCrouchStateChanged))]
    [SerializeField] private bool isCrouching = false;
    [SyncVar(hook = nameof(OnNetworkLocomotionStateChanged))]
    private ushort _networkLocomotionState;

    private HealthSystem _healthSystem;
    private PlayerCombat _combat;
    private Coroutine _respawnRoutine;
    private PlayerLifePresentation _lifePresentation;
    private PlayerRespawnCountdown _respawnCountdown;
    private bool _hasMatchEndPresentation;
    private Transform _matchEndWinnerTarget;
    private bool _isMatchWinner;
    private float _nextTransformSyncTime;
    private float _nextRotationOnlySyncTime;
    private float _nextReliableTransformSyncTime;
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation;
    private Vector3 _lastReliableSentPosition;
    private Quaternion _lastReliableSentRotation;
    private bool _hasSentReliableTransform;
    private Vector3 _remoteTargetPosition;
    private Quaternion _remoteTargetRotation;
    private bool _hasRemoteTransformTarget;
    private readonly FixedRingBuffer<RemoteTransformSnapshot> _remoteTransformSnapshots = new FixedRingBuffer<RemoteTransformSnapshot>(64);
    private double _latestRemoteSnapshotTime = double.NegativeInfinity;
    private double _remoteSnapshotDelayEstimate;
    private double _remoteSnapshotDelayDeviation;
    private float _remoteRenderDelay;
    private bool _hasRemoteDelayEstimate;
    private Vector2 _remoteLocomotionTarget;
    private ushort _lastSentLocomotionState;
    private bool _hasSentLocomotionState;
    private float _nextLocomotionSyncTime;
    private float _standingControllerHeight;
    private Vector3 _standingControllerCenter;
    private readonly MovementEffects _movementEffects = new MovementEffects();
    private readonly ServerMovementValidator _serverMovement = new ServerMovementValidator();
    private readonly MovementControlHistory _serverControlHistory = new MovementControlHistory();
    private readonly CombatPhysicsQuery _movementQuery = new CombatPhysicsQuery();
    private double _nextMovementCorrectionAt;
    [SyncVar] private uint _movementEpoch;
    private Coroutine _forcedMoveRoutine;
    private bool _disconnectedServerControl;
    private readonly ServerForcedMotion _serverForcedMotion = new ServerForcedMotion();
    [SyncVar] private bool _serverMotionActive;
    private bool _ownerServerMotionActive;
    private uint _forcedJumpSequence;
    private uint _forcedInputEpoch;
    private double _nextForcedInputAt;
    private double _nextForcedPoseAt;
    private Quaternion _serverInputRotation;
    private readonly InputLockEffects _inputLocks = new InputLockEffects();
    private bool _wasMoveInputLocked;
    private bool _skillMovementLocked => IsSkillInputLocked(SkillInputLockFlags.Move);
    private double _jumpRequestedUntil;
    private double _jumpAfterChargeUntil;
    private double _lastGroundedAt = double.NegativeInfinity;
    private bool _forcedTauntActive;
    private Vector3 _forcedTauntTargetPosition;
    private float _forcedTauntStopDistance;
    private EmoteData _activeEmote;
    private Coroutine _emoteRoutine;

    private struct RemoteTransformSnapshot
    {
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    public bool IsMatchEndLocked => _matchEndLocked;
    private bool IsFollowingServerMotion => _serverMotionActive || _ownerServerMotionActive;
    public double RemotePoseRenderTime => isLocalPlayer ? NetworkTime.time : NetworkTime.time - _remoteRenderDelay;
    public bool IsCrouching => isCrouching;
    public float CrouchCameraDrop => isCrouching
        ? Mathf.Max(0f, crouchCameraHeightDrop) * Mathf.Abs(transform.lossyScale.y)
        : 0f;
    public bool IsEmoteBlockingAttack => _activeEmote != null && _activeEmote.LockAttack;
    public bool IsEmoteBlockingMovement => _activeEmote != null && _activeEmote.LockMovement;
    public bool IsEmoteBlockingJump => _activeEmote != null && _activeEmote.LockJump;
    public bool IsSkillMoveLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Move);
    public bool IsSkillAttackLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Attack);
    public bool IsSkillJumpLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Jump);
    public bool IsSkillCrouchLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Crouch);
    private double MovementTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;
    private double LocalInputTime => Time.timeAsDouble;
    private bool ShouldHandleLocalInput =>
        (NetworkClient.active && isLocalPlayer) ||
        (!NetworkClient.active && !NetworkServer.active && !isClient && !isServer);

    public Vector3 GetSkillMoveDirection()
    {
        if (inputVector.sqrMagnitude <= 0.001f)
            return transform.forward;

        if (followCamera == null)
            return new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        float cameraYaw = followCamera.GetYaw();
        Vector3 cameraForward = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
        Vector3 cameraRight = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.right;
        return (cameraForward * inputVector.y + cameraRight * inputVector.x).normalized;
    }

    public void ApplySkillMoveMultiplier(float multiplier, float durationSeconds)
    {
        SetMovementEffect(0, multiplier, durationSeconds);
    }

    public void SetMovementEffect(int sourceId, float multiplier, float durationSeconds)
    {
        _movementEffects.Set(sourceId, multiplier, durationSeconds, MovementTime);
        RecordServerMovementControls();
    }

    public void RemoveMovementEffect(int sourceId)
    {
        _movementEffects.Remove(sourceId);
        RecordServerMovementControls();
    }

    [Server]
    public void ServerAuthorizeForcedMove(Vector3 direction, float distance, float durationSeconds)
    {
        if (!CombatValidation.IsFinite(direction) || !float.IsFinite(distance) ||
            !float.IsFinite(durationSeconds) || distance <= 0f || durationSeconds <= 0f ||
            !float.IsFinite(distance / durationSeconds) || (_healthSystem != null && _healthSystem.IsDead)) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.00001f) direction = transform.forward;
        double now = NetworkTime.time;
        bool wasActive = _serverForcedMotion.IsActive;
        if (wasActive) UpdateServerForcedMovement(now, false);
        else if (!isLocalPlayer)
        {
            ApplyRemotePose(_serverMovement.Position, transform.rotation);
            if (!_disconnectedServerControl) velocityY = _serverMovement.GetServerVerticalVelocity(gravity);
        }
        if (!_serverForcedMotion.TryBegin(direction.x, direction.z, distance, durationSeconds, now)) return;
        _serverMotionActive = true;
        _movementEpoch++;
        _forcedJumpSequence = 0u;
        _forcedInputEpoch = _movementEpoch;
        _nextForcedInputAt = _nextForcedPoseAt = 0d;
        _serverInputRotation = transform.rotation;
        _remoteTransformSnapshots.Clear();
        _hasRemoteTransformTarget = false;
        StopPredictedForcedMovement();
        CommitServerMovementPose(now);
        RpcServerMotionState(true, transform.position, transform.rotation, velocityY, _movementEpoch, now);
    }

    public void SetSkillMovementLock(bool locked)
    {
        if (locked) SetInputLock(CombatEffectSources.LegacySkillMovement, SkillInputLockFlags.Move, float.PositiveInfinity);
        else RemoveInputLock(CombatEffectSources.LegacySkillMovement);
    }

    public void ApplySkillInputLock(SkillInputLockFlags flags, float durationSeconds)
    {
        SetInputLock(CombatEffectSources.LegacySkillInput, flags, durationSeconds);
    }

    public void SetInputLock(int source, SkillInputLockFlags flags, float durationSeconds)
    {
        bool wasMoveLocked = _wasMoveInputLocked || IsSkillInputLocked(SkillInputLockFlags.Move);
        _inputLocks.Set(source, flags, durationSeconds, MovementTime);
        _wasMoveInputLocked = IsSkillInputLocked(SkillInputLockFlags.Move);
        RecordServerMovementControls();
        if (_wasMoveInputLocked) inputVector = Vector2.zero;
        else if (wasMoveLocked && ShouldHandleLocalInput) RefreshMoveInputFromCurrentAction();
    }

    public void RemoveInputLock(int source)
    {
        bool wasMoveLocked = _wasMoveInputLocked || IsSkillInputLocked(SkillInputLockFlags.Move);
        _inputLocks.Remove(source);
        if (wasMoveLocked && !IsSkillInputLocked(SkillInputLockFlags.Move) && ShouldHandleLocalInput)
            RefreshMoveInputFromCurrentAction();
        _wasMoveInputLocked = IsSkillInputLocked(SkillInputLockFlags.Move);
        RecordServerMovementControls();
    }

    public void ClearSkillInputLock() => RemoveInputLock(CombatEffectSources.LegacySkillInput);

    public bool IsSkillInputLocked(SkillInputLockFlags flag) => ((_inputLocks.Evaluate(MovementTime) |
        (GetComponent<ExpandedSkillController>()?.ControlFlags ?? SkillInputLockFlags.None)) & flag) != 0;

    public void SetForcedTauntControl(bool active, Vector3 targetPosition, float stopDistance)
    {
        bool wasActive = _forcedTauntActive;
        _forcedTauntActive = active;
        _forcedTauntTargetPosition = targetPosition;
        _forcedTauntStopDistance = Mathf.Max(0f, stopDistance);

        if (isServer && active != wasActive)
        {
            // Reuse the forced-motion epoch and owner correction stream for taunt authority.
            if (active && !_serverMotionActive)
            {
                if (!isLocalPlayer)
                {
                    ApplyRemotePose(_serverMovement.Position, transform.rotation);
                    velocityY = _serverMovement.GetServerVerticalVelocity(gravity);
                }
                _serverMotionActive = true;
                _movementEpoch++;
                StopPredictedForcedMovement();
                _remoteTransformSnapshots.Clear();
                _hasRemoteTransformTarget = false;
                CommitServerMovementPose(NetworkTime.time);
                RpcServerMotionState(true, transform.position, transform.rotation, velocityY, _movementEpoch, NetworkTime.time);
            }
            else if (!active && !_serverForcedMotion.IsActive && _serverMotionActive)
                FinishServerForcedMovement(NetworkTime.time);
        }

        if (active)
        {
            inputVector = Vector2.zero;
            _jumpRequestedUntil = 0d;
            if (isCrouching)
                SetCrouchState(false, true);
        }
        else if (wasActive)
        {
            RefreshMoveInputFromCurrentAction();
        }
    }

    private void LoadEmotesFromResourcesIfNeeded()
    {
        if (_emotes != null && _emotes.Length > 0)
            return;

        _emotes = Resources.LoadAll<EmoteData>("Emotes");
    }

    private EmoteData ResolveEmote(int index)
    {
        if (_emotes == null || _emotes.Length == 0)
            return null;

        if (index < 0 || index >= _emotes.Length)
            index = Mathf.Clamp(_defaultEmoteIndex, 0, _emotes.Length - 1);

        return _emotes[index];
    }

    public void OnEmote(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted()) return;
        if (GameInputController.IsPaused || GameInputController.IsTextInputActive) return;

        var combat = GetComponent<PlayerCombat>();
        if (combat != null && combat.IsBusyForEmote)
            return;

        TryPlayEmote(_defaultEmoteIndex);
    }

    public void TryPlayEmote(int emoteIndex)
    {
        if (!isLocalPlayer)
            return;

        if (_activeEmote != null)
            return;

        EmoteData emote = ResolveEmote(emoteIndex);
        if (emote == null || animator == null)
            return;

        if (_emoteRoutine != null)
        {
            StopCoroutine(_emoteRoutine);
            _emoteRoutine = null;
        }

        _activeEmote = emote;
        SetInputLock(CombatEffectSources.EmoteInput, emote.InputLockFlags, emote.ResolveDurationSeconds());

        PlayEmoteVisual(emote);

        float duration = emote.ResolveDurationSeconds();
        _emoteRoutine = StartCoroutine(CoEmote(duration, emote));

        if (isClient && isLocalPlayer)
        {
            if (isServer)
                RpcPlayEmote(emoteIndex);
            else
                CmdPlayEmote(emoteIndex);
        }
    }

    private IEnumerator CoEmote(float durationSeconds, EmoteData emote)
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, durationSeconds));
        if (_activeEmote == emote)
            StopEmote(emote);
    }

    private void StopEmote(EmoteData emote)
    {
        if (_activeEmote == null || emote == null)
            return;

        if (_emoteRoutine != null)
            StopCoroutine(_emoteRoutine);

        EmoteData active = _activeEmote;
        _activeEmote = null;

        ResetEmoteVisual(active);
        RemoveInputLock(CombatEffectSources.EmoteInput);

        _emoteRoutine = null;
    }

    private void PlayEmoteVisual(EmoteData emote)
    {
        if (animator == null || emote == null)
            return;

        int stateHash = string.IsNullOrWhiteSpace(emote.AnimationStateName)
            ? 0
            : Animator.StringToHash(emote.AnimationStateName);

        if (stateHash != 0 && animator.HasState(emote.AnimationLayer, stateHash))
            animator.Play(stateHash, emote.AnimationLayer, 0f);
        else if (!string.IsNullOrWhiteSpace(emote.FallbackStateName))
            animator.Play(emote.FallbackStateName, emote.AnimationLayer, 0f);
        else
            animator.Play(0, emote.AnimationLayer, 0f);

        animator.Update(0f);

        if (emote.UseSfx != null && _audioSource != null)
            _audioSource.PlayOneShot(emote.UseSfx, Mathf.Clamp01(emote.SfxVolume) * LocalGameSettings.Current.effects);
    }

    private void ResetEmoteVisual(EmoteData emote)
    {
        if (animator == null || emote == null)
            return;

        if (!string.IsNullOrWhiteSpace(emote.FallbackStateName))
        {
            int fallbackHash = Animator.StringToHash(emote.FallbackStateName);
            if (animator.HasState(emote.AnimationLayer, fallbackHash))
            {
                animator.Play(fallbackHash, emote.AnimationLayer, 0f);
                animator.Update(0f);
                return;
            }
        }

        animator.Play(0, emote.AnimationLayer, 0f);
        animator.Update(0f);
    }

    [Command]
    private void CmdPlayEmote(int emoteIndex)
    {
        EmoteData emote = ResolveEmote(emoteIndex);
        if (emoteIndex < 0 || _emotes == null || emoteIndex >= _emotes.Length || emote == null ||
            _activeEmote != null || (_healthSystem != null && _healthSystem.IsDead) ||
            (_statManager != null && !_statManager.HasServerStats) || IsBattleLoadingOrNotStarted() ||
            (_combat != null && _combat.IsBusyForEmote))
        {
            if (connectionToClient != null) TargetRejectEmote(connectionToClient);
            return;
        }
        _activeEmote = emote;
        SetInputLock(CombatEffectSources.EmoteInput, emote.InputLockFlags, emote.ResolveDurationSeconds());
        _emoteRoutine = StartCoroutine(CoEmote(emote.ResolveDurationSeconds(), emote));
        RpcPlayEmote(emoteIndex);
    }

    [TargetRpc]
    private void TargetRejectEmote(NetworkConnection target) => StopEmote(_activeEmote);

    [ClientRpc(includeOwner = false)]
    private void RpcPlayEmote(int emoteIndex)
    {
        if (isLocalPlayer)
            return;

        EmoteData emote = ResolveEmote(emoteIndex);
        if (emote == null)
            return;

        PlayEmoteVisual(emote);
    }

    public void RefreshMoveInputFromCurrentAction()
    {
        if (!ShouldHandleLocalInput)
            return;

        if (isDead || _matchEndLocked || GameInputController.IsPaused || GameInputController.IsTextInputActive || _skillMovementLocked || IsSkillMoveLocked || IsEmoteBlockingMovement)
        {
            inputVector = Vector2.zero;
            return;
        }

        if (_playerInput == null)
            _playerInput = GetComponent<PlayerInput>();

        InputAction moveAction = _playerInput != null ? _playerInput.actions.FindAction("Move", false) : null;
        inputVector = moveAction != null && moveAction.enabled ? moveAction.ReadValue<Vector2>() : Vector2.zero;
    }

    private void ResetLocalInputForPlayMode()
    {
        if (!ShouldHandleLocalInput)
            return;

        if (_playerInput == null)
            _playerInput = GetComponent<PlayerInput>();

        if (_playerInput != null)
        {
            if (!_playerInput.enabled)
                _playerInput.enabled = true;

            _playerInput.ActivateInput();

            var playerActionMap = _playerInput.actions != null
                ? _playerInput.actions.FindActionMap("Player", false)
                : null;

            if (playerActionMap != null)
            {
                if (_playerInput.currentActionMap != playerActionMap)
                    _playerInput.SwitchCurrentActionMap(playerActionMap.name);
                else if (!playerActionMap.enabled)
                    playerActionMap.Enable();
            }
        }

        if (GameInputController.Instance != null)
            GameInputController.Instance.ResetToPlayMode();

        _jumpRequestedUntil = 0d;
        SnapToGroundIfClose();
        _lastGroundedAt = IsGroundedForJump()
            ? LocalInputTime
            : double.NegativeInfinity;

        RefreshMoveInputFromCurrentAction();
    }

    public void MoveBySkill(Vector3 direction, float distance, float durationSeconds)
    {
        // Network skill movement is committed by the server. This path is only for offline preview.
        if (NetworkClient.active || NetworkServer.active || isClient || isServer) return;
        if (!ShouldHandleLocalInput || !isActiveAndEnabled || isDead ||
            !CombatValidation.IsFinite(direction) || !float.IsFinite(distance) || !float.IsFinite(durationSeconds))
            return;
        if (_forcedMoveRoutine != null)
            StopCoroutine(_forcedMoveRoutine);
        _forcedMoveRoutine = StartCoroutine(CoMoveBySkill(direction, distance, durationSeconds));
    }

    private IEnumerator CoMoveBySkill(Vector3 direction, float distance, float durationSeconds)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        float duration = Mathf.Max(0.01f, durationSeconds);
        float speed = Mathf.Max(0f, distance) / duration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!ShouldHandleLocalInput || isDead || !isActiveAndEnabled) break;
            float step = Mathf.Min(Time.deltaTime, duration - elapsed);
            if (controller != null && controller.enabled)
                controller.Move(direction * speed * step);
            elapsed += step;
            yield return null;
        }
        _forcedMoveRoutine = null;
    }

    private void CancelMovementActions()
    {
        if (isServer && _serverMotionActive)
        {
            _forcedTauntActive = false;
            FinishServerForcedMovement(NetworkTime.time);
        }
        _serverForcedMotion.Cancel();
        if (isServer || !isClient) _serverMotionActive = false;
        StopPredictedForcedMovement();
        _movementEffects.Clear();
        _inputLocks.Clear();
        _wasMoveInputLocked = false;
        _jumpAfterChargeUntil = 0d;
        _jumpRequestedUntil = 0d;
        inputVector = Vector2.zero;
        isAttacking = false;
        SetForcedTauntControl(false, Vector3.zero, 0f);
        inputVector = Vector2.zero;
        RecordServerMovementControls();
    }

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int locomotionRateHash = Animator.StringToHash("LocomotionRate");
    private Vector3 _previousLocomotionPosition;
    private bool _hasLocomotionPosition;
    private bool _hasLocomotionRate;
    [SerializeField, Min(.1f)] private float _walkAnimationMetersPerSecond = 5f;
    private readonly int moveXHash = Animator.StringToHash("MoveX");
    private readonly int moveYHash = Animator.StringToHash("MoveY");
    private readonly int isCrouchingHash = Animator.StringToHash("IsCrouching");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        foreach (var parameter in animator.parameters)
            if (parameter.nameHash == locomotionRateHash) _hasLocomotionRate = true;
        _audioSource = GetComponent<AudioSource>();
        rb = GetComponent<Rigidbody>();
        _playerInput = GetComponent<PlayerInput>();
        _healthSystem = GetComponent<HealthSystem>();
        _combat = GetComponent<PlayerCombat>();
        _lifePresentation = GetComponent<PlayerLifePresentation>();
        if (_lifePresentation == null) _lifePresentation = gameObject.AddComponent<PlayerLifePresentation>();
        _lifePresentation.Initialize(transform,
            _healthSystem != null && _healthSystem.LifeAnimator != null ? _healthSystem.LifeAnimator : animator,
            _respawnPromptText, _deathOverlayTextColor);
        if (controller != null)
        {
            _standingControllerHeight = controller.height;
            _standingControllerCenter = controller.center;
        }
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
        LoadEmotesFromResourcesIfNeeded();
        DisableBuiltInNetworkTransforms();
        ConfigureRigidbodyForCharacterController();
    }

    private void DisableBuiltInNetworkTransforms()
    {
        var networkTransforms = GetComponents<NetworkTransformBase>();
        foreach (var networkTransform in networkTransforms)
        {
            if (networkTransform != null)
                networkTransform.enabled = false;
        }
    }

    private void ConfigureRigidbodyForCharacterController()
    {
        if (rb == null)
            return;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    public override void OnStartLocalPlayer()
    {
        BindLocalCamera();

        _lastSentLocomotionState = PackLocomotion(Vector2.zero);
        _hasSentLocomotionState = true;
        ApplyLocomotionAnimation(Vector2.zero, false);
        ResetLocalInputForPlayMode();
        RestoreLifePresentation();
    }

    public void BindLocalCamera()
    {
        if (!ShouldHandleLocalInput) return;
        if (followCamera == null) followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
        if (followCamera != null) followCamera.SetTarget(transform);
        _lifePresentation?.AttachCamera(followCamera);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isLocalPlayer)
            _remoteLocomotionTarget = UnpackLocomotion(_networkLocomotionState);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _serverMovement.Reset(transform.position, NetworkTime.time);
        _serverControlHistory.Clear();
        RecordServerMovementControls();
    }

    private void OnEnable()
    {
        if (_statManager != null)
        {
            _statManager.StatsChanged += OnStatsChanged;
            _statManager.DerivedStatsChanged += UpdateMoveSpeed;
            UpdateMoveSpeed();
        }
        if (_healthSystem != null)
        {
            _healthSystem.OnDied += HandleDeath;
            _healthSystem.OnRevived += HandleRevived;
        }

        if (ShouldHandleLocalInput)
        {
            BindLocalCamera();
            ResetLocalInputForPlayMode();
        }
        RestoreLifePresentation();
    }

    private void OnDisable()
    {
        if (_statManager != null)
        {
            _statManager.StatsChanged -= OnStatsChanged;
            _statManager.DerivedStatsChanged -= UpdateMoveSpeed;
        }
        
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= HandleDeath;
            _healthSystem.OnRevived -= HandleRevived;
        }

        if (_emoteRoutine != null)
            StopCoroutine(_emoteRoutine);
        _emoteRoutine = null;
        _activeEmote = null;
        CancelMovementActions();
        if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
        _respawnRoutine = null;
        _lifePresentation?.Suspend();
    }

    private void OnStatsChanged(StatContainer _)
    {
        if (this == null) return;
        UpdateMoveSpeed();
    }

    private void UpdateMoveSpeed()
    {
        if (_statManager == null) return;
        moveSpeed = _statManager.GetDerivedStats().MoveSpeed;
        RecordServerMovementControls();
    }

    // Input System 메시지 수신 (SendMessage 방식 또는 Player Input 컴포넌트 활용)
    public void OnMove(InputValue value)
    {
        if (!ShouldHandleLocalInput) return;
        if (isDead || _matchEndLocked || _skillMovementLocked || IsSkillMoveLocked || IsEmoteBlockingMovement) { inputVector = Vector2.zero; return; }
        inputVector = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (!ShouldHandleLocalInput) return;
        if (!value.isPressed) return;
        QueueJumpRequest();
    }

    public void OnCrouch(InputValue value)
    {
        if (!ShouldHandleLocalInput) return;
        if (!value.isPressed) return;
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() || _skillMovementLocked || IsSkillCrouchLocked || IsEmoteBlockingJump) return;
        if (GameInputController.IsPaused || GameInputController.IsTextInputActive) return;

        SetCrouchState(!isCrouching, true);
    }

    private void Update()
    {
        RecordServerMovementControls();
        if (isServer && _serverForcedMotion.IsActive)
        {
            if (isLocalPlayer) SubmitForcedMovementInput();
            UpdateServerForcedMovement(NetworkTime.time, true);
            UpdateRemoteLocomotionAnimation();
            return;
        }
        if (isServer && _forcedTauntActive)
        {
            UpdateDisconnectedMovement();
            if (NetworkTime.time >= _nextForcedPoseAt)
            {
                _nextForcedPoseAt = NetworkTime.time + Mathf.Max(0.01f, _transformSyncInterval);
                RpcServerMotionPose(transform.position, transform.rotation, NetworkTime.time, _movementEpoch);
            }
            UpdateRemoteLocomotionAnimation();
            return;
        }
        if (!isServer && IsFollowingServerMotion && isLocalPlayer)
        {
            SubmitForcedMovementInput();
            SmoothServerControlledOwner();
            if (_forcedTauntActive) ApplyLocomotionAnimation(UnpackLocomotion(_networkLocomotionState), false);
            else UpdateLocalLocomotion(inputVector);
            return;
        }
        if (isServer && _disconnectedServerControl)
        {
            UpdateDisconnectedMovement();
            UpdateRemoteLocomotionAnimation();
            return;
        }
        if (!ShouldHandleLocalInput)
        {
            SmoothRemoteTransform();
            UpdateRemoteLocomotionAnimation();
            return;
        }

        if (_hasMatchEndPresentation) _lifePresentation?.RefreshSpectateTarget();

        // 사망 상태이거나 ESC 메뉴(Pause) 상태일 때는 이동 처리를 하지 않음
        bool moveLocked = IsSkillInputLocked(SkillInputLockFlags.Move);
        if (_wasMoveInputLocked && !moveLocked) RefreshMoveInputFromCurrentAction();
        _wasMoveInputLocked = moveLocked;
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted())
        {
            UpdateLocalLocomotion(Vector2.zero);
            return;
        }
        if (GameInputController.IsPaused || GameInputController.IsTextInputActive)
        {
            _jumpAfterChargeUntil = 0d;
            // UI blocks voluntary input, not gravity. Opening chat/terminal in midair must not hover.
            if (controller != null && controller.enabled) { MoveBallistic(Vector3.zero, Time.deltaTime); TrySyncTransform(); }
            UpdateLocalLocomotion(Vector2.zero);
            return;
        }
        PollJumpInputFallback();
        ApplyMovement();
    }

    private bool IsBattleLoadingOrNotStarted()
    {
        if (SceneManager.GetActiveScene().name != "Battle")
            return false;

        var battleState = BattlePvp.Networking.BattleStateMachine.Instance;
        if (battleState == null)
            return false;

        if (battleState.CurrentState == BattlePvp.Networking.BattleState.MatchEnded && !_matchEndLocked)
            return false;

        return battleState.IsLoading || battleState.CurrentState != BattlePvp.Networking.BattleState.InBattle;
    }

    // Animator의 Animation Event 등에서 호출하여 공격 상태를 알립니다.
    private void PollJumpInputFallback()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            QueueJumpRequest();
    }

    private void QueueJumpRequest()
    {
        if (!CanQueueJumpRequest())
            return;

        bool cancelledCharge = GetComponent<ExpandedSkillController>()?.CancelChargeFromInput() == true;
        // Do not lose the jump in the old forced-motion epoch while waiting for server release.
        if (cancelledCharge && isClient && !isServer) _jumpAfterChargeUntil = LocalInputTime + 2d;
        _jumpRequestedUntil = LocalInputTime + Mathf.Max(0.01f, _jumpBufferSeconds);
    }

    private bool CanQueueJumpRequest()
    {
        return !isDead
               && !_matchEndLocked
               && !IsBattleLoadingOrNotStarted()
               && !GameInputController.IsPaused
               && !GameInputController.IsTextInputActive
               && controller != null
               && controller.enabled;
    }

    private bool CanConsumeJumpRequest()
    {
        return CanQueueJumpRequest()
               && !_skillMovementLocked
               && !IsSkillJumpLocked
               && !IsEmoteBlockingJump;
    }

    private bool IsGroundedForJump()
    {
        return controller != null && controller.enabled &&
               (controller.isGrounded || TryGetGroundDistance(out _));
    }

    private bool TryGetGroundDistance(out float distanceToGround)
    {
        distanceToGround = float.PositiveInfinity;
        if (controller == null || !controller.enabled)
            return false;

        float halfHeight = Mathf.Max(controller.radius, controller.height * 0.5f);
        Vector3 origin = transform.position + controller.center + Vector3.up * 0.02f;
        float maxDistance = halfHeight + Mathf.Max(0.01f, _groundSnapDistance);

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.transform != null && hit.transform.IsChildOf(transform))
            return false;

        if (hit.normal.y < 0.5f)
            return false;

        distanceToGround = Mathf.Max(0f, hit.distance - halfHeight);
        return distanceToGround <= Mathf.Max(0.01f, _groundSnapDistance);
    }

    private void SnapToGroundIfClose()
    {
        if (controller == null || !controller.enabled || controller.isGrounded || velocityY > 0f)
            return;

        if (!TryGetGroundDistance(out float distanceToGround))
            return;

        if (distanceToGround > 0.001f)
            controller.Move(Vector3.down * (distanceToGround + 0.01f));

        if (velocityY < 0f)
            velocityY = -0.5f;
    }

    private bool TryConsumeJumpRequest()
    {
        double now = LocalInputTime;
        if (_jumpAfterChargeUntil > 0d)
        {
            if (now > _jumpAfterChargeUntil || !CanQueueJumpRequest()) _jumpAfterChargeUntil = 0d;
            else
            {
                if (IsFollowingServerMotion || GetComponent<ExpandedSkillController>()?.IsCharging == true) return false;
                _jumpAfterChargeUntil = 0d;
                _jumpRequestedUntil = now + Mathf.Max(.01f, _jumpBufferSeconds);
            }
        }
        if (_jumpRequestedUntil < now || velocityY > 0f || !CanConsumeJumpRequest())
            return false;

        bool canUseCoyoteTime = now - _lastGroundedAt <= Mathf.Max(0f, _coyoteTimeSeconds);
        if (!IsGroundedForJump() && !canUseCoyoteTime)
            return false;

        _jumpRequestedUntil = 0d;
        _lastGroundedAt = double.NegativeInfinity;
        velocityY = Mathf.Sqrt(jumpHeight * 2f * gravity);
        return true;
    }

    public void SetMovementLock(bool isLocked)
    {
        // Legacy animation events may exit after the next combo has already acquired the lock.
        if (!isLocked && _combat != null && _combat.IsAttackActive) return;
        isAttacking = isLocked;
        RecordServerMovementControls();
    }

    private void SetCrouchState(bool crouching, bool notifyServer)
    {
        if (isCrouching == crouching)
        {
            ApplyCrouchState(crouching);
            return;
        }

        isCrouching = crouching;
        ApplyCrouchState(crouching);
        RecordServerMovementControls();

        if (notifyServer && isClient && isLocalPlayer)
        {
            if (isServer)
                RpcSetCrouchState(crouching);
            else
                CmdSetCrouchState(crouching);
        }
    }

    private void OnCrouchStateChanged(bool oldValue, bool newValue)
    {
        ApplyCrouchState(newValue);
    }

    private void ApplyCrouchState(bool crouching)
    {
        if (controller != null && _standingControllerHeight > 0f)
        {
            float targetHeight = crouching
                ? _standingControllerHeight * Mathf.Clamp01(crouchControllerHeightMultiplier)
                : _standingControllerHeight;
            float heightDelta = _standingControllerHeight - targetHeight;

            controller.height = targetHeight;
            controller.center = crouching
                ? _standingControllerCenter - (Vector3.up * heightDelta * 0.5f)
                : _standingControllerCenter;
        }

        if (animator != null)
            animator.SetBool(isCrouchingHash, crouching);
    }

    [Command]
    private void CmdSetCrouchState(bool crouching)
    {
        MovementControlState controls = RecordServerMovementControls();
        if ((_healthSystem != null && _healthSystem.IsDead) ||
            (_statManager != null && !_statManager.HasServerStats) || IsBattleLoadingOrNotStarted() || controls.CrouchLocked)
        {
            if (connectionToClient != null) TargetCorrectCrouchState(connectionToClient, isCrouching);
            return;
        }
        SetCrouchState(crouching, false);
        RpcSetCrouchState(crouching);
    }

    [TargetRpc]
    private void TargetCorrectCrouchState(NetworkConnection target, bool crouching) => SetCrouchState(crouching, false);

    [ClientRpc(includeOwner = false)]
    private void RpcSetCrouchState(bool crouching)
    {
        SetCrouchState(crouching, false);
    }

    private void ApplyMovement()
    {
        // 1. 카메라 방향 기준 이동 벡터 계산
        SnapToGroundIfClose();
        Vector3 moveDirection = Vector3.zero;
        bool forcedTauntMoving = false;
        if (_forcedTauntActive)
        {
            Vector3 toTarget = _forcedTauntTargetPosition - transform.position;
            toTarget.y = 0f;
            float stopDistance = Mathf.Max(0f, _forcedTauntStopDistance);
            forcedTauntMoving = toTarget.sqrMagnitude > stopDistance * stopDistance;

            if (toTarget.sqrMagnitude > 0.001f)
            {
                Vector3 targetDirection = toTarget.normalized;
                moveDirection = forcedTauntMoving ? targetDirection : Vector3.zero;
                Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }
        else if (followCamera != null)
        {
            // 카메라의 수평 정면 및 우측 방향 가져오기
            float cameraYaw = followCamera.GetYaw();
            Vector3 cameraForward = Quaternion.Euler(0, cameraYaw, 0) * Vector3.forward;
            Vector3 cameraRight = Quaternion.Euler(0, cameraYaw, 0) * Vector3.right;

            moveDirection = (cameraForward * inputVector.y + cameraRight * inputVector.x).normalized;

            // 2. 캐릭터 회전 (공격 중에도 카메라 방향에 맞춰 회전 허용)
            if (InputModeRules.UsesFpsLook(SceneManager.GetActiveScene().name) || (_combat != null && _combat.IsAimingBow))
                transform.rotation = Quaternion.Euler(0, cameraYaw, 0);
            else if (inputVector.sqrMagnitude > .001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDirection), rotationSpeed * Time.deltaTime);
        }
        else
        {
            // 카메라가 없을 경우 기존 월드 기준 이동 (폴백)
            bool fps = InputModeRules.UsesFpsLook(SceneManager.GetActiveScene().name);
            moveDirection = fps ? (transform.right * inputVector.x + transform.forward * inputVector.y).normalized : new Vector3(inputVector.x, 0, inputVector.y).normalized;
            if (!fps && inputVector.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        if(TryGetComponent<ExpandedSkillController>(out var localSkillControl))
            transform.rotation=localSkillControl.RestrictRotation(transform.rotation);

        // 3. 중력 처리
        bool groundedForJump = velocityY <= 0f && IsGroundedForJump();
        if (groundedForJump)
            _lastGroundedAt = LocalInputTime;

        TryConsumeJumpRequest();

        // 4. 최종 이동
        float currentMoveSpeed = moveSpeed;
        if (_skillMovementLocked)
            currentMoveSpeed = 0f;
        currentMoveSpeed *= _movementEffects.Evaluate(MovementTime);
        if (isAttacking)
            currentMoveSpeed *= 0.6f;
        if (isCrouching)
            currentMoveSpeed *= crouchSpeedMultiplier;
        
        // Anti-Gliding: 입력이 없을 때는 0으로 고정
        if (isAttacking && inputVector.sqrMagnitude < 0.001f && !forcedTauntMoving)
        {
            currentMoveSpeed = 0f;
        }

        MoveBallistic(moveDirection * currentMoveSpeed, Time.deltaTime);
        TrySyncTransform();

        // 5. 애니메이션 (사망 시 업데이트 중지)
        Vector2 locomotion = forcedTauntMoving ? Vector2.up : inputVector;
        if (isDead || currentMoveSpeed <= 0.001f || moveDirection.sqrMagnitude <= 0.001f)
            locomotion = Vector2.zero;

        UpdateLocalLocomotion(locomotion);
    }

    // Integrate acceleration on the launch frame too. Small collision steps prevent a slow
    // frame from adding an entire frame of upward travel without gravity or crossing a ceiling.
    private void MoveBallistic(Vector3 horizontalVelocity, float duration)
    {
        while (duration > .000001f)
        {
            float step = Mathf.Min(duration, 1f / 60f);
            bool grounded = controller.isGrounded && velocityY <= 0f;
            float verticalDistance;
            if (grounded) { velocityY = -.5f; verticalDistance = velocityY * step; }
            else verticalDistance = JumpPhysics.Integrate(ref velocityY, gravity, step);
            CollisionFlags collision = controller.Move(horizontalVelocity * step + Vector3.up * verticalDistance);
            if ((collision & CollisionFlags.Above) != 0 && velocityY > 0f) velocityY = 0f;
            if ((collision & CollisionFlags.Below) != 0 && velocityY < 0f) velocityY = -.5f;
            duration -= step;
        }
    }

    private void LateUpdate()
    {
        Vector3 displacement = transform.position - _previousLocomotionPosition;
        _previousLocomotionPosition = transform.position;
        if (!_hasLocomotionPosition) { _hasLocomotionPosition = true; return; }
        if (!_hasLocomotionRate || animator == null || Time.deltaTime <= 0f) return;
        displacement.y = 0f;
        float metersPerSecond = displacement.magnitude / Time.deltaTime;
        // Ignore respawn/network teleports. Only locomotion states consume this parameter.
        float rate = metersPerSecond > 40f ? 1f : Mathf.Clamp(metersPerSecond / _walkAnimationMetersPerSecond, .1f, 4f);
        animator.SetFloat(locomotionRateHash, rate / Mathf.Max(.01f, animator.speed));
    }

    [Server]
    public void ServerBeginDisconnectedControl()
    {
        _disconnectedServerControl = true;
        inputVector = Vector2.zero;
        _networkLocomotionState = PackLocomotion(Vector2.zero);
        _remoteLocomotionTarget = Vector2.zero;
        _remoteTransformSnapshots.Clear();
        _hasRemoteTransformTarget = false;
        _movementEpoch++;
        // The host's remote rendering may lag behind its accepted physics position.
        ApplyRemotePose(_serverMovement.Position, transform.rotation);
        _serverForcedMotion.ResetOwnerInput();
        if (!_serverForcedMotion.IsActive) velocityY = _serverMovement.GetServerVerticalVelocity(gravity);
        RpcResumeRetainedPose(transform.position, transform.rotation, _movementEpoch);
    }

    [Server]
    public void ServerPrepareReconnect()
    {
        _disconnectedServerControl = false;
        _movementEpoch++;
        _serverMovement.Rebase(transform.position, NetworkTime.time, IsServerPositionGrounded(transform.position));
        _serverForcedMotion.ResetOwnerInput();
        _remoteTransformSnapshots.Clear();
        _hasRemoteTransformTarget = false;
        GetComponent<ServerPoseHistory>()?.ResetHistory();
        RpcResumeRetainedPose(transform.position, transform.rotation, _movementEpoch);
    }

    [Server]
    public void ServerSendReconnectState()
    {
        if (connectionToClient == null) return;
        TargetRestoreMovement(connectionToClient, _movementEffects.Capture(NetworkTime.time),
            _inputLocks.CaptureForReconnect(NetworkTime.time), _forcedTauntActive,
            _forcedTauntTargetPosition, _forcedTauntStopDistance, velocityY);
        TargetRestoreServerMotion(connectionToClient, _serverMotionActive,
            transform.position, transform.rotation, velocityY, _movementEpoch, NetworkTime.time);
    }

    [TargetRpc]
    private void TargetRestoreMovement(NetworkConnection target, MovementEffectSnapshot[] effects,
        InputLockSnapshot[] locks, bool taunted, Vector3 tauntTarget, float stopDistance,
        float verticalSpeed)
    {
        if (isServer) return;
        _movementEffects.Restore(effects, NetworkTime.time);
        _inputLocks.Restore(locks, NetworkTime.time);
        SetForcedTauntControl(taunted, tauntTarget, stopDistance);
        velocityY = verticalSpeed;
    }

    [ClientRpc]
    private void RpcResumeRetainedPose(Vector3 position, Quaternion rotation, uint epoch)
    {
        if (isServer) return;
        _movementEpoch = epoch;
        _remoteTransformSnapshots.Clear();
        _latestRemoteSnapshotTime = double.NegativeInfinity;
        _hasRemoteTransformTarget = false;
        _hasSentReliableTransform = false;
        ApplyRemotePose(position, rotation);
    }

    private void UpdateDisconnectedMovement()
    {
        if (controller == null || !controller.enabled) return;
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Vector3 movement = Vector3.zero;
        if ((_healthSystem == null || !_healthSystem.IsDead) && !IsBattleLoadingOrNotStarted() &&
            (_statManager == null || _statManager.HasServerStats))
        {
            if (_forcedTauntActive && !IsSkillInputLocked(SkillInputLockFlags.Move))
            {
                Vector3 direction = _forcedTauntTargetPosition - transform.position;
                direction.y = 0f;
                float distance = direction.magnitude;
                if (distance > 0.001f)
                    transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                if (distance > _forcedTauntStopDistance)
                    movement += direction.normalized * Mathf.Min(RecordServerMovementControls().Speed,
                        (distance - _forcedTauntStopDistance) / Mathf.Max(step, 0.0001f));
            }
        }
        Vector3 before = transform.position;
        MoveBallistic(movement, step);
        if (_forcedTauntActive)
        {
            Vector3 horizontal = transform.position - before;
            horizontal.y = 0f;
            Vector2 locomotion = horizontal.sqrMagnitude > 0.000001f ? Vector2.up : Vector2.zero;
            SetServerLocomotionState(PackLocomotion(locomotion));
            _remoteLocomotionTarget = locomotion;
        }
        _serverMovement.CommitServerMove(transform.position, NetworkTime.time, controller.isGrounded, velocityY);
        GetComponent<ServerPoseHistory>()?.RecordNetworkPose(NetworkTime.time, transform.position, transform.rotation);
        if (transform.position != before)
            RpcSyncTransform(transform.position, transform.rotation, NetworkTime.time, _movementEpoch);
    }

    private void StopPredictedForcedMovement()
    {
        if (_forcedMoveRoutine != null) StopCoroutine(_forcedMoveRoutine);
        _forcedMoveRoutine = null;
    }

    private void SubmitForcedMovementInput()
    {
        double now = NetworkTime.time;
        if (now < _nextForcedInputAt) return;
        _nextForcedInputAt = now + Mathf.Max(0.01f, _transformSyncInterval);
        bool blocked = isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() ||
            GameInputController.IsPaused || GameInputController.IsTextInputActive;
        if (blocked) _jumpAfterChargeUntil = 0d;
        Vector3 direction = !blocked && inputVector.sqrMagnitude > 0.001f ? GetSkillMoveDirection() : Vector3.zero;
        direction.y = 0f;
        Quaternion rotation = followCamera != null ? Quaternion.Euler(0f, followCamera.GetYaw(), 0f) : transform.rotation;
        if (!blocked)
        {
            PollJumpInputFallback();
            if (_jumpAfterChargeUntil <= 0d && _jumpRequestedUntil > 0d && _jumpRequestedUntil >= LocalInputTime)
            {
                _forcedJumpSequence = CombatRequestSequences.Next(_forcedJumpSequence);
                _jumpRequestedUntil = 0d;
            }
        }
        if (isServer) AcceptServerMotionInput(direction, rotation, _forcedJumpSequence, now, _forcedInputEpoch);
        else CmdServerMotionInput(direction, rotation, _forcedJumpSequence, now, _forcedInputEpoch);
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdServerMotionInput(Vector3 direction, Quaternion rotation, uint jumpSequence, double sampleTime, uint epoch)
        => AcceptServerMotionInput(direction, rotation, jumpSequence, sampleTime, epoch);

    private void AcceptServerMotionInput(Vector3 direction, Quaternion rotation, uint jumpSequence, double sampleTime, uint epoch)
    {
        if (epoch != _movementEpoch || !CombatValidation.IsFinite(direction) || Mathf.Abs(direction.y) > 0.001f ||
            !ServerMovementValidator.IsValidRotation(rotation) ||
            !_serverForcedMotion.TrySetOwnerInput(direction.x, direction.z, jumpSequence, sampleTime, NetworkTime.time)) return;
        _serverInputRotation = TryGetComponent<ExpandedSkillController>(out var skills) ? skills.RestrictRotation(rotation.normalized) : rotation.normalized;
    }

    private void UpdateServerForcedMovement(double now, bool finishWhenComplete)
    {
        if (!_serverForcedMotion.IsActive) return;
        if (controller == null || !controller.enabled || (_healthSystem != null && _healthSystem.IsDead))
        {
            FinishServerForcedMovement(now);
            return;
        }
        double remainingStep = _serverForcedMotion.TakeStep(now);
        if (remainingStep <= 0.0000001d)
        {
            if (_serverForcedMotion.HasFinished && finishWhenComplete) FinishServerForcedMovement(now);
            return;
        }
        MovementControlState controls = RecordServerMovementControls();
        Vector3 forcedVelocity = new Vector3(_serverForcedMotion.DirectionX, 0f, _serverForcedMotion.DirectionZ) * _serverForcedMotion.Speed;
        Vector3 voluntaryDirection = Vector3.zero;
        if (!controls.MoveLocked)
        {
            if (_forcedTauntActive)
            {
                Vector3 toTarget = _forcedTauntTargetPosition - transform.position;
                toTarget.y = 0f;
                if (toTarget.magnitude > _forcedTauntStopDistance) voluntaryDirection = toTarget.normalized;
            }
            else if (_serverForcedMotion.HasInput(now))
                voluntaryDirection = new Vector3(_serverForcedMotion.InputX, 0f, _serverForcedMotion.InputZ);
        }
        bool grounded = controller.isGrounded || IsServerPositionGrounded(transform.position);
        if (_serverForcedMotion.TryConsumeJump(now, grounded && !controls.JumpLocked && !_forcedTauntActive))
        {
            _serverMovement.BeginServerJump(jumpHeight, now);
            velocityY = Mathf.Sqrt(2f * jumpHeight * gravity);
        }
        // Short collision steps retain wall sliding and step/slope handling even after a host hitch.
        while (remainingStep > 0.0000001d)
        {
            float step = (float)Math.Min(remainingStep, 1d / 60d);
            Vector3 voluntaryVelocity = voluntaryDirection * controls.Speed;
            if (_forcedTauntActive)
            {
                Vector3 direction = _forcedTauntTargetPosition - transform.position;
                direction.y = 0f;
                float distance = direction.magnitude;
                if (distance > 0.001f) transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                voluntaryVelocity = controls.MoveLocked || distance <= _forcedTauntStopDistance ? Vector3.zero :
                    direction.normalized * Mathf.Min(controls.Speed, (distance - _forcedTauntStopDistance) / step);
            }
            else if (_serverForcedMotion.HasInput(now))
                transform.rotation = Quaternion.Slerp(transform.rotation, _serverInputRotation, rotationSpeed * step);
            if (TryGetComponent<ExpandedSkillController>(out var skillControl)) transform.rotation = skillControl.RestrictRotation(transform.rotation);
            MoveBallistic(forcedVelocity + voluntaryVelocity, step);
            remainingStep -= step;
        }
        CommitServerMovementPose(now);
        if (_serverForcedMotion.HasFinished && finishWhenComplete)
        {
            FinishServerForcedMovement(now);
            return;
        }
        if (now >= _nextForcedPoseAt)
        {
            _nextForcedPoseAt = now + Mathf.Max(0.01f, _transformSyncInterval);
            RpcServerMotionPose(transform.position, transform.rotation, now, _movementEpoch);
        }
    }

    private void CommitServerMovementPose(double now)
    {
        _serverMovement.CommitServerMove(transform.position, now,
            controller != null && controller.isGrounded, velocityY);
        GetComponent<ServerPoseHistory>()?.RecordNetworkPose(now, transform.position, transform.rotation);
    }

    private void FinishServerForcedMovement(double now)
    {
        _serverForcedMotion.Cancel();
        if (_forcedTauntActive && _healthSystem != null && !_healthSystem.IsDead) return;
        _serverMotionActive = false;
        _networkLocomotionState = 0;
        _remoteLocomotionTarget = Vector2.zero;
        _hasSentLocomotionState = false;
        _movementEpoch++;
        _serverMovement.Rebase(transform.position, now, controller != null && controller.isGrounded);
        CommitServerMovementPose(now);
        _remoteTransformSnapshots.Clear();
        _hasRemoteTransformTarget = false;
        if (NetworkServer.active && netId != 0u)
            RpcServerMotionState(false, transform.position, transform.rotation, velocityY, _movementEpoch, now);
    }

    [ClientRpc]
    private void RpcServerMotionState(bool active, Vector3 position, Quaternion rotation, float verticalSpeed, uint epoch, double sampleTime)
        => ReceiveServerMotionState(active, position, rotation, verticalSpeed, epoch, sampleTime);

    [TargetRpc]
    private void TargetRestoreServerMotion(NetworkConnection target, bool active, Vector3 position,
        Quaternion rotation, float verticalSpeed, uint epoch, double sampleTime)
        => ReceiveServerMotionState(active, position, rotation, verticalSpeed, epoch, sampleTime);

    private void ReceiveServerMotionState(bool active, Vector3 position, Quaternion rotation, float verticalSpeed, uint epoch, double sampleTime)
    {
        if (isServer) return;
        _movementEpoch = epoch;
        _serverMotionActive = active;
        _ownerServerMotionActive = active;
        _forcedJumpSequence = 0u;
        _forcedInputEpoch = epoch;
        _nextForcedInputAt = 0d;
        velocityY = verticalSpeed;
        StopPredictedForcedMovement();
        _remoteTransformSnapshots.Clear();
        _latestRemoteSnapshotTime = double.NegativeInfinity;
        _hasRemoteTransformTarget = false;
        _hasSentReliableTransform = false;
        ApplyRemotePose(position, rotation);
        if (active) AddRemoteTransformSnapshot(position, rotation, sampleTime);
        else _hasSentLocomotionState = false;
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcServerMotionPose(Vector3 position, Quaternion rotation, double sampleTime, uint epoch)
    {
        if (isServer || !_serverMotionActive || epoch != _movementEpoch) return;
        AddRemoteTransformSnapshot(position, rotation, sampleTime);
    }

    private void SmoothServerControlledOwner()
    {
        if (!_hasRemoteTransformTarget) return;
        // Owners follow the newest collision result without the observer's deliberate interpolation delay.
        float amount = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, _transformSyncInterval * 0.5f));
        ApplyRemotePose(Vector3.Lerp(transform.position, _remoteTargetPosition, amount),
            Quaternion.Slerp(transform.rotation, _remoteTargetRotation, amount));
    }

    private void UpdateLocalLocomotion(Vector2 locomotion)
    {
        if (!ShouldHandleLocalInput)
            return;

        Vector2 sanitized = SanitizeLocomotion(locomotion);
        ApplyLocomotionAnimation(sanitized, false);
        TrySyncLocomotionState(sanitized);
    }

    private void TrySyncLocomotionState(Vector2 locomotion)
    {
        if (!isClient || !NetworkClient.active || !NetworkClient.ready || !isOwned)
            return;

        ushort packedState = PackLocomotion(locomotion);
        bool movingStateChanged = IsMovingState(packedState) != IsMovingState(_lastSentLocomotionState);
        bool directionChanged = QuantizedAxisDelta(packedState, _lastSentLocomotionState) >= _locomotionQuantizedChangeThreshold;

        if (_hasSentLocomotionState && !movingStateChanged && !directionChanged)
            return;

        if (_hasSentLocomotionState && !movingStateChanged && Time.time < _nextLocomotionSyncTime)
            return;

        _hasSentLocomotionState = true;
        _lastSentLocomotionState = packedState;
        _nextLocomotionSyncTime = Time.time + 1f / Mathf.Max(1f, _locomotionSyncRate);
        CmdSetLocomotionState(packedState);
    }

    [Command]
    private void CmdSetLocomotionState(ushort packedState)
    {
        if (_forcedTauntActive) return;
        SetServerLocomotionState(packedState);
    }

    [Server]
    private void SetServerLocomotionState(ushort packedState)
    {
        if (_networkLocomotionState == packedState)
            return;

        _networkLocomotionState = packedState;

        if (isClient && !isLocalPlayer)
            _remoteLocomotionTarget = UnpackLocomotion(packedState);
    }

    private void OnNetworkLocomotionStateChanged(ushort oldState, ushort newState)
    {
        if (!isLocalPlayer)
            _remoteLocomotionTarget = UnpackLocomotion(newState);
    }

    private Vector2 SanitizeLocomotion(Vector2 locomotion)
    {
        if (locomotion.sqrMagnitude <= _locomotionInputDeadzone * _locomotionInputDeadzone)
            return Vector2.zero;

        return Vector2.ClampMagnitude(locomotion, 1f);
    }

    private static ushort PackLocomotion(Vector2 locomotion)
    {
        int xValue = Mathf.Clamp(Mathf.RoundToInt(locomotion.x * 127f), -127, 127);
        int yValue = Mathf.Clamp(Mathf.RoundToInt(locomotion.y * 127f), -127, 127);
        byte x = unchecked((byte)(sbyte)xValue);
        byte y = unchecked((byte)(sbyte)yValue);
        return (ushort)(x | (y << 8));
    }

    private static Vector2 UnpackLocomotion(ushort packedState)
    {
        sbyte x = unchecked((sbyte)(byte)(packedState & 0xFF));
        sbyte y = unchecked((sbyte)(byte)(packedState >> 8));
        return new Vector2(x / 127f, y / 127f);
    }

    private static bool IsMovingState(ushort packedState)
    {
        return packedState != 0;
    }

    private static int QuantizedAxisDelta(ushort current, ushort previous)
    {
        sbyte currentX = unchecked((sbyte)(byte)(current & 0xFF));
        sbyte currentY = unchecked((sbyte)(byte)(current >> 8));
        sbyte previousX = unchecked((sbyte)(byte)(previous & 0xFF));
        sbyte previousY = unchecked((sbyte)(byte)(previous >> 8));
        return Mathf.Max(Mathf.Abs(currentX - previousX), Mathf.Abs(currentY - previousY));
    }

    private void ApplyLocomotionAnimation(Vector2 locomotion, bool isRemote)
    {
        if (animator == null)
            return;

        float speed = Mathf.Clamp01(locomotion.magnitude);
        float dampTime = isRemote ? _remoteLocomotionDampTime : _localLocomotionDampTime;
        if (dampTime > 0f)
        {
            animator.SetFloat(speedHash, speed, dampTime, Time.deltaTime);
            animator.SetFloat(moveXHash, locomotion.x, dampTime, Time.deltaTime);
            animator.SetFloat(moveYHash, locomotion.y, dampTime, Time.deltaTime);
            return;
        }

        animator.SetFloat(speedHash, speed);
        animator.SetFloat(moveXHash, locomotion.x);
        animator.SetFloat(moveYHash, locomotion.y);
    }

    private void TrySyncTransform()
    {
        if (!isLocalPlayer || !isClient || IsFollowingServerMotion)
            return;

        float positionDeltaSqr = (_lastSentPosition - transform.position).sqrMagnitude;
        float rotationDelta = Quaternion.Angle(_lastSentRotation, transform.rotation);
        bool positionChanged = positionDeltaSqr >= 0.0001f;
        bool rotationChanged = rotationDelta >= 0.5f;
        bool reliablePositionChanged = (_lastReliableSentPosition - transform.position).sqrMagnitude >= 0.0001f;
        bool reliableRotationChanged = Quaternion.Angle(_lastReliableSentRotation, transform.rotation) >= 0.5f;
        bool reliableKeyframeDue =
            Time.time >= _nextReliableTransformSyncTime &&
            (!_hasSentReliableTransform || reliablePositionChanged || reliableRotationChanged);

        if (reliableKeyframeDue)
        {
            RecordSentTransform(true);
            CmdSyncTransformReliable(transform.position, transform.rotation, NetworkTime.time, _movementEpoch);
            return;
        }

        if (Time.time < _nextTransformSyncTime || (!positionChanged && !rotationChanged))
            return;

        if (!positionChanged && Time.time < _nextRotationOnlySyncTime)
            return;

        if (!positionChanged)
            _nextRotationOnlySyncTime = Time.time + Mathf.Max(_transformSyncInterval, _rotationOnlyTransformSyncInterval);
        RecordSentTransform(false);
        CmdSyncTransform(transform.position, transform.rotation, NetworkTime.time, _movementEpoch);
    }

    private void ForceSyncTransform()
    {
        if (!isLocalPlayer || !isClient || IsFollowingServerMotion)
            return;

        RecordSentTransform(true);
        CmdSyncTransformReliable(transform.position, transform.rotation, NetworkTime.time, _movementEpoch);
    }

    private void RecordSentTransform(bool reliable)
    {
        _nextTransformSyncTime = Time.time + _transformSyncInterval;
        _lastSentPosition = transform.position;
        _lastSentRotation = transform.rotation;

        if (!reliable)
            return;

        _hasSentReliableTransform = true;
        _nextReliableTransformSyncTime = Time.time + Mathf.Max(0.1f, _reliableTransformKeyframeInterval);
        _lastReliableSentPosition = transform.position;
        _lastReliableSentRotation = transform.rotation;
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdSyncTransform(Vector3 position, Quaternion rotation, double sampleTime, uint epoch)
    {
        if (_serverMotionActive || !_serverForcedMotion.AcceptsOwnerPose(epoch, _movementEpoch)) return;
        ApplySyncedTransformOnServer(position, rotation, sampleTime, false);
    }

    [Command]
    private void CmdSyncTransformReliable(Vector3 position, Quaternion rotation, double sampleTime, uint epoch)
    {
        if (_serverMotionActive || !_serverForcedMotion.AcceptsOwnerPose(epoch, _movementEpoch)) return;
        ApplySyncedTransformOnServer(position, rotation, sampleTime, true);
    }

    [Server]
    private void ApplySyncedTransformOnServer(
        Vector3 position,
        Quaternion rotation,
        double sampleTime,
        bool reliable)
    {
        double serverTime = NetworkTime.time;
        if (!CombatValidation.IsFinite(position) || !ServerMovementValidator.IsValidRotation(rotation) ||
            !double.IsFinite(sampleTime)) return;
        if (TryGetComponent<ExpandedSkillController>(out var skillControl)) rotation = skillControl.RestrictRotation(rotation);
        if (!isLocalPlayer)
        {
            // Reliable and unreliable snapshots may arrive out of order.
            if (sampleTime <= _serverMovement.LastSampleTime) return;
            RecordServerMovementControls();
            if (!_serverControlHistory.TrySample(sampleTime, out MovementControlState controls)) return;
            float maximumSpeed = controls.Speed;
            bool dead = _healthSystem != null && _healthSystem.IsDead;
            bool blocked = dead || IsBattleLoadingOrNotStarted() ||
                (_statManager != null && !_statManager.HasServerStats);
            if ((blocked && (position - _serverMovement.Position).sqrMagnitude > 0.0025f) ||
                !IsServerMovementPathClear(_serverMovement.Position, position) ||
                !_serverMovement.TryAccept(position, rotation, sampleTime, serverTime,
                    blocked ? 0f : maximumSpeed * 1.05f, jumpHeight, gravity,
                    IsServerPositionGrounded(position), controller != null ? controller.slopeLimit : 45f,
                    controls.MoveLocked, controls.JumpLocked, controls.JumpLockedSince,
                    controls.DistanceBeforeMoveLock(_serverMovement.LastPositionTime, sampleTime) * 1.05f))
            {
                if (serverTime >= _nextMovementCorrectionAt && connectionToClient != null)
                {
                    _nextMovementCorrectionAt = serverTime + 0.2d;
                    TargetCorrectMovement(connectionToClient, _serverMovement.Position, transform.rotation, _movementEpoch);
                }
                return;
            }
        }
        double acceptedSampleTime = Math.Clamp(sampleTime, serverTime - 0.5d, serverTime + 0.05d);
        rotation = rotation.normalized;
        GetComponent<ServerPoseHistory>()?.RecordNetworkPose(acceptedSampleTime, position, rotation);

        if (!isLocalPlayer)
        {
            if (isClient)
                AddRemoteTransformSnapshot(position, rotation, acceptedSampleTime);
            else
                ApplyRemotePose(position, rotation);
        }

        if (reliable)
            RpcSyncTransformReliable(position, rotation, acceptedSampleTime, _movementEpoch);
        else
            RpcSyncTransform(position, rotation, acceptedSampleTime, _movementEpoch);
    }

    private MovementControlState RecordServerMovementControls()
    {
        if (!isServer) return default;
        double now = NetworkTime.time;
        SkillInputLockFlags flags = _inputLocks.Evaluate(now) | (GetComponent<ExpandedSkillController>()?.ControlFlags ?? SkillInputLockFlags.None);
        bool moveLocked = (flags & SkillInputLockFlags.Move) != 0 || IsEmoteBlockingMovement;
        bool jumpLocked = _forcedTauntActive || moveLocked || (flags & SkillInputLockFlags.Jump) != 0 || IsEmoteBlockingJump;
        bool crouchLocked = _forcedTauntActive || moveLocked || (flags & SkillInputLockFlags.Crouch) != 0 || IsEmoteBlockingJump;
        float speed = moveSpeed * _movementEffects.Evaluate(now);
        if (isAttacking) speed *= 0.6f;
        if (isCrouching) speed *= Mathf.Max(0f, crouchSpeedMultiplier);
        if (moveLocked) speed = 0f;
        var state = new MovementControlState(speed, moveLocked, jumpLocked, crouchLocked);
        _serverControlHistory.Record(now, state);
        return state;
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcSyncTransform(Vector3 position, Quaternion rotation, double sampleTime, uint epoch)
    {
        if (isServer || isLocalPlayer || epoch != _movementEpoch)
            return;

        AddRemoteTransformSnapshot(position, rotation, sampleTime);
    }

    private bool IsServerPositionGrounded(Vector3 position)
    {
        if (controller == null) return false;
        float scale = Mathf.Abs(transform.lossyScale.y);
        Vector3 center = position + Vector3.Scale(controller.center, transform.lossyScale);
        float distance = controller.height * scale * 0.5f + Mathf.Max(0.05f, _groundSnapDistance);
        int count = _movementQuery.Raycast(center, Vector3.down, distance);
        for (int i = 0; i < count; i++)
            if (!_movementQuery.Hits[i].transform.IsChildOf(transform) && _movementQuery.Hits[i].normal.y >= 0.5f)
                return true;
        return false;
    }

    private bool IsServerMovementPathClear(Vector3 from, Vector3 to)
    {
        if (controller == null) return false;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.001f) return true;
        float scale = Mathf.Abs(transform.lossyScale.y);
        float radius = Mathf.Max(0.05f, controller.radius * scale - controller.skinWidth);
        Vector3 center = from + Vector3.Scale(controller.center, transform.lossyScale);
        float halfSegment = Mathf.Max(0f, controller.height * scale * 0.5f - radius);
        Vector3 bottom = center - Vector3.up * halfSegment + Vector3.up * Mathf.Min(halfSegment, controller.stepOffset * scale);
        Vector3 top = center + Vector3.up * halfSegment;
        int count = _movementQuery.CapsuleCast(bottom, top, radius, delta / distance, distance);
        for (int i = 0; i < count; i++)
            if (!_movementQuery.Hits[i].transform.IsChildOf(transform)) return false;
        return true;
    }

    [TargetRpc]
    private void TargetCorrectMovement(NetworkConnection target, Vector3 position, Quaternion rotation, uint epoch)
    {
        if (epoch != _movementEpoch) return;
        ApplyRemotePose(position, rotation);
        velocityY = 0f;
        _hasSentReliableTransform = false;
    }

    [Server]
    public bool ServerTeleportToSpawn()
    {
        var starts = NetworkManager.startPositions;
        if (starts == null || starts.Count == 0) return false;
        Transform start = starts[UnityEngine.Random.Range(0, starts.Count)];
        return start != null && ServerTeleport(start.position, start.rotation);
    }

    [Server]
    public bool ServerTeleport(Vector3 position, Quaternion rotation)
    {
        if (!CombatValidation.IsFinite(position) || !ServerMovementValidator.IsValidRotation(rotation))
            return false;
        ApplyAuthoritativeTeleport(position, rotation.normalized);
        RpcServerTeleport(position, rotation.normalized, _movementEpoch);
        return true;
    }

    private void ApplyAuthoritativeTeleport(Vector3 position, Quaternion rotation)
    {
        _movementEpoch++;
        _movementEffects.Clear();
        ApplyServerTeleport(position, rotation, _movementEpoch);
        // Cancelling an active force commits its old collision pose; the teleport owns the final reset.
        _serverMovement.Reset(position, NetworkTime.time);
        _serverControlHistory.Clear();
        RecordServerMovementControls();
        GetComponent<ServerPoseHistory>()?.ResetHistory();
    }

    [ClientRpc]
    private void RpcServerTeleport(Vector3 position, Quaternion rotation, uint epoch)
    {
        if (!isServer) ApplyServerTeleport(position, rotation, epoch);
    }

    private void ApplyServerTeleport(Vector3 position, Quaternion rotation, uint epoch)
    {
        _movementEpoch = epoch;
        CancelMovementActions();
        _remoteTransformSnapshots.Clear();
        _hasRemoteTransformTarget = false;
        _latestRemoteSnapshotTime = double.NegativeInfinity;
        _hasSentReliableTransform = false;
        velocityY = 0f;
        ApplyRemotePose(position, rotation);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcSyncTransformReliable(Vector3 position, Quaternion rotation, double sampleTime, uint epoch)
    {
        if (isServer || isLocalPlayer || epoch != _movementEpoch)
            return;

        AddRemoteTransformSnapshot(position, rotation, sampleTime);
    }

    private void AddRemoteTransformSnapshot(Vector3 position, Quaternion rotation, double sampleTime)
    {
        if (double.IsNaN(sampleTime) || double.IsInfinity(sampleTime) || sampleTime <= _latestRemoteSnapshotTime)
            return;

        UpdateRemoteDelayEstimate(sampleTime);

        bool shouldSnap = !_hasRemoteTransformTarget ||
                          Vector3.Distance(_remoteTargetPosition, position) > _remoteSnapDistance;
        if (shouldSnap)
        {
            _remoteTransformSnapshots.Clear();
            ApplyRemotePose(position, rotation);
        }

        _remoteTargetPosition = position;
        _remoteTargetRotation = rotation;
        _hasRemoteTransformTarget = true;
        _latestRemoteSnapshotTime = sampleTime;
        _remoteTransformSnapshots.Add(new RemoteTransformSnapshot
        {
            Time = sampleTime,
            Position = position,
            Rotation = rotation
        });

        int maxSnapshots = Mathf.Clamp(_remoteSnapshotBufferSize, 4, 64);
        _remoteTransformSnapshots.TrimToCount(maxSnapshots);
    }

    private void SmoothRemoteTransform()
    {
        if (!_hasRemoteTransformTarget || !isClient || _remoteTransformSnapshots.Count == 0)
            return;

        float minimumDelay = Mathf.Max(0f, _remoteInterpolationBackTime);
        float maximumDelay = Mathf.Max(minimumDelay, _remoteMaxInterpolationBackTime);
        float targetDelay = minimumDelay;
        if (_hasRemoteDelayEstimate)
        {
            targetDelay = Mathf.Clamp(
                (float)(_remoteSnapshotDelayEstimate + 2d * _remoteSnapshotDelayDeviation) + _remoteJitterMargin,
                minimumDelay,
                maximumDelay);
        }

        if (_remoteRenderDelay <= 0f)
            _remoteRenderDelay = targetDelay;
        else
            _remoteRenderDelay = Mathf.MoveTowards(
                _remoteRenderDelay,
                targetDelay,
                Time.deltaTime * (targetDelay > _remoteRenderDelay ? 0.5f : 0.05f));

        double renderTime = NetworkTime.time - _remoteRenderDelay;
        while (_remoteTransformSnapshots.Count >= 3 && _remoteTransformSnapshots[1].Time <= renderTime)
            _remoteTransformSnapshots.RemoveFirst();

        RemoteTransformSnapshot from = _remoteTransformSnapshots[0];
        Vector3 nextPosition = from.Position;
        Quaternion nextRotation = from.Rotation;

        if (_remoteTransformSnapshots.Count >= 2)
        {
            RemoteTransformSnapshot to = _remoteTransformSnapshots[1];
            double duration = Math.Max(0.0001d, to.Time - from.Time);

            if (renderTime <= to.Time)
            {
                float t = Mathf.Clamp01((float)((renderTime - from.Time) / duration));
                nextPosition = Vector3.Lerp(from.Position, to.Position, t);
                nextRotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
            }
            else
            {
                nextPosition = to.Position;
                nextRotation = to.Rotation;

                if (_remoteLocomotionTarget.sqrMagnitude > 0.001f && _remoteExtrapolationLimit > 0f)
                {
                    float extrapolationSeconds = Mathf.Min(
                        (float)(renderTime - to.Time),
                        _remoteExtrapolationLimit);
                    Vector3 velocity = (to.Position - from.Position) / (float)duration;
                    nextPosition += velocity * extrapolationSeconds;
                }
            }
        }

        ApplyRemotePose(nextPosition, nextRotation);
    }

    private void UpdateRemoteDelayEstimate(double sampleTime)
    {
        double observedDelay = Math.Max(0d, NetworkTime.time - sampleTime);
        if (!_hasRemoteDelayEstimate)
        {
            _remoteSnapshotDelayEstimate = observedDelay;
            _remoteSnapshotDelayDeviation = 0d;
            _hasRemoteDelayEstimate = true;
            return;
        }

        const double smoothing = 0.1d;
        double difference = observedDelay - _remoteSnapshotDelayEstimate;
        _remoteSnapshotDelayEstimate += difference * smoothing;
        _remoteSnapshotDelayDeviation += (Math.Abs(difference) - _remoteSnapshotDelayDeviation) * smoothing;
    }

    private void ApplyRemotePose(Vector3 position, Quaternion rotation)
    {
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
            controller.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    private void UpdateRemoteLocomotionAnimation()
    {
        Vector2 locomotion = _healthSystem != null && _healthSystem.IsDead
            ? Vector2.zero
            : _remoteLocomotionTarget;
        ApplyLocomotionAnimation(locomotion, true);
    }

    private void HandleDeath()
    {
        CancelMovementActions();
        PlayDeathVisual();
        BeginLocalDeath();
    }

    private void HandleRevived()
    {
        CancelMovementActions();
        PlayReviveVisual();
        if (!isLocalPlayer) return;
        if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
        _respawnRoutine = null;
        _respawnCountdown = default;
        isDead = false;
        SetCrouchState(false, true);
        if (controller != null) controller.enabled = true;
        if (_hasMatchEndPresentation)
        {
            _lifePresentation.ShowMatchEnd(_matchEndWinnerTarget, _isMatchWinner);
            GameInputController.RefreshCursorState();
            return;
        }
        _lifePresentation.ShowLocalRevived();
        ResetLocalInputForPlayMode();
    }

    private void RestoreLifePresentation()
    {
        if (_lifePresentation == null) return;
        bool healthIsDead = _healthSystem != null && _healthSystem.IsDead;
        if (healthIsDead) PlayDeathVisual();
        else if (_healthSystem != null) PlayReviveVisual();

        if (!isLocalPlayer) return;
        if (_hasMatchEndPresentation)
        {
            _lifePresentation.ShowMatchEnd(_matchEndWinnerTarget, _isMatchWinner);
            return;
        }
        if (!healthIsDead)
        {
            if (isDead) HandleRevived();
            else _lifePresentation.ShowLocalRevived();
            return;
        }
        if (!isDead)
        {
            BeginLocalDeath();
            return;
        }

        // Re-enabling never starts a second death or restarts an existing five-second wait.
        if (!_respawnCountdown.HasStarted) _respawnCountdown = CreateRespawnCountdown();
        if (controller != null) controller.enabled = false;
        _lifePresentation.BeginLocalDeath(_respawnCountdown);
        if (_respawnRoutine == null) _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    [Server]
    public void NotifyDeathFromServer()
    {
        PlayDeathVisual();
        RpcPlayDeathVisual();

        if (connectionToClient != null)
            TargetBeginDeath(connectionToClient, _healthSystem != null ? _healthSystem.ReviveAllowedAt : NetworkTime.time + HealthUiLifeRules.RespawnDelaySeconds);
    }

    [TargetRpc]
    private void TargetBeginDeath(NetworkConnection target, double readyAt)
    {
        PlayDeathVisual();
        BeginLocalDeath(readyAt);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlayDeathVisual()
    {
        PlayDeathVisual();
    }

    private void PlayDeathVisual() => _lifePresentation?.PlayDeathAnimation();

    public void PlayReviveVisual() => _lifePresentation?.PlayReviveAnimation();

    [ClientRpc(includeOwner = false)]
    public void RpcPlayReviveVisual()
    {
        PlayReviveVisual();
    }

    private PlayerRespawnCountdown CreateRespawnCountdown(double readyAt = double.NaN)
    {
        if (!NetworkClient.active && !NetworkServer.active) return new PlayerRespawnCountdown(Time.timeAsDouble);
        double deadline = double.IsFinite(readyAt) ? readyAt : _healthSystem != null ? _healthSystem.ReviveAllowedAt : NetworkTime.time + HealthUiLifeRules.RespawnDelaySeconds;
        double receivedServerTime = !isServer && NetworkClient.connection != null
            ? NetworkClient.connection.remoteTimeStamp : double.NaN;
        return PlayerRespawnCountdown.FromServerDeadline(Time.timeAsDouble, NetworkTime.time, deadline, receivedServerTime);
    }

    private void BeginLocalDeath(double readyAt = double.NaN)
    {
        if (!isLocalPlayer) return;
        if (isDead)
        {
            if (double.IsFinite(readyAt))
            {
                _respawnCountdown = CreateRespawnCountdown(readyAt);
                _lifePresentation.BeginLocalDeath(_respawnCountdown);
            }
            return;
        }

        isDead = true;
        inputVector = Vector2.zero;
        UpdateLocalLocomotion(Vector2.zero);
        isAttacking = false;
        StopEmote(_activeEmote);
        CancelMovementActions();
        SetCrouchState(false, true);

        if (controller != null) controller.enabled = false;

        _respawnCountdown = CreateRespawnCountdown(readyAt);
        if (!isActiveAndEnabled) return;
        _lifePresentation.BeginLocalDeath(_respawnCountdown);
        if (_respawnRoutine != null)
            StopCoroutine(_respawnRoutine);
        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // 연출은 별도 수명으로 진행한다. 여기서는 입력과 승인 요청만 처리한다.
        yield return null;
        while (isDead)
        {
            if (!GameInputController.IsTextInputActive && _respawnCountdown.IsReady(Time.timeAsDouble) &&
                Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                // 위치와 생존 상태는 서버 승인 시에만 바뀐다. 거절 후에는 다시 Space로 요청할 수 있다.
                _healthSystem?.RequestRevive(1f);
                yield return new WaitForSeconds(0.5f);
            }
            else
                yield return null;
        }
    }

    public void EnterMatchEndMode(Transform winnerTarget, bool isWinner)
    {
        if (!isLocalPlayer) return;

        _matchEndLocked = !isWinner;
        inputVector = Vector2.zero;
        UpdateLocalLocomotion(Vector2.zero);
        isAttacking = false;
        isDead = false;
        StopEmote(_activeEmote);
        CancelMovementActions();
        SetCrouchState(false, true);

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        if (controller != null) controller.enabled = true;

        if (_healthSystem != null)
        {
            _healthSystem.RefreshFromStats(keepCurrentHpFlat: false);
            _healthSystem.Revive(1f);
        }

        if (followCamera == null)
            followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
        _hasMatchEndPresentation = true;
        _matchEndWinnerTarget = winnerTarget;
        _isMatchWinner = isWinner;
        _respawnCountdown = default;
        _lifePresentation.AttachCamera(followCamera);
        _lifePresentation.ShowMatchEnd(winnerTarget, isWinner);

        GameInputController.RefreshCursorState();
    }
}
