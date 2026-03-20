using UnityEngine;
using UnityEngine.InputSystem; // 신형 시스템 네임스페이스 추가

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerMagager : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float gravity = 9.81f;

    private CharacterController controller;
    private Animator animator;
    private Vector2 inputVector; // 신형 시스템에서 받을 Vector2 값
    private float velocityY;
    private bool canMove = true; // 이동 가능 여부 플래그

    private readonly int speedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    // Input System 메시지 수신 (SendMessage 방식 또는 Player Input 컴포넌트 활용)
    // 에디터에서 설정한 액션 이름이 "Move"라면 함수 이름은 "OnMove"가 됩니다.
    public void OnMove(InputValue value)
    {
        inputVector = value.Get<Vector2>();
    }

    private void Update()
    {
        if (!canMove) return; // 이동 잠금 상태면 아래 로직 실행 안 함
        ApplyMovement();
    }

    // Animator의 SendMessage("SetMovementLock", true/false)를 받는 함수
    public void SetMovementLock(bool isLocked)
    {
        canMove = !isLocked;

        // 공격 시작 시(isLocked == true) 기존 입력을 초기화해서 미끄러짐 방지
        if (isLocked)
        {
            inputVector = Vector2.zero;
        }
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
        Vector3 finalMove = (moveDirection * moveSpeed) + (Vector3.up * velocityY);
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