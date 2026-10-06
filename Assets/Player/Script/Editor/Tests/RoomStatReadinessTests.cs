using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Logic;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class RoomStatReadinessTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> _objects = new();
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);

        [TearDown] public void Cleanup()
        {
            if (RoomStartNotice.Instance != null) RoomStartNotice.Instance.Close();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private NetworkConnectionToClient Player(int id, float allocation = 30, bool initialized = true)
        {
            var go = new GameObject("Readiness player " + id); _objects.Add(go);
            var stats = EditorTestLifecycle.AddNetwork<StatManager>(go);
            var value = new StatContainer(); value.STR.Invested = allocation;
            Set(stats, "_stats", value); Set(stats, "_serverStatsInitialized", initialized);
            var connection = new NetworkConnectionToClient(id) { isAuthenticated = true, isReady = true };
            typeof(NetworkConnection).GetProperty("identity").SetValue(connection, go.GetComponent<NetworkIdentity>());
            return connection;
        }

        [TestCase(0, false)] [TestCase(15, false)] [TestCase(29.5f, false)]
        [TestCase(30, true)] [TestCase(30.5f, false)] [TestCase(float.NaN, false)]
        public void CompletionRequiresExactlyTheValidInvestmentBudget(float allocation, bool expected)
        {
            var value = new StatContainer(); value.STR.Invested = allocation;
            Assert.That(StatValidation.IsCompletePreset(value), Is.EqualTo(expected));
        }

        [Test] public void EightPlayerReadinessRequiresEveryAuthenticatedInitializedAllocation()
        {
            var players = new List<NetworkConnectionToClient>();
            for (int i = 0; i < 8; i++) players.Add(Player(i));
            RoomStatReadiness.CountReady(players, out int ready, out int total);
            Assert.That(ready, Is.EqualTo(8)); Assert.That(total, Is.EqualTo(8));
            Set(players[7].identity.GetComponent<StatManager>(), "_stats", default(StatContainer));
            players[6].isAuthenticated = false;
            players[5].isReady = false;
            Set(players[4].identity.GetComponent<StatManager>(), "_serverStatsInitialized", false);
            typeof(NetworkConnection).GetProperty("identity").SetValue(players[3], null);
            RoomStatReadiness.CountReady(players, out ready, out total);
            Assert.That(ready, Is.EqualTo(3)); Assert.That(total, Is.EqualTo(8));
        }

        [TestCase(true)] [TestCase(false)]
        public void CountdownStopsIfAnUnallocatedPlayerJoinsOrAnAllocationIsReset(bool newJoin)
        {
            var scene = SceneManager.GetActiveScene(); string originalName = scene.name; scene.name = "Battle_waiting";
            var server = typeof(NetworkServer).GetProperty("active"); bool previousServer = NetworkServer.active;
            var connections = new Dictionary<int, NetworkConnectionToClient>(NetworkServer.connections);
            GameObject button = null;
            try
            {
                NetworkServer.connections.Clear(); NetworkServer.connections.Add(0, Player(0)); server.SetValue(null, true);
                button = new GameObject("Start", typeof(RectTransform), typeof(Button));
                var controller = button.AddComponent<BattleStartController>();
                EditorTestLifecycle.Invoke(controller, "Awake");
                Assert.That(typeof(BattleStartController).GetProperty("CanStart", Private).GetValue(controller), Is.True);
                var routine = (IEnumerator)typeof(BattleStartController).GetMethod("CoStartCountdown", Private).Invoke(controller, null);
                Assert.That(routine.MoveNext(), Is.True); Assert.That(BattleStartController.IsStarting, Is.True);
                if (newJoin) NetworkServer.connections.Add(1, Player(1, 0));
                else Set(NetworkServer.connections[0].identity.GetComponent<StatManager>(), "_stats", default(StatContainer));
                Assert.That(routine.MoveNext(), Is.False); Assert.That(BattleStartController.IsStarting, Is.False);
                Assert.That(typeof(BattleStartController).GetProperty("CanStart", Private).GetValue(controller), Is.False);
            }
            finally
            {
                if (button != null) Object.DestroyImmediate(button);
                server.SetValue(null, previousServer); NetworkServer.connections.Clear();
                foreach (var pair in connections) NetworkServer.connections.Add(pair.Key, pair.Value);
                scene.name = originalName;
            }
        }

        [Test] public void HostCanClickStartAndOnlyPendingNamesAreShownWithoutStartingCountdown()
        {
            var scene = SceneManager.GetActiveScene(); string originalName = scene.name; scene.name = "Battle_waiting";
            var server = typeof(NetworkServer).GetProperty("active"); bool previousServer = NetworkServer.active;
            var connections = new Dictionary<int, NetworkConnectionToClient>(NetworkServer.connections);
            GameObject button = null;
            try
            {
                NetworkServer.connections.Clear();
                var ready = Player(0); var pending = Player(1, 0); var partial = Player(2, 15);
                EditorTestLifecycle.AddNetwork<BattlePvp.Combat.ScoreSystem>(ready.identity.gameObject).PlayerName = "준비완료";
                EditorTestLifecycle.AddNetwork<BattlePvp.Combat.ScoreSystem>(pending.identity.gameObject).PlayerName = "미분배";
                EditorTestLifecycle.AddNetwork<BattlePvp.Combat.ScoreSystem>(partial.identity.gameObject).PlayerName = "<b>일부분배</b>";
                NetworkServer.connections.Add(0, ready); NetworkServer.connections.Add(1, pending); NetworkServer.connections.Add(2, partial);
                server.SetValue(null, true);
                button = new GameObject("Start", typeof(RectTransform), typeof(Button));
                var controller = button.AddComponent<BattleStartController>(); EditorTestLifecycle.Invoke(controller, "Awake");
                EditorTestLifecycle.Invoke(controller, "Update");
                Assert.That(button.GetComponent<Button>().interactable, Is.True, "Host can request a readiness explanation.");
                EditorTestLifecycle.Invoke(controller, "BeginCountdown");
                Assert.That(BattleStartController.IsStarting, Is.False);
                Assert.That(RoomStartNotice.IsOpen, Is.True);
                Assert.That(GameInputController.IsPaused, Is.True, "Popup must suspend FPS attacks/look.");
                var text = RoomStartNotice.Instance.transform.Find("Panel/Players Viewport/Players").GetComponent<TMPro.TMP_Text>();
                Assert.That(text.text, Does.Contain("미분배").And.Contain("<b>일부분배</b>").And.Not.Contain("준비완료"));
                Assert.That(text.richText, Is.False);
                var popup = RoomStartNotice.Instance;
                Set(partial.identity.GetComponent<StatManager>(), "_stats", ready.identity.GetComponent<StatManager>().GetStatsCopy());
                EditorTestLifecycle.Invoke(controller, "BeginCountdown");
                Assert.That(RoomStartNotice.Instance, Is.SameAs(popup));
                Assert.That(text.text, Does.Not.Contain("일부분배"));
                // The confirmation button closes the popup without changing readiness or starting.
                popup.transform.Find("Panel/Confirm").GetComponent<Button>().onClick.Invoke();
                Assert.That(RoomStartNotice.IsOpen, Is.False); Assert.That(BattleStartController.IsStarting, Is.False);
                NetworkServer.connections.Remove(1);
                Assert.That(RoomStatReadiness.GetPendingPlayerNames(), Is.Empty);
                Assert.That(typeof(BattleStartController).GetProperty("CanStart", Private).GetValue(controller), Is.True);
            }
            finally
            {
                if (RoomStartNotice.Instance != null) RoomStartNotice.Instance.Close();
                if (button != null) Object.DestroyImmediate(button);
                server.SetValue(null, previousServer); NetworkServer.connections.Clear();
                foreach (var pair in connections) NetworkServer.connections.Add(pair.Key, pair.Value);
                scene.name = originalName;
            }
        }
    }
}
