using BattlePvp.Combat;
using BattlePvp.Stats;
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using BattlePvp.UI;

public class PlayerCombat : NetworkBehaviour
{
    private const float MonostatStrSkillCastSeconds = 0.7f;
    private const float MonostatStrSkillDurationSeconds = 10f;
    private const float MonostatStrSkillCooldownSeconds = 35f;
    private const float MonostatStrSkillHealRatio = 0.1f;
    private const float MonostatAgiSkillCastSeconds = 1f;
    private const float MonostatAgiSkillDurationSeconds = 7f;
    private const float MonostatAgiSkillCooldownSeconds = 40f;
    private const int MonostatAgiPoisonMaxStacks = 5;
    private const float MonostatAgiPoisonDamagePerStackPerSecond = 2f;
    private const float MonostatAgiPoisonStackDurationSeconds = 6f;

    [Header("Combo Settings")]
    [SerializeField] private AttackData[] comboList;
    [SerializeField] private StatManager _statManager;
    [SerializeField] private MeleeHitBox[] _hitboxes;

    [Header("Job Skill - Monostat STR")]
    [SerializeField] private JobSkillData _monostatStrSkillData;

    [Header("Job Skill - Monostat AGI")]
    [SerializeField] private JobSkillData _monostatAgiSkillData;

    [Header("Job Skill - Monostat CON")]
    [SerializeField] private JobSkillData _monostatConSkillData;

    [Header("Job Skill - Monostat DEF")]
    [SerializeField] private JobSkillData _monostatDefSkillData;

    [Header("Job Skills - Strategist")]
    [SerializeField] private JobSkillData _strategistRollSkillData;
    [SerializeField] private JobSkillData _strategistPresetSkillData;

    [Header("Job Skills - Polymath")]
    [SerializeField] private JobSkillData _polymathRollSkillData;
    [SerializeField] private JobSkillData _polymathPresetSkillData;
    [SerializeField] private JobSkillData _polymathWeaponSwapSkillData;

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private float _currentAttackSpeed = 1.0f;
    [SerializeField] private int _selectedSkillIndex;
    [SyncVar]
    [SerializeField] private bool _isCastingMonostatStrSkill;
    [SyncVar]
    [SerializeField] private double _monostatStrSkillCastCompleteAt;
    [SyncVar]
    [SerializeField] private double _monostatStrSkillActiveUntil;
    [SyncVar]
    [SerializeField] private double _monostatStrSkillCooldownUntil;
    [SyncVar]
    [SerializeField] private bool _isCastingMonostatAgiSkill;
    [SyncVar]
    [SerializeField] private double _monostatAgiSkillCastCompleteAt;
    [SyncVar]
    [SerializeField] private double _monostatAgiSkillActiveUntil;
    [SyncVar]
    [SerializeField] private double _monostatAgiSkillCooldownUntil;
    [SyncVar] [SerializeField] private int _advancedCastingSkillKey = -1;
    [SyncVar] [SerializeField] private double _advancedCastCompleteAt;
    [SyncVar] [SerializeField] private int _advancedActiveSkillKey = -1;
    [SyncVar] [SerializeField] private double _advancedActiveUntil;
    [SyncVar] [SerializeField] private bool _isBowEquipped;
    [SyncVar] [SerializeField] private uint _tauntedByNetId;
    [SyncVar] [SerializeField] private double _tauntedUntil;
    private readonly SyncDictionary<int, double> _advancedCooldownUntil = new SyncDictionary<int, double>();

    private int currentComboIndex;
    private bool isAttacking;
    private bool hasComboReserved;
    private bool _isPointerOverUI;
    private readonly HashSet<IDamageReceiver> _hitTargetsThisSwing = new HashSet<IDamageReceiver>();

    private Animator animator;
    private PlayerInput _playerInput;
    private Coroutine _comboRoutine;
    private HealthSystem _healthSystem;
    private PlayerManager _playerManager;
    private BattlePvp.CameraLogic.FollowCamera _followCamera;
    private Coroutine _monostatStrSkillRoutine;
    private Coroutine _monostatAgiSkillRoutine;
    private Coroutine _monostatAgiPoisonRoutine;
    private double _localMonostatStrSkillAttackLockUntil;
    private double _localMonostatAgiSkillAttackLockUntil;
    private AudioSource _audioSource;
    private AttackProcessor _attackProcessor;
    private Coroutine _advancedSkillRoutine;
    private Coroutine _localAdvancedMoveLockRoutine;
    private double _localAdvancedAttackLockUntil;
    private double _bowChargeStartedAt = -1d;
    private readonly List<PoisonStackState> _monostatAgiPoisonStacks = new List<PoisonStackState>();

    public event Action<SkillHudState> SkillHudChanged;
    public bool IsMonostatStrLifestealActive => NetworkTime.time < _monostatStrSkillActiveUntil;
    public float MonostatStrSkillLifestealRatio => ResolveMonostatStrLifestealRatio();
    public bool IsMonostatAgiPoisonCoatingActive => NetworkTime.time < _monostatAgiSkillActiveUntil;

