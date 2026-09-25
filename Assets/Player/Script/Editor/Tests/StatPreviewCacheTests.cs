using System.Reflection;
using BattlePvp.CameraLogic;
using BattlePvp.Managers;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class StatPreviewCacheTests
    {
        private GameObject _object;
        private GameObject _cameraObject;
        private StatBalanceConfig _config;
        private object _previousConfig;
        private static readonly FieldInfo ConfigField = typeof(StatBalanceCalculator).GetField("_config", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            _previousConfig = ConfigField.GetValue(null);
            _config = ScriptableObject.CreateInstance<StatBalanceConfig>();
            ConfigField.SetValue(null, _config);
            _object = new GameObject("Stat preview cache test");
            _object.SetActive(false);
            _object.AddComponent<NetworkIdentity>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_object);
            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
            ConfigField.SetValue(null, _previousConfig);
            Object.DestroyImmediate(_config);
        }

        [TestCase(30f, 0f, 0f, 0f)]
        [TestCase(0f, 30f, 0f, 0f)]
        [TestCase(0f, 0f, 30f, 0f)]
        [TestCase(0f, 0f, 0f, 30f)]
        [TestCase(8f, 8f, 7f, 7f)]
        [TestCase(15f, 0f, 15f, 0f)]
        public void PreviewMatchesLiveDerivedValues(float str, float con, float agi, float def)
        {
            StatManager manager = _object.AddComponent<StatManager>();
            StatContainer stats = Preset(str, con, agi, def);
            manager.ApplyLocalSceneStats(stats);
            AssertPreview(manager, stats, manager.GetDerivedStats());
        }

        [Test]
        public void DefMonostatIncludesDefHealthAndConfiguredMovementMultiplier()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            StatContainer stats = Preset(0f, 0f, 0f, 30f);
            manager.ApplyLocalSceneStats(stats);
            DerivedCombatStats derived = manager.GetDerivedStats();
            Assert.That(derived.MaxHp, Is.EqualTo(265f).Within(0.0001f));
            Assert.That(derived.MoveSpeed, Is.EqualTo(2.56f).Within(0.0001f));
            AssertPreview(manager, stats, derived);
        }

        [Test]
        public void RepeatedReadsReuseCacheAndConfigChangeInvalidatesIt()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            StatContainer stats = Preset(0f, 0f, 0f, 30f);
            manager.ApplyLocalSceneStats(stats);
            int before = CalculationCount(manager);
            for (int i = 0; i < 100; i++) manager.GetDerivedStats();
            Assert.That(CalculationCount(manager), Is.EqualTo(before));

            SetField(_config, "_maxHpPerDef", 4f);
            SetField(_config, "_monostatDefMoveSpeedMultiplier", 0.5f);
            _config.NotifyChanged();
            DerivedCombatStats changed = manager.GetDerivedStats();
            Assert.That(changed.MaxHp, Is.EqualTo(335f).Within(0.0001f));
            Assert.That(changed.MoveSpeed, Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(CalculationCount(manager), Is.EqualTo(before + 1));
            AssertPreview(manager, stats, changed);
        }

        [Test]
        public void IdentityListenersReadTheNewDerivedSnapshot()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            manager.ApplyLocalSceneStats(Preset(8f, 8f, 7f, 7f));
            StatContainer next = Preset(30f, 0f, 0f, 0f);
            float observedAttack = 0f;
            manager.IdentityChanged += _ => observedAttack = manager.GetDerivedStats().AttackPower;
            manager.ApplyLocalSceneStats(next);
            Assert.That(observedAttack, Is.EqualTo(StatBalanceCalculator.Calculate(next, manager.CurrentIdentity).AttackPower));
        }

        [Test]
        public void ItemOnlyChangesInvalidateValuesWithoutChangingIdentity()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            StatContainer stats = Preset(8f, 8f, 7f, 7f);
            manager.ApplyLocalSceneStats(stats);
            Identity identity = manager.CurrentIdentity;
            float attack = manager.GetDerivedStats().AttackPower;
            stats.STR.Item = 10f;
            manager.ApplyLocalSceneStats(stats);
            Assert.That(manager.CurrentIdentity.Type, Is.EqualTo(identity.Type));
            Assert.That(manager.CurrentIdentity.PrimaryStat, Is.EqualTo(identity.PrimaryStat));
            Assert.That(manager.GetDerivedStats().AttackPower, Is.GreaterThan(attack));
        }

        [Test]
        public void NonLocalScalingDoesNotWriteCameraOffset()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            _cameraObject = new GameObject("Stat camera ownership test");
            _cameraObject.SetActive(false);
            FollowCamera camera = _cameraObject.AddComponent<FollowCamera>();
            Vector3 initialOffset = new Vector3(3f, 4f, 5f);
            camera.Offset = initialOffset;
            SetField(manager, "_followCamera", camera);
            SetField(manager, "_cameraInitialized", true);
            manager.ApplyLocalSceneStats(Preset(30f, 0f, 0f, 0f));
            Assert.That(_object.transform.localScale, Is.EqualTo(Vector3.one * 1.2f));
            Assert.That(camera.Offset, Is.EqualTo(initialOffset));
        }

        [Test]
        public void OfflinePreviewOnlyChangesTheCameraFollowingItsCharacter()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            _cameraObject = new GameObject("Stat offline camera test");
            _cameraObject.SetActive(false);
            FollowCamera camera = _cameraObject.AddComponent<FollowCamera>();
            camera.SetTarget(_object.transform);
            SetField(manager, "_followCamera", camera);
            manager.ApplyLocalSceneStats(Preset(30f, 0f, 0f, 0f));
            Assert.That(camera.Offset, Is.EqualTo(new Vector3(0.35f, 0.4f, -1f)));
            manager.ApplyLocalSceneStats(Preset(8f, 8f, 7f, 7f));
            Assert.That(camera.Offset, Is.EqualTo(new Vector3(0.3f, 0.2f, -1f)));
        }

        [Test]
        public void RepeatedPulseRestartsPreserveNonUnitScale()
        {
            Vector3 original = new Vector3(0.6f, 1.3f, 2f);
            var pulse = new StatPreviewPulse(original);
            for (int i = 0; i < 100; i++)
            {
                pulse.Restart();
                Assert.That(pulse.Scale, Is.EqualTo(original * 1.2f));
                pulse.Advance(0.03f);
            }
            pulse.Advance(0.2f);
            Assert.That(pulse.IsActive, Is.False);
            Assert.That(pulse.Scale, Is.EqualTo(original));
            pulse.Restart();
            pulse.Reset();
            Assert.That(pulse.Scale, Is.EqualTo(original));
        }

        [Test]
        public void RejectedPresetDoesNotChangeLiveOrSavedStats()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            GlobalDataManager profile = _object.AddComponent<GlobalDataManager>();
            StatContainer initial = Preset(8f, 8f, 7f, 7f);
            manager.ApplyLocalSceneStats(initial);
            profile.SavedStats = initial;
            StatContainer invalid = initial;
            invalid.STR.Invested = float.NaN;
            int completions = 0;
            StatPresetApplication.Apply(manager, profile, null, invalid, false, (succeeded, error) =>
            {
                completions++;
                Assert.That(succeeded, Is.False);
                Assert.That(error, Is.Not.Null.And.Not.Empty);
            });
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(manager.GetStatsCopy().STR.Invested, Is.EqualTo(8f));
            Assert.That(profile.SavedStats.STR.Invested, Is.EqualTo(8f));
        }

        [Test]
        public void MissingPersistenceReportsFailureAfterLocalApplication()
        {
            StatManager manager = _object.AddComponent<StatManager>();
            GlobalDataManager profile = _object.AddComponent<GlobalDataManager>();
            StatContainer stats = Preset(8f, 8f, 7f, 7f);
            int completions = 0;
            StatPresetApplication.Apply(manager, profile, null, stats, false, (succeeded, error) =>
            {
                completions++;
                Assert.That(succeeded, Is.False);
                Assert.That(error, Does.Contain("적용되었지만"));
            });
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(manager.GetStatsCopy().STR.Invested, Is.EqualTo(8f));
            Assert.That(profile.SavedStats.STR.Invested, Is.EqualTo(8f));
        }

        private static void AssertPreview(StatManager manager, StatContainer stats, DerivedCombatStats expected)
        {
            manager.CalculatePreviewStats(stats, out float attack, out float defense, out float hp,
                out float penetration, out float regen, out float move, out float speed);
            Assert.That(attack, Is.EqualTo(expected.AttackPower));
            Assert.That(defense, Is.EqualTo(expected.DefenseEfficiencyPercent));
            Assert.That(hp, Is.EqualTo(expected.MaxHp));
            Assert.That(penetration, Is.EqualTo(expected.PenetrationPercent));
            Assert.That(regen, Is.EqualTo(expected.RegenPerSecond));
            Assert.That(move, Is.EqualTo(expected.MoveSpeed));
            Assert.That(speed, Is.EqualTo(expected.AttackSpeed));
        }

        private static StatContainer Preset(float str, float con, float agi, float def) => new StatContainer
        {
            STR = new StatSlot { Invested = str }, CON = new StatSlot { Invested = con },
            AGI = new StatSlot { Invested = agi }, DEF = new StatSlot { Invested = def }
        };

        private static int CalculationCount(StatManager manager) =>
            (int)typeof(StatManager).GetField("_derivedCalculationCount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
