using UnityEngine;
using UnityEngine.Serialization;

namespace BattlePvp.Combat
{
    [System.Flags]
    public enum SkillInputLockFlags
    {
        None = 0,
        Move = 1 << 0,
        Attack = 1 << 1,
        Jump = 1 << 2,
        Crouch = 1 << 3
    }

    [CreateAssetMenu(fileName = "NewJobSkillData", menuName = "Combat/Job Skill Data")]
    public sealed class JobSkillData : ScriptableObject
    {
        [SerializeField] private bool _useGameData;
        public bool UsesGameData => _useGameData;
        private float ReadNumber(JobSkillKind kind, string key, float fallback) => _useGameData ? SkillGameData.Number(kind, key, fallback) : fallback;
        [SerializeField] private JobSkillKind _skillKind = JobSkillKind.MonostatStrLifesteal;

        [SerializeField] private string _displayName = "Skill";
        [SerializeField] private Sprite _iconSprite;

        [Min(0f)] [SerializeField] private float _castSeconds = 0f;
        [Min(0f)] [SerializeField] private float _durationSeconds = 0f;
        [Min(0f)] [SerializeField] private float _cooldownSeconds = 0f;
        [SerializeField] private SkillInputLockFlags _inputLockFlags = SkillInputLockFlags.None;
        [Min(0f)] [SerializeField] private float _inputLockSeconds = 0f;
        [FormerlySerializedAs("_strCastAnimationStateName")]
        [SerializeField] private string _castAnimationStateName = string.Empty;
        [FormerlySerializedAs("_strCastAnimationLayer")]
        [Min(0)] [SerializeField] private int _castAnimationLayer = 0;

        [Min(0f)] [SerializeField] private float _lifestealRatio = 0f;
        [Min(0f)] [SerializeField] private float _strAttackSpeedMultiplier = 1.2f;
        [Min(0f)] [SerializeField] private float _strMoveMultiplier = 1.1f;

        [Min(0)] [SerializeField] private int _poisonMaxStacks = 0;
        [Min(0f)] [SerializeField] private float _poisonDamagePerStackPerSecond = 0f;
        [Min(0f)] [SerializeField] private float _poisonStackDurationSeconds = 0f;
        [SerializeField] private Material _swordMaterial;

        [Min(0f)] [SerializeField] private float _kickDamageMultiplier = 1.5f;
        [Min(0f)] [SerializeField] private float _kickKnockbackDistance = 3f;
        [Range(0f, 1f)] [SerializeField] private float _kickSlowMoveMultiplier = 0.2f;
        [Min(0f)] [SerializeField] private float _kickSlowDurationSeconds = 0.65f;

        [Min(0f)] [SerializeField] private float _tauntReadyDurationSeconds = 30f;
        [Min(0f)] [SerializeField] private float _tauntDurationSeconds = 1.2f;
        [Min(0f)] [SerializeField] private float _tauntStopDistance = 1.8f;
        [Range(0f, 1f)] [SerializeField] private float _tauntIncomingDamageMultiplier = 0.7f;
        [Min(0f)] [SerializeField] private float _tauntReflectMultiplier = 2f;
        [Range(0f, 1f)] [SerializeField] private float _tauntReflectHealthCapRatio = 0.14f;

        [Min(0f)] [SerializeField] private float _rollDistance = 3.5f;
        [Min(0f)] [SerializeField] private float _rollDurationSeconds = 0.35f;

        [SerializeField] private BattlePvp.Stats.StatContainer _targetPreset;
        [Range(0f, 1f)] [SerializeField] private float _maxHealthIncreaseShieldRatio = 0.5f;
        [Min(0f)] [SerializeField] private float _shieldDurationSeconds = 20f;
        [Min(0f)] [SerializeField] private float _strategistStrNextAttackMultiplier = 1.2f;
        [Min(0f)] [SerializeField] private float _strategistStrAttackBonusDurationSeconds = 5f;
        [Min(0f)] [SerializeField] private float _strategistAgiBonusDurationSeconds = 4f;
        [Min(0f)] [SerializeField] private float _strategistAgiMoveMultiplier = 1.15f;
        [Min(0f)] [SerializeField] private float _strategistAgiAttackSpeedMultiplier = 1.35f;
        [Range(0f, 1f)] [SerializeField] private float _strategistConTargetMaxHpShieldRatio = 0.2f;
        [Min(0f)] [SerializeField] private float _strategistDefInvulnerableSeconds = 5f;

