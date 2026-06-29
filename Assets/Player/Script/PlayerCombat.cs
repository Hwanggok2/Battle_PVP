using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerCombat : NetworkBehaviour
{
    [Header("Combo Settings")]
    [SerializeField] private AttackData[] comboList;
    [SerializeField] private StatManager _statManager;
    [SerializeField] private MeleeHitBox[] _hitboxes;

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private float _currentAttackSpeed = 1.0f;

    private int currentComboIndex;
    private bool isAttacking;
    private bool hasComboReserved;
    private bool _isPointerOverUI;

    private Animator animator;
    private PlayerInput _playerInput;
    private Coroutine _comboRoutine;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        _playerInput = GetComponent<PlayerInput>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (_playerInput != null)
            _playerInput.enabled = isLocalPlayer;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        if (_playerInput != null)
            _playerInput.enabled = true;
    }

    private void Update()
    {
        if (isClient && !isLocalPlayer)
            return;

        _isPointerOverUI = UnityEngine.EventSystems.EventSystem.current != null &&
                           UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    public void OnAttack(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;
        if (IsBattleLoadingOrNotStarted()) return;

        var pm = GetComponent<PlayerManager>();
        if (BattlePvp.Networking.BattleStateMachine.Instance != null &&
            BattlePvp.Networking.BattleStateMachine.Instance.CurrentState == BattlePvp.Networking.BattleState.MatchEnded)
        {
            if (pm == null || pm.IsMatchEndLocked)
                return;
        }

        var health = GetComponent<HealthSystem>();
        if (health != null && health.IsDead) return;

        if (BattlePvp.Logic.GameInputController.IsPaused) return;

        if (Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI)
            return;

        if (isAttacking)
        {
            hasComboReserved = true;
            Debug.Log($"{currentComboIndex + 2} attack reserved.");
            return;
        }

        StartAttack(0, true);
    }

    private bool IsBattleLoadingOrNotStarted()
    {
        if (SceneManager.GetActiveScene().name != "Battle")
            return false;

        var battleState = BattlePvp.Networking.BattleStateMachine.Instance;
        if (battleState == null)
            return false;

        var pm = GetComponent<PlayerManager>();
        if (battleState.CurrentState == BattlePvp.Networking.BattleState.MatchEnded &&
            pm != null &&
            !pm.IsMatchEndLocked)
        {
            return false;
        }

        return battleState.IsLoading || battleState.CurrentState != BattlePvp.Networking.BattleState.InBattle;
    }

    private void StartAttack(int index, bool notifyServer)
    {
        if (index < 0 || comboList == null || index >= comboList.Length || comboList[index] == null)
            return;

        isAttacking = true;
        hasComboReserved = false;
        currentComboIndex = index;

        if (animator != null)
            animator.applyRootMotion = false;

        var pm = GetComponent<PlayerManager>();
        if (pm != null)
            pm.SetMovementLock(true);

        if (_statManager != null)
        {
            float agi = _statManager.GetFinalTotal(StatKind.AGI);
            float baseAs = 0.6f + (agi * 0.02f);

            Identity id = _statManager.CurrentIdentity;
            if (id.Type == IdentityType.Monostat)
            {
                if (id.PrimaryStat == StatKind.AGI) baseAs *= 3f;
                else if (id.PrimaryStat == StatKind.STR) baseAs *= 0.75f;
            }

            _currentAttackSpeed = baseAs;
            if (animator != null)
                animator.speed = _currentAttackSpeed;
        }

        if (animator != null)
            animator.Play(comboList[index].animationName, 1, 0f);

        if (_comboRoutine != null)
            StopCoroutine(_comboRoutine);
        _comboRoutine = StartCoroutine(CoComboMonitor(index));

        foreach (var hb in _hitboxes)
        {
            if (hb != null)
                hb.SetAttackData(comboList[index]);
        }

        if (notifyServer && isClient && isLocalPlayer)
        {
            if (isServer)
                RpcStartAttack(index);
            else
                CmdStartAttack(index);
        }
    }

    [Command]
    private void CmdStartAttack(int index)
    {
        StartAttack(index, false);
        RpcStartAttack(index);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcStartAttack(int index)
    {
        if (isServer)
            return;

        StartAttack(index, false);
    }

    public void EnableHitBox()
    {
        foreach (var hb in _hitboxes)
        {
            if (hb != null)
                hb.EnableHitBox();
        }
    }

    public void DisableHitBox()
    {
        foreach (var hb in _hitboxes)
        {
            if (hb != null)
                hb.DisableHitBox();
        }
    }

    private System.Collections.IEnumerator CoComboMonitor(int index)
    {
        yield return null;
        yield return null;

        while (true)
        {
            if (animator == null)
                yield break;

            var stateInfo = animator.GetCurrentAnimatorStateInfo(1);
            if (stateInfo.IsName(comboList[index].animationName))
            {
                if (stateInfo.normalizedTime >= 0.95f)
                    break;
            }
            else if (!animator.IsInTransition(1))
            {
                StopCombo();
                yield break;
            }

            yield return null;
        }

        if (hasComboReserved && currentComboIndex < comboList.Length - 1)
            StartAttack(currentComboIndex + 1, true);
        else
        {
            StopCombo();
            if (animator != null)
                animator.speed = 1.0f;
        }
    }

    public void OnAttackAnimationEnd()
    {
    }

    private void StopCombo()
    {
        isAttacking = false;
        currentComboIndex = 0;
        hasComboReserved = false;

        var pm = GetComponent<PlayerManager>();
        if (pm != null)
            pm.SetMovementLock(false);

        Debug.Log("Combo ended.");
    }
}