    private JobSkillData MonostatStrSkillData => IsSkillDataKind(_monostatStrSkillData, JobSkillKind.MonostatStrLifesteal) ? _monostatStrSkillData : null;
    private JobSkillData MonostatAgiSkillData => IsSkillDataKind(_monostatAgiSkillData, JobSkillKind.MonostatAgiPoison) ? _monostatAgiSkillData : null;
    private JobSkillData MonostatConSkillData => IsSkillDataKind(_monostatConSkillData, JobSkillKind.MonostatConKick) ? _monostatConSkillData : null;
    private JobSkillData MonostatDefSkillData => IsSkillDataKind(_monostatDefSkillData, JobSkillKind.MonostatDefTaunt) ? _monostatDefSkillData : null;
    private float MonostatStrCastSeconds => MonostatStrSkillData != null ? MonostatStrSkillData.CastSeconds : MonostatStrSkillCastSeconds;
    private float MonostatStrDurationSeconds => MonostatStrSkillData != null ? MonostatStrSkillData.DurationSeconds : MonostatStrSkillDurationSeconds;
    private float MonostatStrCooldownSeconds => MonostatStrSkillData != null ? MonostatStrSkillData.CooldownSeconds : MonostatStrSkillCooldownSeconds;
    private string MonostatStrCastAnimationStateName => MonostatStrSkillData != null ? MonostatStrSkillData.StrCastAnimationStateName : string.Empty;
    private int MonostatStrCastAnimationLayer => MonostatStrSkillData != null ? MonostatStrSkillData.StrCastAnimationLayer : 1;
    private float MonostatAgiCastSeconds => MonostatAgiSkillData != null ? MonostatAgiSkillData.CastSeconds : MonostatAgiSkillCastSeconds;
    private float MonostatAgiDurationSeconds => MonostatAgiSkillData != null ? MonostatAgiSkillData.DurationSeconds : MonostatAgiSkillDurationSeconds;
    private float MonostatAgiCooldownSeconds => MonostatAgiSkillData != null ? MonostatAgiSkillData.CooldownSeconds : MonostatAgiSkillCooldownSeconds;
    private int MonostatAgiPoisonMaxStackCount => MonostatAgiSkillData != null && MonostatAgiSkillData.PoisonMaxStacks > 0 ? MonostatAgiSkillData.PoisonMaxStacks : MonostatAgiPoisonMaxStacks;
    private float MonostatAgiPoisonDamagePerStackPerSecondValue => MonostatAgiSkillData != null && MonostatAgiSkillData.PoisonDamagePerStackPerSecond > 0f ? MonostatAgiSkillData.PoisonDamagePerStackPerSecond : MonostatAgiPoisonDamagePerStackPerSecond;
    private float MonostatAgiPoisonStackDurationSecondsValue => MonostatAgiSkillData != null && MonostatAgiSkillData.PoisonStackDurationSeconds > 0f ? MonostatAgiSkillData.PoisonStackDurationSeconds : MonostatAgiPoisonStackDurationSeconds;

    private sealed class PoisonStackState
    {
        public IDamageReceiver Target;
        public int StackCount;
        public double ExpiresAt;
        public Vector3 LastHitPosition;
    }

    private static bool IsSkillDataKind(JobSkillData data, JobSkillKind expectedKind)
    {
        return data != null && data.SkillKind == expectedKind;
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        _playerInput = GetComponent<PlayerInput>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
        _healthSystem = GetComponent<HealthSystem>();
        _playerManager = GetComponent<PlayerManager>();
        _attackProcessor = GetComponent<AttackProcessor>();
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
    }

#if UNITY_EDITOR
    private new void OnValidate()
    {
        WarnIfSkillKindMismatch(_monostatStrSkillData, JobSkillKind.MonostatStrLifesteal, nameof(_monostatStrSkillData));
        WarnIfSkillKindMismatch(_monostatAgiSkillData, JobSkillKind.MonostatAgiPoison, nameof(_monostatAgiSkillData));
        WarnIfSkillKindMismatch(_monostatConSkillData, JobSkillKind.MonostatConKick, nameof(_monostatConSkillData));
        WarnIfSkillKindMismatch(_monostatDefSkillData, JobSkillKind.MonostatDefTaunt, nameof(_monostatDefSkillData));
        WarnIfSkillKindMismatch(_strategistRollSkillData, JobSkillKind.StrategistRoll, nameof(_strategistRollSkillData));
        WarnIfSkillKindMismatch(_strategistPresetSkillData, JobSkillKind.StrategistPresetChange, nameof(_strategistPresetSkillData));
        WarnIfSkillKindMismatch(_polymathRollSkillData, JobSkillKind.PolymathRoll, nameof(_polymathRollSkillData));
        WarnIfSkillKindMismatch(_polymathPresetSkillData, JobSkillKind.PolymathPresetChange, nameof(_polymathPresetSkillData));
        WarnIfSkillKindMismatch(_polymathWeaponSwapSkillData, JobSkillKind.PolymathWeaponSwap, nameof(_polymathWeaponSwapSkillData));
    }

    private void WarnIfSkillKindMismatch(JobSkillData data, JobSkillKind expectedKind, string fieldName)
    {
        if (data == null || data.SkillKind == expectedKind)
            return;

        Debug.LogWarning(
            $"[PlayerCombat] {fieldName} expects {expectedKind}, but assigned {data.SkillKind}: {data.name}",
            this);
    }
#endif

    private void OnEnable()
    {
        if (_healthSystem == null)
            _healthSystem = GetComponent<HealthSystem>();

        if (_healthSystem != null)
        {
            _healthSystem.OnDied += CancelCurrentAttack;
            _healthSystem.OnRevived += HandleRevived;
        }

        if (_statManager != null)
            _statManager.StatsChanged += OnStatsChanged;

        PublishSkillHudState();
    }

