using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class BowProjectileCollisionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly Vector3 _origin = new Vector3(3100f, 3200f, 3300f);
        private BowArrowProjectile _arrow;
        private NetworkIdentity _owner;
        private FieldInfo _connectionState;
        private object _previousConnectionState;

        [SetUp]
        public void SetUp()
        {
            // Avoid local hit feedback; no server, transport, authentication or SDK is started.
            _connectionState = typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic);
            _previousConnectionState = _connectionState.GetValue(null);
            _connectionState.SetValue(null, Enum.Parse(_connectionState.FieldType, "Connected"));
            GameObject owner = NewObject("Arrow owner", _origin, false);
            _owner = owner.AddComponent<NetworkIdentity>();
            StatManager stats = owner.AddComponent<StatManager>();
            owner.AddComponent<PlayerCombat>();
            AttackProcessor processor = owner.AddComponent<AttackProcessor>();
            Set(processor, "_attackerStats", stats);
            Set(processor, "_damageCalculator", new DamageCalculator());
            Set(processor, "_currentAtk", 20f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ArrowProjectile Variant.prefab");
            Assert.That(prefab, Is.Not.Null);
            GameObject arrow = Object.Instantiate(prefab, _origin, Quaternion.identity);
            _objects.Add(arrow);
            _arrow = arrow.GetComponent<BowArrowProjectile>();
            Assert.That(arrow.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(arrow.GetComponent<Rigidbody>().isKinematic, Is.True);
            EditorTestLifecycle.BindNetwork(owner);
            EditorTestLifecycle.BindNetwork(arrow);
            _arrow.Initialize(0, Vector3.forward, 28f, 4f, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            _connectionState.SetValue(null, _previousConnectionState);
        }

        [TestCase(0.016f)]
        [TestCase(0.5f)]
        public void ThinWallStopsArrowBeforeTheTargetAtNormalAndLowFrameRates(float deltaTime)
        {
            float travel = 28f * deltaTime;
            Box("Thin wall", _origin + Vector3.forward * (0.36f + travel * 0.15f), new Vector3(2f, 2f, 0.01f));
            BowProjectileRecordingReceiver target = Target(_origin + Vector3.forward * (0.6f + travel * 0.5f));
            Physics.SyncTransforms();
            Advance(Vector3.forward * travel);
            Assert.That(HasHit(), Is.True);
            Assert.That(target.DamageCalls, Is.Zero);
            Assert.That(_arrow.transform.position.z, Is.LessThan(target.transform.position.z));
        }

        [Test]
        public void OfflinePracticeCannotEnableLocalDamageWhileConnectedToAServer()
        {
            _arrow.InitializeOffline(_owner.GetComponent<PlayerCombat>(), Vector3.forward, 28, 4, 1);
            Assert.That(typeof(BowArrowProjectile).GetField("_offlineShot", PrivateInstance).GetValue(_arrow), Is.False);
        }

        [Test]
        public void ExposedTargetReceivesDamageOnceDespiteMultipleCollidersAndLaterCallbacks()
        {
            BowProjectileRecordingReceiver target = Target(_origin + Vector3.forward * 2f);
            target.gameObject.AddComponent<BoxCollider>().isTrigger = true;
            Physics.SyncTransforms();
            Advance(Vector3.forward * 10f);
            Advance(Vector3.zero);
            Advance(Vector3.forward * 10f);
            Assert.That(target.DamageCalls, Is.EqualTo(1));
            Assert.That(target.TotalDamage, Is.GreaterThan(0f));
            Assert.That(HasHit(), Is.True);
        }

        [Test]
        public void OwnerAndDecorativeTriggersDoNotBlockAnExposedTarget()
        {
            // A bare live owner hierarchy keeps this collision test independent of combat Awake side effects.
            _owner = NewObject("Owner hierarchy", _origin).AddComponent<NetworkIdentity>();
            GameObject ownerShape = NewObject("Owner collision", _origin + Vector3.forward * 0.7f);
            ownerShape.transform.SetParent(_owner.transform, true);
            ownerShape.AddComponent<BoxCollider>();
            BoxCollider decoration = Box("Decoration", _origin + Vector3.forward, Vector3.one * 0.5f);
            decoration.isTrigger = true;
            BowProjectileRecordingReceiver target = Target(_origin + Vector3.forward * 3f);
            Physics.SyncTransforms();
            Advance(Vector3.forward * 8f);
            Assert.That(HasHit(), Is.True);
            Assert.That(_arrow.transform.position.z, Is.GreaterThan(_origin.z + 2f), "The first accepted collision must be the target, after the owner and decoration.");
            Assert.That(_arrow.transform.position.z, Is.LessThan(target.transform.position.z));
        }

        [Test]
        public void PlayerControllerIsSkippedButItsAuthoredBodyTriggerIsEligible()
        {
            GameObject player = NewObject("Player body fixture", _origin + Vector3.forward * 3f, false);
            player.AddComponent<NetworkIdentity>();
            player.AddComponent<StatManager>();
            player.AddComponent<HealthSystem>();
            BoxCollider controller = player.AddComponent<BoxCollider>();
            controller.size = Vector3.one * 2f;
            GameObject body = NewObject("Player body region", player.transform.position);
            body.transform.SetParent(player.transform, true);
            BoxCollider region = body.AddComponent<BoxCollider>();
            region.size = Vector3.one * 0.5f;
            region.isTrigger = true;
            body.AddComponent<HitBodyPart>();
            player.SetActive(true);
            Physics.SyncTransforms();
            object[] args = { Vector3.forward * 10f, _owner.transform, null };
            bool found = (bool)typeof(BowArrowProjectile).GetMethod("TryFindFirstImpact", PrivateInstance).Invoke(_arrow, args);
            Assert.That(found, Is.True);
            Assert.That(args[2].GetType().GetField("Collider").GetValue(args[2]), Is.SameAs(region));
        }

        [Test]
        public void InitialWallOverlapWinsOverAnOverlappingDamageTrigger()
        {
            Box("Starting inside wall", _origin, Vector3.one);
            BowProjectileRecordingReceiver target = Target(_origin);
            Physics.SyncTransforms();
            Advance(Vector3.forward * 10f);
            Assert.That(HasHit(), Is.True);
            Assert.That(target.DamageCalls, Is.Zero);
            Assert.That(_arrow.transform.position, Is.EqualTo(_origin));
        }

        [Test]
        public void DisabledAndInactiveObstaclesDoNotStopTheArrow()
        {
            Box("Disabled wall", _origin + Vector3.forward, Vector3.one * 0.5f).enabled = false;
            Box("Inactive wall", _origin + Vector3.forward * 2f, Vector3.one * 0.5f).gameObject.SetActive(false);
            BowProjectileRecordingReceiver target = Target(_origin + Vector3.forward * 4f);
            Physics.SyncTransforms();
            Advance(Vector3.forward * 8f);
            Assert.That(target.DamageCalls, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullQueryBufferStillFindsTheBlockingWall(bool initialOverlap)
        {
            for (int i = 0; i < 40; i++)
            {
                BoxCollider decoration = Box("Crowded trigger " + i,
                    _origin + Vector3.forward * (initialOverlap ? 0f : 0.5f + i * 0.05f), Vector3.one * 0.05f);
                decoration.isTrigger = true;
            }
            Box("Wall after triggers", _origin + Vector3.forward * (initialOverlap ? 0f : 3f), Vector3.one * 0.3f);
            BowProjectileRecordingReceiver target = Target(_origin + Vector3.forward * 5f);
            Physics.SyncTransforms();
            Advance(Vector3.forward * 10f);
            Assert.That(HasHit(), Is.True);
            Assert.That(target.DamageCalls, Is.Zero);
        }

        [Test]
        public void DisabledProjectileCannotMoveOrApplyDamage()
        {
            BowProjectileRecordingReceiver target = Target(_origin + Vector3.forward * 2f);
            _arrow.enabled = false;
            Physics.SyncTransforms();
            Advance(Vector3.forward * 10f);
            Assert.That(target.DamageCalls, Is.Zero);
            Assert.That(_arrow.transform.position, Is.EqualTo(_origin));
        }

        private BowProjectileRecordingReceiver Target(Vector3 position)
        {
            GameObject target = NewObject("Damage target", position, false);
            target.AddComponent<NetworkIdentity>();
            target.AddComponent<StatManager>();
            BowProjectileRecordingReceiver receiver = target.AddComponent<BowProjectileRecordingReceiver>();
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.5f;
            collider.isTrigger = true;
            target.SetActive(true);
            return receiver;
        }

        private BoxCollider Box(string name, Vector3 position, Vector3 size)
        {
            BoxCollider collider = NewObject(name, position).AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private GameObject NewObject(string name, Vector3 position, bool active = true)
        {
            var obj = new GameObject(name);
            obj.SetActive(active);
            obj.transform.position = position;
            _objects.Add(obj);
            return obj;
        }

        private void Advance(Vector3 displacement) => typeof(BowArrowProjectile)
            .GetMethod("AdvanceServer", PrivateInstance).Invoke(_arrow, new object[] { displacement, _owner });
        private bool HasHit() => (bool)typeof(BowArrowProjectile).GetField("_hasHit", PrivateInstance).GetValue(_arrow);
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    }

    public sealed class BowProjectileRecordingReceiver : MonoBehaviour, IDamageReceiver
    {
        public float CurrentHp { get; private set; } = 1000f;
        public float MaxHp => 1000f;
        public int DamageCalls { get; private set; }
        public float TotalDamage { get; private set; }
        public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition)
        {
            DamageCalls++;
            TotalDamage += amount;
            CurrentHp -= amount;
        }
    }
}
