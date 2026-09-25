using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Networking;
using NUnit.Framework;
using UnityEngine;
using RoomInfo = BattlePvp.Networking.PlayFabBattleManager.RoomInfo;

namespace BattlePvp.EditorTests
{
    public sealed class RoomAvailabilityTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _owner;
        private PlayFabBattleManager _manager;

        [SetUp]
        public void SetUp()
        {
            // Keep Awake/Update/room flow startup inactive: these tests invoke local completion seams only.
            _owner = new GameObject("Room availability regression");
            _owner.SetActive(false);
            _manager = _owner.AddComponent<PlayFabBattleManager>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_owner);
        }

        [Test]
        public void AuthoritativeReplacementRemovesMissingRoomsAndEmptyClearsEveryCache()
        {
            Invoke("ReplaceKnownRoomInfos", Rooms("old"));
            Invoke("ReplaceKnownRoomInfos", Rooms("current"));
            Assert.That(Cached.Count, Is.EqualTo(1));
            Assert.That(Cached.ContainsKey("old"), Is.False);
            Assert.That(Cached["current"].RoomName, Is.EqualTo("current room"));
            Assert.That(Cached["current"].MasterName, Is.EqualTo("Host"));
            Assert.That(Cached["current"].PlayerCount, Is.EqualTo(2));
            Assert.That(Cached["current"].RelayJoinCode, Is.EqualTo("ABCDEF"));
            Invoke("ReplaceKnownRoomInfos", new Dictionary<string, RoomInfo>());
            AssertEveryCacheEmpty();
        }

        [Test]
        public void StaleAndDuplicateResponsesCannotCompleteTheCurrentRequest()
        {
            Invoke("ReplaceKnownRoomInfos", Rooms("old"));
            int calls = 0;
            BeginRequest(2, _ => calls++);
            Complete(1, Rooms("stale"));
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.True);
            Assert.That(Pending.Count, Is.EqualTo(1));
            Assert.That(calls, Is.Zero);
            Assert.That(Cached.ContainsKey("old"), Is.True);
            Complete(2, Rooms("current"));
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.False);
            Assert.That(Pending, Is.Empty);
            Assert.That(calls, Is.EqualTo(1));
            Complete(2, Rooms("duplicate"));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(Cached.Count, Is.EqualTo(1));
            Assert.That(Cached.ContainsKey("current"), Is.True);
        }

        [Test]
        public void RequestTimeoutCompletesEmptyAtTheBoundaryAndRejectsItsLateResponse()
        {
            Invoke("ReplaceKnownRoomInfos", Rooms("old"));
            int calls = 0;
            Dictionary<string, RoomInfo> received = null;
            BeginRequest(1, rooms => { calls++; received = rooms; });
            double started = Get<double>("_roomInfoRequestStarted");
            double timeout = (double)typeof(PlayFabBattleManager).GetField("RoomInfoRequestTimeoutSeconds",
                BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            Invoke("ExpireRoomInfoRequest", started + timeout - 0.001d);
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.True);
            Assert.That(calls, Is.Zero);
            Invoke("ExpireRoomInfoRequest", started + timeout);
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.False);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(received, Is.Empty);
            AssertEveryCacheEmpty();
            Invoke("ExpireRoomInfoRequest", started + timeout + 1d);
            Complete(1, Rooms("late"));
            Assert.That(calls, Is.EqualTo(1));
            AssertEveryCacheEmpty();

            BeginRequest(2, _ => calls++);
            Complete(1, Rooms("late"));
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.True);
            Assert.That(Pending.Count, Is.EqualTo(1));
            Complete(2, Rooms("new"));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(Cached.ContainsKey("new"), Is.True);
        }

        [Test]
        public void LateResponseBeforeTheNextUpdateStillCompletesAsEmpty()
        {
            Invoke("ReplaceKnownRoomInfos", Rooms("old"));
            Dictionary<string, RoomInfo> received = null;
            BeginRequest(1, rooms => received = rooms);
            Set("_roomInfoRequestStarted", Time.realtimeSinceStartupAsDouble - 11d);
            Complete(1, Rooms("arrived after deadline"));
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.False);
            Assert.That(received, Is.Empty);
            AssertEveryCacheEmpty();
        }

        [Test]
        public void ListenerAndCallbackMutationCannotCorruptTheCacheOrFollowingCallbacks()
        {
            _manager.OnRoomInfoListLoaded += rooms => rooms.Clear();
            int firstCount = -1;
            Dictionary<string, RoomInfo> second = null;
            BeginRequest(1, rooms => { firstCount = rooms.Count; rooms.Clear(); });
            Pending.Add(rooms => second = rooms);
            var response = Rooms("current");
            Complete(1, response);
            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(second.ContainsKey("current"), Is.True);
            second.Clear();
            response.Clear();
            Assert.That(Cached.Count, Is.EqualTo(1));
            Assert.That(Cached.ContainsKey("current"), Is.True);
        }

        [Test]
        public void ReentrantNewerCompletionKeepsItsCacheWhileOlderCallbacksReceiveTheirOwnSnapshot()
        {
            int newerCalls = 0;
            Dictionary<string, RoomInfo> olderSecond = null;
            BeginRequest(1, _ =>
            {
                BeginRequest(2, rooms => newerCalls += rooms.ContainsKey("newer") ? 1 : 100);
                Complete(2, Rooms("newer"));
            });
            Pending.Add(rooms => olderSecond = rooms);
            Complete(1, Rooms("older"));
            Assert.That(newerCalls, Is.EqualTo(1));
            Assert.That(olderSecond.ContainsKey("older"), Is.True);
            Assert.That(Cached.Count, Is.EqualTo(1));
            Assert.That(Cached.ContainsKey("newer"), Is.True);
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.False);
            Assert.That(Pending, Is.Empty);
        }

        [Test]
        public void NewRequestQueuedFromACompletionIsNotDrainedByTheOlderCallbackLoop()
        {
            int oldCalls = 0, newCalls = 0;
            BeginRequest(1, _ => BeginRequest(2, rooms => newCalls++));
            Pending.Add(_ => oldCalls++);
            Complete(1, Rooms("older"));
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(newCalls, Is.Zero);
            Assert.That(Pending.Count, Is.EqualTo(1));
            Assert.That(Get<bool>("_isRoomInfoRequestInFlight"), Is.True);
            Complete(1, Rooms("stale"));
            Assert.That(Pending.Count, Is.EqualTo(1));
            Complete(2, Rooms("newer"));
            Assert.That(newCalls, Is.EqualTo(1));
            Assert.That(Cached.ContainsKey("newer"), Is.True);
        }

        [Test]
        public void DisposedServiceRejectsCallbacksWithoutRestoringCachedRooms()
        {
            int calls = 0;
            BeginRequest(1, _ => calls++);
            Set("_roomServiceDisposed", true);
            Complete(1, Rooms("late"));
            Assert.That(calls, Is.Zero);
            AssertEveryCacheEmpty();
        }

        [Test]
        public void FreshCacheFiltersExpiredLeasesAndAnAuthoritativeEmptyListRemainsCacheable()
        {
            var response = Rooms("live");
            response["expired"] = new RoomInfo("Expired", "Host", 2, "OLD", Time.realtimeSinceStartupAsDouble - 1d);
            Invoke("ReplaceKnownRoomInfos", response);
            Snapshot.TryComplete(Snapshot.Revision, Time.realtimeSinceStartupAsDouble);
            Dictionary<string, RoomInfo> received = null;
            Action<Dictionary<string, RoomInfo>> callback = rooms => received = rooms;
            Assert.That((bool)Invoke("TryReturnCachedRoomInfos", callback), Is.True);
            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(received.ContainsKey("live"), Is.True);
            received.Clear();
            Assert.That(Cached.Count, Is.EqualTo(2), "Consumers must receive an isolated cache snapshot.");
            Invoke("ReplaceKnownRoomInfos", new Dictionary<string, RoomInfo>());
            Assert.That((bool)Invoke("TryReturnCachedRoomInfos", callback), Is.True);
            Assert.That(received, Is.Empty, "A confirmed empty list must not force a cache fallback or immediate repeat request.");
            Snapshot.TryComplete(Snapshot.Revision, Time.realtimeSinceStartupAsDouble - 4d);
            received = null;
            Assert.That((bool)Invoke("TryReturnCachedRoomInfos", callback), Is.False);
            Assert.That(received, Is.Null);
        }

        private Dictionary<string, RoomInfo> Cached => Get<Dictionary<string, RoomInfo>>("_lastLoadedRoomInfos");
        private RoomListSnapshotState Snapshot => Get<RoomListSnapshotState>("_roomListSnapshot");
        private List<Action<Dictionary<string, RoomInfo>>> Pending =>
            Get<List<Action<Dictionary<string, RoomInfo>>>>("_pendingRoomInfoCallbacks");

        private void BeginRequest(uint request, Action<Dictionary<string, RoomInfo>> callback)
        {
            Set("_roomInfoRequest", request);
            Set("_roomInfoRequestRevision", Snapshot.Revision);
            Set("_roomInfoRequestStarted", Time.realtimeSinceStartupAsDouble);
            Set("_isRoomInfoRequestInFlight", true);
            Pending.Add(callback);
        }

        private static Dictionary<string, RoomInfo> Rooms(string key) => new Dictionary<string, RoomInfo>
        {
            { key, new RoomInfo(key + " room", "Host", 2, "ABCDEF", Time.realtimeSinceStartupAsDouble + 60d) }
        };

        private void Complete(uint request, Dictionary<string, RoomInfo> rooms) => Invoke("CompleteRoomInfoRequest", request, rooms);
        private void AssertEveryCacheEmpty()
        {
            Assert.That(Cached, Is.Empty);
        }
        private object Invoke(string name, params object[] args) =>
            typeof(PlayFabBattleManager).GetMethod(name, PrivateInstance).Invoke(_manager, args);
        private void Set(string name, object value) =>
            typeof(PlayFabBattleManager).GetField(name, PrivateInstance).SetValue(_manager, value);
        private T Get<T>(string name) =>
            (T)typeof(PlayFabBattleManager).GetField(name, PrivateInstance).GetValue(_manager);
    }
}
