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
    public sealed class CharacterBowCadenceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private BowAttackController _bow;
        private StatManager _stats;
        private CharacterCatalog _catalog, _previousCatalog;
        private CharacterDefinition _character;
        private JobSkillData _data;

        [SetUp] public void Setup()
        {
            _previousCatalog = CharacterCatalog.Instance;
            _catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            _character = ScriptableObject.CreateInstance<CharacterDefinition>();
            _character.Id = "bow-cadence-test";
            _catalog.Characters = new[] { _character };
            CatalogField.SetValue(null, _catalog);
            _root = new GameObject("Character bow cadence fixture");
            _root.SetActive(false);
            var appearance = EditorTestLifecycle.AddNetwork<PlayerAppearance>(_root);
            Set(appearance, "_selectedId", _character.Id);
            _stats = EditorTestLifecycle.AddNetwork<StatManager>(_root);
            _bow = EditorTestLifecycle.AddNetwork<BowAttackController>(_root);
            Set(_bow, "_showCrosshair", false);
            _data = AssetDatabase.LoadAssetAtPath<JobSkillData>("Assets/Player/skill/Poly/Poly_WeaponSwap.asset");
            _root.SetActive(true);
            EditorTestLifecycle.Invoke(_bow, "Awake");
        }

        [TearDown] public void Cleanup()
        {
            if (_bow != null) EditorTestLifecycle.Invoke(_bow, "OnDisable");
            Object.DestroyImmediate(_root);
            CatalogField.SetValue(null, _previousCatalog);
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_character);
        }

        [TestCase(.8f)]
        [TestCase(1f)]
        [TestCase(1.25f)]
        public void CharacterCadenceKeepsClientChargeAndServerRecoveryConsistent(float speed)
        {
            SelectSpeed(speed);
            Call("PlayBowAnimationLocal", "Bow_Draw", Vector3.forward);
            Assert.That(Get<float>("_drawReadyAt") - Time.time, Is.EqualTo(3.05f / speed).Within(.001f));
            float elapsed = .625f / speed;
            Call("QueueShot", _data, elapsed, Vector3.forward);
            float clientDamage = Get<float>("_offlineShotMultiplier");
            Assert.That(clientDamage, Is.EqualTo(.575f).Within(.001f));
            Assert.That(Get<bool>("_releaseArrowEventPending"), Is.True);

            var authority = Get<BowShotAuthority>("_serverShotAuthority");
            Set(_bow, "_serverChargeSpeedMultiplier", speed);
            Assert.That(authority.TryBegin(10), Is.True);
            double released = 10 + elapsed;
            Assert.That((bool)Call("ReleaseServerShot", _data, released), Is.True);
            Assert.That(authority.TryConsume(released, out float serverDamage), Is.True);
            Assert.That(serverDamage, Is.EqualTo(clientDamage).Within(.001f));
            Assert.That(authority.TryConsume(released, out _), Is.False);
            Assert.That(authority.TryBegin(released + 1f / speed - .001), Is.False);
            Assert.That(authority.TryBegin(released + 1f / speed + .001), Is.True);
        }

        [Test] public void ChangingCharacterDuringDrawCannotRepriceThePendingShot()
        {
            SelectSpeed(1.25f);
            Call("PlayBowAnimationLocal", "Bow_Draw", Vector3.forward);
            Set(_bow, "_serverChargeSpeedMultiplier", 1.25f);
            var authority = Get<BowShotAuthority>("_serverShotAuthority");
            authority.TryBegin(10);
            SelectSpeed(.8f);
            Call("QueueShot", _data, .5f, Vector3.forward);
            Assert.That(Get<float>("_offlineShotMultiplier"), Is.EqualTo(.575f).Within(.001f));
            Assert.That((bool)Call("ReleaseServerShot", _data, 10.5d), Is.True);
            Assert.That(authority.TryConsume(10.5, out float damage), Is.True);
            Assert.That(damage, Is.EqualTo(.575f).Within(.001f));
            Call("PlayBowAnimationLocal", "Bow_Draw", Vector3.forward);
            Assert.That(Get<float>("_chargeSpeedMultiplier"), Is.EqualTo(.8f), "The next draw uses the newly selected character.");
        }

        [TestCase(.8f)]
        [TestCase(1.25f)]
        public void ServerDamageUsesItsOwnCadenceSnapshot(float serverSpeed)
        {
            Set(_bow, "_chargeSpeedMultiplier", 100f);
            Set(_bow, "_serverChargeSpeedMultiplier", serverSpeed);
            var authority = Get<BowShotAuthority>("_serverShotAuthority");
            authority.TryBegin(10);
            double released = 10 + .625f / serverSpeed;
            Assert.That((bool)Call("ReleaseServerShot", _data, released), Is.True);
            Assert.That(authority.TryConsume(released, out float damage), Is.True);
            Assert.That(damage, Is.EqualTo(.575f).Within(.001f));
        }

        [Test] public void StatAllocationDoesNotAddAnAgiMultiplierToExistingBowTiming()
        {
            SelectSpeed(1f);
            var allocation = new StatContainer(); allocation.AGI.Invested = 30;
            Set(_stats, "_stats", allocation); _stats.RecalculateIdentity();
            Call("PlayBowAnimationLocal", "Bow_Draw", Vector3.forward);
            Assert.That(Get<float>("_chargeSpeedMultiplier"), Is.EqualTo(1f));
            allocation.AGI.Invested = 0; allocation.STR.Invested = 30;
            Set(_stats, "_stats", allocation); _stats.RecalculateIdentity();
            Call("PlayBowAnimationLocal", "Bow_Draw", Vector3.forward);
            Assert.That(Get<float>("_chargeSpeedMultiplier"), Is.EqualTo(1f));
            SelectSpeed(1.25f);
            Call("PlayBowAnimationLocal", "Bow_Draw", Vector3.forward);
            Assert.That(Get<float>("_chargeSpeedMultiplier"), Is.EqualTo(1.25f));
        }

        private void SelectSpeed(float speed) => _character.CombatModifiers = new CharacterStatModifiers(1, 1, 1, 1, speed);
        private object Call(string method, params object[] args) => typeof(BowAttackController).GetMethod(method, Private).Invoke(_bow, args);
        private T Get<T>(string field) => (T)typeof(BowAttackController).GetField(field, Private).GetValue(_bow);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static FieldInfo CatalogField => typeof(CharacterCatalog).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
    }
}
