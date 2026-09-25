using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Networking;
using NUnit.Framework;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using UnityEngine.TestTools;
using RoomInfo = BattlePvp.Networking.PlayFabBattleManager.RoomInfo;

namespace BattlePvp.EditorTests
{
    public sealed class RoomSnapshotMutationTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const string Room = "battle_a10_0123456789abcdef0123456789abcdef";
        private readonly List<Action<ExecuteCloudScriptResult>> _listResponses = new List<Action<ExecuteCloudScriptResult>>();
        private readonly List<Action<PlayFabError>> _listFailures = new List<Action<PlayFabError>>();
        private readonly List<object> _flows = new List<object>();
        private GameObject _root;
        private PlayFabBattleManager _manager;
        private bool _adminFails;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Room snapshot mutation fixture");
            _root.SetActive(false);
            _manager = _root.AddComponent<PlayFabBattleManager>();
            Set("_executeCloudScript", (Action<ExecuteCloudScriptRequest, Action<ExecuteCloudScriptResult>, Action<PlayFabError>>)Respond);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (object flow in _flows) flow.GetType().GetMethod("Cancel").Invoke(flow, null);
            _flows.Clear();
            _listResponses.Clear();
            _listFailures.Clear();
            _adminFails = false;
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConfirmedAdminDeletionCannotBeUndoneByAnOlderPendingList(bool clearAll)
        {
            Seed();
            Snapshot.Invalidate();
            int completions = 0;
            Dictionary<string, RoomInfo> received = null;
            _manager.GetActiveRoomInfos(rooms => { completions++; received = rooms; });
            Assert.That(_listResponses.Count, Is.EqualTo(1));
            bool? deleted = null;
            if (clearAll) _manager.AdminClearRoomRegistry("fixture-key", (ok, _) => deleted = ok);
            else _manager.AdminDeleteRoom("fixture-key", "  " + Room + "  ", (ok, _) => deleted = ok);
            Assert.That(deleted, Is.True);
            Assert.That(Cached, Is.Empty);
            _listResponses[0](ListResponse());
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(received, Is.Empty);
            Assert.That(Cached, Is.Empty);
            Assert.That(Snapshot.CanReuse(Time.realtimeSinceStartupAsDouble), Is.False,
                "An ignored old list must not start another three-second cache period.");
            _manager.GetActiveRoomInfos(rooms => received = rooms);
            Assert.That(_listResponses.Count, Is.EqualTo(2));
            _listResponses[1](ListResponse(empty: true));
            Assert.That(received, Is.Empty);
            Assert.That(Snapshot.CanReuse(Time.realtimeSinceStartupAsDouble), Is.True);
        }

        [Test]
        public void RejectedAdminDeleteKeepsTheConfirmedSnapshot()
        {
            Seed();
            ulong revision = Snapshot.Revision;
            _adminFails = true;
            LogAssert.Expect(LogType.Error, "[PlayFab] AdminDeleteRoom failed: Denied: fixture");
            bool? completed = null;
            _manager.AdminDeleteRoom("fixture-key", Room, (ok, _) => completed = ok);
            Assert.That(completed, Is.False);
            Assert.That(Cached.ContainsKey(Room), Is.True);
            Assert.That(Snapshot.Revision, Is.EqualTo(revision));
        }

        [TestCase("timeout")]
        [TestCase("failure")]
        [TestCase("late response")]
        public void UnconfirmedListingHidesAllRoomsEvenAfterAMutationChangedTheRevision(string outcome)
        {
            Seed();
            Snapshot.Invalidate();
            int completions = 0;
            Dictionary<string, RoomInfo> received = null;
            _manager.GetActiveRoomInfos(rooms => { completions++; received = rooms; });
            Invoke("UpdateListedRoom", Room, new RoomInfo("Changed", "Host", 2, "NEW"));
            if (outcome == "timeout")
                Invoke("ExpireRoomInfoRequest", Get<double>("_roomInfoRequestStarted") + 10d);
            else if (outcome == "failure") _listFailures[0](new PlayFabError());
            else
            {
                Set("_roomInfoRequestStarted", Time.realtimeSinceStartupAsDouble - 11d);
                _listResponses[0](ListResponse());
            }
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(received, Is.Empty);
            Assert.That(Cached, Is.Empty);
            Assert.That(Snapshot.CanReuse(Time.realtimeSinceStartupAsDouble), Is.True,
                "Preserve the existing short empty-cache period after a failed listing.");
            _listResponses[0](ListResponse());
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(Cached, Is.Empty);
        }

