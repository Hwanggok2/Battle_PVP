using System.Collections.Generic;
using System.Reflection;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

namespace BattlePvp.EditorTests
{
    public sealed class DamagePopupLifecycleTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _owner;
        private DamagePopupManager _manager;
        private FieldInfo _instanceField;
        private object _previousInstance;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Application.isPlaying, Is.False,
                "These tests call actual lifecycle handlers synchronously; they do not run a scene unload or timed animation.");
            _instanceField = typeof(DamagePopupManager).GetField("<Instance>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            _previousInstance = _instanceField.GetValue(null);
            _instanceField.SetValue(null, null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Damage_Popup.prefab");
            Assert.That(prefab, Is.Not.Null);
            _owner = new GameObject("Damage popup lifecycle owner");
            _owner.SetActive(false);
            _owner.transform.localScale = new Vector3(2f, 3f, 4f);
            _manager = _owner.AddComponent<DamagePopupManager>();
            Set(_manager, "_popupPrefab", prefab.GetComponent<DamagePopup>());
            Set(_manager, "_prewarmCount", 2);
            Set(_manager, "_maxRetainedPopups", 2);
            Invoke(_manager, "Awake");
            Assert.That(Pool.CountInactive, Is.EqualTo(2));
            Assert.That(PoolRoot.localScale, Is.EqualTo(Vector3.one));
            _owner.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_manager != null) Invoke(_manager, "OnDestroy");
                if (_owner != null) Object.DestroyImmediate(_owner);
            }
            finally
            {
                if (_instanceField != null) _instanceField.SetValue(null, _previousInstance);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SceneCleanupToleratesThePopupRootBeingDestroyedFirst(bool includeActivePopup)
        {
            if (includeActivePopup) Spawn();
            Object.DestroyImmediate(PoolRoot.gameObject);

            Assert.DoesNotThrow(() => Invoke(_manager, "OnDisable"));
            Assert.DoesNotThrow(() => Invoke(_manager, "OnDestroy"));
            Assert.DoesNotThrow(() => Invoke(_manager, "OnDestroy"));

            Assert.That(Active, Is.Empty);
            Assert.That(Pool, Is.Null);
            Assert.That(PoolRoot == null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SpawnSkipsDestroyedRetainedObjectsAndComponents(bool componentOnly)
        {
            DamagePopup[] retained = PoolRoot.GetComponentsInChildren<DamagePopup>(true);
            Assert.That(retained.Length, Is.EqualTo(2));
            foreach (DamagePopup popup in retained)
            {
                if (componentOnly) Object.DestroyImmediate(popup);
                else Object.DestroyImmediate(popup.gameObject);
            }

            DamagePopup fresh = null;
            Assert.DoesNotThrow(() => fresh = Spawn());

            Assert.That(fresh != null, Is.True);
            Assert.That(Active.Count, Is.EqualTo(1));
            Assert.That(fresh.gameObject.activeInHierarchy, Is.True);
            Invoke(fresh, "ReturnToPool");
            Assert.That(Active, Is.Empty);
            Assert.That(Pool.CountInactive, Is.EqualTo(1));
        }

        [Test]
        public void DisableRemovesStaleHandlesAndAlreadyReturnedEntriesWithoutWaitingForCallbacks()
        {
            DamagePopup destroyed = Spawn();
            DamagePopup returned = Spawn();
            Object.DestroyImmediate(destroyed.gameObject);
            // A destroyed native object can still have a managed HashSet entry during teardown.
            // Reinsert that handle so this test does not depend on Unity callback ordering.
            Active.Add(destroyed);
            Set(returned, "_returned", true);

            Assert.DoesNotThrow(() => Invoke(_manager, "OnDisable"));

            Assert.That(Active, Is.Empty);
            Assert.That(returned.gameObject.activeSelf, Is.False);
            DamagePopup next = Spawn();
            Assert.That(next != null, Is.True);
            Assert.That(Active.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OwnerDisableAndReenableReusePopupsWithoutDuplicateReturns(bool disableGameObject)
        {
            for (int i = 0; i < 10; i++)
            {
                _owner.SetActive(true);
                _manager.enabled = true;
                DamagePopup first = Spawn();
                DamagePopup second = Spawn();
                Assert.That(Active.Count, Is.EqualTo(2));
                if (disableGameObject) _owner.SetActive(false);
                else _manager.enabled = false;
                Invoke(_manager, "OnDisable");

                Assert.That(Active, Is.Empty);
                Assert.That(first.gameObject.activeSelf || second.gameObject.activeSelf, Is.False);
                Assert.That(Pool.CountInactive, Is.EqualTo(2));
                Assert.DoesNotThrow(() => Invoke(first, "ReturnToPool"));
                Assert.DoesNotThrow(() => Invoke(first, "ReturnToPool"));
                Assert.That(Pool.CountInactive, Is.EqualTo(2));
                _manager.CreatePopup(Vector3.zero, 99f);
                Assert.That(Active, Is.Empty, "Disabled owners do not check out more objects.");
            }
        }

        [Test]
        public void DestroyedWorldRootIsRecreatedAtUnitScaleOnTheNextSpawn()
        {
            Transform previousRoot = PoolRoot;
            Object.DestroyImmediate(previousRoot.gameObject);

            DamagePopup popup = Spawn();

            Assert.That(PoolRoot != null, Is.True);
            Assert.That(PoolRoot, Is.Not.SameAs(previousRoot));
            Assert.That(PoolRoot.parent, Is.Null);
            Assert.That(PoolRoot.localScale, Is.EqualTo(Vector3.one));
            Assert.That(PoolRoot.gameObject.scene, Is.EqualTo(_owner.scene));
            Assert.That(popup.transform.parent, Is.SameAs(PoolRoot));
        }

        private DamagePopup Spawn()
        {
            var previous = new HashSet<DamagePopup>(Active);
            _manager.CreatePopup(Vector3.zero, 12f);
            foreach (DamagePopup popup in Active)
                if (!previous.Contains(popup)) return popup;
            Assert.Fail("CreatePopup did not produce a new active popup.");
            return null;
        }

        private HashSet<DamagePopup> Active => Get<HashSet<DamagePopup>>(_manager, "_active");
        private ObjectPool<DamagePopup> Pool => Get<ObjectPool<DamagePopup>>(_manager, "_pool");
        private Transform PoolRoot => Get<Transform>(_manager, "_poolRoot");
        private static object Invoke(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    }
}
