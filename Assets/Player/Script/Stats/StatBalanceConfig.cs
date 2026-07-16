using UnityEngine;

namespace BattlePvp.Stats
{
    [CreateAssetMenu(fileName = "StatBalanceConfig", menuName = "Battle PVP/Stats/Balance Config")]
    public sealed class StatBalanceConfig : ScriptableObject
    {
        public static event System.Action BalanceChanged;

        [Header("STR")]
        [Min(0f)] [SerializeField] private float _baseAttackPower = 22f;
        [Min(0f)] [SerializeField] private float _baseStatTotal = 5f;
        [Min(0f)] [SerializeField] private float _attackPowerPerStr = 3f;
        [Min(0f)] [SerializeField] private float _penetrationPerStr = 0.3f;
        [Min(0f)] [SerializeField] private float _maxHpPerStr = 4f;
        [Min(0f)] [SerializeField] private float _monostatStrAttackMultiplier = 1.4f;
        [Min(0f)] [SerializeField] private float _monostatStrPenetrationBonus = 18f;
        [Min(0f)] [SerializeField] private float _monostatStrMoveSpeedMultiplier = 0.75f;
        [Min(0f)] [SerializeField] private float _monostatStrAttackSpeedMultiplier = 0.75f;

        [Header("AGI")]
        [Min(0f)] [SerializeField] private float _baseMoveSpeed = 3f;
        [Min(0f)] [SerializeField] private float _moveSpeedPerAgi = 0.04f;
        [Min(0f)] [SerializeField] private float _baseAttackSpeed = 0.6f;
        [Min(0f)] [SerializeField] private float _attackSpeedPerAgi = 0.02f;
        [Min(0f)] [SerializeField] private float _monostatAgiMoveSpeedMultiplier = 1.2f;
        [Min(0f)] [SerializeField] private float _monostatAgiAttackSpeedMultiplier = 3f;
        [Min(0f)] [SerializeField] private float _monostatAgiMaxHpMultiplier = 0.7f;

        [Header("DEF")]
        [Min(0f)] [SerializeField] private float _maxHpPerDef = 2f;
        [Range(0f, 1f)] [SerializeField] private float _monostatDefDefenseBonus = 0.5f;
        [Min(0f)] [SerializeField] private float _monostatDefMoveSpeedMultiplier = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float _defenseEfficiencyHardCap = 0.75f;
        [Min(0f)] [SerializeField] private float _thornsAttackPowerRatio = 0.15f;
        [Min(0f)] [SerializeField] private float _thornsFixedDamage = 5f;
        [Range(0f, 1f)] [SerializeField] private float _thornsAttackerMaxHpCapRatio = 0.07f;

        [Header("CON")]
        [Min(0f)] [SerializeField] private float _baseMaxHp = 100f;
        [Min(0f)] [SerializeField] private float _maxHpPerCon = 15f;
        [Min(0f)] [SerializeField] private float _regenPerCon = 0.15f;
        [Min(0f)] [SerializeField] private float _monostatConMaxHpMultiplier = 1.6f;
        [Min(0f)] [SerializeField] private float _monostatConRegenBonus = 5f;
        [Range(0f, 1f)] [SerializeField] private float _monostatConIncomingDamageMultiplier = 0.7f;

        public float BaseAttackPower => _baseAttackPower;
        public float BaseStatTotal => _baseStatTotal;
        public float AttackPowerPerStr => _attackPowerPerStr;
        public float PenetrationPerStr => _penetrationPerStr;
        public float MaxHpPerStr => _maxHpPerStr;
        public float MonostatStrAttackMultiplier => _monostatStrAttackMultiplier;
        public float MonostatStrPenetrationBonus => _monostatStrPenetrationBonus;
        public float MonostatStrMoveSpeedMultiplier => _monostatStrMoveSpeedMultiplier;
        public float MonostatStrAttackSpeedMultiplier => _monostatStrAttackSpeedMultiplier;
        public float BaseMoveSpeed => _baseMoveSpeed;
        public float MoveSpeedPerAgi => _moveSpeedPerAgi;
        public float BaseAttackSpeed => _baseAttackSpeed;
        public float AttackSpeedPerAgi => _attackSpeedPerAgi;
        public float MonostatAgiMoveSpeedMultiplier => _monostatAgiMoveSpeedMultiplier;
        public float MonostatAgiAttackSpeedMultiplier => _monostatAgiAttackSpeedMultiplier;
        public float MonostatAgiMaxHpMultiplier => _monostatAgiMaxHpMultiplier;
        public float MaxHpPerDef => _maxHpPerDef;
        public float MonostatDefDefenseBonus => _monostatDefDefenseBonus;
        public float MonostatDefMoveSpeedMultiplier => _monostatDefMoveSpeedMultiplier;
        public float DefenseEfficiencyHardCap => _defenseEfficiencyHardCap;
        public float ThornsAttackPowerRatio => _thornsAttackPowerRatio;
        public float ThornsFixedDamage => _thornsFixedDamage;
        public float ThornsAttackerMaxHpCapRatio => _thornsAttackerMaxHpCapRatio;
        public float BaseMaxHp => _baseMaxHp;
        public float MaxHpPerCon => _maxHpPerCon;
        public float RegenPerCon => _regenPerCon;
        public float MonostatConMaxHpMultiplier => _monostatConMaxHpMultiplier;
        public float MonostatConRegenBonus => _monostatConRegenBonus;
        public float MonostatConIncomingDamageMultiplier => _monostatConIncomingDamageMultiplier;

