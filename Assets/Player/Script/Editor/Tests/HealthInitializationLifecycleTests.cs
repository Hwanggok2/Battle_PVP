using System.Reflection;
using BattlePvp.Combat;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class HealthInitializationLifecycleTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private GameObject _owner;
        private NetworkIdentity _identity;
        private HealthSystem _health;
        private FieldInfo _clientState;
        private object _previousClientState;
        private bool _previousServerActive;

        [SetUp]
        public void SetUp()
        {
            _clientState = typeof(NetworkClient).GetField("connectState", PrivateStatic);
            Assert.That(_clientState, Is.Not.Null);
            _previousClientState = _clientState.GetValue(null);
            _previousServerActive = NetworkServer.active;
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "This fixture does not start or replace a live network session.");
            Assert.That(Application.isPlaying, Is.False, "These initialization guards are EditMode checks.");

            _owner = new GameObject("Health before Mirror initialization");
            _owner.SetActive(false);
            _health = _owner.AddComponent<HealthSystem>();
            _identity = _owner.GetComponent<NetworkIdentity>();
            SetField("_maxHp", 100f);
            SetField("_currentHp", 40f);
            SetField("_currentShield", 10f);
            // Explicitly reproduce the deserialized NetworkBehaviour before NetworkIdentity.Awake binds it.
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(_health, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_identity != null)
            {
                SetIdentityMode(false, false);
                // Destruction never invokes server cleanup for an object that this fixture did not spawn.
                Object.DestroyImmediate(_owner);
            }
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, _previousServerActive);
            if (_clientState != null) _clientState.SetValue(null, _previousClientState);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void UnboundIdentityRejectsHealthChangesWithoutReadingMirrorAuthority(bool serverActive, bool clientActive)
        {
            SetNetworkMode(serverActive, clientActive);
            SetIdentityMode(serverActive, clientActive);
            int hpEvents = 0, shieldEvents = 0, deaths = 0, revives = 0;
            _health.HpChanged += (_, __) => hpEvents++;
            _health.ShieldChanged += _ => shieldEvents++;
            _health.OnDied += () => deaths++;
            _health.OnRevived += () => revives++;
            DamageResult result = default;

            Assert.That(_health.netIdentity, Is.Null);
            Assert.DoesNotThrow(() =>
            {
                Assert.That(CanChangeHealth(), Is.False);
                _health.SetCurrentHp(0f);
                _health.Heal(20f);
                _health.RefillHealth();
                _health.Revive();
                _health.RequestRevive();
                _health.GrantDecayingShield(20f, 3f);
                _health.SetSkillInvulnerable(3f);
                _health.SetTauntDefense(3f, 0.5f, 2f, 0.1f);
                result = _health.ApplyDamage(new DamageRequest(100f, DamageSource.Poison, 0f, null, Vector3.zero));
            });

            Assert.That(result.Accepted, Is.False);
            Assert.That(_health.CurrentHp, Is.EqualTo(40f));
            Assert.That(_health.CurrentShield, Is.EqualTo(10f));
            Assert.That(_health.IsDead, Is.False);
            Assert.That(_health.DeathSequence, Is.Zero);
            Assert.That(GetField<double>("_skillInvulnerableUntil"), Is.Zero);
            Assert.That(GetField<double>("_tauntDefenseUntil"), Is.Zero);
            Assert.That(hpEvents + shieldEvents + deaths + revives, Is.Zero);
        }

        [TestCase(false, false, false, true)]
        [TestCase(true, false, true, true)]
        [TestCase(false, true, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, false, false)]
        public void BoundHealthUsesItsOwnServerRoleOrOfflineAuthority(
            bool serverActive, bool clientActive, bool ownsServerRole, bool canChange)
        {
            BindWithMirror();
            SetNetworkMode(serverActive, clientActive);
            SetIdentityMode(ownsServerRole, clientActive);
            int hpEvents = 0;
            _health.HpChanged += (_, __) => hpEvents++;

            Assert.DoesNotThrow(() => Assert.That(CanChangeHealth(), Is.EqualTo(canChange)));
            _health.SetCurrentHp(60f);
            _health.Heal(10f);
            _health.SetSkillInvulnerable(3f);
            Assert.That(_health.CurrentHp, Is.EqualTo(canChange ? 70f : 40f));
            Assert.That(hpEvents, Is.EqualTo(canChange ? 2 : 0));
            if (canChange) Assert.That(GetField<double>("_skillInvulnerableUntil"), Is.GreaterThan(0d));
            else
            {
                Assert.That(GetField<double>("_skillInvulnerableUntil"), Is.Zero);
                Assert.That(_health.ApplyDamage(new DamageRequest(100f, DamageSource.Poison, 0f, null, Vector3.zero)).Accepted, Is.False);
                _health.RefillHealth();
                _health.Revive();
                Assert.That(_health.CurrentHp, Is.EqualTo(40f));
                Assert.That(_health.CurrentShield, Is.EqualTo(10f));
                Assert.That(hpEvents, Is.Zero);
            }
        }

        [Test]
        public void RealMirrorBindingEnablesOfflineDeathAndExplicitReviveOnce()
        {
            int deaths = 0, revives = 0;
            _health.OnDied += () => deaths++;
            _health.OnRevived += () => revives++;
            _health.SetCurrentHp(0f);
            Assert.That(_health.CurrentHp, Is.EqualTo(40f));

            BindWithMirror();
            _health.SetCurrentHp(0f);
            _health.SetCurrentHp(0f);
            _health.Heal(100f);
            _health.RefillHealth();
            Assert.That(_health.CurrentHp, Is.Zero);
            Assert.That(_health.IsDead, Is.True);
            Assert.That(_health.DeathSequence, Is.EqualTo(1u));
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(_health.ReviveAllowedAt, Is.GreaterThan(0d));

            _health.RequestRevive(0.5f);
            Assert.That(_health.CurrentHp, Is.EqualTo(50f));
            Assert.That(_health.IsDead, Is.False);
            Assert.That(_health.ReviveAllowedAt, Is.Zero);
            Assert.That(revives, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InspectorValidationOnlyQueuesWorkAndEditModeUpdateDoesNotPublishIt(bool bound)
        {
            if (bound) BindWithMirror();
            SetField("_currentHp", 0f);
            SetField("_pendingEditorValidation", 0);
            int hpEvents = 0, shieldEvents = 0, overflowEvents = 0, deaths = 0;
            _health.HpChanged += (_, __) => hpEvents++;
            _health.ShieldChanged += _ => shieldEvents++;
            _health.OverflowChanged += (_, __) => overflowEvents++;
            _health.OnDied += () => deaths++;

            for (int i = 0; i < 3; i++) Invoke("OnValidate");
            Assert.That(GetField<int>("_pendingEditorValidation"), Is.EqualTo(1), "Repeated validation coalesces into one pending runtime update.");
            Assert.That(_health.IsDead, Is.False);
            Assert.That(_health.DeathSequence, Is.Zero);
            Assert.That(hpEvents + shieldEvents + overflowEvents + deaths, Is.Zero);

            Invoke("Update");
            Assert.That(GetField<int>("_pendingEditorValidation"), Is.EqualTo(1), "EditMode cannot consume the pending runtime operation.");
            Assert.That(_health.IsDead, Is.False);
            Assert.That(hpEvents + shieldEvents + overflowEvents + deaths, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnapprovedSpawnCannotTakeDamageEvenIfSceneReady(bool sceneReady)
        {
            var stats = _owner.AddComponent<BattlePvp.Stats.StatManager>();
            SetField("_statManager", stats);
            _health.isInvincible = false;
            BindWithMirror();
            SetNetworkMode(true, false);
            SetIdentityMode(true, false);
            var connection = new NetworkConnectionToClient(77) { isReady = sceneReady };
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.connectionToClient)).SetValue(_identity, connection);
            var result = _health.ApplyDamage(new DamageRequest(50f, DamageSource.Poison, 0f, null, Vector3.zero));
            Assert.That(result.Accepted, Is.False);
            Assert.That(_health.CurrentHp, Is.EqualTo(40f));
            Assert.That(GetField<float>("_currentShield"), Is.EqualTo(10f));
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.connectionToClient)).SetValue(_identity, null);
        }

        private void BindWithMirror()
        {
            MethodInfo bind = typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", PrivateInstance);
            Assert.That(bind, Is.Not.Null);
            bind.Invoke(_identity, null);
            Assert.That(_health.netIdentity, Is.SameAs(_identity));
        }

        private void SetNetworkMode(bool server, bool client)
        {
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, server);
            _clientState.SetValue(null, client ? ConnectState.Connected : ConnectState.None);
        }

        private void SetIdentityMode(bool server, bool client)
        {
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(_identity, server);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isClient)).SetValue(_identity, client);
        }

        private bool CanChangeHealth() => (bool)typeof(HealthSystem).GetProperty("CanChangeHealth", PrivateInstance).GetValue(_health);
        private T GetField<T>(string name) => (T)typeof(HealthSystem).GetField(name, PrivateInstance).GetValue(_health);
        private void SetField(string name, object value) => typeof(HealthSystem).GetField(name, PrivateInstance).SetValue(_health, value);
        private void Invoke(string name) => typeof(HealthSystem).GetMethod(name, PrivateInstance).Invoke(_health, null);
    }
}
