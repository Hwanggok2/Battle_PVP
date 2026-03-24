using System.Text;
using BattlePvp.Stats;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorTools
{
    /// <summary>
    /// Stat/Identity/Damage를 빠르게 검증하기 위한 EditorWindow.
    /// - 클릭 기반으로만 동작 (Update() 사용 안 함)
    /// - 로그는 Unity Console에 기록
    /// </summary>
    public sealed class StatSimulatorWindow : EditorWindow
    {
        private const float PenetrationMin = 0f;
        private const float PenetrationMax = 100f;

        private static readonly string Title = "Stat Simulator";

        private StatContainer _attackerStats = new StatContainer
        {
            STR = new StatSlot { Invested = 10f, Item = 0f },
            CON = new StatSlot { Invested = 0f, Item = 0f },
            AGI = new StatSlot { Invested = 0f, Item = 0f },
            DEF = new StatSlot { Invested = 0f, Item = 0f }
        };

        private StatContainer _defenderStats = new StatContainer
        {
            STR = new StatSlot { Invested = 0f, Item = 0f },
            CON = new StatSlot { Invested = 10f, Item = 0f },
            AGI = new StatSlot { Invested = 0f, Item = 0f },
            DEF = new StatSlot { Invested = 20f, Item = 0f }
        };

        private float _penetrationPercent = 0f;
        private float _defenseBonusEffNormalized = 0f; // 0..1

        private float _defenderCurrentHp = 100f;
        private float _defenderMaxHp = 100f;

        private bool _enableStrategistPenalty = true;
        private float _strategistPenaltyTimeSeconds = 0f; // 0..1.5 권장

        private bool _enableStrategistOverflowPreview = false;
        private float _overflowDeltaSeconds = 1f;

        private bool _enableThorns = false;

        private IdentityCalculator _identityCalculator;
        private DamageCalculator _damageCalculator;
        private StrategistRules _strategistRules;

        [MenuItem("BattlePVP/Tools/Stat Simulator")]
        private static void Open()
        {
            var window = GetWindow<StatSimulatorWindow>(Title);
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _identityCalculator ??= new IdentityCalculator();
            _damageCalculator ??= new DamageCalculator();
            _strategistRules ??= new StrategistRules();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Attacker Stats", EditorStyles.boldLabel);
            _attackerStats = DrawStatContainer(_attackerStats);

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Defender Stats", EditorStyles.boldLabel);
            _defenderStats = DrawStatContainer(_defenderStats);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Combat Parameters", EditorStyles.boldLabel);

            _penetrationPercent = EditorGUILayout.Slider("Penetration (%)", _penetrationPercent, PenetrationMin, PenetrationMax);
            _defenseBonusEffNormalized = EditorGUILayout.Slider("Defense BonusEff (0..1)", _defenseBonusEffNormalized, 0f, 1f);

            EditorGUILayout.Space(6);
            _defenderCurrentHp = EditorGUILayout.FloatField("Defender CurrentHP", _defenderCurrentHp);
            _defenderMaxHp = EditorGUILayout.FloatField("Defender MaxHP", _defenderMaxHp);

            EditorGUILayout.Space(6);
            _enableStrategistPenalty = EditorGUILayout.Toggle("Apply Strategist Unprotected Penalty", _enableStrategistPenalty);
            using (new EditorGUI.DisabledScope(!_enableStrategistPenalty))
            {
                _strategistPenaltyTimeSeconds = EditorGUILayout.Slider(
                    "Penalty Time (sec)",
                    _strategistPenaltyTimeSeconds,
                    0f,
                    2f);
            }

            _enableStrategistOverflowPreview = EditorGUILayout.Toggle("Preview Strategist HP Overflow", _enableStrategistOverflowPreview);
            using (new EditorGUI.DisabledScope(!_enableStrategistOverflowPreview))
            {
                _overflowDeltaSeconds = EditorGUILayout.Slider("Overflow Tick Delta (sec)", _overflowDeltaSeconds, 0f, 5f);
            }

            _enableThorns = EditorGUILayout.Toggle("Enable Thorns Preview", _enableThorns);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Simulate", GUILayout.Height(34)))
            {
                Simulate();
            }
        }

        private StatContainer DrawStatContainer(StatContainer container)
        {
            container.STR = DrawSlot("STR", container.STR);
            container.CON = DrawSlot("CON", container.CON);
            container.AGI = DrawSlot("AGI", container.AGI);
            container.DEF = DrawSlot("DEF", container.DEF);
            return container;
        }

        private StatSlot DrawSlot(string label, StatSlot slot)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            slot.Invested = EditorGUILayout.Slider("Invested (0..30)", slot.Invested, 0f, 30f);
            slot.Item = EditorGUILayout.Slider("Item (0..10)", slot.Item, 0f, 10f);
            EditorGUILayout.Space(3);
            return slot;
        }

        private void Simulate()
        {
            var attackerIdentityDebug = default(IdentityCalculator.IdentityDebug);
            var defenderIdentityDebug = default(IdentityCalculator.IdentityDebug);

            Identity attackerIdentity = _identityCalculator.ResolveIdentity(_attackerStats, out attackerIdentityDebug);
            Identity defenderIdentity = _identityCalculator.ResolveIdentity(_defenderStats, out defenderIdentityDebug);

            // NOTE:
            // - 공격력/방어율 매핑은 "현재 프로젝트에 구체 스펙이 없어" reference-formulae.md에 맞춘 가장 단순한 기준으로 잡는다.
            // - 공격력 = Attacker STR FinalTotal
            // - 방어율(CurrentDEF) = Defender DEF FinalTotal / 100
            float attackerAtkPower = StatMath.FinalTotal(StatKind.STR, _attackerStats);
            float defenderCurrentDefNormalized = StatMath.FinalTotal(StatKind.DEF, _defenderStats) / 100f;

            float baseDamage = _damageCalculator.PredictFinalDamage(
                attackerAtkPower,
                defenderCurrentDefNormalized,
                _defenseBonusEffNormalized,
                _penetrationPercent);

            float finalDamage = baseDamage;
            float strategistDamageMultiplier = 1f;
            if (_enableStrategistPenalty && defenderIdentity.Type == IdentityType.Strategist)
            {
                strategistDamageMultiplier = _strategistRules.GetIncomingDamageMultiplier(_strategistPenaltyTimeSeconds);
                finalDamage = baseDamage * strategistDamageMultiplier;
            }

            float finalDefenseEff = _damageCalculator.PredictFinalDefenseEfficiency(defenderCurrentDefNormalized, _defenseBonusEffNormalized);

            var sb = new StringBuilder(512);
            sb.AppendLine("[StatSimulator] === Result ===");
            sb.AppendLine($"Attacker Identity: {attackerIdentity} | MaxPure={attackerIdentityDebug.MaxPureTotal:0.##}, MinPure={attackerIdentityDebug.MinPureTotal:0.##}, PrimaryPure={attackerIdentityDebug.PrimaryPureTotal:0.##}");
            sb.AppendLine($"Defender Identity: {defenderIdentity} | MaxPure={defenderIdentityDebug.MaxPureTotal:0.##}, MinPure={defenderIdentityDebug.MinPureTotal:0.##}, PrimaryPure={defenderIdentityDebug.PrimaryPureTotal:0.##}");
            sb.AppendLine();
            sb.AppendLine("[StatSimulator] === Damage Calc ===");
            sb.AppendLine($"AttackPower(STR FinalTotal) = {attackerAtkPower:0.##}");
            sb.AppendLine($"CurrentDEF = {defenderCurrentDefNormalized:0.###} (DEF FinalTotal / 100)");
            sb.AppendLine($"Defense BonusEff = {_defenseBonusEffNormalized:0.###}");
            sb.AppendLine($"FinalDEF_Eff(후 HardCap 적용) = {finalDefenseEff:0.###} (-> DefenseRate%={finalDefenseEff * 100f:0.##})");
            sb.AppendLine($"Penetration = {_penetrationPercent:0.##}%");
            sb.AppendLine($"BaseDamage = {baseDamage:0.##}");
            if (_enableStrategistPenalty && defenderIdentity.Type == IdentityType.Strategist)
                sb.AppendLine($"Strategist Unprotected Penalty Multiplier = {strategistDamageMultiplier:0.###} (t={_strategistPenaltyTimeSeconds:0.##}s)");
            sb.AppendLine($"FinalDamage = {finalDamage:0.##}");

            if (_enableStrategistOverflowPreview && defenderIdentity.Type == IdentityType.Strategist)
            {
                float nextHp = _strategistRules.TickOverflow(_defenderCurrentHp, _defenderMaxHp, _overflowDeltaSeconds);
                sb.AppendLine();
                sb.AppendLine("[StatSimulator] === Strategist HP Overflow Preview ===");
                sb.AppendLine($"CurrentHP={_defenderCurrentHp:0.##}, NewMaxHP={_defenderMaxHp:0.##}");
                sb.AppendLine($"OverflowTickDelta={_overflowDeltaSeconds:0.###}s (fixed rate: NewMaxHP * 0.10 / sec)");
                sb.AppendLine($"NextHP={nextHp:0.##}");
            }

            if (_enableThorns)
            {
                float thornsDamage = _damageCalculator.PredictThornsReflectDamage(attackerAtkPower, _defenderMaxHp);
                sb.AppendLine();
                sb.AppendLine("[StatSimulator] === Thorns Preview ===");
                sb.AppendLine($"ThornsReflectDamage = {thornsDamage:0.##} (cap = {_defenderMaxHp * 0.07f:0.##})");
            }

            sb.AppendLine("[StatSimulator] =====================");
            Debug.Log(sb.ToString());
        }
    }
}