        private void OnValidate()
        {
            BalanceChanged?.Invoke();
        }
    }

    public readonly struct DerivedCombatStats
    {
        public float AttackPower { get; }
        public float PenetrationPercent { get; }
        public float MaxHp { get; }
        public float RegenPerSecond { get; }
        public float DefenseEfficiencyPercent { get; }
        public float DefenseBonusNormalized { get; }
        public float MoveSpeed { get; }
        public float AttackSpeed { get; }
        public float IncomingDamageMultiplier { get; }

        public DerivedCombatStats(
            float attackPower,
            float penetrationPercent,
            float maxHp,
            float regenPerSecond,
            float defenseEfficiencyPercent,
            float defenseBonusNormalized,
            float moveSpeed,
            float attackSpeed,
            float incomingDamageMultiplier)
        {
            AttackPower = attackPower;
            PenetrationPercent = penetrationPercent;
            MaxHp = maxHp;
            RegenPerSecond = regenPerSecond;
            DefenseEfficiencyPercent = defenseEfficiencyPercent;
            DefenseBonusNormalized = defenseBonusNormalized;
            MoveSpeed = moveSpeed;
            AttackSpeed = attackSpeed;
            IncomingDamageMultiplier = incomingDamageMultiplier;
        }
    }

    public static class StatBalanceCalculator
    {
        private const string ResourcePath = "StatBalanceConfig";
        private static StatBalanceConfig _config;
        private static bool _loggedMissingConfig;

        public static StatBalanceConfig Config
        {
            get
            {
                if (_config != null)
                    return _config;

                _config = Resources.Load<StatBalanceConfig>(ResourcePath);
                if (_config == null)
                {
                    _config = ScriptableObject.CreateInstance<StatBalanceConfig>();
                    _config.hideFlags = HideFlags.HideAndDontSave;
                    if (!_loggedMissingConfig)
                    {
                        Debug.LogError($"[StatBalance] Resources/{ResourcePath}.asset is missing. Using built-in defaults.");
                        _loggedMissingConfig = true;
                    }
                }

                return _config;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _config = null;
            _loggedMissingConfig = false;
        }

        public static DerivedCombatStats Calculate(StatContainer stats, Identity identity)
        {
            float str = StatMath.FinalTotal(stats.STR);
            float con = StatMath.FinalTotal(stats.CON);
            float agi = StatMath.FinalTotal(stats.AGI);
            float def = StatMath.FinalTotal(stats.DEF);
            return Calculate(str, con, agi, def, identity);
        }

        public static DerivedCombatStats Calculate(float str, float con, float agi, float def, Identity identity)
        {
            StatBalanceConfig config = Config;
            float attackPower = config.BaseAttackPower
                + Mathf.Max(0f, str - config.BaseStatTotal) * config.AttackPowerPerStr;
            float penetration = str * config.PenetrationPerStr;
            float maxHp = config.BaseMaxHp
                + con * config.MaxHpPerCon
                + str * config.MaxHpPerStr
                + def * config.MaxHpPerDef;
            float regen = con * config.RegenPerCon;
            float moveSpeed = config.BaseMoveSpeed + agi * config.MoveSpeedPerAgi;
            float attackSpeed = config.BaseAttackSpeed + agi * config.AttackSpeedPerAgi;
            float defenseBonus = 0f;
            float incomingDamageMultiplier = 1f;

            if (identity.Type == IdentityType.Monostat)
            {
                switch (identity.PrimaryStat)
                {
                    case StatKind.STR:
                        attackPower *= config.MonostatStrAttackMultiplier;
                        penetration += config.MonostatStrPenetrationBonus;
                        moveSpeed *= config.MonostatStrMoveSpeedMultiplier;
                        attackSpeed *= config.MonostatStrAttackSpeedMultiplier;
                        break;
                    case StatKind.AGI:
                        maxHp *= config.MonostatAgiMaxHpMultiplier;
                        moveSpeed *= config.MonostatAgiMoveSpeedMultiplier;
                        attackSpeed *= config.MonostatAgiAttackSpeedMultiplier;
                        break;
                    case StatKind.DEF:
                        defenseBonus = config.MonostatDefDefenseBonus;
                        moveSpeed *= config.MonostatDefMoveSpeedMultiplier;
                        break;
                    case StatKind.CON:
                        maxHp *= config.MonostatConMaxHpMultiplier;
                        regen += config.MonostatConRegenBonus;
                        incomingDamageMultiplier = config.MonostatConIncomingDamageMultiplier;
                        break;
                }
            }

            float currentDefense = Mathf.Clamp01(def / 100f);
            float finalDefense = 1f - (1f - currentDefense) * (1f - Mathf.Clamp01(defenseBonus));
            finalDefense = Mathf.Clamp(finalDefense, 0f, config.DefenseEfficiencyHardCap);

            return new DerivedCombatStats(
                Mathf.Max(0f, attackPower),
                Mathf.Clamp(penetration, 0f, 100f),
                Mathf.Max(1f, maxHp),
                Mathf.Max(0f, regen),
                finalDefense * 100f,
                defenseBonus,
                Mathf.Max(0f, moveSpeed),
                Mathf.Max(0.01f, attackSpeed),
                Mathf.Max(0f, incomingDamageMultiplier));
        }
    }
}
