using System;
using System.Reflection;
using System.Threading.Tasks;
using BattlePvp.Stats;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class StatValidationLifecycleTests
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo ConfigField = typeof(StatBalanceCalculator).GetField("_config", StaticPrivate);
        private static readonly FieldInfo BalancePending = typeof(StatBalanceConfig).GetField("_pendingEditorValidation", StaticPrivate);
        private GameObject _object;
        private StatManager _manager;
        private StatBalanceConfig _config;
        private object _previousConfig;
        private int _previousPending;
        private bool _subscribedManager;

        [SetUp]
        public void SetUp()
        {
            _previousConfig = ConfigField.GetValue(null);
            _previousPending = (int)BalancePending.GetValue(null);
            _config = ScriptableObject.CreateInstance<StatBalanceConfig>();
            ConfigField.SetValue(null, _config);
            BalancePending.SetValue(null, 0);
            _object = new GameObject("Stat validation lifecycle test");
            _object.SetActive(false);
            _object.AddComponent<NetworkIdentity>();
            _manager = _object.AddComponent<StatManager>();
            BindIdentity();
            SetManagerField("_stats", Preset(8f, 8f, 7f, 7f));
            _manager.RecalculateIdentity();
            _manager.GetDerivedStats();
            SetManagerField("_pendingEditorValidation", 0);
        }

        [TearDown]
        public void TearDown()
        {
            if (_subscribedManager) InvokeManager("OnDisable");
            Object.DestroyImmediate(_object);
            ConfigField.SetValue(null, _previousConfig);
            Object.DestroyImmediate(_config);
            BalancePending.SetValue(null, _previousPending);
        }

        [Test]
        public void ManagerValidationOnlyMarksPendingWithoutLoadingBalanceOrPublishingEvents()
        {
            int calculations = CalculationCount;
            Identity before = _manager.CurrentIdentity;
            int identities = 0, derived = 0;
            _manager.IdentityChanged += _ => identities++;
            _manager.DerivedStatsChanged += () => derived++;
            SetManagerField("_stats", Preset(30f, 0f, 0f, 0f));
            ConfigField.SetValue(null, null);

            InvokeManager("OnValidate");

            Assert.That(ConfigField.GetValue(null), Is.Null);
            Assert.That(CalculationCount, Is.EqualTo(calculations));
            Assert.That(_manager.CurrentIdentity.Type, Is.EqualTo(before.Type));
            Assert.That(_manager.CurrentIdentity.PrimaryStat, Is.EqualTo(before.PrimaryStat));
            Assert.That(identities, Is.Zero);
            Assert.That(derived, Is.Zero);
            Assert.That(ManagerPending, Is.EqualTo(1));
        }

        [Test]
        public void PreparedMainThreadDrainCoalescesEditsAndPublishesTheNewSnapshotOnce()
        {
            SetManagerField("_stats", Preset(30f, 0f, 0f, 0f));
            for (int i = 0; i < 10; i++) InvokeManager("OnValidate");
            int identities = 0, derived = 0;
            float observedAttack = 0f;
            _manager.IdentityChanged += _ =>
            {
                identities++;
                observedAttack = _manager.GetDerivedStats().AttackPower;
            };
            _manager.DerivedStatsChanged += () => derived++;

            // Invoke the prepared stage directly; the Update play-mode gate is tested separately.
            Assert.That(InvokeManager("ApplyPendingEditorValidation"), Is.True);
            Assert.That(InvokeManager("ApplyPendingEditorValidation"), Is.False);

            Assert.That(_manager.CurrentIdentity.Type, Is.EqualTo(IdentityType.Monostat));
            Assert.That(_manager.CurrentIdentity.PrimaryStat, Is.EqualTo(StatKind.STR));
            Assert.That(identities, Is.EqualTo(1));
            Assert.That(derived, Is.EqualTo(1));
            Assert.That(observedAttack, Is.EqualTo(_manager.GetDerivedStats().AttackPower));
        }

        [Test]
        public void EditModeUpdateRetainsBothPendingFlagsWithoutRuntimeNotifications()
        {
            int derived = 0;
            _manager.DerivedStatsChanged += () => derived++;
            InvokeManager("OnValidate");
            ValidateConfig();

            InvokeManager("Update");

            Assert.That(derived, Is.Zero);
            Assert.That(ManagerPending, Is.EqualTo(1));
            Assert.That(BalancePending.GetValue(null), Is.EqualTo(1));
        }

        [Test]
        public void ConfigValidationOnWorkerOnlyChangesRevisionAndPreviewCacheRemainsFresh()
        {
            int notifications = 0;
            Action listener = () => notifications++;
            StatBalanceConfig.BalanceChanged += listener;
            try
            {
                float before = _manager.GetDerivedStats().MaxHp;
                int revision = _config.Revision;
                typeof(StatBalanceConfig).GetField("_maxHpPerCon", InstancePrivate).SetValue(_config, 30f);

                Task validation = Task.Run(ValidateConfig);
                Assert.That(validation.Wait(TimeSpan.FromSeconds(5)), Is.True, "Validation did not complete within the test deadline.");
                validation.GetAwaiter().GetResult();

                Assert.That(_config.Revision, Is.EqualTo(unchecked(revision + 1)));
                Assert.That(notifications, Is.Zero);
                Assert.That(_manager.GetDerivedStats().MaxHp, Is.GreaterThan(before));
                Assert.That(notifications, Is.Zero);
            }
            finally { StatBalanceConfig.BalanceChanged -= listener; }
        }

        [Test]
        public void MainThreadPublicationCoalescesAssetAndManagerValidation()
        {
            SubscribeManager();
            int notifications = 0;
            _manager.DerivedStatsChanged += () => notifications++;
            for (int i = 0; i < 10; i++)
            {
                InvokeManager("OnValidate");
                ValidateConfig();
            }

            PublishConfigValidation();
            InvokeManager("ApplyPendingEditorValidation");
            PublishConfigValidation();

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(ManagerPending, Is.Zero);
        }

        [Test]
        public void ExplicitNotifyChangedIsImmediateAndConsumesEarlierAssetValidation()
        {
            SubscribeManager();
            int notifications = 0;
            _manager.DerivedStatsChanged += () => notifications++;
            ValidateConfig();

            _config.NotifyChanged();

            Assert.That(notifications, Is.EqualTo(1));
            PublishConfigValidation();
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void ReentrantAssetValidationWaitsForTheNextMainThreadPublication()
        {
            int notifications = 0;
            Action listener = () =>
            {
                notifications++;
                if (notifications == 1) ValidateConfig();
            };
            StatBalanceConfig.BalanceChanged += listener;
            try
            {
                ValidateConfig();
                PublishConfigValidation();
                Assert.That(notifications, Is.EqualTo(1));
                PublishConfigValidation();
                Assert.That(notifications, Is.EqualTo(2));
                PublishConfigValidation();
                Assert.That(notifications, Is.EqualTo(2));
            }
            finally { StatBalanceConfig.BalanceChanged -= listener; }
        }

        [Test]
        public void UnboundBalanceSubscriberDefersUntilIdentityBindingIsReady()
        {
            SubscribeManager();
            typeof(NetworkBehaviour).GetProperty("netIdentity").SetValue(_manager, null);
            int notifications = 0;
            _manager.DerivedStatsChanged += () => notifications++;
            int calculations = CalculationCount;

            _config.NotifyChanged();

            Assert.That(notifications, Is.Zero);
            Assert.That(CalculationCount, Is.EqualTo(calculations));
            Assert.That(ManagerPending, Is.EqualTo(1));
            BindIdentity();
            InvokeManager("ApplyPendingEditorValidation");
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void OfflineDisplayWithoutNetworkIdentityStillReceivesBalanceChanges()
        {
            SubscribeManager();
            Object.DestroyImmediate(_object.GetComponent<NetworkIdentity>());
            typeof(NetworkBehaviour).GetProperty("netIdentity").SetValue(_manager, null);
            int notifications = 0;
            _manager.DerivedStatsChanged += () => notifications++;

            _config.NotifyChanged();

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(ManagerPending, Is.Zero);
            Assert.That(typeof(StatManager).GetProperty("CanPublishValidatedStats", InstancePrivate).GetValue(_manager), Is.True);

            Assert.DoesNotThrow(() => _manager.ApplyLocalSceneStats(Preset(30f, 0f, 0f, 0f)));
            Assert.That(_object.transform.localScale, Is.EqualTo(Vector3.one * 1.2f));
            Assert.DoesNotThrow(() => _manager.ApplyStats(Preset(8f, 8f, 7f, 7f)));
            Assert.That(_object.transform.localScale, Is.EqualTo(Vector3.one));
        }

        private int CalculationCount => (int)typeof(StatManager).GetField("_derivedCalculationCount", InstancePrivate).GetValue(_manager);
        private int ManagerPending => (int)typeof(StatManager).GetField("_pendingEditorValidation", InstancePrivate).GetValue(_manager);
        private void SetManagerField(string name, object value) => typeof(StatManager).GetField(name, InstancePrivate).SetValue(_manager, value);
        private object InvokeManager(string name) => typeof(StatManager).GetMethod(name, InstancePrivate).Invoke(_manager, null);
        private void ValidateConfig() => typeof(StatBalanceConfig).GetMethod("OnValidate", InstancePrivate).Invoke(_config, null);
        private static void PublishConfigValidation() => typeof(StatBalanceConfig).GetMethod("PublishPendingEditorValidation", StaticPrivate).Invoke(null, null);
        private void BindIdentity() => typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", InstancePrivate).Invoke(_object.GetComponent<NetworkIdentity>(), null);

        private void SubscribeManager()
        {
            InvokeManager("OnEnable");
            _subscribedManager = true;
        }

        private static StatContainer Preset(float str, float con, float agi, float def) => new StatContainer
        {
            STR = new StatSlot { Invested = str }, CON = new StatSlot { Invested = con },
            AGI = new StatSlot { Invested = agi }, DEF = new StatSlot { Invested = def }
        };
    }
}
