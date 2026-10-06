using System.Reflection;
using BattlePvp.Characters;
using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CharacterStatTests
    {
        private GameObject _player;
        private StatManager _stats;
        private PlayerAppearance _appearance;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            EditorTestLifecycle.BindNetwork(_player);
            _stats = _player.GetComponent<StatManager>();
            _appearance = _player.GetComponent<PlayerAppearance>();
            EditorTestLifecycle.Invoke(_appearance, "Awake");
            var preset = new StatContainer();
            preset.STR.Invested = 8; preset.CON.Invested = 8; preset.AGI.Invested = 7; preset.DEF.Invested = 7;
            _stats.ApplyLocalSceneStats(preset);
        }
        [TearDown] public void Cleanup()
        {
            EditorTestLifecycle.Invoke(_appearance, "OnDestroy");
            Object.DestroyImmediate(_player);
        }
        private void Select(string id) => typeof(PlayerAppearance).GetMethod("SetSelected", Private).Invoke(_appearance, new object[] { id });

        [TestCase("default", 1f, 1f, 1f, 1f, 1f)]
        [TestCase("brute", 1.2f, .9f, 1.15f, .9f, 1.1f)]
        [TestCase("megumi", .9f, .9f, .95f, 1.08f, 1.08f)]
        [TestCase("security-officer", .88f, .9f, .95f, 1.1f, 1.1f)]
        [TestCase("casual-1", .9f, .9f, .95f, 1.08f, 1.08f)]
        [TestCase("picochan", .85f, .85f, .9f, 1.12f, 1.12f)]
        public void SwitchingCharacterChangesCombatAndPreviewWithoutChangingAllocation(string id, float hp, float defense, float attack, float move, float speed)
        {
            var originalStats = _stats.GetStatsCopy();
            var originalIdentity = _stats.CurrentIdentity;
            var baseline = _stats.GetDerivedStats();
            int updates = 0; _stats.DerivedStatsChanged += () => updates++;
            Select(id);
            var actual = _stats.GetDerivedStats();
            Assert.That(updates, Is.EqualTo(1));
            Assert.That(actual.MaxHp, Is.EqualTo(baseline.MaxHp * hp).Within(.001));
            Assert.That(actual.DefenseEfficiencyPercent, Is.EqualTo(baseline.DefenseEfficiencyPercent * defense).Within(.001));
            Assert.That(actual.AttackPower, Is.EqualTo(baseline.AttackPower * attack).Within(.001));
            Assert.That(actual.MoveSpeed, Is.EqualTo(baseline.MoveSpeed * move).Within(.001));
            Assert.That(actual.AttackSpeed, Is.EqualTo(baseline.AttackSpeed * speed).Within(.001));
            Assert.That(_stats.GetStatsCopy(), Is.EqualTo(originalStats));
            Assert.That(_stats.CurrentIdentity, Is.EqualTo(originalIdentity));
            _stats.CalculatePreviewStats(originalStats, out float atk, out float def, out float maxHp, out _, out _, out float moveSpd, out float atkSpd);
            Assert.That(new[] { atk, def, maxHp, moveSpd, atkSpd }, Is.EqualTo(new[] { actual.AttackPower, actual.DefenseEfficiencyPercent, actual.MaxHp, actual.MoveSpeed, actual.AttackSpeed }));
            Select("default");
            Assert.That(_stats.GetDerivedStats(), Is.EqualTo(baseline));
        }

        [Test] public void HpRatioIsPreservedWhenSwitchingLargeAndSmallCharacters()
        {
            var health = _player.GetComponent<HealthSystem>();
            EditorTestLifecycle.Invoke(health, "Awake");
            health.RefreshFromStats(true); health.SetCurrentHp(health.MaxHp * .6f);
            _stats.DerivedStatsChanged += () => health.RefreshFromStats(true);
            foreach (string id in new[] { "brute", "picochan", "default", "brute" })
            {
                Select(id);
                Assert.That(health.CurrentHp / health.MaxHp, Is.EqualTo(.6f).Within(.0001f), id);
                Assert.That(health.MaxHp, Is.EqualTo(_stats.GetDerivedStats().MaxHp).Within(.001f));
            }
        }

        [Test] public void DefaultAndInvalidModifiersDoNotCorruptStatsAndDefenseRetainsCap()
        {
            var basis = _stats.GetDerivedStats();
            Assert.That(default(CharacterStatModifiers).Apply(basis), Is.EqualTo(basis));
            var invalid = new CharacterStatModifiers(float.NaN, -1, float.PositiveInfinity, 0, float.NegativeInfinity);
            Assert.That(invalid.Apply(basis), Is.EqualTo(basis));
            var preset = new StatContainer(); preset.DEF.Invested = 30;
            var defense = StatBalanceCalculator.Calculate(preset, new Identity(IdentityType.Monostat, StatKind.DEF));
            var large = new CharacterStatModifiers(1.2f, 1.5f, 1.1f, .9f, .9f).Apply(defense);
            Assert.That(large.DefenseEfficiencyPercent, Is.LessThanOrEqualTo(StatBalanceCalculator.Config.DefenseEfficiencyHardCap * 100f));
            var damage = new DamageCalculator();
            Assert.That(damage.PredictFinalDamage(100, large.DefenseEfficiencyPercent / 100f, 0, 0), Is.GreaterThan(0));
        }
    }
}