    private void OnDisable()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= CancelCurrentAttack;
            _healthSystem.OnRevived -= HandleRevived;
        }

        if (_statManager != null)
            _statManager.StatsChanged -= OnStatsChanged;

        DisableHitBox();
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

        _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
    }

    private void Update()
    {
        if (isClient && !isLocalPlayer)
            return;

        _isPointerOverUI = UnityEngine.EventSystems.EventSystem.current != null &&
                           UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        HandleSkillMouseInput();
        PublishSkillHudState();
    }

    private void HandleSkillMouseInput()
    {
        if (Mouse.current == null)
            return;

        if (BattlePvp.Logic.GameInputController.IsPaused || BattlePvp.Logic.GameInputController.IsTextInputActive)
            return;

        if (Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI)
            return;

        Vector2 scroll = Mouse.current.scroll.ReadValue();
        if (Mathf.Abs(scroll.y) > 0.01f)
            SelectSkill(scroll.y > 0f ? -1 : 1);

        if (Mouse.current.rightButton.wasPressedThisFrame)
            TryUseSelectedSkill();
    }

    public void OnSkill(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (!value.isPressed) return;

        TryUseSelectedSkill();
    }

    private void OnStatsChanged(StatContainer _)
    {
        if (this == null) return;
        ClampSelectedSkillIndex();
        PublishSkillHudState();
    }

    public void OnAttack(InputValue value)
    {
        if (isClient && !isLocalPlayer) return;
        if (IsPolymath() && _isBowEquipped)
        {
            HandleBowAttackInput(value.isPressed);
            return;
        }
        if (!value.isPressed) return;
        if (IsBattleLoadingOrNotStarted()) return;

        var pm = GetComponent<PlayerManager>();
        if (BattlePvp.Networking.BattleStateMachine.Instance != null &&
            BattlePvp.Networking.BattleStateMachine.Instance.CurrentState == BattlePvp.Networking.BattleState.MatchEnded)
        {
            if (pm == null || pm.IsMatchEndLocked)
                return;
        }

        if (_healthSystem != null && _healthSystem.IsDead) return;

        if (BattlePvp.Logic.GameInputController.IsPaused || BattlePvp.Logic.GameInputController.IsTextInputActive) return;

        if (IsSkillCastingOrAttackLocked()) return;

        if (Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI)
            return;

        if (isAttacking)
        {
            hasComboReserved = true;
            Debug.Log($"{currentComboIndex + 2} attack reserved.");
            return;
        }

        StartAttack(0, true, GetCurrentAimDirection());
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

    private void StartAttack(int index, bool notifyServer, Vector3 aimDirection)
    {
        if (_healthSystem != null && _healthSystem.IsDead)
            return;

        if (IsSkillCastingOrAttackLocked())
            return;

        if (index < 0 || comboList == null || index >= comboList.Length || comboList[index] == null)
            return;

        isAttacking = true;
        hasComboReserved = false;
        currentComboIndex = index;
        aimDirection = ResolveTauntAimDirection(aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : transform.forward);

        if (animator != null)
            animator.applyRootMotion = false;

        var pm = _playerManager != null ? _playerManager : GetComponent<PlayerManager>();
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
            {
                hb.SetAttackData(comboList[index]);
                hb.SetAttackContext(aimDirection, pm != null && pm.IsCrouching);
            }
        }

        if (notifyServer && isClient && isLocalPlayer)
        {
            if (isServer)
                RpcStartAttack(index, aimDirection);
            else
                CmdStartAttack(index, aimDirection);
        }
    }

    [Command]
    private void CmdStartAttack(int index, Vector3 aimDirection)
    {
        if (_healthSystem != null && _healthSystem.IsDead)
            return;

        StartAttack(index, false, aimDirection);
        RpcStartAttack(index, aimDirection);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcStartAttack(int index, Vector3 aimDirection)
    {
        if (isServer)
            return;

        StartAttack(index, false, aimDirection);
    }

    public void EnableHitBox()
    {
        if (_healthSystem != null && _healthSystem.IsDead)
            return;

        _hitTargetsThisSwing.Clear();

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

    public bool TryRegisterHitTarget(IDamageReceiver target)
    {
        if (target == null)
            return false;

        if (_hitTargetsThisSwing.Contains(target))
            return false;

        _hitTargetsThisSwing.Add(target);
        return true;
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
            StartAttack(currentComboIndex + 1, true, GetCurrentAimDirection());
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

    public void CancelCurrentAttack()
    {
        if (_comboRoutine != null)
        {
            StopCoroutine(_comboRoutine);
            _comboRoutine = null;
        }

        DisableHitBox();
        isAttacking = false;
        currentComboIndex = 0;
        hasComboReserved = false;

        if (animator != null)
            animator.speed = 1.0f;

        var pm = _playerManager != null ? _playerManager : GetComponent<PlayerManager>();
        if (pm != null)
            pm.SetMovementLock(false);
    }

    public void NotifyPhysicalDamageDealt(float actualDamage, IDamageReceiver defender = null, Vector3 hitPosition = default)
    {
        if (actualDamage > 0f && IsMonostatStrLifestealActive)
        {
            if (_healthSystem == null)
                _healthSystem = GetComponent<HealthSystem>();

            if (_healthSystem != null)
                _healthSystem.Heal(actualDamage * MonostatStrSkillLifestealRatio);
        }

        if (IsMonostatAgiPoisonCoatingActive && defender != null)
            ApplyMonostatAgiPoisonStack(defender, hitPosition);

        if (_advancedActiveSkillKey == (int)JobSkillKind.MonostatDefTaunt && NetworkTime.time < _advancedActiveUntil)
        {
            JobSkillData taunt = MonostatDefSkillData;
            _advancedActiveSkillKey = -1;
            _advancedActiveUntil = 0d;
            if (_healthSystem != null && taunt != null)
                _healthSystem.SetTauntDefense(taunt.TauntDurationSeconds, taunt.TauntIncomingDamageMultiplier,
                    taunt.TauntReflectMultiplier, taunt.TauntReflectHealthCapRatio);
            if (defender is Component defenderComponent)
            {
                PlayerCombat defenderCombat = defenderComponent.GetComponentInParent<PlayerCombat>();
                defenderCombat?.SetTauntedBy(netId, taunt != null ? taunt.TauntDurationSeconds : 0f);
            }
        }
    }

    private void TryUseMonostatStrSkill()
    {
        if (!CanUseMonostatStrSkillInput())
            return;

        _localMonostatStrSkillAttackLockUntil = NetworkTime.time + MonostatStrCastSeconds;
        PlayMonostatStrCastAnimationLocal();

        if (isClient && isLocalPlayer)
        {
            if (isServer)
                BeginMonostatStrSkill();
            else
                CmdUseMonostatStrSkill();
            return;
        }

        BeginMonostatStrSkill();
    }

    private void TryUseSelectedSkill()
    {
        if (!CanUseSkillInput())
            return;

        if (IsMonostatStr() && _selectedSkillIndex == 0)
        {
            TryUseMonostatStrSkill();
            return;
        }

        if (IsMonostatAgi() && _selectedSkillIndex == 0)
        {
            TryUseMonostatAgiSkill();
            return;
        }

        JobSkillData data = ResolveSelectedAdvancedSkillData();
        if (data != null)
        {
            TryUseAdvancedSkill(data);
            return;
        }

        Debug.Log($"[PlayerCombat] Skill slot {_selectedSkillIndex + 1} is not implemented yet.");
    }

    private void SelectSkill(int direction)
    {
        int count = ResolveAvailableSkillCount();
        if (count <= 1)
            return;

        _selectedSkillIndex = (_selectedSkillIndex + direction) % count;
        if (_selectedSkillIndex < 0)
            _selectedSkillIndex += count;

        PublishSkillHudState();
    }

    [Command]
    private void CmdUseMonostatStrSkill()
    {
        BeginMonostatStrSkill();
    }

    [Command]
    private void CmdUseMonostatAgiSkill()
    {
        BeginMonostatAgiSkill();
    }

    [Command]
    private void CmdUseAdvancedSkill(int skillKey, Vector3 direction)
    {
        BeginAdvancedSkill(skillKey, direction);
    }

    private void TryUseAdvancedSkill(JobSkillData data)
    {
        int key = (int)data.SkillKind;
        Vector3 direction = _playerManager != null ? _playerManager.GetSkillMoveDirection() : transform.forward;
        _localAdvancedAttackLockUntil = NetworkTime.time + data.CastSeconds;
        LockLocalAdvancedMovement(data.CastSeconds);
        if (isClient && isLocalPlayer && !isServer)
            CmdUseAdvancedSkill(key, direction);
        else
            BeginAdvancedSkill(key, direction);
    }

    private void LockLocalAdvancedMovement(float seconds)
    {
        if (seconds <= 0f || _playerManager == null)
            return;

        _playerManager.SetSkillMovementLock(true);
        if (_localAdvancedMoveLockRoutine != null)
            StopCoroutine(_localAdvancedMoveLockRoutine);
        _localAdvancedMoveLockRoutine = StartCoroutine(CoLocalAdvancedMoveLock(seconds));
    }

    private System.Collections.IEnumerator CoLocalAdvancedMoveLock(float seconds)
    {
        float endTime = Time.time + seconds;
        while (Time.time < endTime)
            yield return null;

        _playerManager?.SetSkillMovementLock(false);
        _localAdvancedMoveLockRoutine = null;
    }

    private void BeginAdvancedSkill(int skillKey, Vector3 direction)
    {
        JobSkillData data = ResolveAdvancedSkillData(skillKey);
        double now = NetworkTime.time;
        if (data == null || (_healthSystem != null && _healthSystem.IsDead))
            return;
        if (_advancedCastingSkillKey >= 0 || (_advancedCooldownUntil.TryGetValue(skillKey, out double cooldown) && now < cooldown))
            return;

        if (data.CooldownSeconds > 0f)
            _advancedCooldownUntil[skillKey] = now + data.CooldownSeconds;

        CancelCurrentAttack();
        PlaySkillSfx(skillKey);
        if (data.CastSeconds <= 0f)
        {
            ApplyAdvancedSkill(data, direction);
            return;
        }

        _advancedCastingSkillKey = skillKey;
        _advancedCastCompleteAt = now + data.CastSeconds;
        _playerManager?.SetSkillMovementLock(true);
        if (_advancedSkillRoutine != null)
            StopCoroutine(_advancedSkillRoutine);
        _advancedSkillRoutine = StartCoroutine(CoAdvancedSkillCast(data, direction));
    }

    private System.Collections.IEnumerator CoAdvancedSkillCast(JobSkillData data, Vector3 direction)
    {
        while (NetworkTime.time < _advancedCastCompleteAt)
            yield return null;

        _advancedCastingSkillKey = -1;
        _advancedCastCompleteAt = 0d;
        _playerManager?.SetSkillMovementLock(false);
        ApplyAdvancedSkill(data, direction);
        _advancedSkillRoutine = null;
    }

    private void ApplyAdvancedSkill(JobSkillData data, Vector3 direction)
    {
        switch (data.SkillKind)
        {
            case JobSkillKind.MonostatConKick:
                ExecuteKick(data);
                break;
            case JobSkillKind.MonostatDefTaunt:
                _advancedActiveSkillKey = (int)data.SkillKind;
                _advancedActiveUntil = NetworkTime.time + data.TauntReadyDurationSeconds;
                break;
            case JobSkillKind.StrategistRoll:
            case JobSkillKind.PolymathRoll:
                _advancedActiveSkillKey = (int)data.SkillKind;
                _advancedActiveUntil = NetworkTime.time + data.RollDurationSeconds;
                _healthSystem?.SetSkillInvulnerable(data.RollDurationSeconds);
                RpcExecuteSkillMove(direction, data.RollDistance, data.RollDurationSeconds, 1f, 0f);
                break;
            case JobSkillKind.StrategistPresetChange:
            case JobSkillKind.PolymathPresetChange:
                ExecutePresetChange(data);
                break;
            case JobSkillKind.PolymathWeaponSwap:
                _isBowEquipped = !_isBowEquipped;
                break;
        }
    }

    private void ExecuteKick(JobSkillData data)
    {
        if (_attackProcessor == null)
            _attackProcessor = GetComponent<AttackProcessor>();

        Vector3 center = transform.position + transform.forward * data.KickRange;
        Collider[] hits = Physics.OverlapSphere(center, data.KickRadius, ~0, QueryTriggerInteraction.Collide);
        var targets = new HashSet<IDamageReceiver>();
        foreach (Collider hit in hits)
        {
            if (hit == null || hit.transform.root == transform.root)
                continue;
            IDamageReceiver target = hit.GetComponentInParent<IDamageReceiver>();
            StatManager targetStats = hit.GetComponentInParent<StatManager>();
            if (target == null || targetStats == null || !targets.Add(target))
                continue;
            Vector3 hitPosition = hit.ClosestPoint(center);
            if (!_attackProcessor.ProcessSkillHit(data.KickDamageMultiplier, targetStats, target, hitPosition))
                continue;
            if (target is Component targetComponent)
            {
                PlayerManager targetManager = targetComponent.GetComponentInParent<PlayerManager>();
                if (targetManager != null)
                {
                    Vector3 push = targetManager.transform.position - transform.position;
                    RpcApplyMovementEffect(targetManager.netId, push, data.KickKnockbackDistance, 0.2f,
                        data.KickSlowMoveMultiplier, data.KickSlowDurationSeconds);
                }
            }
        }
    }

    private void ExecutePresetChange(JobSkillData data)
    {
        if (_statManager == null || _healthSystem == null)
            return;
        float oldMax = _healthSystem.MaxHp;
        float oldCurrent = _healthSystem.CurrentHp;
        _statManager.ApplyStats(data.TargetPreset, true);
        float newMax = _healthSystem.MaxHp;
        float shield = newMax > oldMax
            ? (newMax - oldMax) * data.MaxHealthIncreaseShieldRatio
            : Mathf.Max(0f, oldCurrent - newMax);
        _healthSystem.SetCurrentHp(Mathf.Min(oldCurrent, newMax));
        _healthSystem.GrantDecayingShield(shield, data.ShieldDurationSeconds);
    }

    [ClientRpc]
    private void RpcExecuteSkillMove(Vector3 direction, float distance, float duration, float moveMultiplier, float slowDuration)
    {
        _playerManager?.MoveBySkill(direction, distance, duration);
        if (slowDuration > 0f)
            _playerManager?.ApplySkillMoveMultiplier(moveMultiplier, slowDuration);
    }

    [ClientRpc]
    private void RpcApplyMovementEffect(uint targetNetId, Vector3 direction, float distance, float duration, float moveMultiplier, float slowDuration)
    {
        if (!NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
            return;
        PlayerManager manager = identity.GetComponent<PlayerManager>();
        manager?.MoveBySkill(direction, distance, duration);
        manager?.ApplySkillMoveMultiplier(moveMultiplier, slowDuration);
    }

    private void BeginMonostatStrSkill()
    {
        double now = NetworkTime.time;
        if (!CanStartMonostatStrSkill(now))
            return;

        _isCastingMonostatStrSkill = true;
        _monostatStrSkillCastCompleteAt = now + MonostatStrCastSeconds;
        _monostatStrSkillCooldownUntil = now + MonostatStrCooldownSeconds;

        CancelCurrentAttack();
        PlayMonostatStrCastAnimationNetworked();
        PlaySkillSfx(0);
        PublishSkillHudState();

        if (_monostatStrSkillRoutine != null)
            StopCoroutine(_monostatStrSkillRoutine);

        _monostatStrSkillRoutine = StartCoroutine(CoMonostatStrSkill());
    }

    private void TryUseMonostatAgiSkill()
    {
        if (!CanUseMonostatAgiSkillInput())
            return;

        _localMonostatAgiSkillAttackLockUntil = NetworkTime.time + MonostatAgiCastSeconds;

        if (isClient && isLocalPlayer)
        {
            if (isServer)
                BeginMonostatAgiSkill();
            else
                CmdUseMonostatAgiSkill();
            return;
        }

        BeginMonostatAgiSkill();
    }

    private void BeginMonostatAgiSkill()
    {
        double now = NetworkTime.time;
        if (!CanStartMonostatAgiSkill(now))
            return;

        _isCastingMonostatAgiSkill = true;
        _monostatAgiSkillCastCompleteAt = now + MonostatAgiCastSeconds;
        _monostatAgiSkillCooldownUntil = now + MonostatAgiCooldownSeconds;

        CancelCurrentAttack();
        PlaySkillSfx(1);
        PublishSkillHudState();

        if (_monostatAgiSkillRoutine != null)
            StopCoroutine(_monostatAgiSkillRoutine);

        _monostatAgiSkillRoutine = StartCoroutine(CoMonostatAgiSkill());
    }

    private System.Collections.IEnumerator CoMonostatStrSkill()
    {
        bool hasCastAnimation = HasMonostatStrCastAnimation();
        if (hasCastAnimation)
            _playerManager?.SetSkillMovementLock(true);

        while (NetworkTime.time < _monostatStrSkillCastCompleteAt || !IsMonostatStrCastAnimationFinished())
            yield return null;

        if (hasCastAnimation)
            _playerManager?.SetSkillMovementLock(false);

        _isCastingMonostatStrSkill = false;
        PublishSkillHudState();

        if (_healthSystem == null)
            _healthSystem = GetComponent<HealthSystem>();

        if (_healthSystem != null && _healthSystem.IsDead)
        {
            _monostatStrSkillRoutine = null;
            yield break;
        }

        if (!IsMonostatStr())
        {
            _monostatStrSkillRoutine = null;
            yield break;
        }

        _monostatStrSkillActiveUntil = NetworkTime.time + MonostatStrDurationSeconds;
        PublishSkillHudState();

        while (NetworkTime.time < _monostatStrSkillActiveUntil)
            yield return null;

        _monostatStrSkillActiveUntil = 0d;
        _monostatStrSkillRoutine = null;
        PublishSkillHudState();
    }

    private bool HasMonostatStrCastAnimation()
    {
        return !string.IsNullOrWhiteSpace(MonostatStrCastAnimationStateName);
    }

    private bool IsMonostatStrCastAnimationFinished()
    {
        if (!HasMonostatStrCastAnimation())
            return true;

        if (animator == null)
            return true;

        int layer = Mathf.Clamp(MonostatStrCastAnimationLayer, 0, animator.layerCount - 1);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layer);
        if (!stateInfo.IsName(MonostatStrCastAnimationStateName))
            return true;

        return !animator.IsInTransition(layer) && stateInfo.normalizedTime >= 1f;
    }

    private void PlayMonostatStrCastAnimationNetworked()
    {
        if (!HasMonostatStrCastAnimation())
            return;

        string stateName = MonostatStrCastAnimationStateName;
        int layer = MonostatStrCastAnimationLayer;
        PlaySkillAnimationLocal(stateName, layer);

        if (NetworkServer.active)
            RpcPlaySkillAnimation(stateName, layer);
    }

    private void PlayMonostatStrCastAnimationLocal()
    {
        if (!HasMonostatStrCastAnimation())
            return;

        PlaySkillAnimationLocal(MonostatStrCastAnimationStateName, MonostatStrCastAnimationLayer);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlaySkillAnimation(string stateName, int layer)
    {
        PlaySkillAnimationLocal(stateName, layer);
    }

    private void PlaySkillAnimationLocal(string stateName, int layer)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
            return;

        int safeLayer = Mathf.Clamp(layer, 0, animator.layerCount - 1);
        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(safeLayer, stateHash))
        {
            Debug.LogWarning(
                $"[PlayerCombat] Animator state '{stateName}' was not found on layer {safeLayer}. Check the skill SO animation name/layer.",
                this);
            return;
        }

        animator.speed = 1f;
        animator.Play(stateName, safeLayer, 0f);
    }

    private System.Collections.IEnumerator CoMonostatAgiSkill()
    {
        while (NetworkTime.time < _monostatAgiSkillCastCompleteAt)
            yield return null;

        _isCastingMonostatAgiSkill = false;
        PublishSkillHudState();

        if (_healthSystem == null)
            _healthSystem = GetComponent<HealthSystem>();

        if (_healthSystem != null && _healthSystem.IsDead)
        {
            _monostatAgiSkillRoutine = null;
            yield break;
        }

        if (!IsMonostatAgi())
        {
            _monostatAgiSkillRoutine = null;
            yield break;
        }

        _monostatAgiSkillActiveUntil = NetworkTime.time + MonostatAgiDurationSeconds;
        PublishSkillHudState();

        while (NetworkTime.time < _monostatAgiSkillActiveUntil)
            yield return null;

        _monostatAgiSkillActiveUntil = 0d;
        _monostatAgiSkillRoutine = null;
        PublishSkillHudState();
    }

    private bool CanUseSkillInput()
    {
        if (isClient && !isLocalPlayer) return false;
        if (IsBattleLoadingOrNotStarted()) return false;
        if (_healthSystem != null && _healthSystem.IsDead) return false;
        if (BattlePvp.Logic.GameInputController.IsPaused || BattlePvp.Logic.GameInputController.IsTextInputActive) return false;
        if (Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI) return false;
        if (ResolveAvailableSkillCount() <= 0) return false;

        return true;
    }

    private bool CanUseMonostatStrSkillInput()
    {
        if (!CanUseSkillInput()) return false;
        if (!IsMonostatStr()) return false;

        return true;
    }

    private bool CanUseMonostatAgiSkillInput()
    {
        if (!CanUseSkillInput()) return false;
        if (!IsMonostatAgi()) return false;

        return true;
    }

    private bool CanStartMonostatStrSkill(double now)
    {
        if (_isCastingMonostatStrSkill) return false;
        if (now < _monostatStrSkillActiveUntil) return false;
        if (now < _monostatStrSkillCooldownUntil) return false;
        if (_healthSystem != null && _healthSystem.IsDead) return false;
        if (!IsMonostatStr()) return false;

        return true;
    }

    private bool CanStartMonostatAgiSkill(double now)
    {
        if (_isCastingMonostatAgiSkill) return false;
        if (now < _monostatAgiSkillActiveUntil) return false;
        if (now < _monostatAgiSkillCooldownUntil) return false;
        if (_healthSystem != null && _healthSystem.IsDead) return false;
        if (!IsMonostatAgi()) return false;

        return true;
    }

    private bool IsSkillCastingOrAttackLocked()
    {
        if (_isCastingMonostatStrSkill || _isCastingMonostatAgiSkill || _advancedCastingSkillKey >= 0)
            return true;

        double now = NetworkTime.time;
        return now < _localMonostatStrSkillAttackLockUntil ||
               now < _localMonostatAgiSkillAttackLockUntil ||
               now < _localAdvancedAttackLockUntil;

    }

    private bool IsMonostatStr()
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();

        if (_statManager == null)
            return false;

        Identity identity = _statManager.CurrentIdentity;
        return identity.Type == IdentityType.Monostat && identity.PrimaryStat == StatKind.STR;
    }

    private bool IsMonostatAgi()
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();

        if (_statManager == null)
            return false;

        Identity identity = _statManager.CurrentIdentity;
        return identity.Type == IdentityType.Monostat && identity.PrimaryStat == StatKind.AGI;
    }

    private bool IsMonostat(StatKind statKind)
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();
        return _statManager != null && _statManager.CurrentIdentity.Type == IdentityType.Monostat &&
               _statManager.CurrentIdentity.PrimaryStat == statKind;
    }

    private bool IsPolymath()
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();
        return _statManager != null && _statManager.CurrentIdentity.Type == IdentityType.Polymath;
    }

    private JobSkillData ResolveSelectedAdvancedSkillData()
    {
        if (_statManager == null)
            return null;
        Identity identity = _statManager.CurrentIdentity;
        if (identity.Type == IdentityType.Monostat)
        {
            if (identity.PrimaryStat == StatKind.CON) return MonostatConSkillData;
            if (identity.PrimaryStat == StatKind.DEF) return MonostatDefSkillData;
            return null;
        }
        if (identity.Type == IdentityType.Strategist)
            return _selectedSkillIndex == 0
                ? (IsSkillDataKind(_strategistRollSkillData, JobSkillKind.StrategistRoll) ? _strategistRollSkillData : null)
                : (IsSkillDataKind(_strategistPresetSkillData, JobSkillKind.StrategistPresetChange) ? _strategistPresetSkillData : null);
        if (identity.Type == IdentityType.Polymath)
        {
            if (_selectedSkillIndex == 0) return IsSkillDataKind(_polymathRollSkillData, JobSkillKind.PolymathRoll) ? _polymathRollSkillData : null;
            if (_selectedSkillIndex == 1) return IsSkillDataKind(_polymathPresetSkillData, JobSkillKind.PolymathPresetChange) ? _polymathPresetSkillData : null;
            return IsSkillDataKind(_polymathWeaponSwapSkillData, JobSkillKind.PolymathWeaponSwap) ? _polymathWeaponSwapSkillData : null;
        }
        return null;
    }

    private JobSkillData ResolveAdvancedSkillData(int skillKey)
    {
        if (_statManager == null)
            return null;
        Identity identity = _statManager.CurrentIdentity;
        JobSkillKind kind = (JobSkillKind)skillKey;
        if (identity.Type == IdentityType.Monostat)
        {
            if (identity.PrimaryStat == StatKind.CON && kind == JobSkillKind.MonostatConKick) return MonostatConSkillData;
            if (identity.PrimaryStat == StatKind.DEF && kind == JobSkillKind.MonostatDefTaunt) return MonostatDefSkillData;
            return null;
        }
        if (identity.Type == IdentityType.Strategist)
        {
            if (kind == JobSkillKind.StrategistRoll && IsSkillDataKind(_strategistRollSkillData, kind)) return _strategistRollSkillData;
            if (kind == JobSkillKind.StrategistPresetChange && IsSkillDataKind(_strategistPresetSkillData, kind)) return _strategistPresetSkillData;
            return null;
        }
        if (identity.Type == IdentityType.Polymath)
        {
            if (kind == JobSkillKind.PolymathRoll && IsSkillDataKind(_polymathRollSkillData, kind)) return _polymathRollSkillData;
            if (kind == JobSkillKind.PolymathPresetChange && IsSkillDataKind(_polymathPresetSkillData, kind)) return _polymathPresetSkillData;
            if (kind == JobSkillKind.PolymathWeaponSwap && IsSkillDataKind(_polymathWeaponSwapSkillData, kind)) return _polymathWeaponSwapSkillData;
        }
        return null;
    }

    private void HandleBowAttackInput(bool pressed)
    {
        JobSkillData bow = _polymathWeaponSwapSkillData;
        if (bow == null)
            return;
        if (pressed)
        {
            if (IsSkillCastingOrAttackLocked() || _bowChargeStartedAt >= 0d)
                return;
            _bowChargeStartedAt = NetworkTime.time;
            _playerManager?.ApplySkillMoveMultiplier(bow.BowChargeMoveMultiplier, 86400f);
            return;
        }
        if (_bowChargeStartedAt < 0d)
            return;
        float chargeSeconds = Mathf.Max(0f, (float)(NetworkTime.time - _bowChargeStartedAt));
        _bowChargeStartedAt = -1d;
        _playerManager?.ApplySkillMoveMultiplier(1f, 0f);
        Vector3 direction = GetCurrentAimDirection();
        if (isClient && isLocalPlayer && !isServer)
            CmdFireBow(chargeSeconds, direction);
        else
            FireBow(chargeSeconds, direction);
    }

    [Command]
    private void CmdFireBow(float chargeSeconds, Vector3 direction)
    {
        FireBow(chargeSeconds, direction);
    }

    private void FireBow(float chargeSeconds, Vector3 direction)
    {
        JobSkillData bow = _polymathWeaponSwapSkillData;
        if (!IsPolymath() || !_isBowEquipped || bow == null || chargeSeconds < bow.MinimumBowChargeSeconds)
            return;
        float denominator = Mathf.Max(0.001f, bow.MaximumBowDamageChargeSeconds - bow.MinimumBowChargeSeconds);
        float t = Mathf.Clamp01((chargeSeconds - bow.MinimumBowChargeSeconds) / denominator);
        float multiplier = Mathf.Lerp(bow.MinimumBowDamageMultiplier, bow.MaximumBowDamageMultiplier, t);
        Vector3 origin = transform.position + Vector3.up;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, bow.BowRange, ~0, QueryTriggerInteraction.Collide))
            return;
        if (hit.transform.root == transform.root)
            return;
        IDamageReceiver target = hit.collider.GetComponentInParent<IDamageReceiver>();
        StatManager targetStats = hit.collider.GetComponentInParent<StatManager>();
        if (target != null && targetStats != null)
            _attackProcessor?.ProcessSkillHit(multiplier, targetStats, target, hit.point);
    }

    private int ResolveAvailableSkillCount()
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();

        if (_statManager == null)
            return 0;

        Identity identity = _statManager.CurrentIdentity;
        if (identity.Type == IdentityType.Monostat)
            return 1;

        if (identity.Type == IdentityType.Strategist)
            return 2;

        if (identity.Type == IdentityType.Polymath)
            return 3;

        return 0;
    }

    private string ResolveSelectedSkillName()
    {
        if (IsMonostatStr())
            return ResolveMonostatStrDisplayName();

        if (IsMonostatAgi())
            return ResolveMonostatAgiDisplayName();

        JobSkillData advanced = ResolveSelectedAdvancedSkillData();
        if (advanced != null && !string.IsNullOrWhiteSpace(advanced.DisplayName))
            return advanced.DisplayName;

        if (_statManager == null)
            return string.Empty;

        Identity identity = _statManager.CurrentIdentity;
        if (identity.Type == IdentityType.Strategist)
            return _selectedSkillIndex == 0 ? "구르기" : "프리셋";

        if (identity.Type == IdentityType.Polymath)
        {
            if (_selectedSkillIndex == 0) return "구르기";
            if (_selectedSkillIndex == 1) return "프리셋";
            return "무기";
        }

        return string.Empty;
    }

    private string ResolveMonostatStrDisplayName()
    {
        if (MonostatStrSkillData != null && !string.IsNullOrWhiteSpace(MonostatStrSkillData.DisplayName))
            return MonostatStrSkillData.DisplayName;

        return "흡혈";
    }

    private float ResolveMonostatStrLifestealRatio()
    {
        return MonostatStrSkillData != null && MonostatStrSkillData.LifestealRatio > 0f
            ? MonostatStrSkillData.LifestealRatio
            : MonostatStrSkillHealRatio;
    }

    private string ResolveMonostatAgiDisplayName()
    {
        if (MonostatAgiSkillData != null && !string.IsNullOrWhiteSpace(MonostatAgiSkillData.DisplayName))
            return MonostatAgiSkillData.DisplayName;

        return "독 바르기";
    }

    private void ClampSelectedSkillIndex()
    {
        int count = ResolveAvailableSkillCount();
        if (count <= 0)
        {
            _selectedSkillIndex = 0;
            return;
        }

        _selectedSkillIndex = Mathf.Clamp(_selectedSkillIndex, 0, count - 1);
    }

    public SkillHudState GetSkillHudState()
    {
        ClampSelectedSkillIndex();
        int count = ResolveAvailableSkillCount();
        if (count <= 0)
            return new SkillHudState(false, string.Empty, 0, 0, SkillHudPhase.Hidden, 0f, 0f);

        SkillHudPhase phase = SkillHudPhase.Ready;
        float fill = 0f;
        float remaining = 0f;
        Sprite iconSprite = null;

        if (IsMonostatStr() && _selectedSkillIndex == 0)
        {
            if (MonostatStrSkillData != null)
                iconSprite = MonostatStrSkillData.IconSprite;

            double now = NetworkTime.time;
            if (_isCastingMonostatStrSkill)
            {
                phase = SkillHudPhase.Casting;
                remaining = Mathf.Max(0f, (float)(_monostatStrSkillCastCompleteAt - now));
                fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, MonostatStrCastSeconds));
            }
            else if (now < _monostatStrSkillActiveUntil)
            {
                phase = SkillHudPhase.Active;
                remaining = Mathf.Max(0f, (float)(_monostatStrSkillActiveUntil - now));
                fill = 1f;
            }
            else if (now < _monostatStrSkillCooldownUntil)
            {
                phase = SkillHudPhase.Cooldown;
                remaining = Mathf.Max(0f, (float)(_monostatStrSkillCooldownUntil - now));
                fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, MonostatStrCooldownSeconds));
            }
        }
        else if (IsMonostatAgi() && _selectedSkillIndex == 0)
        {
            if (MonostatAgiSkillData != null)
                iconSprite = MonostatAgiSkillData.IconSprite;

            double now = NetworkTime.time;
            if (_isCastingMonostatAgiSkill)
            {
                phase = SkillHudPhase.Casting;
                remaining = Mathf.Max(0f, (float)(_monostatAgiSkillCastCompleteAt - now));
                fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, MonostatAgiCastSeconds));
            }
            else if (now < _monostatAgiSkillActiveUntil)
            {
                phase = SkillHudPhase.Active;
                remaining = Mathf.Max(0f, (float)(_monostatAgiSkillActiveUntil - now));
                fill = 1f;
            }
            else if (now < _monostatAgiSkillCooldownUntil)
            {
                phase = SkillHudPhase.Cooldown;
                remaining = Mathf.Max(0f, (float)(_monostatAgiSkillCooldownUntil - now));
                fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, MonostatAgiCooldownSeconds));
            }
        }
        else
        {
            JobSkillData data = ResolveSelectedAdvancedSkillData();
            if (data != null)
            {
                int key = (int)data.SkillKind;
                iconSprite = data.IconSprite;
                double now = NetworkTime.time;
                if (_advancedCastingSkillKey == key)
                {
                    phase = SkillHudPhase.Casting;
                    remaining = Mathf.Max(0f, (float)(_advancedCastCompleteAt - now));
                    fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, data.CastSeconds));
                }
                else if (_advancedActiveSkillKey == key && now < _advancedActiveUntil)
                {
                    phase = SkillHudPhase.Active;
                    remaining = Mathf.Max(0f, (float)(_advancedActiveUntil - now));
                    fill = 1f;
                }
                else if (_advancedCooldownUntil.TryGetValue(key, out double cooldownUntil) && now < cooldownUntil)
                {
                    phase = SkillHudPhase.Cooldown;
                    remaining = Mathf.Max(0f, (float)(cooldownUntil - now));
                    fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, data.CooldownSeconds));
                }
            }
        }

        return new SkillHudState(true, ResolveSelectedSkillName(), _selectedSkillIndex, count, phase, fill, remaining, iconSprite);
    }

    private void PublishSkillHudState()
    {
        SkillHudChanged?.Invoke(GetSkillHudState());
    }

    private void PlaySkillSfx(int skillId)
    {
        if (NetworkServer.active)
        {
            RpcPlaySkillSfx(skillId);
            return;
        }

        PlaySkillSfxLocal(skillId);
    }

    [ClientRpc]
    private void RpcPlaySkillSfx(int skillId)
    {
        PlaySkillSfxLocal(skillId);
    }

    private void PlaySkillSfxLocal(int skillId)
    {
        JobSkillData skillData = ResolveSkillDataForSfx(skillId);
        AudioClip clip = skillData != null ? skillData.UseSfx : null;
        if (clip == null || _audioSource == null)
            return;

        float volume = skillData != null ? skillData.SfxVolume : 1f;
        _audioSource.PlayOneShot(clip, volume);
    }

    private JobSkillData ResolveSkillDataForSfx(int skillId)
    {
        return skillId switch
        {
            (int)JobSkillKind.MonostatStrLifesteal => MonostatStrSkillData,
            (int)JobSkillKind.MonostatAgiPoison => MonostatAgiSkillData,
            (int)JobSkillKind.MonostatConKick => MonostatConSkillData,
            (int)JobSkillKind.MonostatDefTaunt => MonostatDefSkillData,
            (int)JobSkillKind.StrategistRoll => _strategistRollSkillData,
            (int)JobSkillKind.StrategistPresetChange => _strategistPresetSkillData,
            (int)JobSkillKind.PolymathRoll => _polymathRollSkillData,
            (int)JobSkillKind.PolymathPresetChange => _polymathPresetSkillData,
            (int)JobSkillKind.PolymathWeaponSwap => _polymathWeaponSwapSkillData,
            _ => null
        };
    }

    private void ApplyMonostatAgiPoisonStack(IDamageReceiver target, Vector3 hitPosition)
    {
        if (!NetworkServer.active)
            return;

        if (!IsValidPoisonTarget(target))
            return;

        PoisonStackState state = null;
        for (int i = 0; i < _monostatAgiPoisonStacks.Count; i++)
        {
            if (_monostatAgiPoisonStacks[i].Target == target)
            {
                state = _monostatAgiPoisonStacks[i];
                break;
            }
        }

        if (state == null)
        {
            state = new PoisonStackState { Target = target };
            _monostatAgiPoisonStacks.Add(state);
        }

        state.StackCount = Mathf.Min(state.StackCount + 1, MonostatAgiPoisonMaxStackCount);
        state.ExpiresAt = NetworkTime.time + MonostatAgiPoisonStackDurationSecondsValue;
        state.LastHitPosition = hitPosition;

        if (_monostatAgiPoisonRoutine == null)
            _monostatAgiPoisonRoutine = StartCoroutine(CoMonostatAgiPoisonTick());
    }

    private System.Collections.IEnumerator CoMonostatAgiPoisonTick()
    {
        while (_monostatAgiPoisonStacks.Count > 0)
        {
            double now = NetworkTime.time;
            float deltaTime = Time.deltaTime;
            float damagePerStack = MonostatAgiPoisonDamagePerStackPerSecondValue;

            for (int i = _monostatAgiPoisonStacks.Count - 1; i >= 0; i--)
            {
                PoisonStackState state = _monostatAgiPoisonStacks[i];
                if (state == null || !IsValidPoisonTarget(state.Target) || now >= state.ExpiresAt)
                {
                    _monostatAgiPoisonStacks.RemoveAt(i);
                    continue;
                }

                float damage = state.StackCount * damagePerStack * deltaTime;
                if (damage <= 0f)
                    continue;

                if (_healthSystem == null)
                    _healthSystem = GetComponent<HealthSystem>();

                if (state.Target is IDamageReceiverWithContext ctx)
                    ctx.ApplyDamage(damage, DamageSource.Poison, 0f, _healthSystem, state.LastHitPosition);
                else
                    state.Target.ApplyDamage(damage, DamageSource.Poison, state.LastHitPosition);
            }

            yield return null;
        }

        _monostatAgiPoisonRoutine = null;
    }

    private static bool IsValidPoisonTarget(IDamageReceiver target)
    {
        if (target == null)
            return false;

        if (target is MonoBehaviour mb && mb == null)
            return false;

        if (target.CurrentHp <= 0f)
            return false;

        if (target is HealthSystem health && health.IsDead)
            return false;

        return true;
    }

    private void HandleRevived()
    {
        if (animator != null)
            animator.speed = 1.0f;

        DisableHitBox();
        isAttacking = false;
        hasComboReserved = false;
        currentComboIndex = 0;
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

    private Vector3 GetCurrentAimDirection()
    {
        Vector3 fallback;
        if (_followCamera == null && isLocalPlayer)
            _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();

        if (_followCamera != null)
            fallback = _followCamera.GetAimDirection();
        else
            fallback = transform.forward;

        return ResolveTauntAimDirection(fallback);
    }

    private void SetTauntedBy(uint taunterNetId, float durationSeconds)
    {
        if (!NetworkServer.active || durationSeconds <= 0f)
            return;
        _tauntedByNetId = taunterNetId;
        _tauntedUntil = NetworkTime.time + durationSeconds;
    }

    private Vector3 ResolveTauntAimDirection(Vector3 fallback)
    {
        if (_tauntedByNetId == 0 || NetworkTime.time >= _tauntedUntil)
            return fallback;

        NetworkIdentity targetIdentity = null;
        if (NetworkServer.active)
            NetworkServer.spawned.TryGetValue(_tauntedByNetId, out targetIdentity);
        if (targetIdentity == null && NetworkClient.active)
            NetworkClient.spawned.TryGetValue(_tauntedByNetId, out targetIdentity);
        if (targetIdentity == null)
            return fallback;

        Vector3 direction = targetIdentity.transform.position - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : fallback;
    }
}
