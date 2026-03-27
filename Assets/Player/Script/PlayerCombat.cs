using UnityEngine;
using UnityEngine.InputSystem;
using BattlePvp.Stats;

public class PlayerCombat : MonoBehaviour
{
    [Header("Combo Settings")]
    [SerializeField] private AttackData[] comboList; // 3개의 SO 할당
    private int currentComboIndex = 0;
    private bool isAttacking = false;       // 현재 공격 동작 중인가?
    private bool hasComboReserved = false;  // 다음 공격이 예약되었는가?

    [SerializeField] private StatManager _statManager;
    [SerializeField] private BattlePvp.Combat.MeleeHitBox[] _hitboxes; // 무기 여러 개일 수 있음
    private Animator animator;
    private CharacterController controller; // CharacterController 참조 추가
    private Rigidbody rb; // Rigidbody 참조 추가 (요청사항 반영)

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private float _currentAttackSpeed = 1.0f;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
    }

    // New Input System: 좌클릭 이벤트
    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;
        if (BattlePvp.Logic.BattleInputController.IsPaused) return;

        // UI 위에 있을 때는 공격 무시 (Task 2)
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (isAttacking)
        {
            // [핵심] 이미 공격 중이라면 다음 타수를 '예약'만 합니다.
            // 애니메이션을 끊지 않고 flag만 true로 바꿉니다.
            hasComboReserved = true;
            Debug.Log($"{currentComboIndex + 2}타 예약 완료!");
        }
        else
        {
            // 공격 중이 아니라면 1타(Index 0)부터 즉시 시작합니다.
            StartAttack(0);
        }
    }

    private void StartAttack(int index)
    {
        isAttacking = true;
        hasComboReserved = false;
        currentComboIndex = index;

        // Core Task 4: 공격 시 Root Motion 비활성 및 회전 고정 알림
        if (animator != null) animator.applyRootMotion = false;
        
        // PlayerManager의 회전/이동 로직에 상태 전달
        var pm = GetComponent<PlayerManager>();
        if (pm != null) pm.SetMovementLock(true);

        // 공격 속도 계산 (기본 0.6 + AGI * 0.02)
        if (_statManager != null)
        {
            float agi = _statManager.GetFinalTotal(StatKind.AGI);
            float baseAs = 0.6f + (agi * 0.02f);
            
            // Monostat 보너스/페널티
            Identity id = _statManager.CurrentIdentity;
            if (id.Type == IdentityType.Monostat)
            {
                if (id.PrimaryStat == StatKind.AGI) baseAs *= 1.6f; // 민첩 몰빵: 공속 +60%
                else if (id.PrimaryStat == StatKind.STR) baseAs *= 0.75f; // 힘 몰빵: 공속 -25%
            }

            _currentAttackSpeed = baseAs;
            animator.speed = _currentAttackSpeed;
        }

        // ScriptableObject에 적힌 애니메이션 이름을 재생합니다.
        // 화살표(Transition) 없이도 즉시 실행되지만, 현재 동작을 끊지 않도록 설계되었습니다.
        animator.Play(comboList[index].animationName);

        // 현재 공격 데이터 세팅
        foreach (var hb in _hitboxes)
        {
            if (hb != null) hb.SetAttackData(comboList[index]);
        }
    }

    // Animation Event에서 호출할 함수들
    public void EnableHitBox()
    {
        foreach (var hb in _hitboxes) if (hb != null) hb.EnableHitBox();
    }

    public void DisableHitBox()
    {
        foreach (var hb in _hitboxes) if (hb != null) hb.DisableHitBox();
    }

    // [중요] StateMachineBehaviour에서 애니메이션이 끝날 때 호출할 함수
    public void OnAttackAnimationEnd()
    {
        // 마지막 타수가 아니고, 유저가 클릭을 해서 예약이 되어 있다면
        if (hasComboReserved && currentComboIndex < comboList.Length - 1)
        {
            // 다음 타수로 넘어갑니다.
            StartAttack(currentComboIndex + 1);
        }
        else
        {
            // 예약이 없거나 마지막 3타였다면 콤보를 완전히 종료합니다.
            StopCombo();
            animator.speed = 1.0f; // 속도 원복
        }
    }

    private void StopCombo()
    {
        isAttacking = false;
        currentComboIndex = 0;
        hasComboReserved = false;

        // 상태 원복
        var pm = GetComponent<PlayerManager>();
        if (pm != null) pm.SetMovementLock(false);

        Debug.Log("콤보 종료 및 초기화");
    }
}