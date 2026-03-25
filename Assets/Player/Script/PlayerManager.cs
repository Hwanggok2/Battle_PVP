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

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private Vector2 inputVector; // 신형 시스템에서 받을 Vector2 값
    [SerializeField] private float velocityY;
    [SerializeField] private bool isAttacking = false; // 현재 공격 중인지 여부

    private readonly int speedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
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
    // 에디터에서 설정한 액션 이름이 "Move"라면 함수 이름은 "OnMove"가 됩니다.
    public void OnMove(InputValue value)
    {
        inputVector = value.Get<Vector2>();
    }

    private void Update()
    {
        // 더 이상 canMove로 리턴하지 않고 항상 이동 로직을 태웁니다.
        ApplyMovement();
    }

    // Animator의 Animation Event 등에서 호출하여 공격 상태를 알립니다.
    public void SetMovementLock(bool isLocked)
    {
        isAttacking = isLocked;

        // 공격 시작 시 미끄러짐 방지를 위해 입력을 초기화하고 싶다면 여기서 조절 가능합니다.
        // 유저 요청에 따라 공격 중에도 이동이 가능하므로, 굳이 zero로 만들지 않습니다.
    }

    private void ApplyMovement()
    {
        // 1. 이동 방향 계산 (Vector2를 Vector3 수평면으로 변환)
        Vector3 moveDirection = new Vector3(inputVector.x, 0, inputVector.y).normalized;

        // 2. 중력 처리
        if (controller.isGrounded)
            velocityY = -0.5f;
        else
            velocityY -= gravity * Time.deltaTime;

        // 3. 최종 이동
        // 공격 중이라면 이동 속도를 40% 감소 (-40% => * 0.6)
        float currentMoveSpeed = moveSpeed * (isAttacking ? 0.6f : 1.0f);
        Vector3 finalMove = (moveDirection * currentMoveSpeed) + (Vector3.up * velocityY);
        controller.Move(finalMove * Time.deltaTime);

        // 4. 회전 처리
        if (inputVector.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // 5. 애니메이션 (입력의 크기를 그대로 전달)
        animator.SetFloat(speedHash, inputVector.magnitude);
    }
}