using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.EditorTests
{
    public sealed class ConnectedRosterUiTests
    {
        private readonly List<ScoreSystem> _previousScores = new List<ScoreSystem>();
        private Scene _previousScene;
        private Scene _testScene;

        [SetUp]
        public void SetUp()
        {
            _previousScores.AddRange(ScoreSystem.ActiveScores);
            ScoreSystem.ActiveScores.Clear();
            _previousScene = SceneManager.GetActiveScene();
            _testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(_testScene);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _testScene.GetRootGameObjects()) Object.DestroyImmediate(root);
            ScoreSystem.ActiveScores.Clear();
            ScoreSystem.ActiveScores.AddRange(_previousScores);
            _previousScores.Clear();
            if (_previousScene.IsValid() && _previousScene.isLoaded) SceneManager.SetActiveScene(_previousScene);
            EditorSceneManager.CloseScene(_testScene, true);
        }

        [Test]
        public void BannerTracksJoinLeaveAndZeroWithoutAcceptingStaleMetadataCount()
        {
            BattleRoomInfoBanner banner = CreateBanner(out TextMeshProUGUI count);
            banner.gameObject.SetActive(true);
            Assert.That(count.text, Is.EqualTo("Players: 0"));
            ScoreSystem first = CreatePlayer(1, "First", 10);
            ScoreSystem second = CreatePlayer(2, "Second", 5);
            first.OnStartClient();
            second.OnStartClient();
            second.OnStartClient();
            Assert.That(count.text, Is.EqualTo("Players: 2"));

            ApplyMetadata(banner, new PlayFabBattleManager.RoomInfo("Current room", "Host", 8));
            Assert.That(count.text, Is.EqualTo("Players: 2"));
            Assert.That(GetField<TextMeshProUGUI>(banner, "_roomNameText").text, Is.EqualTo("Room: Current room"));
            Assert.That(GetField<TextMeshProUGUI>(banner, "_masterNameText").text, Is.EqualTo("Master: Host"));
            first.OnStopClient();
            Assert.That(count.text, Is.EqualTo("Players: 1"));
            second.OnStopClient();
            Assert.That(count.text, Is.EqualTo("Players: 0"));
            ApplyMetadata(banner, new PlayFabBattleManager.RoomInfo("Current room", "Host", 8));
            Assert.That(count.text, Is.EqualTo("Players: 0"), "An empty connected roster must not fall back to stored room membership.");
        }

        [Test]
        public void BannerLateEnableReconnectAndSceneTransitionUseTheCurrentRoster()
        {
            BattleRoomInfoBanner banner = CreateBanner(out TextMeshProUGUI count);
            ScoreSystem first = CreatePlayer(1, "Returning player", 10);
            first.OnStartClient();
            banner.gameObject.SetActive(true);
            Assert.That(count.text, Is.EqualTo("Players: 1"));
            banner.gameObject.SetActive(false);
            first.OnStopClient();
            Assert.That(count.text, Is.EqualTo("Players: 1"), "A disabled view must have released its roster subscription.");
            banner.gameObject.SetActive(true);
            Assert.That(count.text, Is.EqualTo("Players: 0"), "Reactivation must read changes missed while disabled.");
            ScoreSystem replacement = CreatePlayer(20, "Returning player", 10);
            replacement.OnStartClient();
            Assert.That(count.text, Is.EqualTo("Players: 1"));

            RoomBannerRequestState state = GetField<RoomBannerRequestState>(banner, "_metadataRequests");
            uint previousRequest = state.BeginRequest("previous-room");
            Invoke(banner, "OnSceneLoaded", _testScene, LoadSceneMode.Single);
            Assert.That(state.IsCurrent(previousRequest, "previous-room"), Is.False);
            Assert.That(count.text, Is.EqualTo("Players: 1"));
        }

        [Test]
        public void BannerAndRankingDeduplicateNetworkIdsAndSkipInvalidRows()
        {
            BattleRoomInfoBanner banner = CreateBanner(out TextMeshProUGUI count);
            RankingUIManager ranking = CreateRanking();
            ScoreSystem valid = CreatePlayer(1, "Player", 2);
            ScoreSystem sameId = CreatePlayer(1, "Duplicate object", 5);
            ScoreSystem unspawned = CreatePlayer(0, "Not connected", 100);
            ScoreSystem destroyed = CreatePlayer(8, "Destroyed", 100);
            ScoreSystem.ActiveScores.AddRange(new[] { valid, valid, sameId, unspawned, destroyed, null });
            Object.DestroyImmediate(destroyed.gameObject);
            banner.gameObject.SetActive(true);
            ranking.gameObject.SetActive(true);
            Assert.That(count.text, Is.EqualTo("Players: 1"));
            Assert.That(VisibleRows(ranking).Count, Is.EqualTo(1));
        }

        [Test]
        public void RankingTracksLateEnableDepartureLastZeroAndReconnectWithReusedRows()
        {
            RankingUIManager ranking = CreateRanking();
            ScoreSystem first = CreatePlayer(1, "Leader", 10);
            ScoreSystem second = CreatePlayer(2, "Second", 5);
            first.OnStartClient();
            second.OnStartClient();
            ranking.gameObject.SetActive(true);
            List<RankingEntryUI> rows = VisibleRows(ranking);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(RowText(rows[0], "Name"), Is.EqualTo("Leader"));
            Assert.That(RowText(rows[1], "Rank"), Is.EqualTo("2"));
            int allocatedRows = ranking.rankingContainer.childCount;
            first.OnStopClient();
            rows = VisibleRows(ranking);
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(RowText(rows[0], "Name"), Is.EqualTo("Second"));
            Assert.That(RowText(rows[0], "Rank"), Is.EqualTo("1"));
            second.OnStopClient();
            Assert.That(VisibleRows(ranking), Is.Empty);

            ranking.gameObject.SetActive(false);
            ScoreSystem returning = CreatePlayer(30, "Leader", 10);
            returning.OnStartClient();
            Assert.That(VisibleRows(ranking), Is.Empty);
            ranking.gameObject.SetActive(true);
            rows = VisibleRows(ranking);
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(RowText(rows[0], "Name"), Is.EqualTo("Leader"));
            Assert.That(RowText(rows[0], "Score"), Is.EqualTo("10"));
            Assert.That(ranking.rankingContainer.childCount, Is.EqualTo(allocatedRows));
        }

        [Test]
        public void MetadataRequestsRejectOlderResponsesAndRoomSwitches()
        {
            var state = new RoomBannerRequestState();
            state.SetActive(true);
            uint old = state.BeginRequest("room-a");
            uint latest = state.BeginRequest("room-a");
            Assert.That(state.IsCurrent(old, "room-a"), Is.False);
            Assert.That(state.IsCurrent(latest, "room-a"), Is.True);
            Assert.That(state.IsCurrent(latest, "room-b"), Is.False);
            uint nextRoom = state.BeginRequest("room-b");
            Assert.That(state.IsCurrent(latest, "room-a"), Is.False);
            Assert.That(state.IsCurrent(nextRoom, "room-b"), Is.True);
        }

        [Test]
        public void MetadataRequestsRejectPreviousActivationEvenWhenTheRoomIsUnchanged()
        {
            var state = new RoomBannerRequestState();
            state.SetActive(true);
            uint request = state.BeginRequest("room-a");
            state.SetActive(false);
            Assert.That(state.IsCurrent(request, "room-a"), Is.False);
            state.SetActive(true);
            Assert.That(state.IsCurrent(request, "room-a"), Is.False);
            Assert.That(state.IsCurrent(state.BeginRequest("room-a"), "room-a"), Is.True);
        }

        private static BattleRoomInfoBanner CreateBanner(out TextMeshProUGUI count)
        {
            var root = new GameObject("Roster banner test", typeof(RectTransform));
            root.SetActive(false);
            BattleRoomInfoBanner banner = root.AddComponent<BattleRoomInfoBanner>();
            SetField(banner, "_roomNameText", CreateText(root.transform, "Room"));
            count = CreateText(root.transform, "Count");
            SetField(banner, "_playerCountText", count);
            SetField(banner, "_masterNameText", CreateText(root.transform, "Master"));
            return banner;
        }

        private static RankingUIManager CreateRanking()
        {
            var root = new GameObject("Roster ranking test", typeof(RectTransform));
            root.SetActive(false);
            RankingUIManager ranking = root.AddComponent<RankingUIManager>();
            var container = new GameObject("Entries", typeof(RectTransform));
            container.transform.SetParent(root.transform);
            ranking.rankingContainer = container.transform;
            var prefab = new GameObject("Ranking template", typeof(RectTransform));
            prefab.SetActive(false);
            RankingEntryUI entry = prefab.AddComponent<RankingEntryUI>();
            SetField(entry, "rankText", CreateText(prefab.transform, "Rank"));
            SetField(entry, "nameText", CreateText(prefab.transform, "Name"));
            SetField(entry, "scoreText", CreateText(prefab.transform, "Score"));
            SetField(entry, "deathText", CreateText(prefab.transform, "Deaths"));
            ranking.rankingEntryPrefab = prefab;
            return ranking;
        }

        private static ScoreSystem CreatePlayer(uint netId, string name, int points)
        {
            var player = new GameObject(name);
            player.SetActive(false);
            NetworkIdentity identity = player.AddComponent<NetworkIdentity>();
            player.AddComponent<PlayerManager>();
            ScoreSystem score = player.AddComponent<ScoreSystem>();
            // Emulate Mirror's binding before its lifecycle callbacks, without starting networking.
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId)).SetValue(identity, netId);
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(score, identity);
            score.PlayerName = name;
            score.CurrentPoints = points;
            return score;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent);
            return text;
        }

        private static List<RankingEntryUI> VisibleRows(RankingUIManager ranking)
        {
            var rows = new List<RankingEntryUI>();
            foreach (RankingEntryUI row in ranking.rankingContainer.GetComponentsInChildren<RankingEntryUI>(true))
                if (row.gameObject.activeSelf) rows.Add(row);
            return rows;
        }

        private static string RowText(RankingEntryUI row, string name) =>
            row.transform.Find(name).GetComponent<TextMeshProUGUI>().text;

        private static void ApplyMetadata(BattleRoomInfoBanner banner, PlayFabBattleManager.RoomInfo info) =>
            typeof(BattleRoomInfoBanner).GetMethod("SetInfo", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(PlayFabBattleManager.RoomInfo) }, null).Invoke(banner, new object[] { info });

        private static void Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
