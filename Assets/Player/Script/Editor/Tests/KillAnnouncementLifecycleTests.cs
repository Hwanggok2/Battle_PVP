using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class KillAnnouncementLifecycleTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private FieldInfo _instanceField;
        private object _previousInstance;
        private GameObject _owner;
        private GameObject _container;
        private GameObject _foreignChild;
        private GameObject _templateOwner;
        private GameObject _prefab;
        private KillAnnouncementUI _ui;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Application.isPlaying, Is.False, "These are synchronous EditMode ownership checks, not a timed PlayMode run.");
            _prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KillAnouncement.prefab");
            Assert.That(_prefab, Is.Not.Null);
            Assert.That(_prefab.GetComponent<KillAnnouncementItemUI>(), Is.Not.Null);
            _instanceField = typeof(KillAnnouncementUI).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            _previousInstance = _instanceField.GetValue(null);
            _instanceField.SetValue(null, null);
            _container = new GameObject("External kill feed container", typeof(RectTransform));
            _foreignChild = new GameObject("Foreign layout child", typeof(RectTransform));
            _foreignChild.transform.SetParent(_container.transform, false);
            _owner = new GameObject("Kill feed owner fixture", typeof(RectTransform));
            _owner.SetActive(false);
            _ui = _owner.AddComponent<KillAnnouncementUI>();
            Set(_ui, "_container", (RectTransform)_container.transform);
            Set(_ui, "_itemPrefab", _prefab);
            Set(_ui, "_battleSceneOnly", false);
            Set(_ui, "_duration", 10f);
            // Regular MonoBehaviour lifecycle scheduling is not guaranteed in EditMode.
            // Invoke the real handlers explicitly; no test claims the PlayerLoop delivered them.
            Invoke(_ui, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_ui != null)
            {
                Invoke(_ui, "OnDisable");
                Invoke(_ui, "OnDestroy");
            }
            if (_owner != null) Object.DestroyImmediate(_owner);
            if (_templateOwner != null) Object.DestroyImmediate(_templateOwner);
            if (_container != null) Object.DestroyImmediate(_container);
            if (_instanceField != null) _instanceField.SetValue(null, _previousInstance);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ShowRejectsInactiveGameObjectAndDisabledComponent(bool inactiveGameObject)
        {
            if (!inactiveGameObject)
            {
                _owner.SetActive(true);
                _ui.enabled = false;
            }

            _ui.Show("Killer", "Victim");

            Assert.That(ActiveItems.Count, Is.Zero);
            AssertOnlyForeignChild();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DisableRemovesOwnedAnnouncementsButKeepsForeignContainerChildren(bool disableGameObject)
        {
            _owner.SetActive(true);
            GameObject first = ShowAndGetItem("First killer", "First victim");
            _ui.Show("Second killer", "Second victim");
            Assert.That(ActiveItems.Count, Is.EqualTo(2));

            if (disableGameObject) _owner.SetActive(false);
            else _ui.enabled = false;
            Invoke(_ui, "OnDisable");

            Assert.That(first == null, Is.True, "Owned EditMode items are destroyed during the disable callback.");
            Assert.That(ActiveItems.Count, Is.Zero);
            AssertOnlyForeignChild();
            _ui.Show("While disabled", "Must not appear");
            AssertOnlyForeignChild();
        }

        [Test]
        public void ReenableShowsOnlyNewAnnouncementsAcrossTenCyclesAndKeepsNamesPlain()
        {
            for (int i = 0; i < 10; i++)
            {
                _owner.SetActive(true);
                Assert.That(ActiveItems.Count, Is.Zero);
                GameObject item = ShowAndGetItem("<b>홍\\u0041</b>", "<size=4>적</size>\n이름");
                var row = item.GetComponent<KillAnnouncementItemUI>();
                AssertPlain(Get<TMP_Text>(row, "_killerNameText"), "<b>홍\\\\u0041</b>");
                AssertPlain(Get<TMP_Text>(row, "_victimNameText"), "<size=4>적</size> 이름");
                Assert.That(_container.transform.childCount, Is.EqualTo(2));

                _owner.SetActive(false);
                Invoke(_ui, "OnDisable");
                Assert.That(item == null, Is.True);
                Assert.That(ActiveItems.Count, Is.Zero);
                AssertOnlyForeignChild();
            }
        }

        [Test]
        public void LifetimeEnumeratorReleasesItsItemAfterTheWaitYieldWithoutClaimingElapsedTime()
        {
            _owner.SetActive(true);
            GameObject item = ShowAndGetItem("Killer", "Victim");
            // Native EditMode coroutine scheduling is not the assertion target. Drive the actual release stage explicitly.
            _ui.StopAllCoroutines();
            var lifetime = (IEnumerator)Invoke(_ui, "CoRemoveAfter", item, 10f);
            Assert.That(lifetime.MoveNext(), Is.True);
            Assert.That(lifetime.Current, Is.InstanceOf<WaitForSecondsRealtime>());
            Assert.That(item != null && ActiveItems.Contains(item), Is.True);

            Assert.That(lifetime.MoveNext(), Is.False);
            Assert.That(item == null, Is.True);
            Assert.That(ActiveItems.Count, Is.Zero);
            AssertOnlyForeignChild();
            Assert.DoesNotThrow(() => Invoke(_ui, "ReleaseItem", item));
            AssertOnlyForeignChild();
        }

        [Test]
        public void DestroyingOwnerReleasesItsItemsFromAnExternalContainer()
        {
            _owner.SetActive(true);
            GameObject item = ShowAndGetItem("Killer", "Victim");
            HashSet<GameObject> items = ActiveItems;

            Invoke(_ui, "OnDestroy");
            Object.DestroyImmediate(_owner);

            Assert.That(item == null, Is.True);
            Assert.That(items.Count, Is.Zero);
            Assert.That(KillAnnouncementUI.Instance, Is.Null);
            AssertOnlyForeignChild();
        }

        [Test]
        public void PrefabOnEnableDisablingOwnerCannotLeaveAnUntrackedAnnouncement()
        {
            _templateOwner = new GameObject("Inactive kill feed template holder");
            _templateOwner.SetActive(false);
            GameObject template = Object.Instantiate(_prefab, _templateOwner.transform);
            var disable = template.AddComponent<KillAnnouncementDisableOwnerOnEnable>();
            disable.Owner = _ui;
            Set(_ui, "_itemPrefab", template);
            _owner.SetActive(true);

            _ui.Show("Killer", "Victim");

            Assert.That(_owner.activeSelf, Is.False, "The cloned prefab's actual OnEnable must exercise the reentrant shutdown.");
            Assert.That(ActiveItems.Count, Is.Zero);
            AssertOnlyForeignChild();
        }

        private HashSet<GameObject> ActiveItems => Get<HashSet<GameObject>>(_ui, "_activeItems");

        private GameObject ShowAndGetItem(string killer, string victim)
        {
            _ui.Show(killer, victim);
            Assert.That(ActiveItems.Count, Is.EqualTo(1));
            using var items = ActiveItems.GetEnumerator();
            Assert.That(items.MoveNext(), Is.True);
            Assert.That(items.Current.activeSelf, Is.True);
            Assert.That(items.Current.transform.parent, Is.SameAs(_container.transform));
            return items.Current;
        }

        private void AssertOnlyForeignChild()
        {
            Assert.That(_container != null && _foreignChild != null, Is.True);
            Assert.That(_container.transform.childCount, Is.EqualTo(1));
            Assert.That(_container.transform.GetChild(0), Is.SameAs(_foreignChild.transform));
            Assert.That(_foreignChild.activeSelf, Is.True);
        }

        private static void AssertPlain(TMP_Text text, string expected)
        {
            Assert.That(text.text, Is.EqualTo(expected));
            Assert.That(text.richText, Is.False);
            Assert.That(text.parseCtrlCharacters, Is.True);
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        private static object Invoke(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Private).Invoke(target, arguments);
    }

    [ExecuteAlways]
    public sealed class KillAnnouncementDisableOwnerOnEnable : MonoBehaviour
    {
        public KillAnnouncementUI Owner;
        private void OnEnable()
        {
            if (Owner != null) Owner.gameObject.SetActive(false);
        }
    }
}
