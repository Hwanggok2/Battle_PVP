using UnityEngine;
using UnityEngine.InputSystem; // 신형 시스템 네임스페이스
using BattlePvp.Stats;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerManager : MonoBehaviour
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

    private readonly int speedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();

        // 카메라 컴포넌트 찾기
        followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
    }

    private void OnEnable()
    {
        if (_statManager != null)
        {
            _statManager.StatsChanged += OnStatsChanged;
            UpdateMoveSpeed();
        }
    }

    private void OnDisable()
    {
        if (_statManager != null)
            _statManager.StatsChanged -= OnStatsChanged;
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
        inputVector = value.Get<Vector2>();
    }

    private void Update()
    {
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

        // 5. 애니메이션 (속도 전달)
        animator.SetFloat(speedHash, inputVector.magnitude);
    }
}