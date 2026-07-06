using UnityEngine;
using UnityEngine.InputSystem; // 신형 시스템 네임스페이스
using BattlePvp.Stats;
using BattlePvp.Combat;
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
    [SerializeField] private float gravity = 9.81f; // 중력 값
    [SerializeField] private float jumpHeight = 1.4f;
    [SerializeField] private float rotationSpeed = 10.0f; // 회전 속도
    [SerializeField] private float _transformSyncInterval = 0.033f;

    [Header("Remote Movement Smoothing")]
    [SerializeField] private float _remotePositionLerpSpeed = 18f;
    [SerializeField] private float _remoteRotationLerpSpeed = 18f;
    [SerializeField] private float _remoteSnapDistance = 3f;

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

    private HealthSystem _healthSystem;
    private Coroutine _respawnRoutine;
    private float _nextTransformSyncTime;
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation;
    private Vector3 _remoteTargetPosition;
    private Quaternion _remoteTargetRotation;
    private bool _hasRemoteTransformTarget;
    private float _standingControllerHeight;
    private Vector3 _standingControllerCenter;
    private float _skillMoveMultiplier = 1f;
    private double _skillMoveMultiplierUntil;
    private Coroutine _forcedMoveRoutine;
    private bool _skillMovementLocked;
    private EmoteData _activeEmote;
    private Coroutine _emoteRoutine;

    public bool IsMatchEndLocked => _matchEndLocked;
    public bool IsCrouching => isCrouching;
    public bool IsEmoteBlockingAttack => _activeEmote != null && _activeEmote.LockAttack;
    public bool IsEmoteBlockingMovement => _activeEmote != null && _activeEmote.LockMovement;
    public bool IsEmoteBlockingJump => _activeEmote != null && _activeEmote.LockJump;
    private double MovementTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;

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
        if (emote.LockMovement)
            SetSkillMovementLock(true);

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

        if (active.LockMovement)
            SetSkillMovementLock(false);

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

        if (isDead || _matchEndLocked || GameInputController.IsPaused || GameInputController.IsTextInputActive || _skillMovementLocked)
        {
            inputVector = Vector2.zero;
            return;
        }

        if (_playerInput == null)
            _playerInput = GetComponent<PlayerInput>();

        InputAction moveAction = _playerInput != null ? _playerInput.actions.FindAction("Move", false) : null;
        inputVector = moveAction != null && moveAction.enabled ? moveAction.ReadValue<Vector2>() : Vector2.zero;
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
    }

    private void OnStatsChanged(StatContainer _)
    {
        if (this == null) return;
        UpdateMoveSpeed();
    }

    private void UpdateMoveSpeed()
    {
        if (_statManager == null) return;
        float agi = _statManager.GetFinalTotal(StatKind.AGI);
        moveSpeed = 3.0f + (agi * 0.04f);

        // Monostat 보너스/페널티 (기획안 반영)
        Identity id = _statManager.CurrentIdentity;
        if (id.Type == IdentityType.Monostat)
        {
            if (id.PrimaryStat == StatKind.AGI) moveSpeed *= 1.2f; // 민첩 몰빵: 이속 +20%
            else if (id.PrimaryStat == StatKind.STR) moveSpeed *= 0.75f; // 힘 몰빵: 이속 -25%
            else if (id.PrimaryStat == StatKind.DEF) moveSpeed *= 0.7f; // 방어 몰빵: 이속 -30%
        }
    }

    // Input System 메시지 수신 (SendMessage 방식 또는 Player Input 컴포넌트 활용)
    public void OnMove(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (isDead || _matchEndLocked || _skillMovementLocked) { inputVector = Vector2.zero; return; }
        inputVector = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() || _skillMovementLocked || IsEmoteBlockingJump) return;
        if (GameInputController.IsPaused || GameInputController.IsTextInputActive) return;
        if (controller == null || !controller.enabled || !controller.isGrounded) return;

        velocityY = Mathf.Sqrt(jumpHeight * 2f * gravity);
    }

    public void OnCrouch(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() || _skillMovementLocked || IsEmoteBlockingJump) return;
        if (GameInputController.IsPaused || GameInputController.IsTextInputActive) return;

        SetCrouchState(!isCrouching, true);
    }

    private void Update()
    {
        if (!isLocalPlayer)
        {
            SmoothRemoteTransform();
            return;
        }

        // 사망 상태이거나 ESC 메뉴(Pause) 상태일 때는 이동 처리를 하지 않음
        if (isDead || _matchEndLocked || IsBattleLoadingOrNotStarted() || GameInputController.IsPaused || GameInputController.IsTextInputActive) return;
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
        Vector3 moveDirection = Vector3.zero;
        if (followCamera != null)
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
        if (controller.isGrounded && velocityY < 0f)
            velocityY = -0.5f;
        else
            velocityY -= gravity * Time.deltaTime;

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
        if (isAttacking && inputVector.sqrMagnitude < 0.001f)
        {
            currentMoveSpeed = 0f;
            if (rb != null) rb.linearVelocity = Vector3.zero; // Rigidbody가 있다면 명시적으로 0
        }

        Vector3 finalMove = (moveDirection * currentMoveSpeed) + (Vector3.up * velocityY);
        controller.Move(finalMove * Time.deltaTime);
        TrySyncTransform();

        // 5. 애니메이션 (사망 시 업데이트 중지)
        if (!isDead)
        {
            animator.SetFloat(speedHash, inputVector.magnitude);
        }
    }

    private void TrySyncTransform()
    {
        if (!isLocalPlayer || !isClient || Time.time < _nextTransformSyncTime)
            return;

        if ((_lastSentPosition - transform.position).sqrMagnitude < 0.0001f &&
            Quaternion.Angle(_lastSentRotation, transform.rotation) < 0.5f)
            return;

        _nextTransformSyncTime = Time.time + _transformSyncInterval;
        _lastSentPosition = transform.position;
        _lastSentRotation = transform.rotation;
        CmdSyncTransform(transform.position, transform.rotation);
    }

    private void ForceSyncTransform()
    {
        if (!isLocalPlayer || !isClient)
            return;

        _nextTransformSyncTime = Time.time + _transformSyncInterval;
        _lastSentPosition = transform.position;
        _lastSentRotation = transform.rotation;
        CmdSyncTransform(transform.position, transform.rotation);
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdSyncTransform(Vector3 position, Quaternion rotation)
    {
        if (!isLocalPlayer)
            ApplySyncedTransform(position, rotation, false);

        RpcSyncTransform(position, rotation);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcSyncTransform(Vector3 position, Quaternion rotation)
    {
        if (isLocalPlayer)
            return;

        ApplySyncedTransform(position, rotation, true);
    }

    private void ApplySyncedTransform(Vector3 position, Quaternion rotation, bool interpolate)
    {
        if (interpolate && isClient && !isLocalPlayer)
        {
            SetRemoteTransformTarget(position, rotation);
            return;
        }

        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
            controller.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        if (controllerWasEnabled)
            controller.enabled = true;

        _remoteTargetPosition = position;
        _remoteTargetRotation = rotation;
        _hasRemoteTransformTarget = true;
    }

    private void SetRemoteTransformTarget(Vector3 position, Quaternion rotation)
    {
        _remoteTargetPosition = position;
        _remoteTargetRotation = rotation;
        _hasRemoteTransformTarget = true;

        if (Vector3.Distance(transform.position, position) > _remoteSnapDistance)
            ApplySyncedTransform(position, rotation, false);
    }

    private void SmoothRemoteTransform()
    {
        if (!_hasRemoteTransformTarget || !isClient)
            return;

        float positionT = 1f - Mathf.Exp(-Mathf.Max(0f, _remotePositionLerpSpeed) * Time.deltaTime);
        float rotationT = 1f - Mathf.Exp(-Mathf.Max(0f, _remoteRotationLerpSpeed) * Time.deltaTime);

        Vector3 nextPosition = Vector3.Lerp(transform.position, _remoteTargetPosition, positionT);
        Quaternion nextRotation = Quaternion.Slerp(transform.rotation, _remoteTargetRotation, rotationT);

        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
            controller.enabled = false;

        transform.SetPositionAndRotation(nextPosition, nextRotation);

        if (controllerWasEnabled)
            controller.enabled = true;
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
        isAttacking = false;
        StopEmote(_activeEmote);
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
        if (GameInputController.Instance != null)
        {
            GameInputController.Instance.ResetToPlayMode();
        }
        
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
        isAttacking = false;
        isDead = false;
        StopEmote(_activeEmote);
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