        [TestCase(false, 2)]
        [TestCase(false, 0)]
        [TestCase(true, 2)]
        public void CleanupUpdatesAllListedMetadataAndNeverResurrectsAnUnlistedRoom(bool hostClosed, int count)
        {
            Seed();
            double deadline = Cached[Room].ValidUntil;
            Snapshot.Invalidate();
            Dictionary<string, RoomInfo> received = null;
            _manager.GetActiveRoomInfos(rooms => received = rooms);
            Invoke("ApplyRoomCleanupToList", Room, count, hostClosed);
            _listResponses[0](ListResponse());
            if (hostClosed || count == 0)
            {
                Assert.That(Cached, Is.Empty);
                Assert.That(received, Is.Empty);
            }
            else
            {
                Assert.That(received[Room].PlayerCount, Is.EqualTo(count));
                Assert.That(Cached[Room].RoomName, Is.EqualTo("Listed"));
                Assert.That(Cached[Room].MasterName, Is.EqualTo("Host"));
                Assert.That(Cached[Room].RelayJoinCode, Is.EqualTo("LISTED"));
                Assert.That(Cached[Room].ValidUntil, Is.EqualTo(deadline));
            }
            Invoke("ReplaceKnownRoomInfos", new Dictionary<string, RoomInfo>());
            Invoke("ApplyRoomCleanupToList", Room, 2, false);
            Assert.That(Cached, Is.Empty);
        }

        [Test]
        public void SessionMetadataUpdatesExistingEntriesWithoutGrantingANewListingLease()
        {
            Seed();
            double deadline = Cached[Room].ValidUntil;
            var session = new RoomInfo("Joined", "New host", 4, "NEW");
            Assert.That(double.IsPositiveInfinity(session.ValidUntil), Is.True);
            Invoke("UpdateListedRoom", Room, session);
            Assert.That(Cached[Room].RoomName, Is.EqualTo("Joined"));
            Assert.That(Cached[Room].MasterName, Is.EqualTo("New host"));
            Assert.That(Cached[Room].PlayerCount, Is.EqualTo(4));
            Assert.That(Cached[Room].RelayJoinCode, Is.EqualTo("NEW"));
            Assert.That(Cached[Room].ValidUntil, Is.EqualTo(deadline));
            Assert.That(Snapshot.CanReuse(Time.realtimeSinceStartupAsDouble), Is.False);
            Invoke("ReplaceKnownRoomInfos", new Dictionary<string, RoomInfo>());
            Invoke("UpdateListedRoom", Room, session);
            Assert.That(Cached, Is.Empty, "A newly created/joined session cannot create a discoverable list entry.");
        }

        [Test]
        public void EmptyAuthoritativeListPreservesCurrentSessionAndItsParserFallback()
        {
            var session = new RoomInfo("Current", "Current host", 3, "CURRENT");
            object flow = MakeFlow(session);
            Set("_currentRoomInfo", session);
            Set("_joinedRoomId", Room);
            Seed();
            Snapshot.Invalidate();
            _manager.GetActiveRoomInfos(_ => { });
            _listResponses[0](ListResponse(empty: true));
            Assert.That(Cached, Is.Empty);
            Assert.That(_manager.CurrentRoomId, Is.EqualTo(Room));
            Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Current"));
            var fallback = (RoomInfo)Invoke("ParseRoomInfoFromCloudScript", null, flow);
            Assert.That(fallback.RoomName, Is.EqualTo("Current"));
            Assert.That(fallback.MasterName, Is.EqualTo("Current host"));
            Assert.That(fallback.PlayerCount, Is.EqualTo(3));
            Assert.That(fallback.RelayJoinCode, Is.EqualTo("CURRENT"));
        }

