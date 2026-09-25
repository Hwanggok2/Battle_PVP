using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BattlePvp.Managers;
using BattlePvp.Networking;
using Mirror;
using NUnit.Framework;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using UnityEngine.TestTools;

namespace BattlePvp.EditorTests
{
    public sealed class RoomObserverIsolationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Room = "battle_abc123_11111111111111111111111111111111";
        private const string Warning = "[PlayFab] A room observer failed.";
        private readonly Dictionary<FieldInfo, object> _statics = new Dictionary<FieldInfo, object>();
        private readonly List<string> _requests = new List<string>();
        private PlayFabAuthenticationContext _previousAuthentication;
        private PlayFabBattleManager _manager;
        private GameObject _profile;
        private NetworkManager _previousNetworkManager;
        private TaskCompletionSource<bool> _relayBlock;

        [SetUp]
        public void SetUp()
        {
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            _previousNetworkManager = NetworkManager.singleton;
            typeof(NetworkManager).GetProperty(nameof(NetworkManager.singleton)).SetValue(null, null);
            Isolate(typeof(PlayFabBattleManager), "<Instance>k__BackingField", null);
            Isolate(typeof(PlayFabBattleManager), "InstanceChanged", null);
            Isolate(typeof(PlayFabBattleManager), "_sharedRoomLifetime", new RoomServiceLifetime());
            Isolate(typeof(GlobalDataManager), "_instance", null);
            Isolate(typeof(GlobalDataManager), "_applicationIsQuitting", false);
            _previousAuthentication = new PlayFabAuthenticationContext();
            _previousAuthentication.CopyFrom(PlayFabSettings.staticPlayer);
            PlayFabSettings.staticPlayer.ForgetAllCredentials();
            PlayFabSettings.staticPlayer.PlayFabId = "ABC123";
            PlayFabSettings.staticPlayer.ClientSessionTicket = "room-observer-test-session";
            _profile = new GameObject("Room observer profile fixture");
            _profile.SetActive(false);
            var profile = _profile.AddComponent<GlobalDataManager>();
            profile.PlayerNickname = "Fixture host";
            typeof(GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, profile);
            var root = new GameObject("Room observer fixture");
            root.SetActive(false);
            _manager = root.AddComponent<PlayFabBattleManager>();
            Set("_executeCloudScript", (Action<ExecuteCloudScriptRequest, Action<ExecuteCloudScriptResult>, Action<PlayFabError>>)Respond);
            _relayBlock = new TaskCompletionSource<bool>();
            // Exercise the actual startup queue without opening Relay or starting a transport.
            Get<RoomOperationQueue>("_relayPreparations").Enqueue("relay", () => _relayBlock.Task);
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string name in new[] { "OnRoomRegistryChanged", "OnRoomJoined", "OnRoomFlowStateChanged", "OnRoomInfoListLoaded", "OnRoomListLoaded" })
                Set(name, null);
            typeof(PlayFabBattleManager).GetField("InstanceChanged", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            _manager.LeaveCurrentRoom();
            _relayBlock.TrySetResult(false);
            UnityEngine.Object.DestroyImmediate(_manager.gameObject);
            UnityEngine.Object.DestroyImmediate(_profile);
            foreach (var item in _statics) item.Key.SetValue(null, item.Value);
            _statics.Clear();
            PlayFabSettings.staticPlayer.CopyFrom(_previousAuthentication);
            typeof(NetworkManager).GetProperty(nameof(NetworkManager.singleton)).SetValue(null, _previousNetworkManager);
            _requests.Clear();
        }

        [Test]
        public void CreateCommitsItsRoomAndQueuesRelayDespiteThrowingRegistryAndStateObservers()
        {
            int registry = 0, busy = 0;
            _manager.OnRoomRegistryChanged += Throw;
            _manager.OnRoomRegistryChanged += () => registry++;
            _manager.OnRoomFlowStateChanged += (_, __) => Throw();
            _manager.OnRoomFlowStateChanged += (_, isBusy) => { if (isBusy) busy++; };
            ExpectWarnings(2);
            _manager.CreateRoom("Created room");
            Assert.That(RoomIdentity.IsValid(_manager.CurrentRoomId), Is.True);
            Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Created room"));
            Assert.That(registry, Is.EqualTo(1));
            Assert.That(busy, Is.EqualTo(1));
            Assert.That(FlowFlag("RelayQueued"), Is.True);
            Assert.That(_requests, Is.Empty, "Registration follows actual Relay startup, not the UI event.");
        }

