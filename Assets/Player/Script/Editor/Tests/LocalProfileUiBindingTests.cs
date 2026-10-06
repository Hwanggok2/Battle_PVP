using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Lobby;
using BattlePvp.Managers;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class LocalProfileUiBindingTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly Dictionary<FieldInfo, object> _previousStatics = new Dictionary<FieldInfo, object>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private GlobalDataManager _profile;

        [SetUp]
        public void SetUp()
        {
            IsolateStatic(typeof(GlobalDataManager), "_instance", null);
            IsolateStatic(typeof(GlobalDataManager), "_applicationIsQuitting", false);
            IsolateStatic(typeof(StatManager), "<Local>k__BackingField", null);
            IsolateStatic(typeof(StatManager), "LocalChanged", null);
            IsolateStatic(typeof(StatCustomizerController), "<Instance>k__BackingField", null);
            IsolateStatic(typeof(StatCustomizerController), "InstanceChanged", null);
            IsolateStatic(typeof(LobbyUIManager), "<Instance>k__BackingField", null);
            IsolateStatic(typeof(PlayFabBattleManager), "<Instance>k__BackingField", null);
            IsolateStatic(typeof(PlayerHUD), "<Instance>k__BackingField", null);
            IsolateStatic(typeof(PlayerHUD), "_globalHud", null);
            FieldInfo connectionState = typeof(NetworkClient).GetField("connectState", PrivateStatic);
            IsolateStatic(typeof(NetworkClient), "connectState", Enum.Parse(connectionState.FieldType, "None"));
            _profile = NewObject("Profile fixture").AddComponent<GlobalDataManager>();
            SetStatic(typeof(GlobalDataManager), "_instance", _profile);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null)
                {
                    foreach (var view in _objects[i].GetComponentsInChildren<StatCustomizerController>(true)) Invoke(view, "OnDisable");
                    foreach (var view in _objects[i].GetComponentsInChildren<LobbyUIManager>(true)) Invoke(view, "OnDisable");
                    foreach (var view in _objects[i].GetComponentsInChildren<PlayerHUD>(true)) Invoke(view, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
                }
            _objects.Clear();
            foreach (var entry in _previousStatics) entry.Key.SetValue(null, entry.Value);
            _previousStatics.Clear();
        }

        [TestCase(true)] [TestCase(false)]
        public void DeadPlayersCanAllocateThirtyPointsAndSwitchIntoMonostat(bool startedMonostat)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); string oldName = scene.name; scene.name = "Battle";
            try
            {
                LoadProfile(startedMonostat ? Preset(30, 0, 0, 0) : Preset(8, 8, 7, 7), true);
                var stats = CreatePlayer("Dead build change");
                stats.ApplyLocalSceneStats(_profile.SavedStats);
                var health = stats.GetComponent<HealthSystem>(); Set(health, "_isDead", true); SetLocal(stats);
                var customizer = CreateCustomizer(); EditorTestLifecycle.SetActive(customizer, true);
                foreach (string field in new[] { "_str", "_con", "_agi", "_def" }) Get<StatSlider>(customizer, field).SetInvestedWithoutNotify(0);
                var slider = Get<StatSlider>(customizer, "_agi"); slider.SetInvestedWithoutNotify(30);
                Invoke(customizer, "OnInvestedChanged", slider, 30f);
                Assert.That(Get<StatContainer>(customizer, "_virtualStats").AGI.Invested, Is.EqualTo(30));
                Invoke(customizer, "Apply");
                Assert.That(stats.GetStatsCopy().AGI.Invested, Is.EqualTo(30));
                Assert.That(stats.CurrentIdentity.Type, Is.EqualTo(IdentityType.Monostat));
                // Exercise the authoritative command path as well as the local UI.
                Set(stats, "_serverStatsInitialized", true); Set(stats, "_nextStatRequestAt", 0d);
                Assert.That(Invoke(stats, "TryAcceptClientStats", Preset(0, 0, 0, 30)), Is.True);
                Assert.That(stats.GetStatsCopy().DEF.Invested, Is.EqualTo(30));
                Set(health, "_isDead", false); Set(stats, "_nextStatRequestAt", 0d);
                Assert.That(Invoke(stats, "TryAcceptClientStats", Preset(30, 0, 0, 0)), Is.False);
            }
            finally { scene.name = oldName; }
        }

        [Test]
        public void ReopeningCustomizerRestoresTheSelectedSavedAllocationWithoutSwitchingSlots()
        {
            LoadProfile(Preset(0, 0, 30, 0), true);
            var customizer = CreateCustomizer();
            Invoke(customizer, "OnEnable");
            var agi = Get<StatSlider>(customizer, "_agi");
            agi.SetInvestedWithoutNotify(1);
            customizer.RefreshForOpen();
            Assert.That(agi.Invested, Is.EqualTo(30));
            Assert.That(Get<StatContainer>(customizer, "_virtualStats").AGI.Invested, Is.EqualTo(30));
            Assert.That(_profile.SelectedStatPresetSlot, Is.Zero);
        }

        [Test]
        public void LoadedEmptyProfileIsVisibleAsLoadedInsideEveryPresetEvent()
        {
            int events = 0;
            bool allLoaded = true;
            _profile.OnStatPresetSlotChanged += (_, __, ___) => { events++; allLoaded &= _profile.HasLoadedPlayerStats; };
            _profile.OnStrategistTargetPresetChanged += (_, __) => { events++; allLoaded &= _profile.HasLoadedPlayerStats; };
            _profile.OnSavedStatsUpdated += _ => { events++; allLoaded &= _profile.HasLoadedPlayerStats; };
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            LoadProfile(default, false);
            Assert.That(events, Is.EqualTo(3));
            Assert.That(allLoaded, Is.True);
            Assert.That(_profile.HasLoadedPlayerStats, Is.True);
            Assert.That(_profile.SavedStats.STR.Invested, Is.Zero);
        }

        [Test]
        public void LobbyPlayerPreservesSceneStatsUntilTheProfileHasLoaded()
        {
            StatManager target = CreatePlayer("Unloaded lobby player");
            target.ApplyLocalSceneStats(Preset(8, 8, 7, 7));
            LobbyPlayerActivator activator = NewObject("Lobby activator").AddComponent<LobbyPlayerActivator>();
            Set(activator, "_targetPlayer", target.gameObject);
            int changes = 0;
            target.StatsChanged += _ => changes++;

            Invoke(activator, "ApplySavedStatsToTarget");

            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            Assert.That(target.GetStatsCopy().STR.Invested, Is.EqualTo(8));
            Assert.That(target.GetStatsCopy().AGI.Invested, Is.EqualTo(7));
            Assert.That(changes, Is.Zero, "Unloaded default data must not overwrite the scene player.");
            Assert.That(StatManager.Local, Is.SameAs(target), "The explicit offline lobby player must own its stat panels before login data arrives.");
        }

        [Test]
        public void LobbyPlayerAppliesLoadedZeroPresetToTheExplicitTarget()
        {
            StatManager target = CreatePlayer("Empty lobby player");
            StatManager other = CreatePlayer("Other player");
            target.ApplyLocalSceneStats(Preset(30, 0, 0, 0));
            other.ApplyLocalSceneStats(Preset(0, 0, 30, 0));
            LobbyPlayerActivator activator = NewObject("Lobby activator").AddComponent<LobbyPlayerActivator>();
            Set(activator, "_targetPlayer", target.gameObject);
            LoadProfile(default, true);
            int changes = 0;
            target.StatsChanged += _ => changes++;

            Invoke(activator, "ApplySavedStatsToTarget");

            StatContainer actual = target.GetStatsCopy();
            Assert.That(actual.STR.Invested + actual.CON.Invested + actual.AGI.Invested + actual.DEF.Invested, Is.Zero);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(other.GetStatsCopy().AGI.Invested, Is.EqualTo(30), "Only the configured lobby player is updated.");
        }

        [Test]
        public void LobbyPlayerClearsThePreviousBuildWhenAnUnusedSlotIsSelected()
        {
            StatManager target = CreatePlayer("Slot switching lobby player");
            LobbyPlayerActivator activator = NewObject("Lobby activator").AddComponent<LobbyPlayerActivator>();
            Set(activator, "_targetPlayer", target.gameObject);
            LoadProfile(Preset(30, 0, 0, 0), true);
            Invoke(activator, "ApplySavedStatsToTarget");
            Assert.That(target.GetStatsCopy().STR.Invested, Is.EqualTo(30));

            _profile.SelectStatPresetSlot(1);
            Invoke(activator, "ApplySavedStatsToTarget");

            Assert.That(_profile.HasLoadedPlayerStats, Is.True);
            Assert.That(_profile.HasStatPresetSlot(1), Is.False);
            StatContainer actual = target.GetStatsCopy();
            Assert.That(actual.STR.Invested + actual.CON.Invested + actual.AGI.Invested + actual.DEF.Invested, Is.Zero);
            Assert.That(target.CurrentIdentity.Type, Is.EqualTo(IdentityType.Polymath), "Clearing the slot must also replace the previous monostat presentation.");
        }

        [Test]
        public void NewAccountClearsOpenNormalAndStrategistEditorsWithoutWaitingForPolling()
        {
            LoadProfile(Preset(8, 8, 7, 7), true, Preset(12, 8, 6, 4), true);
            StatCustomizerController customizer = CreateCustomizer();
            EditorTestLifecycle.SetActive(customizer, true);
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.EqualTo(8));
            _profile.BeginPlayerSession();
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.Zero);
            Assert.That(_profile.HasLoadedPlayerStats, Is.False);
            LoadProfile(default, false);
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.Zero);
            Assert.That(customizer.GetRemainPoints(), Is.EqualTo(30));

            LoadProfile(Preset(8, 8, 7, 7), true, Preset(12, 8, 6, 4), true);
            Invoke(customizer, "SelectStrategistTargetPreset");
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.EqualTo(12));
            _profile.BeginPlayerSession();
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.Zero);
        }

        [Test]
        public void StrategistModeReopensWithItsOwnPresetAndIgnoresNormalPresetChanges()
        {
            LoadProfile(Preset(8, 8, 7, 7), true, Preset(12, 8, 6, 4), true);
            StatCustomizerController customizer = CreateCustomizer();
            EditorTestLifecycle.SetActive(customizer, true);
            Invoke(customizer, "SelectStrategistTargetPreset");
            EditorTestLifecycle.SetActive(customizer, false);
            EditorTestLifecycle.SetActive(customizer, true);
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.EqualTo(12));
            _profile.SavedStats = Preset(30, 0, 0, 0);
            Assert.That(Get<StatSlider>(customizer, "_str").Invested, Is.EqualTo(12));
            Assert.That(Get<bool>(customizer, "_editingStrategistTargetPreset"), Is.True);
        }

        [Test]
        public void PlayerReplacementUpdatesHealthAndPreservesTheCurrentUnsavedEditorValues()
        {
            LoadProfile(Preset(8, 8, 7, 7), true);
            StatManager first = CreatePlayer("First");
            StatManager second = CreatePlayer("Second");
            StatCustomizerController customizer = CreateCustomizer();
            EditorTestLifecycle.SetActive(customizer, true);
            SetLocal(first);
            StatSlider str = Get<StatSlider>(customizer, "_str");
            str.SetInvestedWithoutNotify(6);
            Invoke(customizer, "OnInvestedChanged", str, 6f);
            SetLocal(second);
            Assert.That(Get<StatManager>(customizer, "_statManager"), Is.SameAs(second));
            Assert.That(Get<HealthSystem>(customizer, "_playerHealth"), Is.SameAs(second.GetComponent<HealthSystem>()));
            Assert.That(str.Invested, Is.EqualTo(6));
            Assert.That(Get<StatContainer>(customizer, "_virtualStats").STR.Invested, Is.EqualTo(6));
        }

        [Test]
        public void LobbyBindsLateReplacesExactlyOnceAndUnsubscribesTheOriginalPlayer()
        {
            StatManager first = CreatePlayer("First");
            StatManager second = CreatePlayer("Second");
            SetLocal(first);
            LobbyUIManager lobby = CreateLobby();
            EditorTestLifecycle.SetActive(lobby, true);
            Assert.That(SubscriberCount(first, "StatsChanged", lobby), Is.EqualTo(1));
            Assert.That(SubscriberCount(first.GetComponent<HealthSystem>(), "OnDied", lobby), Is.EqualTo(1));
            SetLocal(second);
            SetLocal(second);
            first.OnStopLocalPlayer();
            Assert.That(StatManager.Local, Is.SameAs(second), "Stopping an older player must not unbind its replacement.");
            Assert.That(SubscriberCount(first, "StatsChanged", lobby), Is.Zero);
            Assert.That(SubscriberCount(first.GetComponent<HealthSystem>(), "OnDied", lobby), Is.Zero);
            Assert.That(SubscriberCount(second, "StatsChanged", lobby), Is.EqualTo(1));
            EditorTestLifecycle.SetActive(lobby, false);
            Assert.That(SubscriberCount(second, "StatsChanged", lobby), Is.Zero);
            Assert.That(SubscriberCount(second.GetComponent<HealthSystem>(), "OnRevived", lobby), Is.Zero);
            SetLocal(first);
            EditorTestLifecycle.SetActive(lobby, true);
            Assert.That(Get<StatManager>(lobby, "_localStatManager"), Is.SameAs(first));
            Assert.That(SubscriberCount(first, "StatsChanged", lobby), Is.EqualTo(1));
            first.OnStopLocalPlayer();
            Assert.That(Get<StatManager>(lobby, "_localStatManager"), Is.Null);
            Assert.That(SubscriberCount(first, "StatsChanged", lobby), Is.Zero);
        }

        [Test]
        public void LobbyRoomServiceReplacementReleasesTheSubscribedService()
        {
            PlayFabBattleManager first = NewObject("First service").AddComponent<PlayFabBattleManager>();
            PlayFabBattleManager second = NewObject("Second service").AddComponent<PlayFabBattleManager>();
            LobbyUIManager lobby = CreateLobby();
            EditorTestLifecycle.SetActive(lobby, true);
            Invoke(lobby, "BindRoomService", first);
            Invoke(lobby, "BindRoomService", second);
            Invoke(lobby, "BindRoomService", second);
            Assert.That(SubscriberCount(first, "OnRoomFlowStateChanged", lobby), Is.Zero);
            Assert.That(SubscriberCount(second, "OnRoomFlowStateChanged", lobby), Is.EqualTo(1));
            // The singleton can already point elsewhere by the time this view is disabled.
            SetStatic(typeof(PlayFabBattleManager), "<Instance>k__BackingField", first);
            EditorTestLifecycle.SetActive(lobby, false);
            Assert.That(SubscriberCount(second, "OnRoomFlowStateChanged", lobby), Is.Zero);
            SetStatic(typeof(PlayFabBattleManager), "<Instance>k__BackingField", null);
        }

        [Test]
        public void CustomizerDisableUnsubscribesItsOriginalProfileEvenIfTheSingletonChanges()
        {
            StatCustomizerController customizer = CreateCustomizer();
            EditorTestLifecycle.SetActive(customizer, true);
            GlobalDataManager original = _profile;
            GlobalDataManager replacement = NewObject("Replacement profile").AddComponent<GlobalDataManager>();
            SetStatic(typeof(GlobalDataManager), "_instance", replacement);
            EditorTestLifecycle.SetActive(customizer, false);
            Assert.That(SubscriberCount(original, "OnSavedStatsUpdated", customizer), Is.Zero);
            Assert.That(SubscriberCount(original, "OnStrategistTargetPresetChanged", customizer), Is.Zero);
            EditorTestLifecycle.SetActive(customizer, true);
            Assert.That(SubscriberCount(replacement, "OnSavedStatsUpdated", customizer), Is.EqualTo(1));
            Assert.That(SubscriberCount(original, "OnSavedStatsUpdated", customizer), Is.Zero);
        }

        [Test]
        public void AutomaticInitialInjectionPreservesServerApprovedReconnectStats()
        {
            StatManager stats = EditorTestLifecycle.AddNetwork<StatManager>(NewObject("Restored stats"));
            Set(stats, "_stats", Preset(2, 18, 6, 4));
            Set(stats, "_serverStatsInitialized", true);
            FieldInfo connectionState = typeof(NetworkClient).GetField("connectState", PrivateStatic);
            connectionState.SetValue(null, Enum.Parse(connectionState.FieldType, "Connected"));
            Invoke(stats, "HandleInitialInjection", Preset(30, 0, 0, 0));
            Assert.That(stats.GetStatsCopy().CON.Invested, Is.EqualTo(18));
            Assert.That(stats.GetStatsCopy().STR.Invested, Is.EqualTo(2));
            Invoke(stats, "OnGlobalStatsUpdated", Preset(0, 0, 30, 0));
            Assert.That(stats.GetStatsCopy().CON.Invested, Is.EqualTo(18));
            Assert.That(stats.GetStatsCopy().AGI.Invested, Is.EqualTo(6));
        }

        [Test]
        public void ProfileBindingReleasesHudOnDepartureAndRebindsTheNextPlayerOnce()
        {
            StatManager first = CreatePlayer("First HUD player");
            StatManager second = CreatePlayer("Second HUD player");
            GameObject hudObject = NewObject("HUD binding fixture");
            var display = new GameObject("Display");
            display.transform.SetParent(hudObject.transform, false);
            HudRecordingView view = display.AddComponent<HudRecordingView>();
            PlayerHUD hud = hudObject.AddComponent<PlayerHUD>();
            Set(hud, "_view", view);
            hudObject.SetActive(true);
            var binding = _profile.gameObject.AddComponent<LocalPlayerProfileBinding>();
            Invoke(binding, "OnLocalPlayerChanged", first);
            Assert.That(SubscriberCount(first, "StatsChanged", hud), Is.EqualTo(1));
            Invoke(binding, "OnLocalPlayerChanged", second);
            Assert.That(SubscriberCount(first, "StatsChanged", hud), Is.Zero);
            Assert.That(SubscriberCount(second.GetComponent<HealthSystem>(), "HpChanged", hud), Is.EqualTo(1));
            Invoke(binding, "OnLocalPlayerChanged", new object[] { null });
            Assert.That(SubscriberCount(second, "StatsChanged", hud), Is.Zero);
            Assert.That(SubscriberCount(second.GetComponent<HealthSystem>(), "HpChanged", hud), Is.Zero);
            Assert.That(Get<StatManager>(hud, "_statManager"), Is.Null);
            Assert.That(view.gameObject.activeSelf, Is.False);
            Invoke(binding, "OnLocalPlayerChanged", first);
            Assert.That(SubscriberCount(first, "StatsChanged", hud), Is.EqualTo(1));
            Assert.That(view.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ApplyResponseTimeoutReleasesTheRequestAndRejectsLateOrOlderReplies()
        {
            StatManager stats = EditorTestLifecycle.AddNetwork<StatManager>(NewObject("Pending stat request"));
            Set(stats, "_stats", Preset(8, 8, 7, 7));
            int oldCalls = 0, nextCalls = 0;
            bool accepted = true;
            SetPending(stats, 4, 100d, (success, _) => { oldCalls++; accepted = success; });
            Invoke(stats, "ExpirePendingApplyRequest", 99.999d);
            Assert.That(oldCalls, Is.Zero);
            Invoke(stats, "ExpirePendingApplyRequest", 100d);
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(accepted, Is.False);
            Assert.That(Get<uint>(stats, "_pendingApplyRequestId"), Is.Zero);
            Invoke(stats, "CompleteApplyResponse", 4u, true, Preset(30, 0, 0, 0));
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(stats.GetStatsCopy().STR.Invested, Is.EqualTo(8));
            SetPending(stats, 5, Time.realtimeSinceStartupAsDouble + 10d, (_, __) => nextCalls++);
            Invoke(stats, "CompleteApplyResponse", 4u, true, Preset(30, 0, 0, 0));
            Assert.That(Get<uint>(stats, "_pendingApplyRequestId"), Is.EqualTo(5u));
            Assert.That(nextCalls, Is.Zero);
            Invoke(stats, "CompleteApplyResponse", 5u, false, default(StatContainer));
            Invoke(stats, "CompleteApplyResponse", 5u, false, default(StatContainer));
            Assert.That(nextCalls, Is.EqualTo(1));
        }

        [Test]
        public void PlayerDisableStopAndDisconnectCompletePendingApplyExactlyOnce()
        {
            foreach (string stop in new[] { "disable", "local stop", "disconnect" })
            {
                StatManager stats = EditorTestLifecycle.AddNetwork<StatManager>(NewObject(stop));
                int calls = 0;
                bool accepted = true;
                SetPending(stats, 1, Time.realtimeSinceStartupAsDouble + 10d,
                    (success, _) => { calls++; accepted = success; });
                if (stop == "disable") Invoke(stats, "OnDisable");
                else if (stop == "local stop") stats.OnStopLocalPlayer();
                else stats.OnStopClient();
                stats.OnStopClient();
                Assert.That(calls, Is.EqualTo(1), stop);
                Assert.That(accepted, Is.False, stop);
                Assert.That(Get<uint>(stats, "_pendingApplyRequestId"), Is.Zero, stop);
            }
        }

        [Test]
        public void ACompletionCallbackCanStartTheNextRequestWithoutBeingClearedByTheOldOne()
        {
            StatManager stats = EditorTestLifecycle.AddNetwork<StatManager>(NewObject("Reentrant stat request"));
            int nextCalls = 0;
            SetPending(stats, 1, Time.realtimeSinceStartupAsDouble + 10d, (_, __) =>
                SetPending(stats, 2, Time.realtimeSinceStartupAsDouble + 10d, (___, ____) => nextCalls++));
            Invoke(stats, "CompleteApplyResponse", 1u, false, default(StatContainer));
            Assert.That(Get<uint>(stats, "_pendingApplyRequestId"), Is.EqualTo(2u));
            Invoke(stats, "CompleteApplyResponse", 1u, false, default(StatContainer));
            Assert.That(nextCalls, Is.Zero);
            Invoke(stats, "CompleteApplyResponse", 2u, false, default(StatContainer));
            Assert.That(nextCalls, Is.EqualTo(1));
        }

        private void LoadProfile(StatContainer stats, bool used, StatContainer strategist = default, bool hasStrategist = false) =>
            _profile.ApplyLoadedStatPresetData(new[] { stats, default, default }, new[] { used, false, false }, 0, strategist, hasStrategist);

        private GameObject NewObject(string name)
        {
            var obj = new GameObject(name);
            obj.SetActive(false);
            _objects.Add(obj);
            return obj;
        }

        private StatManager CreatePlayer(string name)
        {
            GameObject obj = NewObject(name);
            obj.AddComponent<NetworkIdentity>();
            StatManager stats = EditorTestLifecycle.AddNetwork<StatManager>(obj);
            obj.AddComponent<HealthSystem>();
            EditorTestLifecycle.BindNetwork(obj);
            return stats;
        }

        private StatCustomizerController CreateCustomizer()
        {
            GameObject obj = NewObject("Canvas_Customizer");
            StatCustomizerController customizer = obj.AddComponent<StatCustomizerController>();
            foreach (string field in new[] { "_str", "_con", "_agi", "_def" })
            {
                GameObject row = new GameObject(field, typeof(RectTransform));
                row.transform.SetParent(obj.transform, false);
                Slider slider = row.AddComponent<Slider>();
                slider.maxValue = 30f;
                slider.wholeNumbers = true;
                StatSlider statSlider = row.AddComponent<StatSlider>();
                Set(statSlider, "_slider", slider);
                Set(customizer, field, statSlider);
            }
            Invoke(customizer, "Awake");
            return customizer;
        }

        private LobbyUIManager CreateLobby()
        {
            GameObject obj = NewObject("Lobby binding fixture");
            LobbyUIManager lobby = obj.AddComponent<LobbyUIManager>();
            GameObject panel = new GameObject("Lobby_UI", typeof(RectTransform));
            panel.transform.SetParent(obj.transform, false);
            Set(lobby, "_lobby_UI", panel);
            GameObject customizer = new GameObject("Canvas_Customizer");
            customizer.transform.SetParent(obj.transform, false);
            Set(lobby, "_canvas_Customizer", customizer);
            return lobby;
        }

        private static StatContainer Preset(float str, float con, float agi, float def) => new StatContainer
        {
            STR = new StatSlot { Invested = str }, CON = new StatSlot { Invested = con },
            AGI = new StatSlot { Invested = agi }, DEF = new StatSlot { Invested = def }
        };

        private static void SetPending(StatManager stats, uint id, double deadline, Action<bool, string> callback)
        {
            Set(stats, "_pendingApplyRequestId", id);
            Set(stats, "_pendingApplyDeadline", deadline);
            Set(stats, "_pendingApplyCallback", callback);
        }

        private void IsolateStatic(Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, PrivateStatic);
            _previousStatics.Add(field, field.GetValue(null));
            field.SetValue(null, value);
        }
        private static void SetStatic(Type type, string name, object value) => type.GetField(name, PrivateStatic).SetValue(null, value);
        private static void SetLocal(StatManager stats) => typeof(StatManager).GetMethod("SetLocal", PrivateStatic).Invoke(null, new object[] { stats });
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
        private static int SubscriberCount(object publisher, string eventName, object subscriber)
        {
            Delegate handlers = Get<Delegate>(publisher, eventName);
            int count = 0;
            if (handlers != null)
                foreach (Delegate handler in handlers.GetInvocationList())
                    if (ReferenceEquals(handler.Target, subscriber)) count++;
            return count;
        }
    }
}
