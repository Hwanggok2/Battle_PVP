using UnityEngine;
using UnityEngine.InputSystem;
using BattlePvp.Stats;
using BattlePvp.Combat;

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

    private bool _isPointerOverUI = false;

    private void Update()
    {
        // UI 위에 있는지 여부를 갱신 (Update에서 수행하여 타이밍 경고 방지)
        _isPointerOverUI = UnityEngine.EventSystems.EventSystem.current != null && 
                           UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;
        var pm = GetComponent<PlayerManager>();
        // Wait, isDead is private in PlayerManager, so I'll check HealthSystem IsDead instead.
        var health = GetComponent<HealthSystem>();
        if (health != null && health.IsDead) return;

        if (BattlePvp.Logic.GameInputController.IsPaused) return;

        // UI 위에 있을 때는 공격 무시 (Task 2)
        // [Core Fix] 커서가 잠금(Locked) 상태가 아닐 때만 UI 체크를 적용하여
        // 중앙에 위치한 HUD(조준점 등)가 공격을 방해하지 않도록 합니다.
        if (Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI)
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
        // 아바타 마스크가 적용된 1번 레이어(New Layer)에서 실행
        animator.Play(comboList[index].animationName, 1, 0f);

        // 애니메이션 상태 추적 코루틴 시작
        if (_comboRoutine != null) StopCoroutine(_comboRoutine);
        _comboRoutine = StartCoroutine(CoComboMonitor(index));

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

    // [중요] 애니메이션 진행도를 Coroutine으로 추적하여 콤보를 진행합니다.
    private Coroutine _comboRoutine;

    private System.Collections.IEnumerator CoComboMonitor(int index)
    {
        // Animator에 Play 명령이 반영될 때까지 2프레임 대기
        yield return null;
        yield return null;

        while (true)
        {
            if (animator == null) yield break;

            var stateInfo = animator.GetCurrentAnimatorStateInfo(1);

            // 현재 상태가 우리가 실행한 공격 애니메이션인 경우
            if (stateInfo.IsName(comboList[index].animationName))
            {
                // 애니메이션이 거의 끝났을 때 (95% 이상)
                if (stateInfo.normalizedTime >= 0.95f)
                {
                    break;
                }
            }
            else
            {
                // 재생 중이 아닌데 트랜지션(전환) 중도 아니라면, 피격 등 다른 상태로 강제 전환된 것
                if (!animator.IsInTransition(1))
                {
                    StopCombo();
                    yield break;
                }
            }

            yield return null;
        }

        // 애니메이션이 무사히 끝났을 때의 처리
        if (hasComboReserved && currentComboIndex < comboList.Length - 1)
        {
            // 예약이 되어 있으면 다음 타수 진행
            StartAttack(currentComboIndex + 1);
        }
        else
        {
            // 예약이 없거나 마지막 3타였다면 콤보 완전히 종료
            StopCombo();
            animator.speed = 1.0f; // 속도 원복
        }
    }

    // StateMachineBehaviour에서 기존에 호출하던 함수 (이제 내부 처리를 안 함)
    // 에러 방지를 위해 빈 함수로 남겨둡니다.
    public void OnAttackAnimationEnd()
    {
        // 콤보 로직은 이제 CoComboMonitor에서 안전하게 자체 처리됩니다.
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