        [Min(0f)] [SerializeField] private float _minimumBowChargeSeconds = 0.25f;
        [Min(0f)] [SerializeField] private float _maximumBowDamageChargeSeconds = 1f;
        [Min(0f)] [SerializeField] private float _minimumBowDamageMultiplier = 0.4f;
        [Min(0f)] [SerializeField] private float _maximumBowDamageMultiplier = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float _bowChargeMoveMultiplier = 0.25f;
        [Min(0f)] [SerializeField] private float _bowRange = 30f;
        [Min(0f)] [SerializeField] private float _weaponSwapMoveBonusDurationSeconds = 3f;
        [Min(0f)] [SerializeField] private float _weaponSwapMoveMultiplier = 1.2f;
        [Min(0f)] [SerializeField] private float _weaponSwapNextAttackMultiplier = 1.3f;

        [SerializeField] private AudioClip _useSfx;
        [Range(0f, 1f)] [SerializeField] private float _sfxVolume = 0.9f;

        public JobSkillKind SkillKind => _skillKind;
        public string DisplayName => _useGameData && SkillGameData.Instance != null && SkillGameData.Instance.Find((int)_skillKind) != null ? SkillGameData.Text(SkillGameData.Instance.Find((int)_skillKind).NameKey, _displayName) : _displayName;
        public Sprite IconSprite => _iconSprite;
        public float CastSeconds => ReadNumber(_skillKind, "CastSeconds", _castSeconds);
        public float DurationSeconds => ReadNumber(_skillKind, "DurationSeconds", _durationSeconds);
        public float CooldownSeconds => ReadNumber(_skillKind, "CooldownSeconds", _cooldownSeconds);
        public SkillInputLockFlags InputLockFlags => _inputLockFlags != SkillInputLockFlags.None ? _inputLockFlags : GetDefaultInputLockFlags();
        public float InputLockSeconds => ReadNumber(_skillKind, "InputLockSeconds", _inputLockSeconds);
        public float ResolveInputLockSeconds()
        {
            if (InputLockSeconds > 0f)
                return InputLockSeconds;

            return GetDefaultInputLockSeconds();
        }
        public string CastAnimationStateName => _castAnimationStateName;
        public int CastAnimationLayer => _castAnimationLayer;
        public float LifestealRatio => ReadNumber(_skillKind, "LifestealRatio", _lifestealRatio);
        public float StrAttackSpeedMultiplier => 1f + ReadNumber(_skillKind, "AttackSpeedBonus", _strAttackSpeedMultiplier - 1f);
        public float StrMoveMultiplier => 1f + ReadNumber(_skillKind, "MoveBonus", _strMoveMultiplier - 1f);
        public int PoisonMaxStacks => (int)ReadNumber(_skillKind, "PoisonMaxStacks", _poisonMaxStacks);
        public float PoisonDamagePerStackPerSecond => ReadNumber(_skillKind, "PoisonDamagePerStackPerSecond", _poisonDamagePerStackPerSecond);
        public float PoisonStackDurationSeconds => ReadNumber(_skillKind, "PoisonStackDurationSeconds", _poisonStackDurationSeconds);
        public Material SwordMaterial => _swordMaterial;
        public float KickDamageMultiplier => ReadNumber(_skillKind, "KickDamageMultiplier", _kickDamageMultiplier);
        public float KickKnockbackDistance => ReadNumber(_skillKind, "KickKnockbackDistance", _kickKnockbackDistance);
        public float KickSlowMoveMultiplier => ReadNumber(_skillKind, "KickSlowMoveMultiplier", _kickSlowMoveMultiplier);
        public float KickSlowDurationSeconds => ReadNumber(_skillKind, "KickSlowDurationSeconds", _kickSlowDurationSeconds);
        public float TauntReadyDurationSeconds => ReadNumber(_skillKind, "TauntReadyDurationSeconds", _tauntReadyDurationSeconds);
        public float TauntDurationSeconds => ReadNumber(_skillKind, "TauntDurationSeconds", _tauntDurationSeconds);
        public float TauntStopDistance => ReadNumber(_skillKind, "TauntStopDistance", _tauntStopDistance);
        public float TauntIncomingDamageMultiplier => ReadNumber(_skillKind, "TauntIncomingDamageMultiplier", _tauntIncomingDamageMultiplier);
        public float TauntReflectMultiplier => ReadNumber(_skillKind, "TauntReflectMultiplier", _tauntReflectMultiplier);
        public float TauntReflectHealthCapRatio => ReadNumber(_skillKind, "TauntReflectHealthCapRatio", _tauntReflectHealthCapRatio);
        public float RollDistance => ReadNumber(_skillKind, "RollDistance", _rollDistance);
        public float RollDurationSeconds => ReadNumber(_skillKind, "RollDurationSeconds", _rollDurationSeconds);
        public BattlePvp.Stats.StatContainer TargetPreset
        {
            get
            {
                var preset=_targetPreset;
                preset.STR.Invested=ReadNumber(_skillKind,"TargetPresetSTR",preset.STR.Invested);
                preset.CON.Invested=ReadNumber(_skillKind,"TargetPresetCON",preset.CON.Invested);
                preset.AGI.Invested=ReadNumber(_skillKind,"TargetPresetAGI",preset.AGI.Invested);
                preset.DEF.Invested=ReadNumber(_skillKind,"TargetPresetDEF",preset.DEF.Invested);
                return preset;
            }
        }
        public float MaxHealthIncreaseShieldRatio => ReadNumber(_skillKind, "MaxHealthIncreaseShieldRatio", _maxHealthIncreaseShieldRatio);
        public float ShieldDurationSeconds => ReadNumber(_skillKind, "ShieldDurationSeconds", _shieldDurationSeconds);
        public float StrategistStrNextAttackMultiplier => ReadNumber(_skillKind, "StrategistStrNextAttackMultiplier", _strategistStrNextAttackMultiplier);
        public float StrategistStrAttackBonusDurationSeconds => ReadNumber(_skillKind, "StrategistStrAttackBonusDurationSeconds", _strategistStrAttackBonusDurationSeconds);
        public float StrategistAgiBonusDurationSeconds => ReadNumber(_skillKind, "StrategistAgiBonusDurationSeconds", _strategistAgiBonusDurationSeconds);
        public float StrategistAgiMoveMultiplier => ReadNumber(_skillKind, "StrategistAgiMoveMultiplier", _strategistAgiMoveMultiplier);
        public float StrategistAgiAttackSpeedMultiplier => ReadNumber(_skillKind, "StrategistAgiAttackSpeedMultiplier", _strategistAgiAttackSpeedMultiplier);
        public float StrategistConTargetMaxHpShieldRatio => ReadNumber(_skillKind, "StrategistConTargetMaxHpShieldRatio", _strategistConTargetMaxHpShieldRatio);
        public float StrategistDefInvulnerableSeconds => ReadNumber(_skillKind, "StrategistDefInvulnerableSeconds", _strategistDefInvulnerableSeconds);
        public float MinimumBowChargeSeconds => ReadNumber(_skillKind, "MinimumBowChargeSeconds", _minimumBowChargeSeconds);
        public float MaximumBowDamageChargeSeconds => ReadNumber(_skillKind, "MaximumBowDamageChargeSeconds", _maximumBowDamageChargeSeconds);
        public float MinimumBowDamageMultiplier => ReadNumber(_skillKind, "MinimumBowDamageMultiplier", _minimumBowDamageMultiplier);
        public float MaximumBowDamageMultiplier => ReadNumber(_skillKind, "MaximumBowDamageMultiplier", _maximumBowDamageMultiplier);
        public float BowChargeMoveMultiplier => ReadNumber(_skillKind, "BowChargeMoveMultiplier", _bowChargeMoveMultiplier);
        public float BowRange => ReadNumber(_skillKind, "BowRange", _bowRange);
        public float WeaponSwapMoveBonusDurationSeconds => ReadNumber(_skillKind, "WeaponSwapMoveBonusDurationSeconds", _weaponSwapMoveBonusDurationSeconds);
        public float WeaponSwapMoveMultiplier => ReadNumber(_skillKind, "WeaponSwapMoveMultiplier", _weaponSwapMoveMultiplier);
        public float WeaponSwapNextAttackMultiplier => ReadNumber(_skillKind, "WeaponSwapNextAttackMultiplier", _weaponSwapNextAttackMultiplier);
        public AudioClip UseSfx => _useSfx;
        public float SfxVolume => _sfxVolume;