        [Test]
        public void ParserUsesCurrentSessionBeforeSameRoomListAndResponseBeforeBoth()
        {
            Seed();
            object flow = MakeFlow(new RoomInfo("Current", "Current host", 4, "CURRENT"));
            var response = new Dictionary<string, object>
            { { "roomInfo", new Dictionary<string, object> { { "playerCount", 5 } } } };
            var parsed = (RoomInfo)Invoke("ParseRoomInfoFromCloudScript", response, flow);
            Assert.That(parsed.RoomName, Is.EqualTo("Current"));
            Assert.That(parsed.MasterName, Is.EqualTo("Current host"));
            Assert.That(parsed.PlayerCount, Is.EqualTo(5));
            Assert.That(parsed.RelayJoinCode, Is.EqualTo("CURRENT"));
            flow.GetType().GetField("Info").SetValue(flow, default(RoomInfo));
            parsed = (RoomInfo)Invoke("ParseRoomInfoFromCloudScript", null, flow);
            Assert.That(parsed.RoomName, Is.EqualTo("Listed"));
            Assert.That(parsed.RelayJoinCode, Is.EqualTo("LISTED"));
        }

        [TestCase("ttl")]
        [TestCase("lease")]
        [TestCase("infinity")]
        public void ParserDoesNotFallbackToAnExpiredOrUnverifiedListing(string expiry)
        {
            Seed();
            if (expiry == "ttl") Snapshot.TryComplete(Snapshot.Revision, Time.realtimeSinceStartupAsDouble - 4d);
            else Cached[Room] = new RoomInfo("Bad", "Bad", 7, "BAD",
                expiry == "lease" ? Time.realtimeSinceStartupAsDouble - 1d : double.PositiveInfinity);
            var fallback = (RoomInfo)Invoke("ParseRoomInfoFromCloudScript", null, MakeFlow(default));
            Assert.That(fallback.RoomName, Is.EqualTo("Unnamed Room"));
            Assert.That(fallback.MasterName, Is.EqualTo("Unknown"));
            Assert.That(fallback.PlayerCount, Is.EqualTo(1));
            Assert.That(fallback.RelayJoinCode, Is.Empty);
        }

        private object MakeFlow(RoomInfo info)
        {
            Type type = typeof(PlayFabBattleManager).GetNestedType("RoomFlow", BindingFlags.NonPublic);
            object flow = Activator.CreateInstance(type, new object[] { new RoomFlowGeneration().Begin("a10", Room) });
            type.GetField("Info").SetValue(flow, info);
            _flows.Add(flow);
            return flow;
        }

        private void Seed()
        {
            Invoke("ReplaceKnownRoomInfos", new Dictionary<string, RoomInfo>
            { { Room, new RoomInfo("Listed", "Host", 3, "LISTED", Time.realtimeSinceStartupAsDouble + 60d) } });
            Snapshot.TryComplete(Snapshot.Revision, Time.realtimeSinceStartupAsDouble);
        }

        private void Respond(ExecuteCloudScriptRequest request, Action<ExecuteCloudScriptResult> success, Action<PlayFabError> failure)
        {
            if (request.FunctionName == "GetActiveRoomInfos")
            { _listResponses.Add(success); _listFailures.Add(failure); return; }
            Assert.That(request.FunctionName, Is.EqualTo("AdminDeleteRoom").Or.EqualTo("AdminClearRoomRegistry"));
            success(_adminFails ? new ExecuteCloudScriptResult { Error = new ScriptExecutionError { Error = "Denied", Message = "fixture" } } :
                new ExecuteCloudScriptResult { FunctionResult = new Dictionary<string, object>() });
        }

        private static ExecuteCloudScriptResult ListResponse(bool empty = false) => new ExecuteCloudScriptResult
        { FunctionResult = new Dictionary<string, object> { { "roomInfos", empty ? new Dictionary<string, object>() : new Dictionary<string, object>
        { { Room, new Dictionary<string, object> { { "roomName", "Stale listed" }, { "masterName", "Host" }, { "playerCount", 8 },
            { "relayJoinCode", "OLD" }, { "serverNow", 1000d }, { "leaseExpiresAt", 61000d } } } } } } };
        private RoomListSnapshotState Snapshot => Get<RoomListSnapshotState>("_roomListSnapshot");
        private Dictionary<string, RoomInfo> Cached => Get<Dictionary<string, RoomInfo>>("_lastLoadedRoomInfos");
        private object Invoke(string name, params object[] args) => typeof(PlayFabBattleManager).GetMethod(name, Private).Invoke(_manager, args);
        private void Set(string name, object value) => typeof(PlayFabBattleManager).GetField(name, Private).SetValue(_manager, value);
        private T Get<T>(string name) => (T)typeof(PlayFabBattleManager).GetField(name, Private).GetValue(_manager);
    }
}
