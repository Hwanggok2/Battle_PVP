using UnityEngine;
using UnityEngine.InputSystem; // 신형 시스템 네임스페이스
using BattlePvp.Stats;
using BattlePvp.Combat;
using System.Collections;
using Mirror;
using BattlePvp.Logic; // 추가

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerManager : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private StatManager _statManager;
    [SerializeField] private float moveSpeed = 5.0f; // 기본 이동 속도
    [SerializeField] private float gravity = 9.81f; // 중력 값
    [SerializeField] private float rotationSpeed = 10.0f; // 회전 속도

    private CharacterController controller;
    private Animator animator;
    private Rigidbody rb; // Rigidbody 참조 추가 (요청사항 반영)
    private BattlePvp.CameraLogic.FollowCamera followCamera; // 카메라 참조 추가

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private Vector2 inputVector; // 신형 시스템에서 받을 Vector2 값
    [SerializeField] private float velocityY;
    [SerializeField] private bool isAttacking = false; // 현재 공격 중인지 여부
    [SerializeField] private bool isDead = false; // 사망 여부

    private HealthSystem _healthSystem;

    private readonly int speedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        _healthSystem = GetComponent<HealthSystem>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
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
        if (isDead) { inputVector = Vector2.zero; return; }
        inputVector = value.Get<Vector2>();
    }

    private void Update()
    {
        if (!isLocalPlayer) return;
        // 사망 상태이거나 ESC 메뉴(Pause) 상태일 때는 이동 처리를 하지 않음
        if (isDead || GameInputController.IsPaused) return;
        ApplyMovement();
    }

    // Animator의 Animation Event 등에서 호출하여 공격 상태를 알립니다.
    public void SetMovementLock(bool isLocked)
    {
        isAttacking = isLocked;
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

            // 2. 캐릭터 회전 (Core Task 4: 공격 중에는 회전 비활성화)
            if (!isAttacking)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, cameraYaw, 0), rotationSpeed * Time.deltaTime);
            }
        }
        else
        {
            // 카메라가 없을 경우 기존 월드 기준 이동 (폴백)
            moveDirection = new Vector3(inputVector.x, 0, inputVector.y).normalized;
            if (inputVector.sqrMagnitude > 0.001f && !isAttacking)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        // 3. 중력 처리
        if (controller.isGrounded)
            velocityY = -0.5f;
        else
            velocityY -= gravity * Time.deltaTime;

        // 4. 최종 이동
        float currentMoveSpeed = moveSpeed * (isAttacking ? 0.6f : 1.0f);
        
        // Anti-Gliding: 입력이 없을 때는 0으로 고정
        if (isAttacking && inputVector.sqrMagnitude < 0.001f)
        {
            currentMoveSpeed = 0f;
            if (rb != null) rb.linearVelocity = Vector3.zero; // Rigidbody가 있다면 명시적으로 0
        }

        Vector3 finalMove = (moveDirection * currentMoveSpeed) + (Vector3.up * velocityY);
        controller.Move(finalMove * Time.deltaTime);

        // 5. 애니메이션 (사망 시 업데이트 중지)
        if (!isDead)
        {
            animator.SetFloat(speedHash, inputVector.magnitude);
        }
    }

    private void HandleDeath()
    {
        if (!isLocalPlayer) return;
        if (isDead) return; // 중복 실행 방지
        
        isDead = true;
        inputVector = Vector2.zero;
        isAttacking = false;

        // 즉시 애니메이션 파라미터 초기화 및 사망 상태 설정
        animator.SetFloat(speedHash, 0f);
        animator.SetBool("IsDead", true); // Trigger 대신 Bool 사용 권장
        
        if (controller != null) controller.enabled = false;

        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // 1. Die 애니메이션 진행 기간 (3초 대기)
        if (BattlePvp.UI.PlayerHUD.Instance != null)
            BattlePvp.UI.PlayerHUD.Instance.UpdateDeathOverlay(true, "유다희...");
            
        yield return new WaitForSeconds(3f);

        // 2. 캐릭터 시각적/물리적 제거 (3초 후)
        ToggleCharacterVisibility(false);

        // 3. 부활 카운트다운 (5초)
        for (int i = 5; i > 0; i--)
        {
            if (BattlePvp.UI.PlayerHUD.Instance != null)
                BattlePvp.UI.PlayerHUD.Instance.UpdateDeathOverlay(true, $"부활 대기 중... {i}");
            yield return new WaitForSeconds(1f);
        }

        if (BattlePvp.UI.PlayerHUD.Instance != null)
            BattlePvp.UI.PlayerHUD.Instance.UpdateDeathOverlay(true, "스페이스바를 눌러 부활 (Space)");

        // 4. Space 키 입력 대기
        bool keyPressed = false;
        while (!keyPressed)
        {
            // 신형 Input System 방식 (스페이스바 감지)
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) keyPressed = true;

            // AnyKey를 누르면 스탯 분배 여부와 상관없이 즉시 부활 프로세스 진행
            yield return null;
        }

        // 5. 부활 및 랜덤 스폰 로직
        if (BattlePvp.UI.PlayerHUD.Instance != null)
            BattlePvp.UI.PlayerHUD.Instance.UpdateDeathOverlay(false);

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
        
        // 상태 복구
        isDead = false;
        ToggleCharacterVisibility(true);
        if (controller != null) controller.enabled = true;
        
        // 애니메이션 상태 강제 초기화
        animator.SetBool("IsDead", false);
        animator.SetFloat(speedHash, 0f);
        animator.Play("Idle", 0, 0f); 

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
            _healthSystem.Revive(1f);
        }
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