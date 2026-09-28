using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using BodyPart = BattlePvp.Combat.BodyPart;

namespace BattlePvp.EditorTests
{
    public sealed class MeleeBladeCollisionTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly Vector3 _origin = new Vector3(4100, 4200, 4300);
        private MeleeHitBox _blade;
        private AttackData _attack;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            var owner = New("Attacker", _origin);
            owner.AddComponent<Mirror.NetworkIdentity>();
            var stats = owner.AddComponent<StatManager>();
            var values = new StatContainer(); values.STR.Invested = 10;
            Set(stats, "_stats", values);
            var processor = owner.AddComponent<AttackProcessor>();
            EditorTestLifecycle.Invoke(processor, "Awake");
            var sword = New("Blade", _origin); sword.transform.SetParent(owner.transform, true);
            var shape = sword.AddComponent<BoxCollider>();
            shape.size = new Vector3(.07f, .02f, 1); shape.center = Vector3.forward * .6f;
            _blade = sword.AddComponent<MeleeHitBox>();
            EditorTestLifecycle.Invoke(_blade, "Awake");
            _attack = ScriptableObject.CreateInstance<AttackData>(); _attack.damage = 1;
            _blade.SetAttackData(_attack);
        }

        [TearDown] public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear(); Object.DestroyImmediate(_attack);
        }

        [Test] public void DamageFollowsTheVisibleBladeWithoutAForwardOffsetOrDuplicateHits()
        {
            _blade.transform.rotation = Quaternion.Euler(0, 90, 0);
            var actual = Target(_origin + Vector3.right * .8f);
            var phantom = Target(_origin + Vector3.forward * 1.4f);
            Physics.SyncTransforms(); _blade.EnableHitBox(); Tick(); Tick();
            Assert.That(actual.DamageCalls, Is.EqualTo(1));
            Assert.That(actual.CurrentHp, Is.LessThan(1000));
            Assert.That(phantom.DamageCalls, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void SweptBladeHitsBetweenFramesIncludingTheClosingAnimationEvent(bool closeWindow)
        {
            var target = Target(_origin + Vector3.forward);
            _blade.transform.rotation = Quaternion.Euler(0, -60, 0);
            Physics.SyncTransforms(); _blade.EnableHitBox(); Tick();
            Assert.That(target.DamageCalls, Is.Zero);
            _blade.transform.rotation = Quaternion.Euler(0, 60, 0);
            if (closeWindow) _blade.EndHitWindow();
            Tick(); Tick();
            Assert.That(target.DamageCalls, Is.EqualTo(1));
        }

        [Test] public void CancellationDoesNotApplyThePendingSweep()
        {
            var target = Target(_origin + Vector3.forward);
            _blade.transform.rotation = Quaternion.Euler(0, -60, 0);
            Physics.SyncTransforms(); _blade.EnableHitBox(); Tick();
            _blade.transform.rotation = Quaternion.Euler(0, 60, 0);
            _blade.EndHitWindow(); _blade.DisableHitBox(); Tick();
            Assert.That(target.DamageCalls, Is.Zero);
        }

        [TestCase(.51f, .8f)] [TestCase(1.2f, 1f)] [TestCase(1.8f, 1.5f)] [TestCase(.08f, 0f)]
        public void ActualTrainingPrefabUsesBladeContactForLegTorsoHeadAndIgnoresStand(float height, float multiplier)
        {
            var dummy = TrainingDummy();
            // First measure torso damage with the same real attack pipeline and stats.
            float bodyDamage = Strike(dummy, 1.2f);
            Assert.That(bodyDamage, Is.GreaterThan(0));
            float partDamage = Strike(dummy, height);
            Assert.That(partDamage, Is.EqualTo(bodyDamage * multiplier).Within(.001f));
        }

        [TestCase(false)] [TestCase(true)]
        public void SimultaneousTorsoAndLegOverlapUsesTheNearestRegionRegardlessOfPhysicsOrder(bool bodyFirst)
        {
            var dummy = TrainingDummy();
            float bodyDamage = Strike(dummy, 1.2f);
            var parts = dummy.GetComponentsInChildren<HitBodyPart>();
            Collider leg = System.Array.Find(parts, p => p.Part == BodyPart.Legs).GetComponent<Collider>();
            Collider body = System.Array.Find(parts, p => p.Part == BodyPart.Body).GetComponent<Collider>();
            var query = (CombatPhysicsQuery)typeof(MeleeHitBox).GetField("_sweepQuery", Private).GetValue(_blade);
            query.Colliders[0] = bodyFirst ? body : leg;
            query.Colliders[1] = bodyFirst ? leg : body;
            float before = dummy.CurrentHp;
            _blade.EnableHitBox();
            Vector3 center = _origin + new Vector3(-.15f, .75f, .6f);
            typeof(MeleeHitBox).GetMethod("ProcessContacts", Private).Invoke(_blade, new object[] { 2, center });
            typeof(MeleeHitBox).GetMethod("ProcessContacts", Private).Invoke(_blade, new object[] { 2, center });
            Assert.That(before - dummy.CurrentHp, Is.EqualTo(bodyDamage * .8f).Within(.001f));
        }

        private DummyHealth TrainingDummy()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/training-dummy.prefab"),
                _origin + Vector3.forward * .6f, Quaternion.identity);
            _objects.Add(root);
            var stats = root.GetComponent<StatManager>();
            EditorTestLifecycle.BindNetwork(root); EditorTestLifecycle.Invoke(stats, "Awake");
            var dummy = root.GetComponent<DummyHealth>();
            EditorTestLifecycle.Invoke(dummy, "Awake"); EditorTestLifecycle.Invoke(dummy, "OnEnable");
            return dummy;
        }
        private float Strike(DummyHealth dummy, float height)
        {
            Set(dummy, "_currentHp", dummy.MaxHp);
            _blade.DisableHitBox();
            _blade.transform.position = _origin + new Vector3(-.15f, height, 0);
            Physics.SyncTransforms(); _blade.EnableHitBox(); Tick(); Tick();
            return dummy.MaxHp - dummy.CurrentHp;
        }

        private MeleeRecordingReceiver Target(Vector3 point)
        {
            var target = New("Damage target", point); target.AddComponent<Mirror.NetworkIdentity>(); target.AddComponent<StatManager>();
            var receiver = target.AddComponent<MeleeRecordingReceiver>();
            var shape = target.AddComponent<BoxCollider>(); shape.size = Vector3.one * .04f; shape.isTrigger = true;
            return receiver;
        }
        private GameObject New(string name, Vector3 point)
        { var obj = new GameObject(name); obj.transform.position = point; _objects.Add(obj); return obj; }
        private void Tick() => EditorTestLifecycle.Invoke(_blade, "LateUpdate");
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    }

    public sealed class MeleeRecordingReceiver : MonoBehaviour, IDamageReceiver
    {
        public float CurrentHp { get; private set; } = 1000;
        public float MaxHp => 1000;
        public int DamageCalls { get; private set; }
        public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition)
        { DamageCalls++; CurrentHp -= amount; }
    }
}