        private SkillInputLockFlags GetDefaultInputLockFlags()
        {
            return _skillKind switch
            {
                JobSkillKind.Bash or JobSkillKind.Thorns => SkillInputLockFlags.None,
                JobSkillKind.PolymathWeaponSwap => SkillInputLockFlags.Move,
                _ => SkillInputLockFlags.Move | SkillInputLockFlags.Attack | SkillInputLockFlags.Jump
            };
        }

        private float GetDefaultInputLockSeconds()
        {
            return _skillKind switch
            {
                JobSkillKind.Bash or JobSkillKind.Thorns => 0f,
                JobSkillKind.MonostatConKick => 0.75f,
                JobSkillKind.MonostatDefTaunt => 1.2f,
                JobSkillKind.MonostatStrLifesteal => Mathf.Max(0.7f, CastSeconds),
                JobSkillKind.MonostatAgiPoison => Mathf.Max(1f, CastSeconds),
                JobSkillKind.StrategistRoll => Mathf.Max(0.35f, RollDurationSeconds),
                JobSkillKind.StrategistPresetChange => Mathf.Max(0.35f, CastSeconds),
                JobSkillKind.PolymathRoll => Mathf.Max(0.35f, RollDurationSeconds),
                JobSkillKind.PolymathPresetChange => Mathf.Max(0.35f, CastSeconds),
                JobSkillKind.PolymathWeaponSwap => Mathf.Max(0.15f, CastSeconds),
                _ => Mathf.Max(0.35f, CastSeconds)
            };
        }
    }
}
