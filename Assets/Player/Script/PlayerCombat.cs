using BattlePvp.Combat;
using BattlePvp.Stats;
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using BattlePvp.Managers;
using BattlePvp.UI;

/// <summary>
/// Owns combat requests, authoritative action state, and action coroutine lifetimes.
/// Presentation helpers receive explicit display inputs and never mutate that state.
/// Death, revival, and disable all terminate owned actions through CancelAllCombatActions.
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    private enum AuraPrimitiveShape
    {
        Sphere,
        Capsule,
        Cube
    }

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

    [Header("Identity / Weapon Visuals")]
    [SerializeField] private GameObject _backBowVisual;
    [SerializeField] private GameObject _backQuiverVisual;
    [SerializeField] private GameObject _handBowVisual;
    [SerializeField] private GameObject _handSwordVisual;
    [SerializeField] private GameObject _hipSwordVisual;
    [SerializeField] private BowAttackController _bowAttackController;

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

    [Header("Job Skill Hit Boxes")]
    [SerializeField] private KickSkillHitBox _kickHitBox;

    [Header("Network Hit Validation")]
    [SerializeField, Min(1f)] private float _remoteMeleeValidationDistance = 7.5f;

    [Header("Network Action Timing")]
    [SerializeField, Range(0.05f, 0.5f)] private float _maxActionRewindSeconds = 0.25f;
    [SerializeField, Range(0f, 0.1f)] private float _maxFutureActionLeadSeconds = 0.05f;
    [SerializeField, Range(0.5f, 3f)] private float _serverAttackReportGraceSeconds = 2f;

    [Header("Strategist Preset Aura")]
    [SerializeField] private Material _strategistStrAuraMaterial;
    [SerializeField] private Material _strategistAgiAuraMaterial;
    [SerializeField] private Material _strategistConAuraMaterial;
    [SerializeField] private Material _strategistDefAuraMaterial;
    [SerializeField] private AuraPrimitiveShape _strategistStrAuraShape = AuraPrimitiveShape.Sphere;
    [SerializeField] private bool _fitStrategistStrAuraToPlayer = true;
    [SerializeField] private bool _preferCharacterControllerAuraBounds = true;
    [SerializeField] private Vector3 _strategistStrAuraScale = new Vector3(1.25f, 1.15f, 1.25f);
    [SerializeField] private Vector3 _strategistStrAuraOffset = Vector3.zero;

    [Header("Runtime Status (Read Only)")]
    [SerializeField] private float _currentAttackSpeed = 1.0f;
    [SerializeField] private int _selectedSkillIndex;
    [SyncVar]
    [SerializeField] private bool _isCastingMonostatStrSkill;
    [SyncVar]
    [SerializeField] private double _monostatStrSkillCastCompleteAt;
    [SyncVar(hook = nameof(OnSkillSwordVisualStateChanged))]
    [SerializeField] private double _monostatStrSkillActiveUntil;
    [SyncVar]
    [SerializeField] private double _monostatStrSkillCooldownUntil;
    [SyncVar]
    [SerializeField] private bool _isCastingMonostatAgiSkill;
    [SyncVar]
    [SerializeField] private double _monostatAgiSkillCastCompleteAt;
    [SyncVar(hook = nameof(OnSkillSwordVisualStateChanged))]
    [SerializeField] private double _monostatAgiSkillActiveUntil;
    [SyncVar]
    [SerializeField] private double _monostatAgiSkillCooldownUntil;
    [SyncVar] [SerializeField] private int _advancedCastingSkillKey = -1;
    [SyncVar] [SerializeField] private double _advancedCastCompleteAt;
    [SyncVar(hook = nameof(OnAdvancedActiveSkillKeyChanged))] [SerializeField] private int _advancedActiveSkillKey = -1;
    [SyncVar(hook = nameof(OnSkillSwordVisualStateChanged))] [SerializeField] private double _advancedActiveUntil;
    [SyncVar(hook = nameof(OnBowEquippedChanged))] [SerializeField] private bool _isBowEquipped;
    [SyncVar] [SerializeField] private uint _tauntedByNetId;
    [SyncVar] [SerializeField] private double _tauntedUntil;
    private readonly SyncDictionary<int, double> _advancedCooldownUntil = new SyncDictionary<int, double>();
    private readonly Dictionary<int, double> _offlineAdvancedCooldownUntil = new Dictionary<int, double>();

    private int currentComboIndex;
    private bool isAttacking;
    private bool hasComboReserved;
    private bool _isPointerOverUI;
    private readonly HashSet<IDamageReceiver> _hitTargetsThisAttack = new HashSet<IDamageReceiver>();
    private uint _nextLocalAttackSequence;
    private uint _currentAttackSequence;
    [SyncVar] private uint _lastServerAttackSequence;
    private uint _lastRemoteAttackSequence;
    private uint _serverReportableAttackSequence;
    private int _serverReportableAttackIndex = -1;
    private double _serverAttackReportExpiresAt;
    private readonly HashSet<uint> _serverAttackHitTargetNetIds = new HashSet<uint>();
    private const double DuplicateAttackInputWindowSeconds = 0.075d;
    private double _lastAttackPressedAt = double.NegativeInfinity;
    private uint _nextLocalSkillSequence;
    [SyncVar] private uint _lastServerSkillSequence;
    private uint _lastRemoteSkillSequence;

    private Animator animator;
    private PlayerInput _playerInput;
    private Coroutine _comboRoutine;
    private HealthSystem _healthSystem;
    private PlayerManager _playerManager;
    private BattlePvp.CameraLogic.FollowCamera _followCamera;
    private Coroutine _monostatStrSkillRoutine;
    private Coroutine _monostatAgiSkillRoutine;
    private Coroutine _monostatAgiPoisonRoutine;
    private readonly CombatActionLocks _actionLocks = new CombatActionLocks();
    private AttackProcessor _attackProcessor;
    private ServerPoseHistory _serverPoseHistory;
    private Coroutine _advancedSkillRoutine;
    private Coroutine _localSkillAnimationAttackLockRoutine;
    private int _pendingAdvancedSkillHitKey = -1;
    private Vector3 _pendingAdvancedSkillDirection;
    private bool _isKickHitBoxEnabled;
    private readonly KickHitWindowAuthority _kickWindow = new KickHitWindowAuthority();
    private bool _kickClosePending;
    private readonly HashSet<IDamageReceiver> _kickHitTargets = new HashSet<IDamageReceiver>();
    [SyncVar] private float _nextAttackDamageMultiplier = 1f;
    [SyncVar] private float _attackPowerBonusMultiplier = 1f;
    [SyncVar] private double _attackPowerBonusUntil;
    [SyncVar] private float _attackSpeedBonusMultiplier = 1f;
    [SyncVar] private double _attackSpeedBonusUntil;
    [SyncVar] private double _skillMoveBonusUntil;
    [SyncVar] private int _acceptedSkillAnimationKey = -1;
    [SyncVar] private double _acceptedSkillStartedAt;
    [SyncVar] private double _acceptedSkillAnimationUntil;
    [SyncVar] private SkillInputLockFlags _acceptedSkillInputFlags;
    [SyncVar] private double _acceptedSkillInputUntil;
    private double _appliedAcceptedInputUntil;
    private SkillInputLockFlags _appliedAcceptedInputFlags;
    private StatContainer _runtimeStrategistTargetPreset;
    private bool _hasRuntimeStrategistTargetPreset;
    private StatContainer _strategistSwapReturnPreset;
    private bool _hasStrategistSwapReturnPreset;
    private readonly PoisonStackCollection<IDamageReceiver, Vector3> _poisonStacks = new PoisonStackCollection<IDamageReceiver, Vector3>();
    private readonly List<PoisonTick<IDamageReceiver, Vector3>> _poisonTicks = new List<PoisonTick<IDamageReceiver, Vector3>>();
    private bool _restoredOwnerCastMovement;
    private bool _localTauntControlActive;
    private bool _serverTauntBowActive;
    private readonly ServerComboSequence _serverCombo = new ServerComboSequence();

    private CombatSkillPresentation _skillPresentation;
    private SkillAuraPresentation _auraPresentation;
    private readonly SkillHudPresenter _skillHudPresenter = new SkillHudPresenter();
    private readonly JobSkillData[] _describedSkills = new JobSkillData[2];
    private readonly string[] _skillDescriptions = new string[2];
    private bool _swingSoundPlayed;

    private CombatSkillPresentation SkillPresentation =>
        _skillPresentation ??= new CombatSkillPresentation(gameObject, animator);

    private SkillAuraPresentation AuraPresentation =>
        _auraPresentation ??= new SkillAuraPresentation(transform, GetComponent<CharacterController>(),
            new SkillAuraSettings(_strategistStrAuraMaterial, _strategistAgiAuraMaterial,
                _strategistConAuraMaterial, _strategistDefAuraMaterial,
                (SkillAuraShape)_strategistStrAuraShape, _fitStrategistStrAuraToPlayer,
                _preferCharacterControllerAuraBounds, _strategistStrAuraScale, _strategistStrAuraOffset));

    public event Action<SkillHudState> SkillHudChanged;
    public bool IsBusyForEmote => IsSkillCastingOrAttackLocked() || isAttacking || (_bowAttackController != null && _bowAttackController.IsBusy);
    private double SkillTime => NetworkServer.active || NetworkClient.isConnected ? NetworkTime.time : Time.timeAsDouble;
    private bool ShouldHandleLocalInput =>
        (NetworkClient.active && isLocalPlayer) ||
        (!NetworkClient.active && !NetworkServer.active && !isClient && !isServer);
    public bool IsMonostatStrLifestealActive => SkillTime < _monostatStrSkillActiveUntil;
    public float MonostatStrSkillLifestealRatio => ResolveMonostatStrLifestealRatio();
    public float MonostatStrMoveMultiplier => IsMonostatStrLifestealActive ? (MonostatStrSkillData != null ? MonostatStrSkillData.StrMoveMultiplier : 1.1f) : 1f;
    public bool IsMonostatAgiPoisonCoatingActive => SkillTime < _monostatAgiSkillActiveUntil;
    public bool HasAttackPowerSkillBonus => SkillTime < _attackPowerBonusUntil && _attackPowerBonusMultiplier > 1f;
    public bool HasAttackSpeedSkillBonus => SkillTime < _attackSpeedBonusUntil && _attackSpeedBonusMultiplier > 1f;
    public bool HasMovementSkillBonus => SkillTime < _skillMoveBonusUntil;
    public bool HasNextAttackSkillBonus => _nextAttackDamageMultiplier > 1f;
    public float AttackPowerBonusMultiplier => (SkillTime < _attackPowerBonusUntil ? Mathf.Max(0f, _attackPowerBonusMultiplier) : 1f) * (GetComponent<ExpandedSkillController>()?.AttackMultiplier ?? 1f);
    public uint CurrentAttackPredictionId => _currentAttackSequence;
    public bool IsAttackActive => isAttacking;
    private MeleeAimPose _meleeAimPose;
    private Vector3 _meleeAimDirection;
    private float _meleeAimReach;
    private readonly CombatPhysicsQuery _meleeAimQuery = new CombatPhysicsQuery();
    // Network aim vectors carry direction and the requested blade radius; reference selection clamps reach.
    private Vector3 MeleeAimVector => _meleeAimDirection * _meleeAimReach;
    private float _meleeAimWeight;
    [SyncVar] private float _networkLookPitch;
    private float _lookPitch, _lookPoseWeight, _sentLookPitch = float.NaN;
    private double _nextLookSendAt, _nextLookReceiveAt;
    private uint _meleeAimRevision, _acceptedMeleeAimRevision;
    private double _nextMeleeAimSendAt, _nextMeleeAimReceiveAt;
    public Vector3 MeleeAimDirection => _meleeAimDirection.sqrMagnitude > .001f ? _meleeAimDirection : transform.forward;
    public Vector3 MeleeAimPivot => _meleeAimPose != null ? _meleeAimPose.Pivot : transform.position + transform.up * 1.2f;
    private AttackData _meleeAnimationData;
    private AttackData CurrentMeleeData => _meleeAnimationData != null ? _meleeAnimationData :
        comboList != null && currentComboIndex >= 0 && currentComboIndex < comboList.Length ? comboList[currentComboIndex] : null;
    public bool IsServerTaunted => _tauntedByNetId != 0 && SkillTime < _tauntedUntil;

    public JobSkillData ServerBowData => _polymathWeaponSwapSkillData;
    public bool IsAimingBow => _bowAttackController != null && _bowAttackController.IsBusy;
    internal Vector3 ReplicatedLookDirection => Quaternion.AngleAxis(_networkLookPitch, transform.right) * transform.forward;
    private bool HasAuthoritativeCombatStats => !NetworkServer.active ||
        (_statManager != null && _statManager.HasServerStats);
    public bool CanServerUseBow => NetworkServer.active && isActiveAndEnabled &&
        HasAuthoritativeCombatStats &&
        _healthSystem != null && !_healthSystem.IsDead && IsPolymath() && _isBowEquipped &&
        _polymathWeaponSwapSkillData != null && !IsBattleLoadingOrNotStarted() &&
        (IsServerTaunted || !IsSkillCastingOrAttackLocked());

    private JobSkillData MonostatStrSkillData => IsSkillDataKind(_monostatStrSkillData, JobSkillKind.MonostatStrLifesteal) ? _monostatStrSkillData : null;
    private JobSkillData MonostatAgiSkillData => IsSkillDataKind(_monostatAgiSkillData, JobSkillKind.MonostatAgiPoison) ? _monostatAgiSkillData : null;
    private JobSkillData MonostatConSkillData => IsSkillDataKind(_monostatConSkillData, JobSkillKind.MonostatConKick) ? _monostatConSkillData : null;
    private JobSkillData MonostatDefSkillData => IsSkillDataKind(_monostatDefSkillData, JobSkillKind.MonostatDefTaunt) ? _monostatDefSkillData : null;
    private float MonostatStrCastSeconds => MonostatStrSkillData != null ? MonostatStrSkillData.CastSeconds : MonostatStrSkillCastSeconds;
    private float MonostatStrDurationSeconds => MonostatStrSkillData != null ? MonostatStrSkillData.DurationSeconds : MonostatStrSkillDurationSeconds;
    private float MonostatStrCooldownSeconds => MonostatStrSkillData != null ? MonostatStrSkillData.CooldownSeconds : MonostatStrSkillCooldownSeconds;
    private float MonostatAgiCastSeconds => MonostatAgiSkillData != null ? MonostatAgiSkillData.CastSeconds : MonostatAgiSkillCastSeconds;
    private float MonostatAgiDurationSeconds => MonostatAgiSkillData != null ? MonostatAgiSkillData.DurationSeconds : MonostatAgiSkillDurationSeconds;
    private float MonostatAgiCooldownSeconds => MonostatAgiSkillData != null ? MonostatAgiSkillData.CooldownSeconds : MonostatAgiSkillCooldownSeconds;
    private int MonostatAgiPoisonMaxStackCount => MonostatAgiSkillData != null && MonostatAgiSkillData.PoisonMaxStacks > 0 ? MonostatAgiSkillData.PoisonMaxStacks : MonostatAgiPoisonMaxStacks;
    private float MonostatAgiPoisonDamagePerStackPerSecondValue => MonostatAgiSkillData != null && MonostatAgiSkillData.PoisonDamagePerStackPerSecond > 0f ? MonostatAgiSkillData.PoisonDamagePerStackPerSecond : MonostatAgiPoisonDamagePerStackPerSecond;
    private float MonostatAgiPoisonStackDurationSecondsValue => MonostatAgiSkillData != null && MonostatAgiSkillData.PoisonStackDurationSeconds > 0f ? MonostatAgiSkillData.PoisonStackDurationSeconds : MonostatAgiPoisonStackDurationSeconds;

    // Existing SyncVars remain the serialization boundary; lifetime decisions live in the pure value type.
    private CombatSkillExecution StrengthExecution
    {
        get => new CombatSkillExecution(_isCastingMonostatStrSkill, _monostatStrSkillCastCompleteAt,
            _monostatStrSkillActiveUntil, _monostatStrSkillCooldownUntil);
        set
        {
            _isCastingMonostatStrSkill = value.IsCasting;
            _monostatStrSkillCastCompleteAt = value.CastCompleteAt;
            _monostatStrSkillActiveUntil = value.ActiveUntil;
            _monostatStrSkillCooldownUntil = value.CooldownUntil;
        }
    }

    private CombatSkillExecution AgilityExecution
    {
        get => new CombatSkillExecution(_isCastingMonostatAgiSkill, _monostatAgiSkillCastCompleteAt,
            _monostatAgiSkillActiveUntil, _monostatAgiSkillCooldownUntil);
        set
        {
            _isCastingMonostatAgiSkill = value.IsCasting;
            _monostatAgiSkillCastCompleteAt = value.CastCompleteAt;
            _monostatAgiSkillActiveUntil = value.ActiveUntil;
            _monostatAgiSkillCooldownUntil = value.CooldownUntil;
        }
    }

    private CombatOwnerAction AcceptedOwnerAction
    {
        get => new CombatOwnerAction(_acceptedSkillAnimationKey, _acceptedSkillStartedAt, _acceptedSkillAnimationUntil,
            (int)_acceptedSkillInputFlags, _acceptedSkillInputUntil);
        set
        {
            _acceptedSkillAnimationKey = value.SkillKey;
            _acceptedSkillStartedAt = value.StartedAt;
            _acceptedSkillAnimationUntil = value.AnimationUntil;
            _acceptedSkillInputFlags = (SkillInputLockFlags)value.InputFlags;
            _acceptedSkillInputUntil = value.InputUntil;
        }
    }

    private static bool IsSkillDataKind(JobSkillData data, JobSkillKind expectedKind)
    {
        return data != null && data.SkillKind == expectedKind;
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        _meleeAimPose = new MeleeAimPose(transform, animator);
        if (animator != null)
        {
            SkillAnimationEventRelay relay = animator.GetComponent<SkillAnimationEventRelay>();
            if (relay == null)
                relay = animator.gameObject.AddComponent<SkillAnimationEventRelay>();
            relay.Initialize(this);
        }
        _playerInput = GetComponent<PlayerInput>();
        if (_statManager == null) _statManager = GetComponentInParent<StatManager>();
        ResolveBowAttackController();
        ResolveWeaponVisualReferences();
        ApplyIdentityVisuals();
        _healthSystem = GetComponent<HealthSystem>();
        _playerManager = GetComponent<PlayerManager>();
        _attackProcessor = GetComponent<AttackProcessor>();
        _serverPoseHistory = GetComponent<ServerPoseHistory>();
        if (_serverPoseHistory == null)
            _serverPoseHistory = gameObject.AddComponent<ServerPoseHistory>();
        if (_kickHitBox == null)
            _kickHitBox = GetComponentInChildren<KickSkillHitBox>(true);
        if (_kickHitBox != null)
            _kickHitBox.Initialize(this);
        _skillPresentation = new CombatSkillPresentation(gameObject, animator);
        if (GetComponent<MeleeAimDriver>() == null) gameObject.AddComponent<MeleeAimDriver>();
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
            _healthSystem.OnDied += HandleDied;
            _healthSystem.OnRevived += HandleRevived;
        }

        if (_statManager != null)
        {
            _statManager.StatsChanged += OnStatsChanged;
            _statManager.IdentityChanged += OnIdentityChanged;
        }

        ResolveBowAttackController();
        ResolveWeaponVisualReferences();
        ApplyIdentityVisuals();

        PublishSkillHudState();
    }

    private void OnDisable()
    {
        _meleeAimPose?.Restore();
        _meleeAimWeight = 0;
        _lookPoseWeight = 0;
        _sentLookPitch = float.NaN;
        ClearServerTauntControl();
        CancelAllCombatActions();
        StopActionRoutine(ref _monostatAgiPoisonRoutine);
        _poisonStacks.Clear();
        _poisonTicks.Clear();
        _lastAttackPressedAt = double.NegativeInfinity;

        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= HandleDied;
            _healthSystem.OnRevived -= HandleRevived;
        }

        if (_statManager != null)
        {
            _statManager.StatsChanged -= OnStatsChanged;
            _statManager.IdentityChanged -= OnIdentityChanged;
        }

        ForceDisableHitBoxes();
        ForceDisableKickHitBox();
        _bowAttackController?.CancelCharge();
        _bowAttackController?.SetCrosshairVisible(false);
        _auraPresentation?.Cancel();
        SetSkillSwordVisual(null);
        ClearLocalTauntControl();
    }

    private void OnDestroy()
    {
        _skillPresentation?.Dispose();
        _auraPresentation?.Dispose();
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

        RestoreLocalOwnerCombatState();
        _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
        ApplyIdentityVisuals();
        PublishSkillHudState();
    }

    private void Update()
    {
        ApplyAcceptedOwnerInputLock();
        RefreshRestoredOwnerCastMovement();
        UpdateServerTauntControl();
        UpdateStrategistStrAura();
        RefreshSkillSwordVisualFromState();

        if (!ShouldHandleLocalInput)
            return;

        UpdateLocalTauntControl();
        UpdateLocalLookPitch();
        if (isAttacking) UpdateLocalMeleeAim();

        _isPointerOverUI = UnityEngine.EventSystems.EventSystem.current != null &&
                           UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        HandleBowReleaseFallback();
        PublishSkillHudState(false);
    }

    internal void RestoreMeleeAimPose() => _meleeAimPose?.Restore();

    public bool TrySampleMeleeMotion(float phase, out Pose pose) => MeleeMotionSample.TryEvaluate(
        CurrentMeleeData, transform, phase, MeleeAimVector,
        Quaternion.AngleAxis(_lookPitch * _lookPoseWeight, transform.right) * transform.forward,
        _meleeAimWeight, out pose);

    internal void UpdateMeleeAimPose()
    {
        if (_bowAttackController != null && _bowAttackController.ControlsAimPose)
        {
            _lookPoseWeight = _meleeAimWeight = 0f;
            return;
        }
        // Resolve after animation moved the hips/spine, using the same frame as the hit query.
        if (isAttacking && ShouldHandleLocalInput) SetMeleeAim(GetCurrentMeleeAimDirection());
        bool alive = _healthSystem == null || !_healthSystem.IsDead;
        bool lookEnabled = alive && BattlePvp.Logic.InputModeRules.UsesFpsLook(SceneManager.GetActiveScene().name) &&
            (_playerManager == null || !_playerManager.IsEmoteBlockingAttack) &&
            (_bowAttackController == null || !_bowAttackController.IsBusy) && !IsSkillCastingOrAttackLocked() &&
            (_expanded == null || !_expanded.Active(JobSkillKind.Fortify));
        _lookPoseWeight = Mathf.MoveTowards(_lookPoseWeight, lookEnabled ? 1f : 0f, Time.deltaTime * 12f);
        // Owners use this frame's camera pitch; observers smoothly follow the replicated pitch.
        _lookPitch = ShouldHandleLocalInput ? _networkLookPitch :
            Mathf.Lerp(_lookPitch, _networkLookPitch, 1f - Mathf.Exp(-Time.deltaTime * 20f));
        Vector3 lookDirection = Quaternion.AngleAxis(_lookPitch * _lookPoseWeight, transform.right) * transform.forward;
        _meleeAimWeight = alive ? Mathf.MoveTowards(_meleeAimWeight, isAttacking ? 1f : 0f, Time.deltaTime * 12f) : 0f;
        if (_meleeAimWeight > 0f)
        {
            var data = CurrentMeleeData;
            if (data != null && data.aimBladePoint.sqrMagnitude > .001f && animator != null)
                _meleeAimPose?.ApplyCalibrated(MeleeAimDirection, _meleeAimPose.SelectReference(data, _meleeAimReach),
                    Mathf.Clamp01(animator.GetCurrentAnimatorStateInfo(1).normalizedTime), _meleeAimWeight, lookDirection);
            else _meleeAimPose?.Apply(Vector3.Slerp(lookDirection, MeleeAimDirection, _meleeAimWeight));
        }
        else if (_lookPoseWeight > 0f) _meleeAimPose?.ApplyCalibrated(lookDirection, Vector3.forward, 0, 0, lookDirection);
    }

    private void UpdateLocalLookPitch()
    {
        if (!BattlePvp.Logic.InputModeRules.UsesFpsLook(SceneManager.GetActiveScene().name)) return;
        if (_followCamera == null) _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
        if (_followCamera == null || _followCamera.Target != transform) return;
        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(_followCamera.GetAimDirection().y, -1, 1)) * Mathf.Rad2Deg, -60, 60);
        _networkLookPitch = pitch;
        if (!NetworkClient.active || !NetworkClient.ready || !isLocalPlayer || isServer ||
            Time.unscaledTimeAsDouble < _nextLookSendAt ||
            (Mathf.Abs(pitch - _sentLookPitch) < .25f && Time.unscaledTimeAsDouble < _nextLookSendAt + .4d)) return;
        _nextLookSendAt = Time.unscaledTimeAsDouble + .1d;
        _sentLookPitch = pitch;
        CmdSetLookPitch(pitch);
    }

    [Command]
    private void CmdSetLookPitch(float pitch) => TryAcceptLookPitch(pitch, NetworkTime.time);

    private bool TryAcceptLookPitch(float pitch, double now)
    {
        if (!float.IsFinite(pitch) || pitch < -60 || pitch > 60 || now < _nextLookReceiveAt ||
            (_healthSystem != null && _healthSystem.IsDead)) return false;
        _nextLookReceiveAt = now + .05d;
        _networkLookPitch = pitch;
        return true;
    }

    private void SetMeleeAim(Vector3 direction)
    {
        if (!CombatValidation.IsFinite(direction) || float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .001f) return;
        _meleeAimDirection = direction.normalized;
        _meleeAimReach = direction.magnitude;
    }

    private void UpdateLocalMeleeAim()
    {
        SetMeleeAim(GetCurrentMeleeAimDirection());
        if (!NetworkClient.active || !NetworkClient.ready || !isLocalPlayer || Time.unscaledTimeAsDouble < _nextMeleeAimSendAt) return;
        _nextMeleeAimSendAt = Time.unscaledTimeAsDouble + .05d;
        _meleeAimRevision = CombatRequestSequences.Next(_meleeAimRevision);
        if (isServer) RpcUpdateMeleeAim(_currentAttackSequence, _meleeAimRevision, MeleeAimVector);
        else CmdUpdateMeleeAim(_currentAttackSequence, _meleeAimRevision, MeleeAimVector);
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdUpdateMeleeAim(uint sequence, uint revision, Vector3 direction)
    {
        if (TryAcceptMeleeAim(sequence, revision, direction, NetworkTime.time))
            RpcUpdateMeleeAim(sequence, revision, MeleeAimVector);
    }

    private bool TryAcceptMeleeAim(uint sequence, uint revision, Vector3 direction, double now)
    {
        if (!isAttacking || !HasAuthoritativeCombatStats || (_healthSystem != null && _healthSystem.IsDead) ||
            sequence != _currentAttackSequence || !IsNewerSequence(revision, _acceptedMeleeAimRevision) ||
            !CombatValidation.IsFinite(direction) || float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .001f ||
            now < _nextMeleeAimReceiveAt) return false;
        _nextMeleeAimReceiveAt = now + .025d;
        _acceptedMeleeAimRevision = revision;
        SetMeleeAim(ResolveTauntAimDirection(direction.normalized) * direction.magnitude);
        return true;
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcUpdateMeleeAim(uint sequence, uint revision, Vector3 direction)
    {
        if (isServer || !isAttacking || sequence != _lastRemoteAttackSequence ||
            !IsNewerSequence(revision, _acceptedMeleeAimRevision)) return;
        _acceptedMeleeAimRevision = revision;
        SetMeleeAim(direction);
    }

    private void HandleBowReleaseFallback()
    {
        if (IsServerTaunted) return;
        if (Mouse.current == null || !IsPolymath() || !_isBowEquipped)
            return;

        ResolveBowAttackController();
        if (_bowAttackController == null || !_bowAttackController.IsCharging)
            return;

        if (Mouse.current.leftButton.wasReleasedThisFrame)
            HandleBowAttackInput(false);
    }

    private bool _skillButtonRequest;
    private SkillLoadout _loadout;
    private ExpandedSkillController _expanded;
    public Vector3 SkillAimDirection => GetCurrentAimDirection();
    private bool TrySelectEquipped(int slot, out JobSkillKind kind)
    {
        if (_loadout == null) _loadout = GetComponent<SkillLoadout>();
        if (_loadout != null) return _loadout.Select(slot, out kind);
        kind = default; return _statManager != null && CombatSkillRules.TrySelect(_statManager.CurrentIdentity, slot, out kind);
    }
    public bool AllowsEquipped(JobSkillKind kind)
    {
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (_expanded != null && _expanded.AllowsBorrowed(kind)) return true;
        return HasEquippedOnly(kind);
    }
    private bool HasEquippedOnly(JobSkillKind kind) => (TrySelectEquipped(0, out var first) && first == kind) || (TrySelectEquipped(1, out var second) && second == kind);
    public void OnSkillLoadoutChanged()
    {
        CancelAllCombatActions();
        if (NetworkServer.active || !NetworkClient.active) _isBowEquipped = false;
        ApplyIdentityVisuals();
        PublishSkillHudState();
    }
    public bool CanBeginExpandedSkill => HasAuthoritativeCombatStats && !IsBattleLoadingOrNotStarted() &&
        (_healthSystem == null || !_healthSystem.IsDead) && !isAttacking && !IsAimingBow && !IsSkillCastingOrAttackLocked();
    public bool BeginCopiedLegacy(JobSkillKind kind)
    {
        if (!AllowsEquipped(kind) || !CanBeginExpandedSkill) return false;
        if (kind == JobSkillKind.MonostatStrLifesteal) { _monostatStrSkillCooldownUntil = SkillTime; return BeginMonostatStrSkill(SkillTime, NextSkillSequence()); }
        if (kind == JobSkillKind.MonostatAgiPoison) { _monostatAgiSkillCooldownUntil = SkillTime; return BeginMonostatAgiSkill(SkillTime, NextSkillSequence()); }
        SetAdvancedCooldownUntil((int)kind, SkillTime);
        return BeginAdvancedSkillCore((int)kind, SkillAimDirection, SkillTime, NextSkillSequence(), true);
    }
    public void AddKnifePoison(IDamageReceiver target, Vector3 position)
    {
        // ProcessSkillHit already applies the coating's stack. A knife hit must add one,
        // including when coating and knives are equipped together.
        if (!IsMonostatAgiPoisonCoatingActive) ApplyMonostatAgiPoisonStack(target, position);
    }
    public void OnSkill1(InputValue value) { if (value.isPressed) UseSkillSlot(0); }
    public void OnSkill2(InputValue value) { if (value.isPressed) UseSkillSlot(1); }
    public void UseSkillSlot(int index, bool fromHud = false)
    {
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (!fromHud && (_expanded == null || !_expanded.IsCharging) && WaitingRoomTerminal.ConsumesSkillInput(index)) return;
        if (!ShouldHandleLocalInput || index < 0 || index >= ResolveAvailableSkillCount()) return;
        if (_playerManager != null && (_playerManager.IsEmoteBlockingAttack || _playerManager.IsSkillAttackLocked)) return;
        _skillButtonRequest = fromHud;
        try
        {
            if (!CanUseSkillInput()) return;
            _selectedSkillIndex = index;
            TryUseSelectedSkill();
            PublishSkillHudState();
        }
        finally { _skillButtonRequest = false; }
    }

    public void OnSkill(InputValue value)
    {
        if (!ShouldHandleLocalInput) return;
        if (!value.isPressed) return;
        if (_playerManager != null && (_playerManager.IsEmoteBlockingAttack || _playerManager.IsSkillAttackLocked)) return;

        TryUseSelectedSkill();
    }

    private void OnStatsChanged(StatContainer _)
    {
        if (this == null) return;
        ClampSelectedSkillIndex();
        ApplyIdentityVisuals();
        PublishSkillHudState();
    }

    private void OnIdentityChanged(Identity _)
    {
        if (!IsPolymath()) _bowAttackController?.CancelCharge();
        ApplyIdentityVisuals();
    }

    private void ApplyIdentityVisuals()
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();

        bool isPolymath = _statManager != null && _statManager.CurrentIdentity.Type == IdentityType.Polymath;
        bool bowEquipped = isPolymath && _isBowEquipped;

        SetVisualActive(_backBowVisual, isPolymath && !bowEquipped);
        SetVisualActive(_backQuiverVisual, isPolymath);
        SetVisualActive(_handBowVisual, bowEquipped);
        SetVisualActive(_handSwordVisual, !bowEquipped);
        SetVisualActive(_hipSwordVisual, bowEquipped);
        ResolveBowAttackController();
        _bowAttackController?.SetCrosshairVisible(ShouldHandleLocalInput);
    }

    private void OnBowEquippedChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            _bowAttackController?.CancelCharge();

        ApplyIdentityVisuals();
    }

    private void ResolveBowAttackController()
    {
        if (_bowAttackController == null)
            _bowAttackController = GetComponent<BowAttackController>();
    }

    private void ResolveWeaponVisualReferences()
    {
        _backBowVisual ??= FindChildGameObject("Bow_01");
        _backQuiverVisual ??= FindChildGameObject("Quiver_Arrows_01");
        _handBowVisual ??= FindChildGameObject("Bow_hand");
        _handSwordVisual ??= FindChildGameObject("Sword");
        _hipSwordVisual ??= FindChildGameObject("Sword_Hip");
    }

    private GameObject FindChildGameObject(string childName)
    {
        Transform child = FindChildTransform(childName);
        return child != null ? child.gameObject : null;
    }

    private Transform FindChildTransform(string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
            return null;

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child != transform && child.name == childName)
                return child;
        }

        return null;
    }

    private static void SetVisualActive(GameObject visual, bool active)
    {
        if (visual != null && visual.activeSelf != active)
            visual.SetActive(active);
    }

    public void OnAttack(InputValue value)
    {
        HandleAttackInput(value.isPressed, false);
    }

    public void AttackFromHud(bool pressed) => HandleAttackInput(pressed, true);

    private void HandleAttackInput(bool pressed, bool fromHud)
    {
        if (!ShouldHandleLocalInput) return;
        if (!pressed)
        {
            if (IsPolymath() && _isBowEquipped)
                HandleBowAttackInput(false);
            return;
        }

        double pressedAt = Time.unscaledTimeAsDouble;
        if (pressedAt - _lastAttackPressedAt < DuplicateAttackInputWindowSeconds)
            return;

        _lastAttackPressedAt = pressedAt;
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (!BattlePvp.Logic.GameInputController.IsPaused && !BattlePvp.Logic.GameInputController.IsTextInputActive &&
            !_isPointerOverUI && _expanded != null && _expanded.HandleAttackInput()) return;
        if (_playerManager != null && _playerManager.IsSkillAttackLocked) return;
        if (IsPolymath() && _isBowEquipped)
        {
            HandleBowAttackInput(true, fromHud);
            return;
        }
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
        if (_playerManager != null && (_playerManager.IsEmoteBlockingAttack || _playerManager.IsSkillAttackLocked)) return;

        if (IsSkillCastingOrAttackLocked()) return;

        if (!fromHud && Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI)
            return;

        if (isAttacking)
        {
            hasComboReserved = true;
            return;
        }

        StartAttack(0, true, GetCurrentMeleeAimDirection());
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

    private void StartAttack(
        int index,
        bool notifyServer,
        Vector3 aimDirection,
        bool ignoreControlLocks = false)
    {
        if (!HasAuthoritativeCombatStats)
            return;
        if (_healthSystem != null && _healthSystem.IsDead)
            return;

        if (!ignoreControlLocks && _playerManager != null && (_playerManager.IsEmoteBlockingAttack || _playerManager.IsSkillAttackLocked))
            return;

        if (!ignoreControlLocks && IsSkillCastingOrAttackLocked())
            return;

        if (index < 0 || comboList == null || index >= comboList.Length || comboList[index] == null)
            return;

        bool sendOwnerRequest = notifyServer && isClient && isLocalPlayer && NetworkClient.active && NetworkClient.ready;
        if (sendOwnerRequest)
        {
            _currentAttackSequence = NextAttackSequence();
            if (isServer) _lastServerAttackSequence = _currentAttackSequence;
        }
        GetComponent<ExpandedSkillController>()?.NotifyAttackStarted();
        isAttacking = true;
        hasComboReserved = false;
        currentComboIndex = index;
        if (isServer) _serverCombo.Reset();
        _hitTargetsThisAttack.Clear();
        aimDirection = ResolveTauntAimDirection(aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : transform.forward) * Mathf.Max(.1f, aimDirection.magnitude);
        SetMeleeAim(aimDirection);
        _acceptedMeleeAimRevision = _meleeAimRevision = 0;
        _nextMeleeAimSendAt = _nextMeleeAimReceiveAt = 0;

        if (animator != null)
            animator.applyRootMotion = false;

        var pm = _playerManager != null ? _playerManager : GetComponent<PlayerManager>();
        if (pm != null)
            pm.SetMovementLock(true);

        if (_statManager != null)
        {
            _currentAttackSpeed = ResolveCurrentAttackSpeed();
            if (animator != null)
                animator.speed = _currentAttackSpeed;
        }

        foreach (var hb in _hitboxes)
        {
            if (hb != null)
            {
                hb.SetAttackData(comboList[index]);
            }
        }

        // Seeking frame zero can invoke hit events synchronously. Install data and deduplication first.
        if (isServer && _currentAttackSequence != 0)
            RegisterServerAcceptedAttack(_currentAttackSequence, index);
        if (sendOwnerRequest && !isServer)
            CmdStartAttack(index, aimDirection, _currentAttackSequence);
        if (animator != null)
            PlayAttackAnimation(index);
        if (!isAttacking) return;

        if (_comboRoutine != null)
            StopCoroutine(_comboRoutine);
        _comboRoutine = StartCoroutine(CoComboMonitor(index));

        if (sendOwnerRequest && isServer)
        {
            uint sequence = _currentAttackSequence;
            RpcStartAttackFast(index, aimDirection, sequence);
            RpcStartAttack(index, aimDirection, sequence);
        }
    }

    [Command]
    private void CmdStartAttack(
        int index,
        Vector3 aimDirection,
        uint sequence)
    {
        if (!HasAuthoritativeCombatStats || !CombatValidation.IsFinite(aimDirection) || float.IsInfinity(aimDirection.sqrMagnitude) || aimDirection.sqrMagnitude <= 0.001f ||
            IsBattleLoadingOrNotStarted() || _isBowEquipped || IsServerTaunted ||
            !IsNewerSequence(sequence, _lastServerAttackSequence) ||
            index < 0 || comboList == null || index >= comboList.Length || comboList[index] == null ||
            (_healthSystem != null && _healthSystem.IsDead))
        {
            TargetResolveAttackRequest(connectionToClient, sequence, false);
            return;
        }

        _lastServerAttackSequence = sequence;
        if (IsSkillCastingOrAttackLocked() ||
            (_playerManager != null && (_playerManager.IsEmoteBlockingAttack || _playerManager.IsSkillAttackLocked)))
        {
            TargetResolveAttackRequest(connectionToClient, sequence, false);
            return;
        }
        float comboProgress = animator != null && isAttacking
            ? animator.GetCurrentAnimatorStateInfo(1).normalizedTime : float.NaN;
        if (!_serverCombo.CanStart(index, isAttacking, currentComboIndex, comboProgress, SkillTime))
        {
            TargetResolveAttackRequest(connectionToClient, sequence, false);
            return;
        }
        _currentAttackSequence = sequence;
        StartAttack(index, false, aimDirection);
        if (!isAttacking || currentComboIndex != index)
        {
            TargetResolveAttackRequest(connectionToClient, sequence, false);
            return;
        }
        RpcStartAttackFast(index, aimDirection, sequence);
        RpcStartAttack(index, aimDirection, sequence);
        TargetResolveAttackRequest(connectionToClient, sequence, true);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcStartAttackFast(int index, Vector3 aimDirection, uint sequence)
    {
        ReceiveRemoteAttackStart(index, aimDirection, sequence);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcStartAttack(int index, Vector3 aimDirection, uint sequence)
    {
        ReceiveRemoteAttackStart(index, aimDirection, sequence);
    }

    private void ReceiveRemoteAttackStart(int index, Vector3 aimDirection, uint sequence)
    {
        if (isServer)
            return;

        if (!IsNewerSequence(sequence, _lastRemoteAttackSequence))
            return;

        _lastRemoteAttackSequence = sequence;
        StartRemoteAttackVisual(index, aimDirection);
    }

    [TargetRpc]
    private void TargetResolveAttackRequest(NetworkConnectionToClient target, uint sequence, bool accepted)
    {
        if (!accepted && sequence == _currentAttackSequence)
            CancelCurrentAttack();
    }

    private void StartRemoteAttackVisual(int index, Vector3 aimDirection)
    {
        if (_healthSystem != null && _healthSystem.IsDead)
            return;

        if (index < 0 || comboList == null || index >= comboList.Length || comboList[index] == null)
            return;

        isAttacking = true;
        hasComboReserved = false;
        currentComboIndex = index;
        aimDirection = aimDirection.sqrMagnitude > 0.001f ? aimDirection : transform.forward;
        SetMeleeAim(aimDirection);
        _acceptedMeleeAimRevision = 0;

        if (_statManager != null)
        {
            _currentAttackSpeed = ResolveCurrentAttackSpeed();
        }

        foreach (var hb in _hitboxes)
        {
            if (hb != null)
            {
                hb.SetAttackData(comboList[index]);
            }
        }
        if (animator != null)
            PlayAttackAnimation(index);
        if (_comboRoutine != null)
            StopCoroutine(_comboRoutine);
        _comboRoutine = StartCoroutine(CoComboMonitor(index));
    }

    private float ResolveCurrentAttackSpeed()
    {
        float attackSpeed = _statManager != null ? _statManager.GetDerivedStats().AttackSpeed : 1f;
        if (SkillTime < _attackSpeedBonusUntil)
            attackSpeed *= Mathf.Max(0f, _attackSpeedBonusMultiplier);
        if(IsMonostatStrLifestealActive)
            attackSpeed*=MonostatStrSkillData != null ? MonostatStrSkillData.StrAttackSpeedMultiplier : 1.2f;
        return Mathf.Max(0.01f, attackSpeed * (GetComponent<ExpandedSkillController>()?.AttackSpeedMultiplier ?? 1f));
    }

    private void PlayAttackAnimation(int index)
    {
        if (animator == null || comboList == null || index < 0 || index >= comboList.Length || comboList[index] == null)
            return;

        animator.applyRootMotion = false;
        _meleeAimPose?.Restore();
        _meleeAnimationData = comboList[index];
        animator.speed = Mathf.Max(0.01f, _currentAttackSpeed);
        _swingSoundPlayed = false;
        ForceDisableHitBoxes();
        animator.Play(comboList[index].animationName, 1, 0f);
        animator.Update(0f);
        foreach (var hitbox in _hitboxes) if (hitbox != null) hitbox.BeginAnimationSampling(animator);
        // State duration already includes Animator/state speed. Dividing again cuts fast swings short.
        GetComponent<BattlePvp.Combat.BlockAttackVfx>()?.Play(false,
            animator.GetCurrentAnimatorStateInfo(1).length);
    }

    [Server]
    private void RegisterServerAcceptedAttack(uint sequence, int attackIndex)
    {
        _serverReportableAttackSequence = sequence;
        _serverReportableAttackIndex = attackIndex;
        _serverAttackReportExpiresAt = SkillTime + Mathf.Max(0.5f, _serverAttackReportGraceSeconds);
        _serverAttackHitTargetNetIds.Clear();
    }

    [Server]
    private bool IsServerAttackReportValid(uint sequence, int attackIndex)
    {
        return sequence != 0 &&
               sequence == _serverReportableAttackSequence &&
               attackIndex == _serverReportableAttackIndex &&
               SkillTime <= _serverAttackReportExpiresAt;
    }

    [Server]
    private bool TryRegisterServerAttackHitTarget(uint sequence, uint targetNetId)
    {
        return sequence == _serverReportableAttackSequence &&
               targetNetId != 0 &&
               _serverAttackHitTargetNetIds.Add(targetNetId);
    }

    private uint NextAttackSequence()
    {
        _nextLocalAttackSequence = CombatRequestSequences.Next(_nextLocalAttackSequence);
        return _nextLocalAttackSequence;
    }

    private uint NextSkillSequence()
    {
        _nextLocalSkillSequence = CombatRequestSequences.Next(_nextLocalSkillSequence);
        return _nextLocalSkillSequence;
    }

    /// <summary>Rebinds owner-local state without restarting or cancelling retained server actions.</summary>
    public void RestoreLocalOwnerCombatState()
    {
        if (!isLocalPlayer) return;
        RestoreLocalRequestSequences();
        if (isServer) return;
        double restoreNow = CombatOwnerAction.ResolveRestoreTime(SkillTime,
            NetworkClient.connection != null ? NetworkClient.connection.remoteTimeStamp : SkillTime);
        ApplyAcceptedOwnerInputLock(restoreNow);

        CombatOwnerAction accepted = AcceptedOwnerAction;
        JobSkillData animatedSkill = ResolveSkillData(accepted.SkillKey);
        if (accepted.HasAnimation(restoreNow) && HasSkillCastAnimation(animatedSkill))
        {
            SkillPresentation.PlayAnimation(animatedSkill.CastAnimationStateName, animatedSkill.CastAnimationLayer,
                accepted.StartedAt, restoreNow);
            LockLocalSkillAnimationAttack(animatedSkill);
        }

        JobSkillData castingSkill = null;
        double castCompleteAt = 0d;
        CombatCastChannel channel = CombatCastChannel.Advanced;
        if (_isCastingMonostatStrSkill)
        {
            castingSkill = MonostatStrSkillData;
            castCompleteAt = _monostatStrSkillCastCompleteAt;
            channel = CombatCastChannel.Strength;
        }
        else if (_isCastingMonostatAgiSkill)
        {
            castingSkill = MonostatAgiSkillData;
            castCompleteAt = _monostatAgiSkillCastCompleteAt;
            channel = CombatCastChannel.Agility;
        }
        else if (_advancedCastingSkillKey >= 0)
        {
            castingSkill = ResolveAdvancedSkillData(_advancedCastingSkillKey);
            castCompleteAt = _advancedCastCompleteAt;
        }

        if (castingSkill == null) return;
        _actionLocks.LockUntil(channel, castCompleteAt);
        if (_advancedCastingSkillKey >= 0)
        {
            if (ShouldLockMovementDuringSkillCast(castingSkill))
            {
                _playerManager?.SetInputLock(CombatEffectSources.ServerCastMovement, SkillInputLockFlags.Move,
                    float.PositiveInfinity);
                _restoredOwnerCastMovement = true;
            }
        }
    }

    private void ApplyAcceptedOwnerInputLock(double minimumNow = double.NegativeInfinity)
    {
        if ((!isServer && !isLocalPlayer) || _playerManager == null ||
            (double.IsNegativeInfinity(minimumNow) && _appliedAcceptedInputUntil == _acceptedSkillInputUntil &&
             _appliedAcceptedInputFlags == _acceptedSkillInputFlags)) return;
        _appliedAcceptedInputUntil = _acceptedSkillInputUntil;
        _appliedAcceptedInputFlags = _acceptedSkillInputFlags;
        double remaining = AcceptedOwnerAction.RemainingInput(Math.Max(SkillTime, minimumNow));
        if (remaining > 0d)
            // SetInputLock adds the manager's current network clock: retain the absolute server deadline.
            _playerManager.SetInputLock(CombatEffectSources.AuthoritativeSkillInput, _acceptedSkillInputFlags,
                (float)Math.Max(0d, _acceptedSkillInputUntil - SkillTime));
        else
            _playerManager.RemoveInputLock(CombatEffectSources.AuthoritativeSkillInput);
    }

    private void RestoreLocalRequestSequences()
    {
        _nextLocalAttackSequence = CombatRequestSequences.RestoreOwner(_nextLocalAttackSequence, _lastServerAttackSequence);
        _nextLocalSkillSequence = CombatRequestSequences.RestoreOwner(_nextLocalSkillSequence, _lastServerSkillSequence);
    }

    private void RefreshRestoredOwnerCastMovement()
    {
        if (!_restoredOwnerCastMovement || (isLocalPlayer && _advancedCastingSkillKey >= 0)) return;
        _playerManager?.RemoveInputLock(CombatEffectSources.ServerCastMovement);
        _restoredOwnerCastMovement = false;
    }

    private bool TryAcceptSkillSequence(uint sequence, out double acceptedStartTime, double requestedStartTime)
    {
        acceptedStartTime = SkillTime;
        if (!HasAuthoritativeCombatStats || !double.IsFinite(requestedStartTime) ||
            !IsNewerSequence(sequence, _lastServerSkillSequence))
            return false;

        _lastServerSkillSequence = sequence;
        acceptedStartTime = ResolveServerActionTime(requestedStartTime);
        return true;
    }

    [TargetRpc]
    private void TargetResolveSkillRequest(NetworkConnectionToClient target, uint sequence, bool accepted)
    {
        if (accepted || sequence != _nextLocalSkillSequence)
            return;

        CancelPredictedSkillAction();
        PublishSkillHudState();
    }

    private void CancelPredictedSkillAction()
    {
        _actionLocks.Cancel();
        StopActionRoutine(ref _localSkillAnimationAttackLockRoutine);
        _playerManager?.RemoveInputLock(CombatEffectSources.PredictedCastMovement);
        _playerManager?.RemoveInputLock(CombatEffectSources.AdvancedSkillInput);
    }

    private static bool IsNewerSequence(uint candidate, uint baseline)
    {
        return CombatRequestSequences.IsNewer(candidate, baseline);
    }

    private double ResolveServerActionTime(double requestedTime)
    {
        double now = SkillTime;
        if (double.IsNaN(requestedTime) || double.IsInfinity(requestedTime))
            return now;

        double connectionRewind = connectionToClient != null
            ? Math.Max(0d, connectionToClient.rtt)
            : 0d;
        double maxRewind = Math.Min(Math.Max(0.05f, _maxActionRewindSeconds), connectionRewind + 0.05d);
        return Math.Clamp(requestedTime, now - maxRewind, now + Math.Max(0f, _maxFutureActionLeadSeconds));
    }

    public void EnableHitBox()
    {
        if (!isActiveAndEnabled || !isAttacking || (_healthSystem != null && _healthSystem.IsDead))
            return;

        if (!_swingSoundPlayed) { _swingSoundPlayed = true; GetComponent<CombatAudio>()?.PlaySwing(); }

        foreach (var hb in _hitboxes)
        {
            if (hb != null)
                hb.EnableHitBox();
        }
    }

    public void DisableHitBox()
    {
        foreach (var hb in _hitboxes) if (hb != null) hb.EndHitWindow();
        GetComponent<BlockAttackVfx>()?.EndMeleeEmission();
    }

    private void ForceDisableHitBoxes()
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

        if (isServer &&
            target is Component targetComponent &&
            targetComponent.GetComponentInParent<NetworkIdentity>() is NetworkIdentity targetIdentity &&
            targetIdentity.netId != 0 &&
            _currentAttackSequence == _serverReportableAttackSequence &&
            !_serverAttackHitTargetNetIds.Add(targetIdentity.netId))
        {
            return false;
        }

        if (_hitTargetsThisAttack.Contains(target))
            return false;

        _hitTargetsThisAttack.Add(target);
        return true;
    }

    public void RequestServerMeleeHit(IDamageReceiver target, BodyPart bodyPart, Vector3 hitPosition)
    {
        if (!isClient || !isLocalPlayer || isServer || target is not Component targetComponent)
            return;

        NetworkIdentity targetIdentity = targetComponent.GetComponentInParent<NetworkIdentity>();
        if (targetIdentity == null || targetIdentity.netId == 0)
            return;

        CmdReportMeleeHit(
            _currentAttackSequence,
            currentComboIndex,
            targetIdentity.netId,
            bodyPart,
            hitPosition,
            SkillTime,
            targetIdentity.TryGetComponent(out PlayerManager targetManager)
                ? targetManager.RemotePoseRenderTime : SkillTime);
    }

    [Command]
    private void CmdReportMeleeHit(
        uint attackSequence,
        int attackIndex,
        uint targetNetId,
        BodyPart bodyPart,
        Vector3 hitPosition,
        double reportedHitTime,
        double reportedTargetPoseTime)
    {
        if (!CombatValidation.IsFinite(hitPosition) || !double.IsFinite(reportedHitTime) ||
            !double.IsFinite(reportedTargetPoseTime) ||
            (_healthSystem != null && _healthSystem.IsDead) ||
            !IsServerAttackReportValid(attackSequence, attackIndex) ||
            attackIndex < 0 || comboList == null ||
            attackIndex >= comboList.Length || comboList[attackIndex] == null)
            return;

        if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity) ||
            targetIdentity == null || targetIdentity == netIdentity)
            return;

        double validationTime = ResolveServerActionTime(reportedHitTime);
        // A remote target is displayed from its interpolation buffer, independently of attack input time.
        double targetValidationTime = Math.Clamp(reportedTargetPoseTime, SkillTime - 0.5d, SkillTime);
        Vector3 attackerPosition = SampleServerPosition(_serverPoseHistory, transform.position, validationTime);
        ServerPoseHistory targetHistory = targetIdentity.GetComponent<ServerPoseHistory>();
        Vector3 targetPosition = SampleServerPosition(targetHistory, targetIdentity.transform.position, targetValidationTime);
        float maxDistance = Mathf.Max(1f, _remoteMeleeValidationDistance);
        if ((targetPosition - attackerPosition).sqrMagnitude > maxDistance * maxDistance)
            return;

        HealthSystem targetHealth = targetIdentity.GetComponent<HealthSystem>();
        StatManager targetStats = targetIdentity.GetComponent<StatManager>();
        if (targetHealth == null || targetStats == null || targetHealth.IsDead)
            return;

        if (_attackProcessor == null)
            _attackProcessor = GetComponent<AttackProcessor>();
        if (_attackProcessor == null)
            return;

        if (!Enum.IsDefined(typeof(BodyPart), bodyPart) || targetHistory == null ||
            !targetHistory.TryValidateBodyPart(targetValidationTime, bodyPart, hitPosition,
                out Vector3 validatedHitPosition, out float bodyPartMultiplier))
            return;
        bool intersectsAttack = false;
        if (_hitboxes != null)
            foreach (MeleeHitBox hitbox in _hitboxes)
                if (hitbox != null && hitbox.ValidateServerHit(validatedHitPosition, SkillTime, attackerPosition))
                { intersectsAttack = true; break; }
        if (!intersectsAttack ||
            !CombatValidation.HasClearPath(attackerPosition + Vector3.up, validatedHitPosition,
                transform, targetIdentity.transform) ||
            !TryRegisterServerAttackHitTarget(attackSequence, targetNetId))
            return;
        float attackBuffMultiplier = ConsumeNextAttackDamageMultiplier();

        _attackProcessor.ProcessHit(
            comboList[attackIndex],
            targetStats,
            targetHealth,
            validatedHitPosition,
            bodyPartMultiplier: bodyPartMultiplier * attackBuffMultiplier,
            bodyPart: bodyPart,
            popupPredictionId: attackSequence);
    }

    private static Vector3 SampleServerPosition(ServerPoseHistory history, Vector3 fallback, double time)
    {
        return history != null && history.TrySample(time, out Vector3 position)
            ? position
            : fallback;
    }

    public float ConsumeNextAttackDamageMultiplier()
    {
        float multiplier = Mathf.Max(1f, _nextAttackDamageMultiplier);
        _nextAttackDamageMultiplier = 1f;
        return multiplier;
    }

    private System.Collections.IEnumerator CoComboMonitor(int index)
    {
        yield return null;
        yield return null;

        while (true)
        {
            if (animator == null)
            {
                StopCombo();
                yield break;
            }

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
            StartAttack(currentComboIndex + 1, true, GetCurrentMeleeAimDirection(currentComboIndex + 1));
        else
        {
            StopCombo(allowDelayedContinuation: true);
            if (animator != null)
                animator.speed = 1.0f;
        }
    }

    public void OnAttackAnimationEnd()
    {
        if (!isAttacking) _playerManager?.SetMovementLock(false);
    }

    public void CancelCurrentAttack()
    {
        _meleeAimPose?.Restore();
        _meleeAimWeight = 0;
        GetComponent<BlockAttackVfx>()?.StopMelee();
        _serverCombo.Reset();
        if (_comboRoutine != null)
        {
            StopCoroutine(_comboRoutine);
            _comboRoutine = null;
        }

        ForceDisableHitBoxes();
        ForceDisableKickHitBox();
        isAttacking = false;
        currentComboIndex = 0;
        hasComboReserved = false;

        if (animator != null)
            animator.speed = 1.0f;

        var pm = _playerManager != null ? _playerManager : GetComponent<PlayerManager>();
        if (pm != null)
            pm.SetMovementLock(false);
    }

    private void CancelAllCombatActions()
    {
        CancelCurrentAttack();
        StopActionRoutine(ref _advancedSkillRoutine);
        StopActionRoutine(ref _monostatStrSkillRoutine);
        StopActionRoutine(ref _monostatAgiSkillRoutine);
        CancelPredictedSkillAction();
        _bowAttackController?.CancelCharge();
        StrengthExecution = StrengthExecution.Cancel();
        AgilityExecution = AgilityExecution.Cancel();
        AcceptedOwnerAction = CombatOwnerAction.Cancelled;
        _playerManager?.RemoveInputLock(CombatEffectSources.AuthoritativeSkillInput);
        FinishAdvancedCast();
        _advancedActiveSkillKey = -1;
        _advancedActiveUntil = 0d;
        _serverReportableAttackSequence = 0;
        _serverAttackHitTargetNetIds.Clear();
        _nextAttackDamageMultiplier = 1f;
        _attackPowerBonusMultiplier = 1f;
        _attackPowerBonusUntil = 0d;
        _attackSpeedBonusMultiplier = 1f;
        _attackSpeedBonusUntil = 0d;
        _skillMoveBonusUntil = 0d;
        _auraPresentation?.Cancel();
        _playerManager?.RemoveMovementEffect(CombatEffectSources.WeaponSwap);
        _playerManager?.RemoveMovementEffect(CombatEffectSources.StrategistMove);
        SetSkillSwordVisual(null);
    }

    private void StopActionRoutine(ref Coroutine routine)
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    public void NotifyPhysicalDamageDealt(float actualDamage, IDamageReceiver defender = null, Vector3 hitPosition = default)
    {
        if (actualDamage > 0) GetComponent<ExpandedSkillController>()?.NotifyPhysicalHit(defender);
        if (actualDamage > 0f && IsMonostatStrLifestealActive)
        {
            if (_healthSystem == null)
                _healthSystem = GetComponent<HealthSystem>();

            if (_healthSystem != null)
                _healthSystem.Heal(actualDamage * MonostatStrSkillLifestealRatio);
        }

        if (IsMonostatAgiPoisonCoatingActive && defender != null)
            ApplyMonostatAgiPoisonStack(defender, hitPosition);

        if (_advancedActiveSkillKey == (int)JobSkillKind.MonostatDefTaunt && SkillTime < _advancedActiveUntil)
        {
            JobSkillData taunt = MonostatDefSkillData;
            _advancedActiveSkillKey = -1;
            _advancedActiveUntil = 0d;
            SetSkillSwordVisual(null);
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

        uint sequence = NextSkillSequence();
        double requestedStartTime = SkillTime;
        _actionLocks.LockUntil(CombatCastChannel.Strength, SkillTime + MonostatStrCastSeconds);
        PlaySkillAnimationLocal(MonostatStrSkillData);
        LockLocalSkillAnimationAttack(MonostatStrSkillData);

        if (isClient && isLocalPlayer)
        {
            if (isServer)
            {
                _lastServerSkillSequence = sequence;
                BeginMonostatStrSkill(requestedStartTime, sequence);
            }
            else
                CmdUseMonostatStrSkill(sequence, requestedStartTime);
            return;
        }

        BeginMonostatStrSkill(requestedStartTime, sequence);
    }

    private void TryUseSelectedSkill()
    {
        if (!CanUseSkillInput())
            return;

        if (_statManager == null || !TrySelectEquipped(_selectedSkillIndex, out JobSkillKind kind))
            return;

        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (_expanded != null && _expanded.CancelChargeFromInput()) return;

        if ((int)kind >= 100)
        { if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>(); _expanded?.RequestUse(_selectedSkillIndex); return; }
        if (kind == JobSkillKind.MonostatStrLifesteal)
        {
            TryUseMonostatStrSkill();
            return;
        }

        if (kind == JobSkillKind.MonostatAgiPoison)
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
    private void CmdUseMonostatStrSkill(uint sequence, double requestedStartTime)
    {
        bool accepted = HasEquippedOnly(JobSkillKind.MonostatStrLifesteal) && TryAcceptSkillSequence(sequence, out double acceptedStartTime, requestedStartTime) &&
                        BeginMonostatStrSkill(acceptedStartTime, sequence);
        TargetResolveSkillRequest(connectionToClient, sequence, accepted);
    }

    [Command]
    private void CmdUseMonostatAgiSkill(uint sequence, double requestedStartTime)
    {
        bool accepted = HasEquippedOnly(JobSkillKind.MonostatAgiPoison) && TryAcceptSkillSequence(sequence, out double acceptedStartTime, requestedStartTime) &&
                        BeginMonostatAgiSkill(acceptedStartTime, sequence);
        TargetResolveSkillRequest(connectionToClient, sequence, accepted);
    }

    [Command]
    private void CmdUseAdvancedSkill(
        int skillKey,
        Vector3 direction,
        StatContainer strategistTargetPreset,
        bool hasStrategistTargetPreset,
        uint sequence,
        double requestedStartTime)
    {
        if (!HasEquippedOnly((JobSkillKind)skillKey) || !CombatValidation.IsFinite(direction) ||
            (hasStrategistTargetPreset && (_statManager == null ||
                !StatValidation.TryValidateClientStats(strategistTargetPreset, _statManager.GetStatsCopy(), out strategistTargetPreset))))
        {
            TargetResolveSkillRequest(connectionToClient, sequence, false);
            return;
        }
        bool accepted = TryAcceptSkillSequence(sequence, out double acceptedStartTime, requestedStartTime);
        if (accepted)
        {
            SetRuntimeStrategistTargetPreset(strategistTargetPreset, hasStrategistTargetPreset);
            accepted = BeginAdvancedSkill(skillKey, direction, acceptedStartTime, sequence);
        }

        TargetResolveSkillRequest(connectionToClient, sequence, accepted);
    }

    private void TryUseAdvancedSkill(JobSkillData data)
    {
        if (!CanUseAdvancedSkillInput(data))
            return;

        int key = (int)data.SkillKind;
        Vector3 direction = ResolveAdvancedSkillDirection(data);
        StatContainer strategistTargetPreset = default;
        bool hasStrategistTargetPreset = GlobalDataManager.Instance != null && GlobalDataManager.Instance.HasStrategistTargetPreset;
        if (hasStrategistTargetPreset)
            strategistTargetPreset = GlobalDataManager.Instance.StrategistTargetPreset;
        uint sequence = NextSkillSequence();
        double requestedStartTime = SkillTime;
        _actionLocks.LockUntil(CombatCastChannel.Advanced, SkillTime + data.CastSeconds);
        _pendingAdvancedSkillHitKey = key;
        _pendingAdvancedSkillDirection = direction;
        PlaySkillAnimationLocal(data);
        LockLocalSkillAnimationAttack(data);
        _playerManager?.SetInputLock(CombatEffectSources.AdvancedSkillInput, data.InputLockFlags, data.ResolveInputLockSeconds());
        if (isClient && isLocalPlayer && !isServer)
            CmdUseAdvancedSkill(
                key,
                direction,
                strategistTargetPreset,
                hasStrategistTargetPreset,
                sequence,
                requestedStartTime);
        else
        {
            _lastServerSkillSequence = sequence;
            SetRuntimeStrategistTargetPreset(strategistTargetPreset, hasStrategistTargetPreset);
            BeginAdvancedSkill(key, direction, requestedStartTime, sequence);
        }
    }

    private void SetRuntimeStrategistTargetPreset(StatContainer targetPreset, bool hasTargetPreset)
    {
        _runtimeStrategistTargetPreset = targetPreset;
        _hasRuntimeStrategistTargetPreset = hasTargetPreset;
    }

    private Vector3 ResolveAdvancedSkillDirection(JobSkillData data)
    {
        if (data != null && CombatSkillRules.EffectOf(data.SkillKind) == AdvancedSkillEffect.Roll)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        return _playerManager != null ? _playerManager.GetSkillMoveDirection() : transform.forward;
    }

    private bool ShouldLockMovementDuringSkillCast(JobSkillData data)
    {
        return data != null && CombatSkillRules.LocksCastMovement(data.SkillKind);
    }

    private void LockLocalSkillAnimationAttack(JobSkillData data)
    {
        if (!HasSkillCastAnimation(data))
            return;

        _actionLocks.SetAnimationLocked(true);
        if (_localSkillAnimationAttackLockRoutine != null)
            StopCoroutine(_localSkillAnimationAttackLockRoutine);
        _localSkillAnimationAttackLockRoutine = StartCoroutine(CoLocalSkillAnimationAttackLock(data));
    }

    private System.Collections.IEnumerator CoLocalSkillAnimationAttackLock(JobSkillData data)
    {
        while (!IsSkillCastAnimationFinished(data))
            yield return null;

        _actionLocks.SetAnimationLocked(false);
        _localSkillAnimationAttackLockRoutine = null;
    }

    private bool BeginAdvancedSkill(int skillKey, Vector3 direction, double requestedStartTime, uint sequence)
        => BeginAdvancedSkillCore(skillKey, direction, requestedStartTime, sequence, false);

    private bool BeginAdvancedSkillCore(int skillKey, Vector3 direction, double requestedStartTime, uint sequence, bool copied)
    {
        if (!HasAuthoritativeCombatStats) return false;
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        JobSkillData data = ResolveAdvancedSkillData(skillKey);
        double now = SkillTime;
        if (data == null || (_healthSystem != null && _healthSystem.IsDead))
            return false;
        if (CombatSkillRules.EffectOf(data.SkillKind)==AdvancedSkillEffect.Roll &&
            (GetComponent<ExpandedSkillController>()?.BlocksVoluntaryDisplacement ?? false)) return false;
        TryGetAdvancedCooldownUntil(skillKey, out double cooldown);
        bool charged=ExpandedSkillController.UsesCharges(data.SkillKind) && _expanded!=null;
        if(charged) cooldown=0;
        var execution = new CombatSkillExecution(_advancedCastingSkillKey >= 0, _advancedCastCompleteAt, 0d, cooldown);
        if (!AdvancedSkillPlan.TryBegin(data.SkillKind, execution, now, requestedStartTime,
                data.CastSeconds, data.CooldownSeconds, HasSkillCastAnimation(data), out AdvancedSkillPlan plan))
            return false;

        if(charged && !copied && !_expanded.SpendCharge(data.SkillKind)) return false;
        if (!charged && data.CooldownSeconds > 0f)
            SetAdvancedCooldownUntil(skillKey, plan.Execution.CooldownUntil);

        CancelCurrentAttack();
        _bowAttackController?.CancelCharge();
        if (plan.AppliesImmediately)
        {
            RecordAcceptedOwnerAction(data, requestedStartTime);
            PlaySkillSfx(skillKey);
            ApplyAdvancedSkill(plan, data, direction);
            return true;
        }

        _advancedCastingSkillKey = skillKey;
        _advancedCastCompleteAt = plan.Execution.CastCompleteAt;
        if (plan.LocksMovement)
            _playerManager?.SetInputLock(CombatEffectSources.ServerCastMovement, SkillInputLockFlags.Move, float.PositiveInfinity);
        if (plan.ApplyAtStart)
            ApplyAdvancedSkill(plan, data, direction);
        _pendingAdvancedSkillHitKey = skillKey;
        _pendingAdvancedSkillDirection = direction;
        // Seeking can fire events; the approved clock window is installed after the state is known.
        // LateUpdate opens/queries it even when a seek skipped the opening event.
        PlaySkillAnimationNetworked(data, requestedStartTime, sequence);
        if (plan.HasHitWindow)
        {
            ResolveKickAnimationWindow(data, out float opensAt, out float closesAt);
            _kickWindow.Begin(sequence, requestedStartTime + opensAt, requestedStartTime + closesAt);
        }
        PlaySkillSfx(skillKey);
        if (_advancedSkillRoutine != null)
            StopCoroutine(_advancedSkillRoutine);
        _advancedSkillRoutine = StartCoroutine(CoAdvancedSkillCast(plan, data, direction));
        return true;
    }

    private void ResolveKickAnimationWindow(JobSkillData data, out float opensAt, out float closesAt)
    {
        opensAt = 0f;
        closesAt = Mathf.Max(0f, data.CastSeconds);
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name != data.CastAnimationStateName) continue;
            int layer = Mathf.Clamp(data.CastAnimationLayer, 0, animator.layerCount - 1);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            float secondsPerClipSecond = state.IsName(data.CastAnimationStateName) && clip.length > 0f
                ? state.length / clip.length : 1f;
            foreach (AnimationEvent animationEvent in clip.events)
            {
                if (animationEvent.functionName == nameof(EnableKickHitBox) ||
                    animationEvent.functionName == nameof(EnableSkillHitBox) ||
                    animationEvent.functionName == nameof(OnSkillHitWindow))
                    opensAt = animationEvent.time * secondsPerClipSecond;
                if (animationEvent.functionName == nameof(DisableKickHitBox) ||
                    animationEvent.functionName == nameof(DisableSkillHitBox))
                    closesAt = animationEvent.time * secondsPerClipSecond + 1f / 30f;
            }
            return;
        }
    }

    private System.Collections.IEnumerator CoAdvancedSkillCast(AdvancedSkillPlan plan, JobSkillData data, Vector3 direction)
    {
        while (SkillTime < _advancedCastCompleteAt)
            yield return null;

        FinishAdvancedCast();
        if (plan.CanApplyAtFinish(_healthSystem == null || !_healthSystem.IsDead,
                ResolveAdvancedSkillData((int)plan.Kind) == data))
            ApplyAdvancedSkill(plan, data, direction);
        if (plan.HasHitWindow)
            while (SkillTime <= _kickWindow.EndsAt && (_healthSystem == null || !_healthSystem.IsDead))
                yield return null;
        ForceDisableKickHitBox();
        _advancedSkillRoutine = null;
    }

    private void FinishAdvancedCast()
    {
        _advancedCastingSkillKey = -1;
        _advancedCastCompleteAt = 0d;
        _playerManager?.RemoveInputLock(CombatEffectSources.ServerCastMovement);
        _restoredOwnerCastMovement = false;
    }

    public void OnSkillHitWindow()
    {
        EnableKickHitBox();
    }

    public void EnableSkillHitBox()
    {
        EnableKickHitBox();
    }

    public void EnableKickHitBox()
    {
        SetKickHitBoxEnabled(true);
    }

    public void DisableSkillHitBox()
    {
        DisableKickHitBox();
    }

    public void DisableKickHitBox()
    {
        _kickClosePending = true;
    }

    [Command]
    private void CmdRequestKickContact(uint skillSequence)
    {
        // Client animation events may request a query, but cannot open or resize the server window.
        if (_kickWindow.CanHit(skillSequence, SkillTime) && _isKickHitBoxEnabled &&
            _healthSystem != null && !_healthSystem.IsDead)
            _kickHitBox?.ProcessCurrentOverlaps();
    }

    private void SetKickHitBoxEnabled(bool enabled)
    {
        int skillKey = ResolveKickHitBoxSkillKey();
        if (isClient && isLocalPlayer && !isServer)
        {
            if (enabled) CmdRequestKickContact(_nextLocalSkillSequence);
            return;
        }

        SetKickHitBoxEnabledServer(skillKey, enabled);
    }

    private int ResolveKickHitBoxSkillKey()
    {
        return _pendingAdvancedSkillHitKey == (int)JobSkillKind.MonostatConKick
            ? _pendingAdvancedSkillHitKey
            : (int)JobSkillKind.MonostatConKick;
    }

    private void SetKickHitBoxEnabledServer(int skillKey, bool enabled)
    {
        if ((NetworkClient.active && !NetworkServer.active) || !HasAuthoritativeCombatStats ||
            skillKey != (int)JobSkillKind.MonostatConKick ||
            skillKey != _pendingAdvancedSkillHitKey || _healthSystem == null || _healthSystem.IsDead)
            return;

        JobSkillData data = ResolveAssignedAdvancedSkillData(skillKey);
        if (data == null || data.SkillKind != JobSkillKind.MonostatConKick)
            return;

        if (enabled)
        {
            if (_isKickHitBoxEnabled || ResolveAdvancedSkillData(skillKey) != data ||
                !_kickWindow.TryOpen(_kickWindow.Sequence, SkillTime))
                return;

            _isKickHitBoxEnabled = true;
            _kickHitTargets.Clear();
            if (_kickHitBox != null)
            {
                _kickHitBox.SetActive(true);
            }
            return;
        }

        _isKickHitBoxEnabled = false;
        _kickWindow.Close();
        if (_kickHitBox != null)
            _kickHitBox.SetActive(false);
    }

    public void TickKickHitWindow()
    {
        if (NetworkClient.active && !NetworkServer.active) return;
        SetKickHitBoxEnabledServer(ResolveKickHitBoxSkillKey(), true);
        if (_isKickHitBoxEnabled)
            _kickHitBox?.ProcessCurrentOverlaps();
        if (_kickClosePending || (_isKickHitBoxEnabled && !_kickWindow.CanHit(_kickWindow.Sequence, SkillTime)))
        {
            _kickClosePending = false;
            SetKickHitBoxEnabledServer(ResolveKickHitBoxSkillKey(), false);
        }
    }

    private void ForceDisableKickHitBox()
    {
        _kickWindow.Cancel();
        _kickClosePending = false;
        _isKickHitBoxEnabled = false;
        if (_kickHitBox != null)
            _kickHitBox.SetActive(false);
        _kickHitTargets.Clear();
        _pendingAdvancedSkillHitKey = -1;
        _pendingAdvancedSkillDirection = Vector3.zero;
    }

    private void ApplyAdvancedSkill(AdvancedSkillPlan plan, JobSkillData data, Vector3 direction)
    {
        switch (plan.Effect)
        {
            case AdvancedSkillEffect.KickWindow:
                break;
            case AdvancedSkillEffect.TauntReady:
                _advancedActiveSkillKey = (int)plan.Kind;
                _advancedActiveUntil = SkillTime + data.TauntReadyDurationSeconds;
                SetSkillSwordVisual(data);
                break;
            case AdvancedSkillEffect.Roll:
                _advancedActiveSkillKey = (int)plan.Kind;
                _advancedActiveUntil = SkillTime + data.RollDurationSeconds;
                _healthSystem?.SetSkillInvulnerable(data.RollDurationSeconds);
                ExecuteSkillMove(direction, data.RollDistance, data.RollDurationSeconds, 1f, 0f);
                break;
            case AdvancedSkillEffect.PresetChange:
                ExecutePresetChange(data);
                break;
            case AdvancedSkillEffect.WeaponSwap:
                var swap = new CombatWeaponSwapPlan(_isBowEquipped, _nextAttackDamageMultiplier,
                    data.WeaponSwapNextAttackMultiplier, data.WeaponSwapMoveMultiplier, data.WeaponSwapMoveBonusDurationSeconds);
                _bowAttackController?.CancelCharge();
                _isBowEquipped = swap.BowEquipped;
                ApplyIdentityVisuals();
                ApplyWeaponSwapBonus(swap);
                break;
        }
    }

    public void TryProcessKickHit(Collider hit)
    {
        if ((NetworkClient.active && !NetworkServer.active) || !HasAuthoritativeCombatStats ||
            !_isKickHitBoxEnabled || !_kickWindow.CanHit(_kickWindow.Sequence, SkillTime))
            return;

        JobSkillData data = ResolveAssignedAdvancedSkillData((int)JobSkillKind.MonostatConKick);
        if (data == null || hit == null || hit.transform.root == transform.root)
            return;

        if (_attackProcessor == null)
            _attackProcessor = GetComponent<AttackProcessor>();

        CombatHitTargets.Resolve(hit, out IDamageReceiver target, out StatManager targetStats, out var bodyPart);
        if (target == null || targetStats == null || (target is HealthSystem && bodyPart == null))
            return;

        if (_kickHitTargets.Contains(target))
            return;

        Vector3 hitPosition = hit.ClosestPoint(_kickHitBox != null ? _kickHitBox.transform.position : transform.position);
        if (!CombatValidation.HasClearPath(transform.position + Vector3.up, hitPosition, transform, hit.transform))
            return;
        if (!_attackProcessor.ProcessSkillHit(data.KickDamageMultiplier, targetStats, target, hitPosition))
            return;
        _kickHitTargets.Add(target);

        if (NetworkServer.active && target is Component targetComponent)
        {
            PlayerManager targetManager = targetComponent.GetComponentInParent<PlayerManager>();
            if (targetManager != null)
            {
                Vector3 push = targetManager.transform.position - transform.position;
                targetManager.ServerAuthorizeForcedMove(push, data.KickKnockbackDistance, 0.2f);
                targetManager.SetMovementEffect(CombatEffectSources.KickSlow,
                    data.KickSlowMoveMultiplier, data.KickSlowDurationSeconds);
                RpcApplyMovementEffect(targetManager.netId, push, data.KickKnockbackDistance, 0.2f,
                    data.KickSlowMoveMultiplier, data.KickSlowDurationSeconds);
            }
        }
    }

    private void ExecutePresetChange(JobSkillData data)
    {
        if (_statManager == null || _healthSystem == null)
            return;

        StatContainer currentPreset = _statManager.GetStatsCopy();
        StatContainer targetPreset = CombatPresetPlan.ResolveUnconfiguredTarget(data.TargetPreset, currentPreset);
        if (data.SkillKind == JobSkillKind.StrategistPresetChange && _hasRuntimeStrategistTargetPreset)
        {
            targetPreset = _runtimeStrategistTargetPreset;
        }
        else if (!NetworkServer.active && data.SkillKind == JobSkillKind.StrategistPresetChange &&
                 GlobalDataManager.Instance != null &&
                 GlobalDataManager.Instance.HasStrategistTargetPreset)
        {
            targetPreset = GlobalDataManager.Instance.StrategistTargetPreset;
        }

        if (NetworkServer.active && !StatValidation.TryValidateClientStats(targetPreset, currentPreset, out targetPreset))
            return;

        bool strategist = data.SkillKind == JobSkillKind.StrategistPresetChange;
        if (!CombatPresetPlan.TryCreate(strategist, currentPreset, targetPreset, _strategistSwapReturnPreset,
                _hasStrategistSwapReturnPreset, out CombatPresetPlan plan))
        {
            Debug.LogWarning("[PlayerCombat] Strategist preset change ignored. Target preset is not complete.", this);
            return;
        }

        StatKind targetDominantStat = CombatPresetPlan.DominantStat(plan.Target);
        float oldMax = _healthSystem.MaxHp;
        float oldCurrent = _healthSystem.CurrentHp;
        if (NetworkServer.active)
        {
            if (!_statManager.TryApplyServerPreset(plan.Target)) return;
        }
        else if (!NetworkClient.active)
            _statManager.ApplyLocalSceneStats(plan.Target, true);
        else return;
        _strategistSwapReturnPreset = plan.ReturnPreset;
        _hasStrategistSwapReturnPreset = plan.HasReturnPreset;
        float newMax = _healthSystem.MaxHp;
        float bonusShield = strategist ? ApplyStrategistPresetBonus(data, targetDominantStat, newMax) : 0f;
        CombatPresetPlan.ResolveVitals(oldMax, oldCurrent, newMax, data.MaxHealthIncreaseShieldRatio,
            bonusShield, out float newCurrent, out float shield);
        _healthSystem.SetCurrentHp(newCurrent);
        _healthSystem.GrantDecayingShield(shield, data.ShieldDurationSeconds);
    }

    private float ApplyStrategistPresetBonus(JobSkillData data, StatKind targetDominantStat, float targetMaxHp)
    {
        CombatPresetBonus bonus = CombatPresetBonus.Resolve(targetDominantStat, targetMaxHp, new CombatPresetBonusSettings
        {
            StrAttackMultiplier = data.StrategistStrNextAttackMultiplier,
            StrDuration = data.StrategistStrAttackBonusDurationSeconds,
            AgiMoveMultiplier = data.StrategistAgiMoveMultiplier,
            AgiAttackSpeedMultiplier = data.StrategistAgiAttackSpeedMultiplier,
            AgiDuration = data.StrategistAgiBonusDurationSeconds,
            ConShieldRatio = data.StrategistConTargetMaxHpShieldRatio,
            DefInvulnerableSeconds = data.StrategistDefInvulnerableSeconds
        });
        switch (bonus.Stat)
        {
            case StatKind.STR:
                ApplyStrPresetBonusLocal(bonus.AttackMultiplier, bonus.Duration);
                if (NetworkServer.active)
                    RpcApplyStrPresetBonus(bonus.AttackMultiplier, bonus.Duration);
                break;
            case StatKind.AGI:
                ApplyAgiPresetBonusLocal(bonus.MoveMultiplier, bonus.AttackSpeedMultiplier, bonus.Duration);
                if (NetworkServer.active)
                    RpcApplyAgiPresetBonus(bonus.MoveMultiplier, bonus.AttackSpeedMultiplier, bonus.Duration);
                break;
            case StatKind.DEF:
                _healthSystem?.SetSkillInvulnerable(bonus.Duration);
                ShowStrategistPresetAuraLocal(bonus.Stat, bonus.Duration);
                if (NetworkServer.active)
                    RpcShowStrategistPresetAura(bonus.Stat, bonus.Duration);
                break;
        }

        return bonus.Shield;
    }

    [ClientRpc]
    private void RpcApplyStrPresetBonus(float attackMultiplier, float durationSeconds)
    {
        ApplyStrPresetBonusLocal(attackMultiplier, durationSeconds);
    }

    private void ApplyStrPresetBonusLocal(float attackMultiplier, float durationSeconds)
    {
        _attackPowerBonusMultiplier = Mathf.Max(1f, attackMultiplier);
        _attackPowerBonusUntil = SkillTime + Mathf.Max(0f, durationSeconds);
        ShowStrategistPresetAuraLocal(StatKind.STR, durationSeconds);
    }

    private void ApplyWeaponSwapBonus(CombatWeaponSwapPlan plan)
    {
        ApplyMoveBonusLocal(CombatEffectSources.WeaponSwap, plan.MoveMultiplier, plan.MoveDuration);
        if (NetworkServer.active)
            RpcApplyMoveBonus(plan.MoveMultiplier, plan.MoveDuration);
        _nextAttackDamageMultiplier = plan.NextAttackMultiplier;
    }

    [ClientRpc]
    private void RpcApplyAgiPresetBonus(float moveMultiplier, float attackSpeedMultiplier, float durationSeconds)
    {
        ApplyAgiPresetBonusLocal(moveMultiplier, attackSpeedMultiplier, durationSeconds);
    }

    [ClientRpc]
    private void RpcApplyMoveBonus(float moveMultiplier, float durationSeconds)
    {
        ApplyMoveBonusLocal(CombatEffectSources.WeaponSwap, moveMultiplier, durationSeconds);
    }

    [ClientRpc]
    private void RpcShowStrategistPresetAura(StatKind statKind, float durationSeconds)
    {
        ShowStrategistPresetAuraLocal(statKind, durationSeconds);
    }

    private void ApplyAgiPresetBonusLocal(float moveMultiplier, float attackSpeedMultiplier, float durationSeconds)
    {
        ApplyMoveBonusLocal(CombatEffectSources.StrategistMove, moveMultiplier, durationSeconds);
        _attackSpeedBonusMultiplier = attackSpeedMultiplier;
        _attackSpeedBonusUntil = SkillTime + Mathf.Max(0f, durationSeconds);
        ShowStrategistPresetAuraLocal(StatKind.AGI, durationSeconds);
    }

    private void ApplyMoveBonusLocal(int source, float moveMultiplier, float durationSeconds)
    {
        _playerManager?.SetMovementEffect(source, moveMultiplier, durationSeconds);
        if (moveMultiplier > 1f && durationSeconds > 0f)
            _skillMoveBonusUntil = Math.Max(_skillMoveBonusUntil, SkillTime + durationSeconds);
    }

    private void ShowStrategistPresetAuraLocal(StatKind statKind, float durationSeconds)
    {
        if (GetComponent<ExpandedSkillController>() != null) return;
        AuraPresentation.ShowTimed(statKind, durationSeconds, SkillTime,
            _healthSystem != null ? _healthSystem.CurrentShield : 0f);
    }

    private void UpdateStrategistStrAura()
    {
        // The shared body glow reads replicated buff state, including late joiners.
        if (GetComponent<ExpandedSkillController>() != null) { _auraPresentation?.Cancel(); return; }
        AuraPresentation.Update(SkillTime, _healthSystem != null ? _healthSystem.CurrentShield : 0f);
    }

    [ClientRpc]
    private void RpcExecuteSkillMove(Vector3 direction, float distance, float duration, float moveMultiplier, float slowDuration)
    {
        ExecuteSkillMoveLocal(direction, distance, duration, moveMultiplier, slowDuration);
    }

    private void ExecuteSkillMove(Vector3 direction, float distance, float duration, float moveMultiplier, float slowDuration)
    {
        if (NetworkServer.active)
        {
            _playerManager?.ServerAuthorizeForcedMove(direction, distance, duration);
            if (slowDuration > 0f)
                _playerManager?.SetMovementEffect(CombatEffectSources.SkillMoveSlow, moveMultiplier, slowDuration);
            RpcExecuteSkillMove(direction, distance, duration, moveMultiplier, slowDuration);
            return;
        }

        ExecuteSkillMoveLocal(direction, distance, duration, moveMultiplier, slowDuration);
    }

    private void ExecuteSkillMoveLocal(Vector3 direction, float distance, float duration, float moveMultiplier, float slowDuration)
    {
        if (NetworkClient.active && !isLocalPlayer) return;
        if (!NetworkClient.active && !NetworkServer.active)
            _playerManager?.MoveBySkill(direction, distance, duration);
        if (slowDuration > 0f)
            _playerManager?.SetMovementEffect(CombatEffectSources.SkillMoveSlow, moveMultiplier, slowDuration);
    }

    [ClientRpc]
    private void RpcApplyMovementEffect(uint targetNetId, Vector3 direction, float distance, float duration, float moveMultiplier, float slowDuration)
    {
        if (!NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
            return;
        PlayerManager manager = identity.GetComponent<PlayerManager>();
        if (manager == null || !manager.isLocalPlayer) return;
        manager?.SetMovementEffect(CombatEffectSources.KickSlow, moveMultiplier, slowDuration);
    }

    private bool BeginMonostatStrSkill(double requestedStartTime, uint sequence)
    {
        if (!HasAuthoritativeCombatStats) return false;
        double now = SkillTime;
        if (!CanStartMonostatStrSkill(now))
            return false;

        if (!StrengthExecution.TryBegin(now, requestedStartTime, MonostatStrCastSeconds,
                MonostatStrCooldownSeconds, out CombatSkillExecution execution)) return false;
        StrengthExecution = execution;

        CancelCurrentAttack();
        PlaySkillAnimationNetworked(MonostatStrSkillData, requestedStartTime, sequence);
        PlaySkillSfx(0);
        PublishSkillHudState();

        if (_monostatStrSkillRoutine != null)
            StopCoroutine(_monostatStrSkillRoutine);

        _monostatStrSkillRoutine = StartCoroutine(CoMonostatStrSkill());
        return true;
    }

    private void TryUseMonostatAgiSkill()
    {
        if (!CanUseMonostatAgiSkillInput())
            return;

        uint sequence = NextSkillSequence();
        double requestedStartTime = SkillTime;
        _actionLocks.LockUntil(CombatCastChannel.Agility, SkillTime + MonostatAgiCastSeconds);
        PlaySkillAnimationLocal(MonostatAgiSkillData);
        LockLocalSkillAnimationAttack(MonostatAgiSkillData);

        if (isClient && isLocalPlayer)
        {
            if (isServer)
            {
                _lastServerSkillSequence = sequence;
                BeginMonostatAgiSkill(requestedStartTime, sequence);
            }
            else
                CmdUseMonostatAgiSkill(sequence, requestedStartTime);
            return;
        }

        BeginMonostatAgiSkill(requestedStartTime, sequence);
    }

    private bool BeginMonostatAgiSkill(double requestedStartTime, uint sequence)
    {
        if (!HasAuthoritativeCombatStats) return false;
        double now = SkillTime;
        if (!CanStartMonostatAgiSkill(now))
            return false;

        if (!AgilityExecution.TryBegin(now, requestedStartTime, MonostatAgiCastSeconds,
                MonostatAgiCooldownSeconds, out CombatSkillExecution execution)) return false;
        AgilityExecution = execution;

        CancelCurrentAttack();
        PlaySkillAnimationNetworked(MonostatAgiSkillData, requestedStartTime, sequence);
        PlaySkillSfx(1);
        PublishSkillHudState();

        if (_monostatAgiSkillRoutine != null)
            StopCoroutine(_monostatAgiSkillRoutine);

        _monostatAgiSkillRoutine = StartCoroutine(CoMonostatAgiSkill());
        return true;
    }

    private System.Collections.IEnumerator CoMonostatStrSkill()
    {
        while (SkillTime < _monostatStrSkillCastCompleteAt)
            yield return null;

        StrengthExecution = StrengthExecution.FinishCast();
        PublishSkillHudState();

        if (_healthSystem == null)
            _healthSystem = GetComponent<HealthSystem>();

        if (_healthSystem != null && _healthSystem.IsDead)
        {
            _monostatStrSkillRoutine = null;
            yield break;
        }

        if (!AllowsEquipped(JobSkillKind.MonostatStrLifesteal))
        {
            _monostatStrSkillRoutine = null;
            yield break;
        }

        if (!StrengthExecution.TryActivate(SkillTime, MonostatStrDurationSeconds, out CombatSkillExecution active))
        {
            _monostatStrSkillRoutine = null;
            yield break;
        }
        StrengthExecution = active;
        SetSkillSwordVisual(MonostatStrSkillData);
        PublishSkillHudState();

        while (SkillTime < _monostatStrSkillActiveUntil)
            yield return null;

        StrengthExecution = StrengthExecution.EndActive();
        SetSkillSwordVisual(null);
        _monostatStrSkillRoutine = null;
        PublishSkillHudState();
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlaySkillAnimation(string stateName, int layer, uint sequence, double acceptedStartTime)
    {
        if (isServer || !IsNewerSequence(sequence, _lastRemoteSkillSequence))
            return;

        _lastRemoteSkillSequence = sequence;
        PlaySkillAnimationLocal(stateName, layer, acceptedStartTime);
    }

    private void PlaySkillAnimationLocal(string stateName, int layer)
    {
        PlaySkillAnimationLocal(stateName, layer, double.NaN);
    }

    private void PlaySkillAnimationLocal(string stateName, int layer, double visualStartedAt)
    {
        SkillPresentation.PlayAnimation(stateName, layer, visualStartedAt, SkillTime);
    }

    private bool HasSkillCastAnimation(JobSkillData data)
    {
        return data != null && !string.IsNullOrWhiteSpace(data.CastAnimationStateName);
    }

    private bool IsSkillCastAnimationFinished(JobSkillData data)
    {
        return data == null || SkillPresentation.IsAnimationFinished(data.CastAnimationStateName, data.CastAnimationLayer);
    }

    private void SetSkillSwordVisual(JobSkillData data)
    {
        if (data == null)
        {
            _skillPresentation?.Cancel();
            return;
        }
        if (_handSwordVisual == null) ResolveWeaponVisualReferences();
        SkillPresentation.SetSwordMaterial(_handSwordVisual, data.SwordMaterial);
    }

    private void RefreshSkillSwordVisualFromState()
    {
        double now = SkillTime;
        if (now < _monostatStrSkillActiveUntil)
        {
            SetSkillSwordVisual(MonostatStrSkillData);
            return;
        }

        if (now < _monostatAgiSkillActiveUntil)
        {
            SetSkillSwordVisual(MonostatAgiSkillData);
            return;
        }

        if (_advancedActiveSkillKey == (int)JobSkillKind.MonostatDefTaunt && now < _advancedActiveUntil)
        {
            SetSkillSwordVisual(MonostatDefSkillData);
            return;
        }

        SetSkillSwordVisual(null);
    }

    private void OnSkillSwordVisualStateChanged(double oldValue, double newValue)
    {
        RefreshSkillSwordVisualFromState();
    }

    private void OnAdvancedActiveSkillKeyChanged(int oldValue, int newValue)
    {
        RefreshSkillSwordVisualFromState();
    }

    private void PlaySkillAnimationNetworked(JobSkillData data, double acceptedStartTime, uint sequence)
    {
        RecordAcceptedOwnerAction(data, acceptedStartTime);
        if (!HasSkillCastAnimation(data))
            return;

        PlaySkillAnimationLocal(data.CastAnimationStateName, data.CastAnimationLayer, acceptedStartTime);
        // Read the played state's effective duration, so clip names and controller overrides remain supported.
        if (animator != null && animator.layerCount > 0 &&
            _acceptedSkillAnimationKey == (int)data.SkillKind && _acceptedSkillStartedAt == acceptedStartTime)
        {
            int layer = Mathf.Clamp(data.CastAnimationLayer, 0, animator.layerCount - 1);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            if (state.IsName(data.CastAnimationStateName))
                AcceptedOwnerAction = AcceptedOwnerAction.WithAnimation(state.length);
        }

        if (NetworkServer.active)
            RpcPlaySkillAnimation(data.CastAnimationStateName, data.CastAnimationLayer, sequence, acceptedStartTime);
    }

    private void RecordAcceptedOwnerAction(JobSkillData data, double startedAt)
    {
        if (data == null) return;
        bool advanced = data.SkillKind != JobSkillKind.MonostatStrLifesteal && data.SkillKind != JobSkillKind.MonostatAgiPoison;
        AcceptedOwnerAction = CombatOwnerAction.Begin((int)data.SkillKind, startedAt,
            advanced ? (int)data.InputLockFlags : 0, advanced ? data.ResolveInputLockSeconds() : 0d);
        ApplyAcceptedOwnerInputLock();
    }

    private void PlaySkillAnimationLocal(JobSkillData data)
    {
        if (!HasSkillCastAnimation(data))
            return;

        PlaySkillAnimationLocal(data.CastAnimationStateName, data.CastAnimationLayer);
    }

    private System.Collections.IEnumerator CoMonostatAgiSkill()
    {
        while (SkillTime < _monostatAgiSkillCastCompleteAt)
            yield return null;

        AgilityExecution = AgilityExecution.FinishCast();
        PublishSkillHudState();

        if (_healthSystem == null)
            _healthSystem = GetComponent<HealthSystem>();

        if (_healthSystem != null && _healthSystem.IsDead)
        {
            _monostatAgiSkillRoutine = null;
            yield break;
        }

        if (!AllowsEquipped(JobSkillKind.MonostatAgiPoison))
        {
            _monostatAgiSkillRoutine = null;
            yield break;
        }

        if (!AgilityExecution.TryActivate(SkillTime, MonostatAgiDurationSeconds, out CombatSkillExecution active))
        {
            _monostatAgiSkillRoutine = null;
            yield break;
        }
        AgilityExecution = active;
        SetSkillSwordVisual(MonostatAgiSkillData);
        PublishSkillHudState();

        while (SkillTime < _monostatAgiSkillActiveUntil)
            yield return null;

        AgilityExecution = AgilityExecution.EndActive();
        SetSkillSwordVisual(null);
        _monostatAgiSkillRoutine = null;
        PublishSkillHudState();
    }

    private bool CanUseSkillInput()
    {
        if (isClient && !isLocalPlayer) return false;
        if (IsBattleLoadingOrNotStarted()) return false;
        if (_healthSystem != null && _healthSystem.IsDead) return false;
        if (BattlePvp.Logic.GameInputController.IsPaused || BattlePvp.Logic.GameInputController.IsTextInputActive) return false;
        if (!_skillButtonRequest && Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI) return false;
        if (ResolveAvailableSkillCount() <= 0) return false;

        return true;
    }

    private bool CanUseMonostatStrSkillInput()
    {
        if (!CanUseSkillInput()) return false;

        return CanStartMonostatStrSkill(SkillTime);
    }

    private bool CanUseMonostatAgiSkillInput()
    {
        if (!CanUseSkillInput()) return false;

        return CanStartMonostatAgiSkill(SkillTime);
    }

    private bool CanUseAdvancedSkillInput(JobSkillData data)
    {
        if (!CanUseSkillInput()) return false;
        if (data == null) return false;
        TryGetAdvancedCooldownUntil((int)data.SkillKind, out double cooldownUntil);
        if(ExpandedSkillController.UsesCharges(data.SkillKind) && _expanded!=null)
        { if(_expanded.ChargeState(data.SkillKind).Charges<=0) return false; cooldownUntil=0; }
        var execution = new CombatSkillExecution(_advancedCastingSkillKey >= 0, _advancedCastCompleteAt,
            _advancedActiveSkillKey == (int)data.SkillKind ? _advancedActiveUntil : 0d, cooldownUntil);
        if (!execution.CanBegin(SkillTime)) return false;

        return ResolveAdvancedSkillData((int)data.SkillKind) == data;
    }

    private bool TryGetAdvancedCooldownUntil(int skillKey, out double cooldownUntil)
    {
        if (NetworkServer.active || NetworkClient.active)
            return _advancedCooldownUntil.TryGetValue(skillKey, out cooldownUntil);

        return _offlineAdvancedCooldownUntil.TryGetValue(skillKey, out cooldownUntil);
    }

    private void SetAdvancedCooldownUntil(int skillKey, double cooldownUntil)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            _advancedCooldownUntil[skillKey] = cooldownUntil;
            return;
        }

        _offlineAdvancedCooldownUntil[skillKey] = cooldownUntil;
    }

    private bool CanStartMonostatStrSkill(double now)
    {
        if (!StrengthExecution.CanBegin(now)) return false;
        if (_healthSystem != null && _healthSystem.IsDead) return false;
        if (!AllowsEquipped(JobSkillKind.MonostatStrLifesteal)) return false;

        return true;
    }

    private bool CanStartMonostatAgiSkill(double now)
    {
        if (!AgilityExecution.CanBegin(now)) return false;
        if (_healthSystem != null && _healthSystem.IsDead) return false;
        if (!AllowsEquipped(JobSkillKind.MonostatAgiPoison)) return false;

        return true;
    }

    private bool IsSkillCastingOrAttackLocked()
    {
        return (GetComponent<ExpandedSkillController>()?.BlocksCombat ?? false) || _isCastingMonostatStrSkill || _isCastingMonostatAgiSkill || _advancedCastingSkillKey >= 0 ||
               AcceptedOwnerAction.HasAnimation(SkillTime) || _actionLocks.IsLocked(SkillTime);
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
        if (_statManager == null ||
            !TrySelectEquipped(_selectedSkillIndex, out JobSkillKind kind))
            return null;
        return ResolveAssignedAdvancedSkillData((int)kind);
    }

    private JobSkillData ResolveAdvancedSkillData(int skillKey)
    {
        if (_statManager == null)
            return null;
        JobSkillKind kind = (JobSkillKind)skillKey;
        return AllowsEquipped(kind)
            ? ResolveAssignedAdvancedSkillData(skillKey) : null;
    }

    private JobSkillData ResolveAssignedAdvancedSkillData(int skillKey)
    {
        JobSkillKind kind = (JobSkillKind)skillKey;
        if (CombatSkillRules.EffectOf(kind) == AdvancedSkillEffect.None) return null;
        JobSkillData data = ResolveSkillData(skillKey);
        return IsSkillDataKind(data, kind) ? data : null;
    }

    private void HandleBowAttackInput(bool pressed, bool fromHud = false)
    {
        if (IsServerTaunted) return;
        JobSkillData bow = _polymathWeaponSwapSkillData;
        if (bow == null)
            return;

        ResolveBowAttackController();
        if (_bowAttackController == null)
        {
            Debug.LogWarning("[PlayerCombat] BowAttackController is missing. Add it to the player prefab.", this);
            return;
        }

        if (IsBattleLoadingOrNotStarted())
            return;
        if (_healthSystem != null && _healthSystem.IsDead)
            return;

        if (pressed)
        {
            if (BattlePvp.Logic.GameInputController.IsPaused || BattlePvp.Logic.GameInputController.IsTextInputActive)
                return;
            if (!fromHud && Cursor.lockState != CursorLockMode.Locked && _isPointerOverUI)
                return;
            if (_playerManager != null && (_playerManager.IsEmoteBlockingAttack || _playerManager.IsSkillAttackLocked))
                return;
            if (IsSkillCastingOrAttackLocked())
                return;
        }

        _bowAttackController.HandleAttackInput(pressed, bow, GetCurrentAimDirection());
    }

    public void OnBowDrawReady()
    {
        ResolveBowAttackController();
        _bowAttackController?.OnBowDrawReady();
    }

    public void OnBowNockArrow()
    {
        ResolveBowAttackController();
        _bowAttackController?.OnBowNockArrow();
    }

    public void OnBowReleaseArrow()
    {
        ResolveBowAttackController();
        _bowAttackController?.OnBowReleaseArrow();
    }

    public bool ProcessBowProjectileHit(float damageMultiplier, StatManager defenderStats, IDamageReceiver defender, Vector3 hitPosition, float bodyPartMultiplier, BodyPart bodyPart, uint popupPredictionId)
    {
        if (_attackProcessor == null)
            _attackProcessor = GetComponent<AttackProcessor>();

        return _attackProcessor != null && _attackProcessor.ProcessSkillHit(
            damageMultiplier,
            defenderStats,
            defender,
            hitPosition,
            bodyPartMultiplier,
            bodyPart,
            popupPredictionId);
    }

    private int ResolveAvailableSkillCount()
    {
        if (_statManager == null)
            _statManager = GetComponentInParent<StatManager>();

        if (_statManager == null)
            return 0;

        return CombatSkillRules.SlotCount(_statManager.CurrentIdentity);
    }

    private float ResolveMonostatStrLifestealRatio()
    {
        return MonostatStrSkillData != null && MonostatStrSkillData.LifestealRatio > 0f
            ? MonostatStrSkillData.LifestealRatio
            : MonostatStrSkillHealRatio;
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
        return GetSkillHudState(_selectedSkillIndex);
    }

    public SkillHudState GetSkillHudState(int index)
    {
        int count = ResolveAvailableSkillCount();
        if (index < 0 || index >= count)
            return new SkillHudState(false, string.Empty, index, count, SkillHudPhase.Hidden, 0f, 0f);
        TrySelectEquipped(index, out JobSkillKind equipped);
        if ((int)equipped >= 100 && TryGetComponent<ExpandedSkillController>(out var extra)) return extra.Hud(index, equipped);
        if(ExpandedSkillController.UsesCharges(equipped) && TryGetComponent<ExpandedSkillController>(out var charges))
        {
            var hud=charges.Hud(index,equipped);
            if(_advancedCastingSkillKey==(int)equipped || (_advancedActiveSkillKey==(int)equipped && _advancedActiveUntil>SkillTime))
                return new SkillHudState(hud.Visible,hud.Name,index,count,SkillHudPhase.Casting,1,
                    (float)(Math.Max(_advancedCastCompleteAt,_advancedActiveUntil)-SkillTime),hud.IconSprite);
            return hud;
        }
        JobSkillData data;
        bool casting = false;
        double castCompleteAt = 0d, activeUntil = 0d, cooldownUntil = 0d;
        float castSeconds = 0f, cooldownSeconds = 0f;

        if (equipped == JobSkillKind.MonostatStrLifesteal)
        {
            data = MonostatStrSkillData;
            casting = _isCastingMonostatStrSkill;
            castCompleteAt = _monostatStrSkillCastCompleteAt;
            activeUntil = _monostatStrSkillActiveUntil;
            cooldownUntil = _monostatStrSkillCooldownUntil;
            castSeconds = MonostatStrCastSeconds;
            cooldownSeconds = MonostatStrCooldownSeconds;
        }
        else if (equipped == JobSkillKind.MonostatAgiPoison)
        {
            data = MonostatAgiSkillData;
            casting = _isCastingMonostatAgiSkill;
            castCompleteAt = _monostatAgiSkillCastCompleteAt;
            activeUntil = _monostatAgiSkillActiveUntil;
            cooldownUntil = _monostatAgiSkillCooldownUntil;
            castSeconds = MonostatAgiCastSeconds;
            cooldownSeconds = MonostatAgiCooldownSeconds;
        }
        else
        {
            data = _statManager != null && TrySelectEquipped(index, out JobSkillKind kind)
                ? ResolveAssignedAdvancedSkillData((int)kind) : null;
            if (data != null)
            {
                int key = (int)data.SkillKind;
                casting = _advancedCastingSkillKey == key;
                castCompleteAt = _advancedCastCompleteAt;
                activeUntil = _advancedActiveSkillKey == key ? _advancedActiveUntil : 0d;
                TryGetAdvancedCooldownUntil(key, out cooldownUntil);
                castSeconds = data.CastSeconds;
                cooldownSeconds = data.CooldownSeconds;
            }
        }

        string name = SkillHudPresenter.ResolveName(
            _statManager != null ? _statManager.CurrentIdentity : (Identity?)null,
            index, data != null ? data.DisplayName : null);
        var snapshot = new SkillHudSnapshot(name, data != null ? data.IconSprite : null,
            index, count, casting, castCompleteAt, activeUntil, cooldownUntil,
            castSeconds, cooldownSeconds);
        return SkillHudPresenter.Build(snapshot, SkillTime);
    }

    public string GetSkillDescription(int index)
    {
        if (index < 0 || index >= _describedSkills.Length || _statManager == null ||
            !TrySelectEquipped(index, out JobSkillKind kind)) return string.Empty;
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (kind == JobSkillKind.Steal && _expanded != null && _expanded.CopiedKind >= 0)
            kind = (JobSkillKind)_expanded.CopiedKind;
        var data = ResolveSkillData((int)kind);
        if (_describedSkills[index] != data)
        { _describedSkills[index] = data; _skillDescriptions[index] = SkillDescription.Build(data); }
        return _skillDescriptions[index] ?? string.Empty;
    }

    private void PublishSkillHudState(bool force = true)
    {
        if (NetworkClient.active && !isLocalPlayer) return;
        SkillHudState state = GetSkillHudState();
        if (_skillHudPresenter.ShouldPublish(state, force, Time.unscaledTime))
            SkillHudChanged?.Invoke(state);
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
        JobSkillData data = ResolveSkillData(skillId);
        if (data != null) SkillPresentation.PlaySound(data.UseSfx, data.SfxVolume, !NetworkClient.active || isLocalPlayer);
    }

    private JobSkillData ResolveSkillData(int skillId)
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
            _ => SkillPresentationCatalog.Data(skillId)
        };
    }

    private void ApplyMonostatAgiPoisonStack(IDamageReceiver target, Vector3 hitPosition)
    {
        if (NetworkClient.active && !NetworkServer.active)
            return;

        if (!IsValidPoisonTarget(target))
            return;

        if (!_poisonStacks.Add(target, hitPosition, SkillTime, MonostatAgiPoisonStackDurationSecondsValue,
                MonostatAgiPoisonMaxStackCount)) return;

        if (target is Component component) component.GetComponent<DebuffIndicator>()?.TrackPoison(this);

        if (_monostatAgiPoisonRoutine == null)
            _monostatAgiPoisonRoutine = StartCoroutine(CoMonostatAgiPoisonTick());
    }

    internal double PoisonExpiresAt(IDamageReceiver target) => _poisonStacks.ExpiresAt(target);

    private System.Collections.IEnumerator CoMonostatAgiPoisonTick()
    {
        var wait = new WaitForSeconds(1f);
        while (_poisonStacks.Count > 0)
        {
            _poisonStacks.CollectTicks(SkillTime, MonostatAgiPoisonDamagePerStackPerSecondValue,
                IsValidPoisonTarget, _poisonTicks);
            for (int i = 0; i < _poisonTicks.Count; i++)
            {
                PoisonTick<IDamageReceiver, Vector3> tick = _poisonTicks[i];
                // A preceding damage callback may have killed or removed another target.
                if (!IsValidPoisonTarget(tick.Target)) continue;

                if (_healthSystem == null)
                    _healthSystem = GetComponent<HealthSystem>();

                if (tick.Target is IDamageReceiverWithContext ctx)
                    ctx.ApplyDamage(tick.Damage, DamageSource.Poison, 0f, _healthSystem, tick.HitPosition);
                else
                    tick.Target.ApplyDamage(tick.Damage, DamageSource.Poison, tick.HitPosition);
            }

            yield return wait;
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

    private void HandleDied()
    {
        ClearServerTauntControl();
        ClearLocalTauntControl();
        CancelAllCombatActions();
        SetSkillSwordVisual(null);
    }

    private void HandleRevived()
    {
        CancelAllCombatActions();
        if (animator != null)
            animator.speed = 1.0f;

        ForceDisableHitBoxes();
        SetSkillSwordVisual(null);
        isAttacking = false;
        hasComboReserved = false;
        currentComboIndex = 0;
    }

    private void StopCombo(bool allowDelayedContinuation = false)
    {
        if (isServer && allowDelayedContinuation)
            _serverCombo.Complete(currentComboIndex, comboList != null ? comboList.Length : 0, SkillTime);
        else _serverCombo.Reset();
        isAttacking = false;
        currentComboIndex = 0;
        hasComboReserved = false;
        if (animator != null)
            animator.speed = 1.0f;

        var pm = GetComponent<PlayerManager>();
        if (pm != null)
            pm.SetMovementLock(false);

    }

    private Vector3 GetCurrentAimDirection()
    {
        Vector3 fallback;
        if (_followCamera == null && ShouldHandleLocalInput)
            _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();

        if (_followCamera != null)
            fallback = _followCamera.GetAimDirection();
        else
            fallback = transform.forward;

        return ResolveTauntAimDirection(fallback);
    }

    private Vector3 GetCurrentMeleeAimDirection(int comboIndex = -1)
    {
        int index = comboIndex >= 0 ? comboIndex : currentComboIndex;
        var data = comboList != null && index >= 0 && index < comboList.Length ? comboList[index] : null;
        float reach = data != null && data.aimBladePoint.sqrMagnitude > .001f ?
            (_meleeAimPose != null ? _meleeAimPose.ReferenceVector(data.aimBladePoint) : transform.TransformVector(data.aimBladePoint)).magnitude : 1.4f;
        if (!BattlePvp.Logic.InputModeRules.UsesFpsLook(SceneManager.GetActiveScene().name))
            return ResolveTauntAimDirection(transform.forward) * reach;
        if (_followCamera == null)
            _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();
        if (_followCamera == null) return ResolveTauntAimDirection(transform.forward) * reach;
        Ray ray = _followCamera.GetAimRay();
        float distance = 8f;
        bool found = false;
        int count = _meleeAimQuery.Raycast(ray.origin, ray.direction, distance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var hit = _meleeAimQuery.Hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
            if (hit.collider.isTrigger && hit.collider.GetComponentInParent<IDamageReceiver>() == null) continue;
            distance = hit.distance; found = true;
        }
        if (found && data != null && _meleeAimPose != null)
            reach = _meleeAimPose.ReferenceVector(_meleeAimPose.SelectReference(data,
                Vector3.Distance(ray.GetPoint(distance), MeleeAimPivot))).magnitude;
        Vector3 direction = MeleeAimPose.ReachablePoint(ray, MeleeAimPivot, reach) - MeleeAimPivot;
        if (Vector3.Dot(direction, ray.direction) <= .01f) direction = ray.direction;
        return ResolveTauntAimDirection(direction.normalized) * reach;
    }

    private void SetTauntedBy(uint taunterNetId, float durationSeconds)
    {
        if (!NetworkServer.active || durationSeconds <= 0f)
            return;
        _tauntedByNetId = taunterNetId;
        _tauntedUntil = SkillTime + durationSeconds;
        CancelAllCombatActions();
        UpdateServerTauntControl();
    }

    private void UpdateServerTauntControl()
    {
        if (!isServer) return;
        if ((_healthSystem != null && _healthSystem.IsDead) || _tauntedByNetId == 0 ||
            SkillTime >= _tauntedUntil || !TryResolveTauntTarget(out Transform taunter))
        {
            ClearServerTauntControl();
            return;
        }

        float stopDistance = MonostatDefSkillData != null ? MonostatDefSkillData.TauntStopDistance : 1.8f;
        _playerManager?.SetForcedTauntControl(true, taunter.position, stopDistance);
        if (!HasAuthoritativeCombatStats || IsBattleLoadingOrNotStarted()) return;
        Vector3 direction = taunter.position - transform.position;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        if (_isBowEquipped)
        {
            _serverTauntBowActive = true;
            _bowAttackController?.ServerTickTauntAttack(direction);
            return;
        }
        if (isAttacking) return;
        uint sequence = CombatRequestSequences.Next(_lastServerAttackSequence);
        _currentAttackSequence = sequence;
        StartAttack(0, false, direction, true);
        if (!isAttacking) return;
        _lastServerAttackSequence = sequence;
        RpcStartAttackFast(0, direction, sequence);
        RpcStartAttack(0, direction, sequence);
        if (connectionToClient != null) TargetStartTauntAttack(connectionToClient, direction, sequence);
    }

    private void ClearServerTauntControl()
    {
        if (netIdentity == null || !isServer) return;
        if (_serverTauntBowActive) _bowAttackController?.ServerEndTauntAttack();
        _serverTauntBowActive = false;
        _tauntedByNetId = 0;
        _tauntedUntil = 0d;
        _playerManager?.SetForcedTauntControl(false, Vector3.zero, 0f);
    }

    [TargetRpc]
    private void TargetStartTauntAttack(NetworkConnectionToClient target, Vector3 direction, uint sequence)
    {
        if (isServer) return;
        _currentAttackSequence = sequence;
        _nextLocalAttackSequence = CombatRequestSequences.RestoreOwner(_nextLocalAttackSequence, sequence);
        StartRemoteAttackVisual(0, direction);
    }

    private void UpdateLocalTauntControl()
    {
        if (_healthSystem != null && _healthSystem.IsDead)
        {
            ClearLocalTauntControl();
            return;
        }

        if (_tauntedByNetId == 0 || SkillTime >= _tauntedUntil || !TryResolveTauntTarget(out Transform taunter))
        {
            ClearLocalTauntControl();
            return;
        }

        if (_playerManager == null)
            _playerManager = GetComponent<PlayerManager>();
        if (_followCamera == null)
            _followCamera = FindFirstObjectByType<BattlePvp.CameraLogic.FollowCamera>();

        float stopDistance = MonostatDefSkillData != null ? MonostatDefSkillData.TauntStopDistance : 1.8f;
        if (!_localTauntControlActive && !isServer && _bowAttackController != null && _bowAttackController.IsCharging)
            _bowAttackController?.CancelCharge();
        if (!isServer) _playerManager?.SetForcedTauntControl(true, taunter.position, stopDistance);
        _followCamera?.SetForcedLookTarget(taunter);
        _localTauntControlActive = true;

    }

    public void NotifyConfirmedHit(bool isHeadshot)
    {
        if (NetworkServer.active)
        {
            if (connectionToClient != null)
                TargetShowHitFeedback(connectionToClient, isHeadshot);
            else if (isLocalPlayer)
                PlayHitFeedbackLocal(isHeadshot);
            return;
        }

        if (!NetworkClient.active || isLocalPlayer)
            PlayHitFeedbackLocal(isHeadshot);
    }

    [TargetRpc]
    private void TargetShowHitFeedback(NetworkConnectionToClient target, bool isHeadshot)
    {
        PlayHitFeedbackLocal(isHeadshot);
    }

    private void PlayHitFeedbackLocal(bool isHeadshot)
    {
        SkillPresentation.PlayConfirmedHit(isHeadshot);
        GetComponent<CombatAudio>()?.PlayConfirmedHit();
    }

    private void ClearLocalTauntControl()
    {
        if (!_localTauntControlActive)
            return;

        if (!isServer) _playerManager?.SetForcedTauntControl(false, Vector3.zero, 0f);
        _followCamera?.SetForcedLookTarget(null);
        _localTauntControlActive = false;
    }

    private bool TryResolveTauntTarget(out Transform target)
    {
        target = null;
        NetworkIdentity targetIdentity = null;
        if (NetworkServer.active)
            NetworkServer.spawned.TryGetValue(_tauntedByNetId, out targetIdentity);
        if (targetIdentity == null && NetworkClient.active)
            NetworkClient.spawned.TryGetValue(_tauntedByNetId, out targetIdentity);

        if (targetIdentity == null)
            return false;

        target = targetIdentity.transform;
        return target != null;
    }

    private Vector3 ResolveTauntAimDirection(Vector3 fallback)
    {
        if (_tauntedByNetId == 0 || SkillTime >= _tauntedUntil)
            return fallback;

        if (!TryResolveTauntTarget(out Transform target))
            return fallback;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : fallback;
    }
}