        [Test]
        public void JoinSuccessCannotBecomeFailureOrCleanupBecauseAUiObserverThrows()
        {
            int registry = 0, joined = 0;
            _manager.OnRoomRegistryChanged += Throw;
            _manager.OnRoomRegistryChanged += () => registry++;
            _manager.OnRoomJoined += Throw;
            _manager.OnRoomJoined += () => joined++;
            ExpectWarnings(2);
            _manager.JoinRoom(Room);
            Assert.That(_manager.CurrentRoomId, Is.EqualTo(Room));
            Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Service room"));
            Assert.That(registry, Is.EqualTo(1));
            Assert.That(joined, Is.EqualTo(1));
            Assert.That(FlowFlag("RelayQueued"), Is.True);
            Assert.That(FlowFlag("MembershipPossible"), Is.True);
            CollectionAssert.AreEqual(new[] { "JoinRoom" }, _requests);
        }

        [Test]
        public void HostRegistrationDoesNotRetryOrLeaveAfterSuccessfulResponseAndThrowingObservers()
        {
            _manager.CreateRoom("Created room");
            int joined = 0;
            _manager.OnRoomRegistryChanged += Throw;
            _manager.OnRoomJoined += Throw;
            _manager.OnRoomJoined += () => joined++;
            _manager.OnRoomFlowStateChanged += (_, __) => Throw();
            ExpectWarnings(4);
            Invoke("RegisterRoomToRegistry", Get<object>("_activeRoomFlow"), "RELAY", 1);
            Assert.That(_manager.CurrentRoomId, Is.Not.Null);
            Assert.That(joined, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "RegisterRoomToRegistry" }, _requests);
            Assert.That(FlowFlag("CleanupQueued"), Is.False);
        }

        [Test]
        public void LeaveClearsMembershipOnceAndStillNotifiesOtherObservers()
        {
            _manager.JoinRoom(Room);
            int registry = 0, idle = 0;
            _manager.OnRoomRegistryChanged += Throw;
            _manager.OnRoomRegistryChanged += () => registry++;
            _manager.OnRoomFlowStateChanged += (_, __) => Throw();
            _manager.OnRoomFlowStateChanged += (_, busy) => { if (!busy) idle++; };
            ExpectWarnings(2);
            _manager.LeaveCurrentRoom();
            Assert.That(_manager.CurrentRoomId, Is.Null);
            Assert.That(Get<object>("_activeRoomFlow"), Is.Null);
            Assert.That(registry, Is.EqualTo(1));
            Assert.That(idle, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "JoinRoom", "LeaveRoom" }, _requests);
        }

        [Test]
        public void NewIdleNotificationDuringBusyNotificationCannotBeOverwrittenByTheOldBusyState()
        {
            var states = new List<bool>();
            _manager.OnRoomFlowStateChanged += (_, busy) => { if (busy) _manager.LeaveCurrentRoom(); };
            _manager.OnRoomFlowStateChanged += (_, busy) => states.Add(busy);
            _manager.CreateRoom("Cancelled by observer");
            CollectionAssert.AreEqual(new[] { false }, states);
            Assert.That(_manager.CurrentRoomId, Is.Null);
            Assert.That(_requests, Is.Empty);
        }

        [Test]
        public void JoinedObserverCancellationStopsRemainingStaleJoinedNotifications()
        {
            int stale = 0;
            _manager.OnRoomJoined += () => _manager.LeaveCurrentRoom();
            _manager.OnRoomJoined += () => stale++;
            _manager.JoinRoom(Room);
            Assert.That(stale, Is.Zero);
            Assert.That(_manager.CurrentRoomId, Is.Null);
            CollectionAssert.AreEqual(new[] { "JoinRoom", "LeaveRoom" }, _requests);
        }

        [Test]
        public void FailedJoinNotificationMayStartAnotherRoomWithoutAnOldFailureOverwritingItsBusyState()
        {
            Set("_executeCloudScript", (Action<ExecuteCloudScriptRequest, Action<ExecuteCloudScriptResult>, Action<PlayFabError>>)((request, success, failure) =>
            {
                if (request.FunctionName == "JoinRoom")
                {
                    _requests.Add(request.FunctionName);
                    success(new ExecuteCloudScriptResult { Error = new ScriptExecutionError { Error = "RoomClosed" } });
                }
                else Respond(request, success, failure);
            }));
            var states = new List<bool>();
            _manager.OnRoomFlowStateChanged += (_, busy) => { if (!busy) _manager.CreateRoom("Replacement room"); };
            _manager.OnRoomFlowStateChanged += (_, busy) => states.Add(busy);
            _manager.JoinRoom(Room);
            Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Replacement room"));
            Assert.That(states, Is.Not.Empty);
            Assert.That(states.TrueForAll(busy => busy), Is.True, "Old failure/idle cannot follow the new room's busy event.");
            Assert.That(FlowFlag("RelayQueued"), Is.True);
        }

        [Test]
        public void CleanupObserverStartingAnotherRoomSuppressesTheOldDisconnectNotice()
        {
            _manager.JoinRoom(Room);
            Set("_networkRoomFlow", Get<object>("_activeRoomFlow"));
            bool replaced = false;
            var states = new List<bool>();
            _manager.OnRoomRegistryChanged += () =>
            {
                if (replaced) return;
                replaced = true;
                _manager.CreateRoom("Replacement room");
            };
            _manager.OnRoomFlowStateChanged += (_, busy) => states.Add(busy);
            _manager.NotifyRoomNetworkDisconnected(false, false);
            Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Replacement room"));
            Assert.That(_manager.LastRoomNotice, Is.Null);
            CollectionAssert.AreEqual(new[] { true }, states);
        }

        [Test]
        public void CleanupDuringCreateCannotOverwriteTheReplacementRoomWithTheAbandonedCreate()
        {
            _manager.JoinRoom(Room);
            bool replaced = false;
            _manager.OnRoomRegistryChanged += () =>
            {
                if (replaced) return;
                replaced = true;
                _manager.CreateRoom("Replacement room");
            };
            _manager.CreateRoom("Abandoned room");
            Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Replacement room"));
            object flow = Get<object>("_activeRoomFlow");
            var ticket = (RoomFlowTicket)flow.GetType().GetProperty("Ticket").GetValue(flow);
            Assert.That(_manager.CurrentRoomId, Is.EqualTo(ticket.RoomId));
            Assert.That(FlowFlag("RelayQueued"), Is.True);
        }

        [TestCase("disable")]
        [TestCase("destroy")]
        [TestCase("quit")]
        public void ServiceShutdownObserversCannotStartANewRoom(string shutdown)
        {
            _manager.JoinRoom(Room);
            _manager.OnRoomFlowStateChanged += (_, __) =>
            {
                _manager.CreateRoom("Must not start");
                _manager.JoinRoom(Room);
            };
            if (shutdown == "destroy") Invoke("OnDestroy");
            else if (shutdown == "quit") Invoke("OnApplicationQuit");
            else _manager.gameObject.SetActive(false);
            Assert.That(_manager.CurrentRoomId, Is.Null);
            Assert.That(Get<object>("_activeRoomFlow"), Is.Null);
            CollectionAssert.AreEqual(new[] { "JoinRoom", "LeaveRoom" }, _requests);
            if (shutdown == "disable")
            {
                Set("OnRoomFlowStateChanged", null);
                _manager.gameObject.SetActive(true);
                _manager.CreateRoom("Reactivated room");
                Assert.That(_manager.CurrentRoomInfo.RoomName, Is.EqualTo("Reactivated room"));
            }
        }

        [Test]
        public void RoomInfoObserversReceiveIndependentSnapshotsAndAllWaitingCallbacksComplete()
        {
            int listeners = 0, callbacks = 0;
            _manager.OnRoomInfoListLoaded += rooms => { rooms.Clear(); Throw(); };
            _manager.OnRoomInfoListLoaded += rooms => { Assert.That(rooms.ContainsKey(Room), Is.True); listeners++; };
            var pending = Get<List<Action<Dictionary<string, PlayFabBattleManager.RoomInfo>>>>("_pendingRoomInfoCallbacks");
            pending.Add(_ => Throw());
            pending.Add(rooms => { Assert.That(rooms.ContainsKey(Room), Is.True); callbacks++; });
            Set("_isRoomInfoRequestInFlight", true);
            Set("_roomInfoRequest", 1u);
            Set("_roomInfoRequestRevision", Get<RoomListSnapshotState>("_roomListSnapshot").Revision);
            Set("_roomInfoRequestStarted", Time.realtimeSinceStartupAsDouble);
            ExpectWarnings(2);
            Invoke("CompleteRoomInfoRequest", 1u, Rooms());
            Assert.That(listeners, Is.EqualTo(1));
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(pending, Is.Empty);
            Assert.That(Get<Dictionary<string, PlayFabBattleManager.RoomInfo>>("_lastLoadedRoomInfos").ContainsKey(Room), Is.True);
        }

        [Test]
        public void LegacyRoomListObserverCannotMutateTheNextObserverOrSuppressTheCaller()
        {
            Invoke("ReplaceKnownRoomInfos", Rooms());
            var snapshot = Get<RoomListSnapshotState>("_roomListSnapshot");
            snapshot.TryComplete(snapshot.Revision, Time.realtimeSinceStartupAsDouble);
            int listeners = 0, completed = 0;
            _manager.OnRoomListLoaded += rooms => { rooms.Clear(); Throw(); };
            _manager.OnRoomListLoaded += rooms => { Assert.That(rooms.ContainsKey(Room), Is.True); listeners++; };
            ExpectWarnings(1);
            _manager.GetActiveRooms(rooms => { Assert.That(rooms.ContainsKey(Room), Is.True); completed++; });
            Assert.That(listeners, Is.EqualTo(1));
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void RelayCodeUpdateKeepsSuccessfulResultWhenRegistryAndCallbackObserversThrow()
        {
            _manager.CreateRoom("Created room");
            _manager.OnRoomRegistryChanged += Throw;
            bool? completed = null;
            Action<bool> callbacks = _ => Throw();
            callbacks += success => completed = success;
            ExpectWarnings(2);
            _manager.UpdateCurrentRoomRelayJoinCode("NEWRELAY", callbacks);
            Assert.That(completed, Is.True);
            Assert.That(_manager.CurrentRoomInfo.RelayJoinCode, Is.EqualTo("NEWRELAY"));
            CollectionAssert.AreEqual(new[] { "UpdateRoomRelayJoinCode" }, _requests);
        }

        [Test]
        public void InstanceObserversCannotInterruptServicePublicationOrRemoval()
        {
            int notifications = 0;
            PlayFabBattleManager.InstanceChanged += _ => Throw();
            PlayFabBattleManager.InstanceChanged += _ => notifications++;
            typeof(PlayFabBattleManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            ExpectWarnings(2);
            Invoke("Awake");
            Assert.That(PlayFabBattleManager.Instance, Is.SameAs(_manager));
            Invoke("OnDestroy");
            Assert.That(PlayFabBattleManager.Instance, Is.Null);
            Assert.That(notifications, Is.EqualTo(2));
        }

        private void Respond(ExecuteCloudScriptRequest request, Action<ExecuteCloudScriptResult> success, Action<PlayFabError> failure)
        {
            _requests.Add(request.FunctionName);
            var parameters = request.FunctionParameter as Dictionary<string, object>;
            string roomId = parameters != null && parameters.TryGetValue("roomId", out object id) ? (string)id : Room;
            string relay = parameters != null && parameters.TryGetValue("relayJoinCode", out object code) ? (string)code : "RELAY";
            success(new ExecuteCloudScriptResult { FunctionResult = new Dictionary<string, object>
            {
                { "roomId", roomId }, { "serverNow", 1000d }, { "leaseExpiresAt", 61000d }, { "playerCount", 0 },
                { "roomInfo", new Dictionary<string, object> { { "roomName", "Service room" }, { "masterName", "Fixture host" }, { "playerCount", 2 }, { "relayJoinCode", relay } } }
            } });
        }

        private static Dictionary<string, PlayFabBattleManager.RoomInfo> Rooms() => new Dictionary<string, PlayFabBattleManager.RoomInfo>
        { { Room, new PlayFabBattleManager.RoomInfo("Service room", "Fixture host", 2, "RELAY", Time.realtimeSinceStartupAsDouble + 60d) } };
        private bool FlowFlag(string name)
        {
            object flow = Get<object>("_activeRoomFlow");
            var state = (RoomSessionState)flow.GetType().GetField("State").GetValue(flow);
            return (bool)typeof(RoomSessionState).GetProperty(name).GetValue(state);
        }
        private static void Throw() => throw new InvalidOperationException("Observer fixture failure");
        private static void ExpectWarnings(int count) { for (int i = 0; i < count; i++) LogAssert.Expect(LogType.Warning, Warning); }
        private object Invoke(string name, params object[] args) => typeof(PlayFabBattleManager).GetMethod(name, Private).Invoke(_manager, args);
        private void Set(string name, object value) => typeof(PlayFabBattleManager).GetField(name, Private).SetValue(_manager, value);
        private T Get<T>(string name) => (T)typeof(PlayFabBattleManager).GetField(name, Private).GetValue(_manager);
        private void Isolate(Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            _statics[field] = field.GetValue(null);
            field.SetValue(null, value);
        }
    }
}
