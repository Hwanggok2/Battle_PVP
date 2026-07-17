using UnityEngine;
using UnityEngine.InputSystem; // 신형 시스템 네임스페이스
using BattlePvp.Stats;
using BattlePvp.Combat;
using System;
using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private float gravity = 9.81f; // 중력 값
    [SerializeField] private float jumpHeight = 1.4f;
    [SerializeField] private float rotationSpeed = 10.0f; // 회전 속도
    [SerializeField] private float _transformSyncInterval = 0.033f;
    [SerializeField] private float _rotationOnlyTransformSyncInterval = 0.1f;
    [SerializeField] private float _jumpBufferSeconds = 0.15f;
    [SerializeField] private float _coyoteTimeSeconds = 0.08f;
    [SerializeField] private float _groundSnapDistance = 0.2f;

    [Header("Remote Movement Smoothing")]
    [SerializeField, Range(0.03f, 0.25f)] private float _remoteInterpolationBackTime = 0.1f;
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
    private Coroutine _respawnRoutine;
    private float _nextTransformSyncTime;
    private float _nextRotationOnlySyncTime;
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation;
    private Vector3 _remoteTargetPosition;
    private Quaternion _remoteTargetRotation;
    private bool _hasRemoteTransformTarget;
    private readonly List<RemoteTransformSnapshot> _remoteTransformSnapshots = new List<RemoteTransformSnapshot>(32);
    private double _latestRemoteSnapshotTime = double.NegativeInfinity;
    private Vector2 _remoteLocomotionTarget;
    private ushort _lastSentLocomotionState;
    private bool _hasSentLocomotionState;
    private float _nextLocomotionSyncTime;
    private float _standingControllerHeight;
    private Vector3 _standingControllerCenter;
    private float _skillMoveMultiplier = 1f;
    private double _skillMoveMultiplierUntil;
    private Coroutine _forcedMoveRoutine;
    private bool _skillMovementLocked;
    private SkillInputLockFlags _skillInputLockFlags;
    private double _skillInputLockUntil;
    private double _jumpRequestedUntil;
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
    public bool IsCrouching => isCrouching;
    public bool IsEmoteBlockingAttack => _activeEmote != null && _activeEmote.LockAttack;
    public bool IsEmoteBlockingMovement => _activeEmote != null && _activeEmote.LockMovement;
    public bool IsEmoteBlockingJump => _activeEmote != null && _activeEmote.LockJump;
    public bool IsSkillMoveLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Move);
    public bool IsSkillAttackLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Attack);
    public bool IsSkillJumpLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Jump);
    public bool IsSkillCrouchLocked => _forcedTauntActive || IsSkillInputLocked(SkillInputLockFlags.Crouch);
    private double MovementTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;
    private double LocalInputTime => Time.timeAsDouble;

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
        _skillMoveMultiplier = Mathf.Max(0f, multiplier);
        _skillMoveMultiplierUntil = MovementTime + Mathf.Max(0f, durationSeconds);
    }

    public void SetSkillMovementLock(bool locked)
    {
        bool wasLocked = _skillMovementLocked;
        _skillMovementLocked = locked;
        if (locked)
        {
            inputVector = Vector2.zero;
        }
        else if (wasLocked)
        {
            RefreshMoveInputFromCurrentAction();
        }
    }

    public void ApplySkillInputLock(SkillInputLockFlags flags, float durationSeconds)
    {
        _skillInputLockFlags = flags;
        _skillInputLockUntil = MovementTime + Mathf.Max(0f, durationSeconds);

        if ((flags & SkillInputLockFlags.Move) != 0)
            inputVector = Vector2.zero;
    }

    public void ClearSkillInputLock()
    {
        _skillInputLockFlags = SkillInputLockFlags.None;
        _skillInputLockUntil = 0d;
    }

    public bool IsSkillInputLocked(SkillInputLockFlags flag)
    {
        return (_skillInputLockFlags & flag) != 0 && MovementTime < _skillInputLockUntil;
    }

    public void SetForcedTauntControl(bool active, Vector3 targetPosition, float stopDistance)
    {
        bool wasActive = _forcedTauntActive;
        _forcedTauntActive = active;
        _forcedTauntTargetPosition = targetPosition;
        _forcedTauntStopDistance = Mathf.Max(0f, stopDistance);

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
        ApplySkillInputLock(emote.InputLockFlags, emote.ResolveDurationSeconds());

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
        ClearSkillInputLock();

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
            _audioSource.PlayOneShot(emote.UseSfx, Mathf.Clamp01(emote.SfxVolume));
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
        RpcPlayEmote(emoteIndex);
    }

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
        if (isClient && !isLocalPlayer)
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
        if (!isLocalPlayer)
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
            float step = Mathf.Min(Time.deltaTime, duration - elapsed);
            if (controller != null && controller.enabled)
                controller.Move(direction * speed * step);
            elapsed += step;
            yield return null;
        }
        _forcedMoveRoutine = null;
    }

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int moveXHash = Animator.StringToHash("MoveX");
    private readonly int moveYHash = Animator.StringToHash("MoveY");
    private readonly int dieHash = Animator.StringToHash("Die");
    private readonly int isDeadHash = Animator.StringToHash("IsDead");
    private readonly int movementStateHash = Animator.StringToHash("Movement");
    private readonly int isCrouchingHash = Animator.StringToHash("IsCrouching");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        _audioSource = GetComponent<AudioSource>();
        rb = GetComponent<Rigidbody>();
        _playerInput = GetComponent<PlayerInput>();
        _healthSystem = GetComponent<HealthSystem>();
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

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public override void OnStartLocalPlayer()
    {
        // 1. 카메라 컴포넌트 찾기
        followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
        if (followCamera != null)
        {
            followCamera.SetTarget(this.transform);
        }

        _lastSentLocomotionState = PackLocomotion(Vector2.zero);
        _hasSentLocomotionState = true;
        ApplyLocomotionAnimation(Vector2.zero, false);
        ResetLocalInputForPlayMode();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isLocalPlayer)
            _remoteLocomotionTarget = UnpackLocomotion(_networkLocomotionState);
    }

    private void OnEnable()
    {
        if (_statManager != null)
        {
            _statManager.StatsChanged += OnStatsChanged;
            UpdateMoveSpeed();
        }
        if (_healthSystem != null)
        {
            _healthSystem.OnDied += HandleDeath;
        }
    }

    private void OnDisable()
    {
        if (_statManager != null)
            _statManager.StatsChanged -= OnStatsChanged;
        
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= HandleDeath;
        }

        if (_emoteRoutine != null)
            StopCoroutine(_emoteRoutine);
        _emoteRoutine = null;
        _activeEmote = null;
        SetForcedTauntControl(false, Vector3.zero, 0f);
        ClearSkillInputLock();
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
    }

    // Input System 메시지 수신 (SendMessage 방식 또는 Player Input 컴포넌트 활용)
    public void OnMove(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (isDead || _matchEndLocked || _skillMovementLocked || IsSkillMoveLocked || IsEmoteBlockingMovement) { inputVector = Vector2.zero; return; }
        inputVector = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;
        QueueJumpRequest();
    }

    public void OnCrouch(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() || _skillMovementLocked || IsSkillCrouchLocked || IsEmoteBlockingJump) return;
        if (GameInputController.IsPaused || GameInputController.IsTextInputActive) return;

        SetCrouchState(!isCrouching, true);
    }

    private void Update()
    {
        if (!isLocalPlayer)
        {
            SmoothRemoteTransform();
            UpdateRemoteLocomotionAnimation();
            return;
        }

        // 사망 상태이거나 ESC 메뉴(Pause) 상태일 때는 이동 처리를 하지 않음
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() || GameInputController.IsPaused || GameInputController.IsTextInputActive)
        {
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
        if (_jumpRequestedUntil < now || !CanConsumeJumpRequest())
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
        isAttacking = isLocked;
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
        SetCrouchState(crouching, false);
        RpcSetCrouchState(crouching);
    }

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
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, cameraYaw, 0), rotationSpeed * Time.deltaTime);
        }
        else
        {
            // 카메라가 없을 경우 기존 월드 기준 이동 (폴백)
            moveDirection = new Vector3(inputVector.x, 0, inputVector.y).normalized;
            if (inputVector.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        // 3. 중력 처리
        bool groundedForJump = IsGroundedForJump();
        if (groundedForJump)
            _lastGroundedAt = LocalInputTime;

        if (!TryConsumeJumpRequest())
        {
            if (groundedForJump && velocityY < 0f)
                velocityY = -0.5f;
            else
                velocityY -= gravity * Time.deltaTime;
        }

        // 4. 최종 이동
        float currentMoveSpeed = moveSpeed;
        if (_skillMovementLocked)
            currentMoveSpeed = 0f;
        if (MovementTime >= _skillMoveMultiplierUntil)
            _skillMoveMultiplier = 1f;
        currentMoveSpeed *= _skillMoveMultiplier;
        if (isAttacking)
            currentMoveSpeed *= 0.6f;
        if (isCrouching)
            currentMoveSpeed *= crouchSpeedMultiplier;
        
        // Anti-Gliding: 입력이 없을 때는 0으로 고정
        if (isAttacking && inputVector.sqrMagnitude < 0.001f && !forcedTauntMoving)
        {
            currentMoveSpeed = 0f;
            if (rb != null) rb.linearVelocity = Vector3.zero; // Rigidbody가 있다면 명시적으로 0
        }

        Vector3 finalMove = (moveDirection * currentMoveSpeed) + (Vector3.up * velocityY);
        controller.Move(finalMove * Time.deltaTime);
        TrySyncTransform();

        // 5. 애니메이션 (사망 시 업데이트 중지)
        Vector2 locomotion = forcedTauntMoving ? Vector2.up : inputVector;
        if (isDead || currentMoveSpeed <= 0.001f || moveDirection.sqrMagnitude <= 0.001f)
            locomotion = Vector2.zero;

        UpdateLocalLocomotion(locomotion);
    }

    private void UpdateLocalLocomotion(Vector2 locomotion)
    {
        if (!isLocalPlayer)
            return;

        Vector2 sanitized = SanitizeLocomotion(locomotion);
        ApplyLocomotionAnimation(sanitized, false);
        TrySyncLocomotionState(sanitized);
    }

    private void TrySyncLocomotionState(Vector2 locomotion)
    {
        if (!isClient)
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
        if (!isLocalPlayer || !isClient || Time.time < _nextTransformSyncTime)
            return;

        float positionDeltaSqr = (_lastSentPosition - transform.position).sqrMagnitude;
        float rotationDelta = Quaternion.Angle(_lastSentRotation, transform.rotation);
        bool positionChanged = positionDeltaSqr >= 0.0001f;
        bool rotationChanged = rotationDelta >= 0.5f;

        if (!positionChanged && !rotationChanged)
            return;

        if (!positionChanged && Time.time < _nextRotationOnlySyncTime)
            return;

        _nextTransformSyncTime = Time.time + _transformSyncInterval;
        if (!positionChanged)
            _nextRotationOnlySyncTime = Time.time + Mathf.Max(_transformSyncInterval, _rotationOnlyTransformSyncInterval);
        _lastSentPosition = transform.position;
        _lastSentRotation = transform.rotation;
        CmdSyncTransform(transform.position, transform.rotation, NetworkTime.time);
    }

    private void ForceSyncTransform()
    {
        if (!isLocalPlayer || !isClient)
            return;

        _nextTransformSyncTime = Time.time + _transformSyncInterval;
        _lastSentPosition = transform.position;
        _lastSentRotation = transform.rotation;
        CmdSyncTransform(transform.position, transform.rotation, NetworkTime.time);
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdSyncTransform(Vector3 position, Quaternion rotation, double sampleTime)
    {
        double serverTime = NetworkTime.time;
        double acceptedSampleTime = Math.Clamp(sampleTime, serverTime - 0.5d, serverTime + 0.05d);
        GetComponent<ServerPoseHistory>()?.RecordNetworkPose(acceptedSampleTime, position);

        if (!isLocalPlayer)
        {
            if (isClient)
                AddRemoteTransformSnapshot(position, rotation, acceptedSampleTime);
            else
                ApplyRemotePose(position, rotation);
        }

        RpcSyncTransform(position, rotation, acceptedSampleTime);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcSyncTransform(Vector3 position, Quaternion rotation, double sampleTime)
    {
        if (isServer || isLocalPlayer)
            return;

        AddRemoteTransformSnapshot(position, rotation, sampleTime);
    }

    private void AddRemoteTransformSnapshot(Vector3 position, Quaternion rotation, double sampleTime)
    {
        if (double.IsNaN(sampleTime) || double.IsInfinity(sampleTime) || sampleTime <= _latestRemoteSnapshotTime)
            return;

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
        while (_remoteTransformSnapshots.Count > maxSnapshots)
            _remoteTransformSnapshots.RemoveAt(0);
    }

    private void SmoothRemoteTransform()
    {
        if (!_hasRemoteTransformTarget || !isClient || _remoteTransformSnapshots.Count == 0)
            return;

        double renderTime = NetworkTime.time - Mathf.Max(0f, _remoteInterpolationBackTime);
        while (_remoteTransformSnapshots.Count >= 3 && _remoteTransformSnapshots[1].Time <= renderTime)
            _remoteTransformSnapshots.RemoveAt(0);

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
        BeginLocalDeath();
    }

    [Server]
    public void NotifyDeathFromServer()
    {
        PlayDeathVisual();
        RpcPlayDeathVisual();

        if (connectionToClient != null)
            TargetBeginDeath(connectionToClient);
    }

    [TargetRpc]
    private void TargetBeginDeath(NetworkConnection target)
    {
        PlayDeathVisual();
        BeginLocalDeath();
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlayDeathVisual()
    {
        PlayDeathVisual();
    }

    private void PlayDeathVisual()
    {
        if (animator == null)
            return;

        animator.SetFloat(speedHash, 0f);
        animator.SetFloat(moveXHash, 0f);
        animator.SetFloat(moveYHash, 0f);
        animator.SetBool(isDeadHash, true);
        animator.ResetTrigger(dieHash);
        animator.SetTrigger(dieHash);
    }

    public void PlayReviveVisual()
    {
        if (animator == null)
            return;

        animator.ResetTrigger(dieHash);
        animator.SetBool(isDeadHash, false);
        animator.SetFloat(speedHash, 0f);
        animator.SetFloat(moveXHash, 0f);
        animator.SetFloat(moveYHash, 0f);
        animator.Play(movementStateHash, 0, 0f);
        animator.Update(0f);
    }

    [ClientRpc(includeOwner = false)]
    public void RpcPlayReviveVisual()
    {
        PlayReviveVisual();
    }

    private void BeginLocalDeath()
    {
        if (!isLocalPlayer) return;
        if (isDead) return;

        isDead = true;
        inputVector = Vector2.zero;
        UpdateLocalLocomotion(Vector2.zero);
        isAttacking = false;
        StopEmote(_activeEmote);
        ClearSkillInputLock();
        SetCrouchState(false, true);

        PlayDeathVisual();

        if (controller != null) controller.enabled = false;

        if (_respawnRoutine != null)
            StopCoroutine(_respawnRoutine);
        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // 1. 사망 즉시 5초 카운트다운 시작
        for (int i = 5; i > 0; i--)
        {
            BattlePvp.UI.PlayerHUD.UpdateLocalDeathOverlay(true, $"{i}", _deathOverlayTextColor);
            yield return new WaitForSeconds(1f);
        }

        // 2. 캐릭터 시각적/물리적 제거
        ToggleCharacterVisibility(false);

        BattlePvp.UI.PlayerHUD.UpdateLocalDeathOverlay(true, _respawnPromptText, _deathOverlayTextColor);

        // 3. Space 키 입력 대기
        bool keyPressed = false;
        while (!keyPressed)
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) keyPressed = true;
            yield return null;
        }

        // 5. 부활 및 랜덤 스폰 로직
        BattlePvp.UI.PlayerHUD.UpdateLocalDeathOverlay(false);

        // 위치 이동 (NetworkManager의 시작지점 활용)
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        var startPositions = Mirror.NetworkManager.startPositions;
        if (startPositions != null && startPositions.Count > 0)
        {
            Transform start = startPositions[UnityEngine.Random.Range(0, startPositions.Count)];
            spawnPos = start.position;
            spawnRot = start.rotation;
        }

        transform.SetPositionAndRotation(spawnPos, spawnRot);
        ForceSyncTransform();
        
        // 상태 복구
        isDead = false;
        SetCrouchState(false, true);
        ToggleCharacterVisibility(true);
        if (controller != null) controller.enabled = true;
        
        // 애니메이션 상태 강제 초기화
        PlayReviveVisual();

        // 카메라 및 입력을 게임 모드로 리셋
        if (followCamera != null)
        {
            followCamera.SetTarget(this.transform);
        }
        
        // [추가] 부활 시 강제로 게임 플레이 모드(커서 잠금 등)로 전환
        ResetLocalInputForPlayMode();
        
        if (_healthSystem != null)
        {
            _healthSystem.RefreshFromStats(keepCurrentHpFlat: false);
            _healthSystem.RequestRevive(1f);
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
        ClearSkillInputLock();
        SetCrouchState(false, true);

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        ToggleCharacterVisibility(true);
        if (controller != null) controller.enabled = true;

        if (animator != null)
        {
            PlayReviveVisual();
        }

        if (_healthSystem != null)
        {
            _healthSystem.RefreshFromStats(keepCurrentHpFlat: false);
            _healthSystem.Revive(1f);
        }

        BattlePvp.UI.PlayerHUD.UpdateLocalDeathOverlay(false);

        if (followCamera == null)
            followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();

        if (followCamera != null)
        {
            followCamera.SetTarget(isWinner ? transform : winnerTarget);
            followCamera.IsLocked = !isWinner;
        }

        Cursor.lockState = isWinner ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !isWinner;
    }

    private void ToggleCharacterVisibility(bool visible)
    {
        // 렌더러 비활성화 (자식 객체 포함)
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers) r.enabled = visible;

        // 충돌체 비활성화 (트리거 제외)
        var colliders = GetComponentsInChildren<Collider>();
        foreach (var c in colliders) c.enabled = visible;
        
        // CharacterController는 별도로 관리
        if (controller != null) controller.enabled = visible && !isDead;
    }
}
