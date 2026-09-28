using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Managers;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class GameplayUiOwnershipTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Dictionary<FieldInfo, object> _statics = new Dictionary<FieldInfo, object>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            Save(typeof(StatCustomizerController), "<Instance>k__BackingField", null);
            Save(typeof(StatCustomizerController), "InstanceChanged", null);
            Save(typeof(StatManager), "<Local>k__BackingField", null);
            Save(typeof(StatManager), "LocalChanged", null);
            Save(typeof(GlobalDataManager), "_instance", null);
            var profile = NewRoot("Profile fixture").AddComponent<GlobalDataManager>();
            typeof(GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, profile);
            Save(typeof(LobbyUIManager), "<Instance>k__BackingField", null);
            Save(typeof(GameInputController), "<Instance>k__BackingField", null);
            Save(typeof(GameInputController), "_paused", false);
            Save(typeof(GameInputController), "_textInputActive", false);
            Save(typeof(GameInputController), "_textInputConsumedFrame", -1);
            var field = typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic);
            Save(typeof(NetworkClient), "connectState", Enum.Parse(field.FieldType, "Connected"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in _objects)
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            _objects.Clear();
            foreach (var entry in _statics) entry.Key.SetValue(null, entry.Value);
            _statics.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LocalCustomizerSurvivesEitherRemoteSpawnOrderAndRemoteDeparture(bool remoteFirst)
        {
            StatCustomizerController local, remote;
            if (remoteFirst) { remote = CreateView(false); local = CreateView(true); }
            else { local = CreateView(true); remote = CreateView(false); }
            Assert.That(StatCustomizerController.Instance, Is.SameAs(local));
            Assert.That(remote.ViewRoot.activeSelf, Is.False);
            Assert.That(local.gameObject != null && remote.gameObject != null, Is.True, "Duplicates must not destroy player UI.");
            local.ViewRoot.SetActive(true);
            UnityEngine.Object.DestroyImmediate(remote.transform.root.gameObject);
            Assert.That(StatCustomizerController.Instance, Is.SameAs(local));
            Assert.That(local.ViewRoot.activeSelf, Is.True, "Remote departure cannot close or delete local settings.");
        }

        [Test]
        public void LocalOwnershipCanArriveAfterAwakeAndReplaceThePreviousOwner()
        {
            var old = CreateView(true);
            var next = CreateView(false);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isLocalPlayer)).SetValue(old.GetComponentInParent<NetworkIdentity>(true), false);
            Invoke(old, "OnLocalPlayerChanged", new object[] { null });
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isLocalPlayer)).SetValue(next.GetComponentInParent<NetworkIdentity>(true), true);
            Invoke(next, "OnLocalPlayerChanged", next.GetComponentInParent<StatManager>(true));
            Assert.That(StatCustomizerController.Instance, Is.SameAs(next));
            Assert.That(Get<StatManager>(next, "_statManager"), Is.SameAs(next.GetComponentInParent<StatManager>(true)));
            Assert.That(old.ViewRoot.activeSelf, Is.False);
        }

        [Test]
        public void ApprovedRevivalClosesTheLocalCustomizerWithoutCancellingItsPendingSave()
        {
            var local = CreateView(true);
            var root = NewRoot("Lobby view fixture");
            var lobby = root.AddComponent<LobbyUIManager>();
            // Invoke the actual binding and life transition without enabling scene-discovery callbacks.
            Invoke(lobby, "FindCanvasCustomizer");
            local.ViewRoot.SetActive(true);
            Set(local, "_isApplying", true);
            Set(local, "_applyGeneration", 7u);
            Invoke(lobby, "OnLocalPlayerRevived");
            Assert.That(local.ViewRoot.activeSelf, Is.False);
            Assert.That(Get<bool>(local, "_isApplying"), Is.True);
            Assert.That(Get<uint>(local, "_applyGeneration"), Is.EqualTo(7u), "Closing the canvas cannot invalidate a server/save acknowledgement.");
        }

        [Test]
        public void ClosingChatStillConsumesGameplayInputForTheCurrentFrame()
        {
            GameInputController.SetTextInputActive(true);
            GameInputController.SetTextInputActive(false);
            Assert.That(GameInputController.IsTextInputActive, Is.True, "Chat submit/close and respawn polling can run in either order this frame.");
        }

        [Test]
        public void PlayerPrefabDoesNotUseTheCustomizerIconForOverflow()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            var view = prefab.GetComponentInChildren<PlayerHudView>(true);
            var customizer = prefab.GetComponentInChildren<StatCustomizerController>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(customizer, Is.Not.Null);
            var hudData = new SerializedObject(view);
            var customizerData = new SerializedObject(customizer);
            Assert.That(customizerData.FindProperty("_identityIcon").objectReferenceValue, Is.Not.Null);
            Assert.That(hudData.FindProperty("_overflowEffect").objectReferenceValue, Is.Null,
                "This prefab has no separate overflow image; the existing health bar remains its HP display.");
        }

        private StatCustomizerController CreateView(bool local)
        {
            var root = NewRoot(local ? "Local avatar" : "Remote avatar");
            var identity = root.AddComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isLocalPlayer)).SetValue(identity, local);
            root.AddComponent<StatManager>();
            var setting = new GameObject("Stat_Setting");
            setting.transform.SetParent(root.transform, false);
            var canvas = new GameObject("Canvas_Customizer");
            canvas.transform.SetParent(setting.transform, false);
            var view = setting.AddComponent<StatCustomizerController>();
            Invoke(view, "Awake");
            return view;
        }

        private GameObject NewRoot(string name)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            _objects.Add(root);
            return root;
        }

        private void Save(Type type, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            _statics.Add(field, field.GetValue(null));
            field.SetValue(null, value);
        }
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    }
}
