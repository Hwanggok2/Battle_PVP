using System;
using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.UI;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class RoomAndRankingTests
    {
        private static readonly Func<int, int> PointsOf = points => points;
        private readonly List<string> _removed = new List<string>();
        private readonly List<string> _changed = new List<string>();

        [Test]
        public void TiedWinnersAndRewardsUseCompetitionRanksRegardlessOfInputOrder()
        {
            var xp = new SimpleXpDistributor();
            int[] first = { 12, 12, 5, 3, 3 };
            int[] shuffled = { 3, 12, 3, 5, 12 };
            foreach (int points in first)
            {
                int firstRank = CompetitionRanking.GetRank(points, first, PointsOf);
                int otherRank = CompetitionRanking.GetRank(points, shuffled, PointsOf);
                Assert.That(firstRank, Is.EqualTo(otherRank));
                Assert.That(xp.CalculateXp(firstRank, points), Is.EqualTo(xp.CalculateXp(otherRank, points)));
            }
            Assert.That(CompetitionRanking.GetRank(12, first, PointsOf), Is.EqualTo(1));
            Assert.That(CompetitionRanking.GetRank(5, first, PointsOf), Is.EqualTo(3));
            Assert.That(CompetitionRanking.GetRank(3, first, PointsOf), Is.EqualTo(4));
            Assert.That(xp.CalculateXp(CompetitionRanking.GetRank(12, first, PointsOf), 12), Is.EqualTo(220));
        }

        [Test]
        public void ZeroScoreMatchSharesFirstPlaceAndEmptyMatchHasNoRank()
        {
            Assert.That(CompetitionRanking.GetRank(0, new[] { 0, 0, 0 }, PointsOf), Is.EqualTo(1));
            Assert.That(CompetitionRanking.GetRank(0, Array.Empty<int>(), PointsOf), Is.Zero);
        }

        [Test]
        public void LatestRoomResponseWinsAndOlderResponseCannotAddRows()
        {
            var state = new RoomListState();
            state.SetActive(true);
            uint oldRequest = state.BeginRequest();
            uint latest = state.BeginRequest();
            Assert.That(state.Apply(latest, Rooms("new", "Newest", 2), _removed, _changed), Is.True);
            Assert.That(state.Apply(oldRequest, Rooms("old", "Stale", 1), _removed, _changed), Is.False);
            Assert.That(state.Rooms.Count, Is.EqualTo(1));
            Assert.That(state.Rooms.ContainsKey("new"), Is.True);
            Assert.That(state.Rooms.ContainsKey("old"), Is.False);
        }

        [Test]
        public void DisabledViewAndReenabledViewRejectPriorRequests()
        {
            var state = new RoomListState();
            state.SetActive(true);
            uint request = state.BeginRequest();
            state.SetActive(false);
            Assert.That(state.Apply(request, Rooms("room", "Title", 1), _removed, _changed), Is.False);
            state.SetActive(true);
            Assert.That(state.Apply(request, Rooms("room", "Title", 1), _removed, _changed), Is.False);
            Assert.That(state.Apply(state.BeginRequest(), Rooms("room", "Title", 1), _removed, _changed), Is.True);
        }

        [Test]
        public void IdenticalRoomResponsesDoNotRequestAnyUiChanges()
        {
            var state = new RoomListState();
            state.SetActive(true);
            state.Apply(state.BeginRequest(), Rooms("room", "Title", 1), _removed, _changed);
            Assert.That(_changed, Is.EqualTo(new[] { "room" }));
            state.Apply(state.BeginRequest(), Rooms("room", "Title", 1), _removed, _changed);
            Assert.That(_changed, Is.Empty);
            Assert.That(_removed, Is.Empty);
            Assert.That(state.Rooms.Count, Is.EqualTo(1));
        }

        [Test]
        public void ChangedMetadataUpdatesSameIdAndDeletionOnlyRemovesMissingId()
        {
            var state = new RoomListState();
            state.SetActive(true);
            var rooms = Rooms("a", "Original", 1);
            rooms["b"] = new PlayFabBattleManager.RoomInfo("Other", "Host", 3);
            state.Apply(state.BeginRequest(), rooms, _removed, _changed);
            state.Apply(state.BeginRequest(), Rooms("a", "Renamed", 5), _removed, _changed);
            Assert.That(_changed, Is.EqualTo(new[] { "a" }));
            Assert.That(_removed, Is.EqualTo(new[] { "b" }));
            Assert.That(state.Rooms["a"].PlayerCount, Is.EqualTo(5));
            Assert.That(state.Rooms.Count, Is.EqualTo(1));
        }

        [Test]
        public void RoomOrderingIsStableForDuplicateTitlesAndSkipsEmptyIds()
        {
            var state = new RoomListState();
            state.SetActive(true);
            var rooms = Rooms("b", "Same", 1);
            rooms["a"] = new PlayFabBattleManager.RoomInfo("Same", "Host", 2);
            rooms[" "] = new PlayFabBattleManager.RoomInfo("Hidden", "Host", 1);
            state.Apply(state.BeginRequest(), rooms, _removed, _changed);
            var ids = new List<string>();
            state.CopyOrderedIds(ids);
            Assert.That(ids, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void ResultTextCanBeBuiltWithoutSceneObjectsAndPreservesAllFields()
        {
            var result = new PersonalBattleResult("Player", 1, "Player, Ally", -4f, 12.26f,
                null, -1, "Enemy", 3);
            var labels = new BattleResultLabels
            {
                NicknamePrefix = "Name: ", RankPrefix = "Rank: ", DamageTakenPrefix = "Taken: ",
                DamageDealtPrefix = "Dealt: ", WinnerPrefix = "Winners: ",
                MostKilledByPrefix = "By: ", MostKilledPrefix = "Against: ", RestartPrompt = "Restart"
            };
            string text = BattleResultText.Build(result, labels);
            Assert.That(text, Does.Contain("Name: Player\nRank: 1"));
            Assert.That(text, Does.Contain("Taken: 0\nDealt: 12.3"));
            Assert.That(text, Does.Contain("Winners: Player, Ally"));
            Assert.That(text, Does.Contain("By: None (0)\nAgainst: Enemy (3)\n\nRestart"));
            Assert.That(BattleResultText.FormatDamage(float.NaN), Is.EqualTo("0"));
        }

        private static Dictionary<string, PlayFabBattleManager.RoomInfo> Rooms(string id, string title, int count) =>
            new Dictionary<string, PlayFabBattleManager.RoomInfo>
            {
                { id, new PlayFabBattleManager.RoomInfo(title, "Host", count) }
            };
    }
}
