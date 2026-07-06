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

    public enum JobSkillKind
    {
        MonostatStrLifesteal = 0,
        MonostatAgiPoison = 1,
        MonostatConKick = 2,
        MonostatDefTaunt = 3,
        StrategistRoll = 10,
        StrategistPresetChange = 11,
        PolymathRoll = 20,
        PolymathPresetChange = 21,
        PolymathWeaponSwap = 22
    }

    [CreateAssetMenu(fileName = "NewJobSkillData", menuName = "Combat/Job Skill Data")]
    public sealed class JobSkillData : ScriptableObject
    {
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

        [Min(0)] [SerializeField] private int _poisonMaxStacks = 0;
        [Min(0f)] [SerializeField] private float _poisonDamagePerStackPerSecond = 0f;
        [Min(0f)] [SerializeField] private float _poisonStackDurationSeconds = 0f;

        [Min(0f)] [SerializeField] private float _kickDamageMultiplier = 1.5f;
        [Min(0f)] [SerializeField] private float _kickKnockbackDistance = 3f;
        [Range(0f, 1f)] [SerializeField] private float _kickSlowMoveMultiplier = 0.2f;
        [Min(0f)] [SerializeField] private float _kickSlowDurationSeconds = 0.65f;

        [Min(0f)] [SerializeField] private float _tauntReadyDurationSeconds = 30f;
        [Min(0f)] [SerializeField] private float _tauntDurationSeconds = 1.2f;
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
        public string DisplayName => _displayName;
        public Sprite IconSprite => _iconSprite;
        public float CastSeconds => _castSeconds;
        public float DurationSeconds => _durationSeconds;
        public float CooldownSeconds => _cooldownSeconds;
        public SkillInputLockFlags InputLockFlags => _inputLockFlags != SkillInputLockFlags.None ? _inputLockFlags : GetDefaultInputLockFlags();
        public float InputLockSeconds => _inputLockSeconds;
        public float ResolveInputLockSeconds()
        {
            if (_inputLockSeconds > 0f)
                return _inputLockSeconds;

            return GetDefaultInputLockSeconds();
        }
        public string CastAnimationStateName => _castAnimationStateName;
        public int CastAnimationLayer => _castAnimationLayer;
        public float LifestealRatio => _lifestealRatio;
        public int PoisonMaxStacks => _poisonMaxStacks;
        public float PoisonDamagePerStackPerSecond => _poisonDamagePerStackPerSecond;
        public float PoisonStackDurationSeconds => _poisonStackDurationSeconds;
        public float KickDamageMultiplier => _kickDamageMultiplier;
        public float KickKnockbackDistance => _kickKnockbackDistance;
        public float KickSlowMoveMultiplier => _kickSlowMoveMultiplier;
        public float KickSlowDurationSeconds => _kickSlowDurationSeconds;
        public float TauntReadyDurationSeconds => _tauntReadyDurationSeconds;
        public float TauntDurationSeconds => _tauntDurationSeconds;
        public float TauntIncomingDamageMultiplier => _tauntIncomingDamageMultiplier;
        public float TauntReflectMultiplier => _tauntReflectMultiplier;
        public float TauntReflectHealthCapRatio => _tauntReflectHealthCapRatio;
        public float RollDistance => _rollDistance;
        public float RollDurationSeconds => _rollDurationSeconds;
        public BattlePvp.Stats.StatContainer TargetPreset => _targetPreset;
        public float MaxHealthIncreaseShieldRatio => _maxHealthIncreaseShieldRatio;
        public float ShieldDurationSeconds => _shieldDurationSeconds;
        public float StrategistStrNextAttackMultiplier => _strategistStrNextAttackMultiplier;
        public float StrategistStrAttackBonusDurationSeconds => _strategistStrAttackBonusDurationSeconds;
        public float StrategistAgiBonusDurationSeconds => _strategistAgiBonusDurationSeconds;
        public float StrategistAgiMoveMultiplier => _strategistAgiMoveMultiplier;
        public float StrategistAgiAttackSpeedMultiplier => _strategistAgiAttackSpeedMultiplier;
        public float StrategistConTargetMaxHpShieldRatio => _strategistConTargetMaxHpShieldRatio;
        public float StrategistDefInvulnerableSeconds => _strategistDefInvulnerableSeconds;
        public float MinimumBowChargeSeconds => _minimumBowChargeSeconds;
        public float MaximumBowDamageChargeSeconds => _maximumBowDamageChargeSeconds;
        public float MinimumBowDamageMultiplier => _minimumBowDamageMultiplier;
        public float MaximumBowDamageMultiplier => _maximumBowDamageMultiplier;
        public float BowChargeMoveMultiplier => _bowChargeMoveMultiplier;
        public float BowRange => _bowRange;
        public float WeaponSwapMoveBonusDurationSeconds => _weaponSwapMoveBonusDurationSeconds;
        public float WeaponSwapMoveMultiplier => _weaponSwapMoveMultiplier;
        public float WeaponSwapNextAttackMultiplier => _weaponSwapNextAttackMultiplier;
        public AudioClip UseSfx => _useSfx;
        public float SfxVolume => _sfxVolume;

        private SkillInputLockFlags GetDefaultInputLockFlags()
        {
            return _skillKind switch
            {
                JobSkillKind.PolymathWeaponSwap => SkillInputLockFlags.Move,
                _ => SkillInputLockFlags.Move | SkillInputLockFlags.Attack | SkillInputLockFlags.Jump
            };
        }

        private float GetDefaultInputLockSeconds()
        {
            return _skillKind switch
            {
                JobSkillKind.MonostatConKick => 0.75f,
                JobSkillKind.MonostatDefTaunt => 1.2f,
                JobSkillKind.MonostatStrLifesteal => Mathf.Max(0.7f, _castSeconds),
                JobSkillKind.MonostatAgiPoison => Mathf.Max(1f, _castSeconds),
                JobSkillKind.StrategistRoll => Mathf.Max(0.35f, _rollDurationSeconds),
                JobSkillKind.StrategistPresetChange => Mathf.Max(0.35f, _castSeconds),
                JobSkillKind.PolymathRoll => Mathf.Max(0.35f, _rollDurationSeconds),
                JobSkillKind.PolymathPresetChange => Mathf.Max(0.35f, _castSeconds),
                JobSkillKind.PolymathWeaponSwap => Mathf.Max(0.15f, _castSeconds),
                _ => Mathf.Max(0.35f, _castSeconds)
            };
        }
    }
}
