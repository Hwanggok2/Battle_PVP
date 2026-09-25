using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using BattlePvp.Stats;
using BattlePvp.Managers;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mirror;

namespace BattlePvp.Networking
{
    /// <summary>
    /// PlayFab??Shared Group Data瑜??댁슜??諛⑷?由?諛??좎? ?곗씠?곕? ?숆린?뷀븯???대옒?ㅼ엯?덈떎.
    /// </summary>
    public class PlayFabBattleManager : MonoBehaviour
    {
        public static PlayFabBattleManager Instance { get; private set; }
        public static event Action<PlayFabBattleManager> InstanceChanged;

        private const string REGISTER_ROOM_FUNCTION = "RegisterRoomToRegistry";
        private const string HEARTBEAT_ROOM_FUNCTION = "HeartbeatRoom";
        private const string GET_ACTIVE_ROOM_INFOS_FUNCTION = "GetActiveRoomInfos";
        private const string JOIN_ROOM_FUNCTION = "JoinRoom";
        private const string LEAVE_ROOM_FUNCTION = "LeaveRoom";
        private const string UPDATE_ROOM_RELAY_JOIN_CODE_FUNCTION = "UpdateRoomRelayJoinCode";
        private const string ADMIN_VALIDATE_ROOM_KEY_FUNCTION = "AdminValidateRoomKey";
        private const string ADMIN_DELETE_ROOM_FUNCTION = "AdminDeleteRoom";
        private const string ADMIN_CLEAR_ROOM_REGISTRY_FUNCTION = "AdminClearRoomRegistry";
        private const int ROOM_REGISTRATION_MAX_ATTEMPTS = 3;
        private const float ROOM_REGISTRATION_RETRY_DELAY_SECONDS = 0.75f;

        public struct RoomInfo
        {
            public RoomInfo(string roomName, string masterName, int playerCount, string relayJoinCode = "",
                double validUntil = double.PositiveInfinity)
            {
                RoomName = roomName;
                MasterName = masterName;
                PlayerCount = playerCount;
                RelayJoinCode = relayJoinCode ?? string.Empty;
                ValidUntil = validUntil;
            }

            public string RoomName { get; private set; }
            public string MasterName { get; private set; }
            public int PlayerCount { get; private set; }
            public string RelayJoinCode { get; private set; }
            public double ValidUntil { get; private set; }
        }


        public event Action<Dictionary<string, string>> OnRoomListLoaded;
        public event Action<Dictionary<string, RoomInfo>> OnRoomInfoListLoaded;
        public event Action OnRoomRegistryChanged;
        public event Action OnRoomJoined;
        public event Action<string, bool> OnRoomFlowStateChanged;



        private string _ownedRoomId;
        private string _joinedRoomId;

        private RoomInfo _currentRoomInfo;
        // Only authoritative list responses insert entries. Session metadata lives on RoomFlow.Info.
        private readonly Dictionary<string, RoomInfo> _lastLoadedRoomInfos = new Dictionary<string, RoomInfo>();
        private readonly RoomListSnapshotState _roomListSnapshot = new RoomListSnapshotState();
        private readonly List<Action<Dictionary<string, RoomInfo>>> _pendingRoomInfoCallbacks = new List<Action<Dictionary<string, RoomInfo>>>();
        private bool _isRoomInfoRequestInFlight;
        private uint _roomInfoRequest;
        private ulong _roomInfoRequestRevision;
        private double _roomInfoRequestStarted;
        private const double RoomInfoRequestTimeoutSeconds = 10d;
        private const int RoomMutationTimeoutMilliseconds = 15_000;
        private const string RoomMutationUnconfirmedMessage = "방 요청의 응답을 확인하지 못했습니다. 확인이 끝날 때까지 이 방의 재참가는 제한됩니다. 다른 방을 이용해 주세요.";
        private const string RoomClosedMessage = "호스트 연결이 종료되었거나 방이 만료되어 로비로 돌아왔습니다.";
        public string LastRoomNotice { get; private set; }
        private static RoomServiceLifetime _sharedRoomLifetime = new RoomServiceLifetime();
        private readonly RoomServiceLifetime _roomLifetime = _sharedRoomLifetime;
        private RoomFlowGeneration _roomFlows => _roomLifetime.Flows;
        private RoomOperationQueue _roomMutations => _roomLifetime.Mutations;
        private RoomServiceResponseGate _roomResponses => _roomLifetime.Responses;
        private readonly RoomOperationQueue _relayPreparations = new RoomOperationQueue();
        private RoomFlow _activeRoomFlow;
        private RoomFlow _networkRoomFlow;
        private bool _roomServiceDisposed;
        private bool _roomServiceStopped;
        private string _observedRoomAccountId;
        private uint _roomStateNotification;
        private Action<ExecuteCloudScriptRequest, Action<ExecuteCloudScriptResult>, Action<PlayFabError>> _executeCloudScript =
            (request, success, failure) => PlayFabClientAPI.ExecuteCloudScript(request, success, failure);

        private sealed class RoomFlow
        {
            public readonly RoomSessionState State;
            public RoomFlowTicket Ticket => State.Ticket;
            public PlayFabAuthenticationContext Authentication;
            public bool IsHost;
            public RoomInfo Info;
            public readonly HostRoomLease Lease = new HostRoomLease();
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            public readonly CancellationToken Cancellation;

            public RoomFlow(RoomSessionState state) { State = state; Cancellation = _cancellation.Token; }
            public void Cancel()
            {
                if (Cancellation.IsCancellationRequested) return;
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
        }
        // Construction only wires managed state; SDK/Unity access is deferred until runtime use.
        private readonly NetworkProfileRepository _profileRepository = new NetworkProfileRepository();

        public string CurrentRoomId => _joinedRoomId;
        public RoomInfo CurrentRoomInfo => _currentRoomInfo;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                // 諛??낆옣???깃났?섎㈃ ???꾪솚 ?대깽???곌껐
                NotifyRoomObservers(InstanceChanged, subscriber => ((Action<PlayFabBattleManager>)subscriber)(this),
                    () => Instance == this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private string CurrentAccountId => PlayFabClientAPI.IsClientLoggedIn() ? PlayFabSettings.staticPlayer.PlayFabId : null;

        private bool CanUseRoomService => !_roomServiceDisposed && !_roomServiceStopped && isActiveAndEnabled;

        private bool IsCurrentRoomFlow(RoomFlow flow) => CanUseRoomService && flow != null &&
            _roomFlows.IsCurrent(flow.Ticket, CurrentAccountId);

        private RoomFlow BeginRoomFlow(string roomId, bool host)
        {
            ++_roomStateNotification;
            var previous = _activeRoomFlow;
            var authentication = new PlayFabAuthenticationContext();
            authentication.CopyFrom(PlayFabSettings.staticPlayer);
            var flow = new RoomFlow(_roomLifetime.Begin(CurrentAccountId, roomId))
            {
                Authentication = authentication, IsHost = host
            };
            _activeRoomFlow = flow;
            previous?.Cancel();
            LastRoomNotice = null;
            ClearCurrentRoomState();
            if (host) _ownedRoomId = roomId;
            StopPreviousRoomNetwork();
            ScheduleRoomCleanup(previous);
            return flow;
        }

        private void ClearCurrentRoomState()
        {
            _joinedRoomId = null;
            _currentRoomInfo = default;
        }

        private void StopPreviousRoomNetwork()
        {
            if (NetworkManager.singleton == null) return;
            if (NetworkManager.singleton.transport is UnityRelayTransport relay) relay.CancelPendingPreparation();
            if (NetworkServer.active && NetworkClient.active) NetworkManager.singleton.StopHost();
            else if (NetworkClient.active) NetworkManager.singleton.StopClient();
            else if (NetworkServer.active) NetworkManager.singleton.StopServer();
            if (NetworkManager.singleton != null && NetworkManager.singleton.mode == NetworkManagerMode.Offline &&
                NetworkClient.connection == null) _networkRoomFlow = null;
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            _profileRepository.Tick();
            ExpireRoomInfoRequest(now);
            string accountId = CurrentAccountId;
            if (!string.Equals(_observedRoomAccountId, accountId, StringComparison.Ordinal))
            {
                _observedRoomAccountId = accountId;
                if (_activeRoomFlow != null && !_roomFlows.IsCurrent(_activeRoomFlow.Ticket, accountId))
                {
                    _ownedRoomId = null;
                    LeaveRoomWithNotice("로그인 계정이 변경되어 방 연결을 취소했습니다.");
                }
            }
            RoomFlow flow = _activeRoomFlow;
            if (flow != null && flow.IsHost && NetworkServer.active)
            {
                var relay = NetworkManager.singleton != null ? NetworkManager.singleton.transport as UnityRelayTransport : null;
                if (relay == null || relay.ServerRelayFailed)
                    LeaveRoomWithNotice("릴레이 호스트 연결이 종료되어 로비로 돌아왔습니다.");
                else if (flow.Lease.HasExpired(now)) CloseExpiredHost(flow);
                else if (relay.ServerRelayReady && flow.Lease.TryBeginHeartbeat(now)) SendHostHeartbeat(flow);
            }
        }

        private void OnEnable()
        {
            if (Instance == this && !_roomServiceDisposed) _roomServiceStopped = false;
        }

        private void OnDisable()
        {
            if (Instance != this) return;
            _roomServiceStopped = true;
            LeaveCurrentRoom();
            _profileRepository.ResetSession();
            if (_isRoomInfoRequestInFlight)
                CompleteRoomInfoRequest(_roomInfoRequest, null);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            _roomServiceStopped = true;
            LeaveCurrentRoom();
            if (_isRoomInfoRequestInFlight)
                CompleteRoomInfoRequest(_roomInfoRequest, null);
            _roomServiceDisposed = true;
            Instance = null;
            NotifyRoomObservers(InstanceChanged, subscriber => ((Action<PlayFabBattleManager>)subscriber)(null),
                () => Instance == null);
        }

        private async void HandleRoomJoinSuccess(RoomFlow flow)
        {
            if (!IsCurrentRoomFlow(flow) ||
                (ReferenceEquals(_networkRoomFlow, flow) && (NetworkServer.active || NetworkClient.active))) return;
            if (!flow.State.TryQueueRelay()) return;
            try
            {
                await _relayPreparations.Enqueue("relay", async () =>
                {
                    if (!IsCurrentRoomFlow(flow)) return false;
                    await WaitForPreviousNetworkShutdownAsync(flow);
                    if (!IsCurrentRoomFlow(flow)) return false;
                    if (NetworkManager.singleton == null) throw new InvalidOperationException("NetworkManager is missing.");
                    var relayTransport = EnsureRelayTransport();
                    if (flow.IsHost) await StartRelayHostAsync(relayTransport, flow);
                    else await StartRelayClientAsync(relayTransport, flow);
                    return true;
                });
            }
            catch (Exception)
            {
                if (IsCurrentRoomFlow(flow))
                {
                    LeaveRoomWithNotice("릴레이 연결에 실패했습니다. 다시 참가해 주세요.");
                }
            }
            finally { flow.State.CompleteRelayPreparation(); }
        }

        private async System.Threading.Tasks.Task WaitForPreviousNetworkShutdownAsync(RoomFlow flow)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
            while (IsCurrentRoomFlow(flow))
            {
                var manager = NetworkManager.singleton;
                if (manager != null && manager.mode == NetworkManagerMode.Offline && !NetworkServer.active &&
                    NetworkClient.connection == null && NetworkManager.loadingSceneAsync == null) return;
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new InvalidOperationException("The previous room has not finished shutting down.");
                await Task.Delay(25, flow.Cancellation);
            }
        }

        private UnityRelayTransport EnsureRelayTransport()
        {
            var manager = NetworkManager.singleton;
            var transport = manager.GetComponent<UnityRelayTransport>();
            if (transport == null) transport = manager.gameObject.AddComponent<UnityRelayTransport>();
            manager.transport = transport;
            Transport.active = transport;
            return transport;
        }

        private async System.Threading.Tasks.Task StartRelayHostAsync(UnityRelayTransport transport, RoomFlow flow)
        {
            SetRoomFlowState(flow, "릴레이 방을 준비하는 중...", true);
            string joinCode = await transport.PrepareHostAsync(NetworkManager.singleton.maxConnections, flow.Cancellation);
            if (!IsCurrentRoomFlow(flow)) return;
            flow.Info = new RoomInfo(flow.Info.RoomName, flow.Info.MasterName, 1, joinCode);
            _currentRoomInfo = flow.Info;
            _networkRoomFlow = flow;
            NetworkManager.singleton.StartHost();
            double deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (IsCurrentRoomFlow(flow) && !transport.ServerRelayReady)
            {
                if (transport.ServerRelayFailed || Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new InvalidOperationException("Relay host did not become ready.");
                await Task.Delay(25, flow.Cancellation);
            }
            if (IsCurrentRoomFlow(flow)) RegisterRoomToRegistry(flow, joinCode);
        }

        private async System.Threading.Tasks.Task StartRelayClientAsync(UnityRelayTransport transport, RoomFlow flow)
        {
            SetRoomFlowState(flow, "릴레이 서버에 연결하는 중...", true);
            string joinCode = await WaitForRelayJoinCodeAsync(flow);
            if (!IsCurrentRoomFlow(flow)) return;
            if (string.IsNullOrWhiteSpace(joinCode)) throw new InvalidOperationException("Room has no Relay code.");
            await transport.PrepareClientAsync(joinCode, flow.Cancellation);
            if (!IsCurrentRoomFlow(flow)) return;
            _networkRoomFlow = flow;
            NetworkManager.singleton.networkAddress = "relay";
            NetworkManager.singleton.StartClient();
            SetRoomFlowState(flow, "방에 접속하는 중...", true);
        }

        private async System.Threading.Tasks.Task<string> WaitForRelayJoinCodeAsync(RoomFlow flow)
        {
            for (int attempt = 0; attempt < 10 && IsCurrentRoomFlow(flow); attempt++)
            {
                if (!string.IsNullOrWhiteSpace(flow.Info.RelayJoinCode)) return flow.Info.RelayJoinCode;
                var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
                RefreshCurrentRoomInfo(_ => completion.TrySetResult(true));
                await ServiceTaskDeadline.WaitAsync(completion.Task, flow.Cancellation);
                if (!IsCurrentRoomFlow(flow)) return string.Empty;
                if (!string.IsNullOrWhiteSpace(flow.Info.RelayJoinCode)) return flow.Info.RelayJoinCode;
                await Task.Delay(500, flow.Cancellation);
            }
            return string.Empty;
        }

        private System.Threading.Tasks.Task<ExecuteCloudScriptResult> ExecuteRoomMutation(
            RoomFlow flow, string function, Dictionary<string, object> parameters, bool cleanup = false)
        {
            return _roomMutations.Enqueue(flow.Ticket.MembershipKey, () =>
            {
                if (cleanup && !_roomLifetime.AuthorizeCleanup(flow.State, CurrentAccountId))
                {
                    flow.State.ReleaseCleanupQueue();
                    return System.Threading.Tasks.Task.FromResult<ExecuteCloudScriptResult>(null);
                }
                if (!cleanup && !IsCurrentRoomFlow(flow))
                    return System.Threading.Tasks.Task.FromResult<ExecuteCloudScriptResult>(null);
                return ExecuteConfirmedRoomMutation(flow, function, parameters, cleanup);
            });
        }

        private async Task<ExecuteCloudScriptResult> ExecuteConfirmedRoomMutation(RoomFlow flow, string function,
            Dictionary<string, object> parameters, bool cleanup)
        {
            using (var deadline = new CancellationTokenSource(RoomMutationTimeoutMilliseconds))
            {
                Task<ExecuteCloudScriptResult> SendRequest()
                {
                    if (!cleanup && (function == JOIN_ROOM_FUNCTION || function == REGISTER_ROOM_FUNCTION))
                        flow.State.MarkMembershipPossible();
                    var response = new TaskCompletionSource<ExecuteCloudScriptResult>();
                    try
                    {
                        _executeCloudScript(new ExecuteCloudScriptRequest
                        {
                            FunctionName = function, FunctionParameter = parameters, GeneratePlayStreamEvent = false,
                            AuthenticationContext = flow.Authentication
                        }, result => response.TrySetResult(result),
                            _ => response.TrySetException(new InvalidOperationException("Room service request failed.")));
                    }
                    catch (Exception) { response.TrySetException(new InvalidOperationException("Room service request failed.")); }
                    return response.Task;
                }
                if (function == HEARTBEAT_ROOM_FUNCTION)
                    return await RoomServiceResponseGate.WaitForLeaseRefreshAsync(SendRequest, deadline.Token);
                return await _roomResponses.ExecuteAsync(flow.Ticket.MembershipKey, SendRequest, deadline.Token, result =>
                {
                    // Only a server response releases quarantine. A late transport error cannot prove completion.
                    if (cleanup)
                    {
                        flow.State.ReleaseCleanupQueue();
                        if (result != null && result.Error == null) flow.State.ConfirmCleanup();
                    }
                    else if (!IsCurrentRoomFlow(flow))
                    {
                        flow.State.ReleaseCleanupQueue();
                        ScheduleRoomCleanup(flow);
                    }
                });
            }
        }

        #region [Room Management]

        /// <summary>
        /// ?덈줈??諛⑹쓣 ?앹꽦?섍퀬 湲濡쒕쾶 ?덉??ㅽ듃由ъ뿉 ?깅줉?⑸땲??
        /// </summary>
        public void CreateRoom(string roomName)
        {
            if (!CanUseRoomService) return;
            if (!PlayFabClientAPI.IsClientLoggedIn() ||
                !RoomIdentity.TryCreate(CurrentAccountId, Guid.NewGuid(), out string roomId))
            {
                SetRoomFlowState("로그인 정보를 확인한 후 방을 만들어 주세요.", false);
                return;
            }
            RoomFlow flow = BeginRoomFlow(roomId, true);
            if (!IsCurrentRoomFlow(flow)) return;
            flow.Info = new RoomInfo(roomName, GetCurrentPlayerNickname(), 1);
            _joinedRoomId = roomId;
            _currentRoomInfo = flow.Info;
            NotifyRoomRegistryChanged(flow);
            SetRoomFlowState(flow, "방 생성을 준비하는 중...", true);
            if (IsCurrentRoomFlow(flow)) HandleRoomJoinSuccess(flow);
        }

        private async void JoinRoomThroughCloudScript(string roomId)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn())
            {
                SetRoomFlowState("로그인 정보를 확인한 후 참가해 주세요.", false);
                return;
            }
            if (IsCurrentRoomFlow(_activeRoomFlow) && _activeRoomFlow.Ticket.RoomId == roomId) return;
            bool host = roomId == _ownedRoomId && RoomAuthenticationRules.IsLocalOwner(roomId, CurrentAccountId);
            RoomFlow flow = BeginRoomFlow(roomId, host);
            SetRoomFlowState(flow, "방 정보를 확인하는 중...", true);
            try
            {
                ExecuteCloudScriptResult result = await ExecuteRoomMutation(flow, JOIN_ROOM_FUNCTION,
                    new Dictionary<string, object> { { "roomId", roomId } });
                if (!IsCurrentRoomFlow(flow)) { ScheduleRoomCleanup(flow); return; }
                if (result == null || result.Error != null) throw new InvalidOperationException("Room join failed.");
                flow.Info = ParseRoomInfoFromCloudScript(result.FunctionResult, flow);
                _joinedRoomId = roomId;
                _currentRoomInfo = flow.Info;
                UpdateListedRoom(roomId, flow.Info);
                NotifyRoomRegistryChanged(flow);
                CompleteRoomJoin(flow);
            }
            catch (Exception error)
            {
                if (!IsCurrentRoomFlow(flow)) { ScheduleRoomCleanup(flow); return; }
                LeaveRoomWithNotice(error is RoomServiceUnconfirmedException ? RoomMutationUnconfirmedMessage :
                    "방 참가에 실패했습니다. 다시 참가해 주세요.");
            }
        }

        private RoomInfo ParseRoomInfoFromCloudScript(object functionResult, RoomFlow flow)
        {
            RoomInfo fallback = GetRoomInfoFallback(flow);
            if (functionResult is IDictionary<string, object> resultDict &&
                resultDict.TryGetValue("roomInfo", out object roomInfoObject) &&
                roomInfoObject is IDictionary<string, object> infoDict)
            {
                return new RoomInfo(
                    GetStringValue(infoDict, "roomName", fallback.RoomName),
                    GetStringValue(infoDict, "masterName", fallback.MasterName),
                    GetIntValue(infoDict, "playerCount", fallback.PlayerCount),
                    GetStringValue(infoDict, "relayJoinCode", fallback.RelayJoinCode));
            }

            return fallback;
        }

        /// <summary>
        /// 紐⑤뱺 ?뚮젅?댁뼱媛 蹂????덈뒗 怨듭슜 洹몃９???꾩옱 諛??뺣낫瑜?異붽??⑸땲??
        /// </summary>
        private async void RegisterRoomToRegistry(RoomFlow flow, string relayJoinCode, int attempt = 1)
        {
            if (!IsCurrentRoomFlow(flow) || !flow.IsHost) return;
            SetRoomFlowState(flow, "방을 등록하는 중...", true);
            try
            {
                double requestStarted = Time.realtimeSinceStartupAsDouble;
                flow.Lease.BeginRegistration(requestStarted);
                ExecuteCloudScriptResult result = await ExecuteRoomMutation(flow, REGISTER_ROOM_FUNCTION,
                    new Dictionary<string, object>
                    {
                        { "roomId", flow.Ticket.RoomId }, { "roomName", flow.Info.RoomName },
                        { "masterName", flow.Info.MasterName }, { "relayJoinCode", relayJoinCode }
                    });
                if (!IsCurrentRoomFlow(flow)) { ScheduleRoomCleanup(flow); return; }
                if (result == null || result.Error != null) throw new InvalidOperationException("Room registration failed.");
                if (!AcceptHostLease(flow, result, requestStarted))
                    throw new InvalidOperationException("Room registration returned an expired or invalid lease.");
                _roomListSnapshot.Invalidate();
                SetRoomFlowState(flow, string.Empty, false);
                if (!IsCurrentRoomFlow(flow)) return;
                NotifyRoomRegistryChanged(flow);
                CompleteRoomJoin(flow);
            }
            catch (Exception error)
            {
                if (!IsCurrentRoomFlow(flow)) { ScheduleRoomCleanup(flow); return; }
                if (error is RoomServiceUnconfirmedException)
                {
                    LeaveRoomWithNotice(RoomMutationUnconfirmedMessage);
                    return;
                }
                if (attempt < ROOM_REGISTRATION_MAX_ATTEMPTS)
                {
                    SetRoomFlowState(flow, "방 등록을 다시 시도하는 중...", true);
                    await Task.Delay((int)(ROOM_REGISTRATION_RETRY_DELAY_SECONDS * attempt * 1000f));
                    if (IsCurrentRoomFlow(flow)) RegisterRoomToRegistry(flow, relayJoinCode, attempt + 1);
                }
                else
                {
                    LeaveRoomWithNotice("방 등록에 실패했습니다. 다시 방을 만들어 주세요.");
                }
            }
        }

        private bool AcceptHostLease(RoomFlow flow, ExecuteCloudScriptResult result, double requestStarted)
        {
            if (!(result.FunctionResult is IDictionary<string, object> values)) return false;
            return GetStringValue(values, "roomId", "") == flow.Ticket.RoomId &&
                flow.Lease.Accept(requestStarted, Time.realtimeSinceStartupAsDouble,
                    GetDoubleValue(values, "serverNow"), GetDoubleValue(values, "leaseExpiresAt"));
        }

        private async void SendHostHeartbeat(RoomFlow flow)
        {
            double requestStarted = Time.realtimeSinceStartupAsDouble;
            try
            {
                ExecuteCloudScriptResult result = await ExecuteRoomMutation(flow, HEARTBEAT_ROOM_FUNCTION,
                    new Dictionary<string, object> { { "roomId", flow.Ticket.RoomId } });
                if (!IsCurrentRoomFlow(flow)) return;
                if (result != null && result.Error == null && AcceptHostLease(flow, result, requestStarted)) return;
            }
            catch (Exception) { }
            if (IsCurrentRoomFlow(flow)) flow.Lease.Failed(Time.realtimeSinceStartupAsDouble);
        }

        private void CloseExpiredHost(RoomFlow flow)
        {
            if (!IsCurrentRoomFlow(flow)) return;
            LeaveRoomWithNotice(RoomClosedMessage, retainNotice: true);
        }

        private static double GetDoubleValue(IDictionary<string, object> values, string key)
        {
            if (!values.TryGetValue(key, out object value) || value == null) return double.NaN;
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return double.NaN; }
        }

        private void SetRoomFlowState(RoomFlow flow, string message, bool isBusy)
        {
            if (IsCurrentRoomFlow(flow)) SetRoomFlowState(message, isBusy);
        }

        private void SetRoomFlowState(string message, bool isBusy)
        {
            uint notification = ++_roomStateNotification;
            NotifyRoomObservers(OnRoomFlowStateChanged, subscriber => ((Action<string, bool>)subscriber)(message ?? string.Empty, isBusy),
                () => !_roomServiceDisposed && notification == _roomStateNotification);
        }

        private void CompleteRoomJoin(RoomFlow flow)
        {
            if (!IsCurrentRoomFlow(flow)) return;
            // The service owns connection startup; UI listeners only observe its committed state.
            HandleRoomJoinSuccess(flow);
            NotifyRoomObservers(OnRoomJoined, subscriber => ((Action)subscriber)(), () => IsCurrentRoomFlow(flow));
        }

        private void NotifyRoomRegistryChanged(RoomFlow flow = null)
        {
            NotifyRoomObservers(OnRoomRegistryChanged, subscriber => ((Action)subscriber)(),
                () => !_roomServiceDisposed && (flow == null || IsCurrentRoomFlow(flow)));
        }

        private static void NotifyRoomObservers(Delegate observers, Action<Delegate> invoke, Func<bool> isCurrent = null)
        {
            if (observers == null) return;
            foreach (Delegate observer in observers.GetInvocationList())
            {
                if (isCurrent != null && !isCurrent()) return;
                try { invoke(observer); }
                catch (Exception) { Debug.LogWarning("[PlayFab] A room observer failed."); }
            }
        }

        private static void NotifyRoomResult(Action<bool, string> callback, bool success, string message) =>
            NotifyRoomObservers(callback, subscriber => ((Action<bool, string>)subscriber)(success, message));

        private static void NotifyRoomInfo(Action<RoomInfo> callback, RoomInfo info) =>
            NotifyRoomObservers(callback, subscriber => ((Action<RoomInfo>)subscriber)(info));

        public void NotifyRoomNetworkConnected()
        {
            SetRoomFlowState(_networkRoomFlow, string.Empty, false);
        }

        /// <summary>
        /// ?꾩옱 媛쒖꽕??諛?紐⑸줉??媛?몄샃?덈떎. (Key: RoomID, Value: RoomName)
        /// </summary>
        public void GetActiveRooms(Action<Dictionary<string, string>> callback)
        {
            GetActiveRoomInfos(infos =>
            {
                var rooms = new Dictionary<string, string>();
                foreach (var entry in infos) rooms[entry.Key] = entry.Value.RoomName;
                NotifyRoomObservers(OnRoomListLoaded, subscriber => ((Action<Dictionary<string, string>>)subscriber)(new Dictionary<string, string>(rooms)));
                NotifyRoomObservers(callback, subscriber => ((Action<Dictionary<string, string>>)subscriber)(new Dictionary<string, string>(rooms)));
            });
        }

        private void GetActiveRoomInfosFromCloudScript(uint request, double requestStarted)
        {
            try
            {
                _executeCloudScript(new ExecuteCloudScriptRequest
                {
                    FunctionName = GET_ACTIVE_ROOM_INFOS_FUNCTION, GeneratePlayStreamEvent = false
                }, result =>
                {
                    if (!IsCurrentRoomInfoRequest(request)) return;
                    if (result == null || result.Error != null)
                    {
                        Debug.LogWarning("[PlayFab] Live room listing failed; unverified cached rooms are hidden.");
                        CompleteRoomInfoRequest(request, null);
                        return;
                    }
                    CompleteRoomInfoRequest(request, ParseRoomInfoListFromCloudScript(result.FunctionResult, requestStarted));
                }, error => CompleteRoomInfoRequest(request, null));
            }
            catch (Exception) { CompleteRoomInfoRequest(request, null); }
        }

        private bool IsCurrentRoomInfoRequest(uint request) => !_roomServiceDisposed &&
            _isRoomInfoRequestInFlight && request == _roomInfoRequest;

        private void ExpireRoomInfoRequest(double now)
        {
            if (_isRoomInfoRequestInFlight && now - _roomInfoRequestStarted >= RoomInfoRequestTimeoutSeconds)
                CompleteRoomInfoRequest(_roomInfoRequest, null);
        }

        private void CompleteRoomInfoRequest(uint request, Dictionary<string, RoomInfo> roomInfos)
        {
            if (!IsCurrentRoomInfoRequest(request)) return;
            // A callback can run before Update in the frame that crosses the deadline.
            double now = Time.realtimeSinceStartupAsDouble;
            bool discardUnverified = roomInfos == null || now - _roomInfoRequestStarted >= RoomInfoRequestTimeoutSeconds;
            if (discardUnverified)
                roomInfos = new Dictionary<string, RoomInfo>();
            _isRoomInfoRequestInFlight = false;
            if (_roomListSnapshot.TryComplete(_roomInfoRequestRevision, now, discardUnverified))
                ReplaceKnownRoomInfos(roomInfos);
            else
                // A confirmed mutation supersedes an older list request, even if that response arrives last.
                roomInfos = CopyLiveRoomInfos();
            var callbacks = _pendingRoomInfoCallbacks.ToArray();
            _pendingRoomInfoCallbacks.Clear();
            NotifyRoomObservers(OnRoomInfoListLoaded, subscriber => ((Action<Dictionary<string, RoomInfo>>)subscriber)(new Dictionary<string, RoomInfo>(roomInfos)));
            foreach (var callback in callbacks)
                NotifyRoomObservers(callback, subscriber => ((Action<Dictionary<string, RoomInfo>>)subscriber)(new Dictionary<string, RoomInfo>(roomInfos)));
        }

        private void ReplaceKnownRoomInfos(Dictionary<string, RoomInfo> roomInfos)
        {
            // An authoritative empty list must also clear old rooms. No Shared Group/cache resurrection.
            _lastLoadedRoomInfos.Clear();
            foreach (var entry in roomInfos)
                _lastLoadedRoomInfos[entry.Key] = entry.Value;
        }

        private void UpdateListedRoom(string roomId, RoomInfo info)
        {
            // A Join/Relay response does not grant a new list lease or make an unlisted room discoverable.
            if (_lastLoadedRoomInfos.TryGetValue(roomId, out RoomInfo listed))
                _lastLoadedRoomInfos[roomId] = new RoomInfo(info.RoomName, info.MasterName, info.PlayerCount,
                    info.RelayJoinCode, listed.ValidUntil);
            _roomListSnapshot.Invalidate();
        }

        private void ApplyRoomCleanupToList(string roomId, int count, bool hostClosed)
        {
            if (hostClosed || count <= 0) _lastLoadedRoomInfos.Remove(roomId);
            else if (_lastLoadedRoomInfos.TryGetValue(roomId, out RoomInfo listed))
                _lastLoadedRoomInfos[roomId] = new RoomInfo(listed.RoomName, listed.MasterName, count,
                    listed.RelayJoinCode, listed.ValidUntil);
            _roomListSnapshot.Invalidate();
        }

        private Dictionary<string, RoomInfo> CopyLiveRoomInfos()
        {
            var available = new Dictionary<string, RoomInfo>();
            double now = Time.realtimeSinceStartupAsDouble;
            foreach (var entry in _lastLoadedRoomInfos)
                if (double.IsFinite(entry.Value.ValidUntil) && entry.Value.ValidUntil > now)
                    available[entry.Key] = entry.Value;
            return available;
        }
        private Dictionary<string, RoomInfo> ParseRoomInfoListFromCloudScript(object functionResult, double requestStarted)
        {
            var roomInfos = new Dictionary<string, RoomInfo>();
            if (!(functionResult is IDictionary<string, object> resultDict))
                return roomInfos;

            if (!resultDict.TryGetValue("roomInfos", out object roomInfosObject) || roomInfosObject == null)
                return roomInfos;

            if (!(roomInfosObject is IDictionary<string, object> objectRoomInfos))
                return roomInfos;

            foreach (var kv in objectRoomInfos)
            {
                if (!RoomIdentity.IsValid(kv.Key)) continue;
                if (!(kv.Value is IDictionary<string, object> infoDict))
                    continue;

                string roomName = GetStringValue(infoDict, "roomName", "Unnamed Room");
                string masterName = GetStringValue(infoDict, "masterName", "Unknown");
                int playerCount = GetIntValue(infoDict, "playerCount", 0);
                string relayJoinCode = GetStringValue(infoDict, "relayJoinCode", "");
                if (playerCount <= 0 || string.IsNullOrWhiteSpace(relayJoinCode) ||
                    !HostRoomLease.TryGetDeadline(requestStarted, GetDoubleValue(infoDict, "serverNow"),
                        GetDoubleValue(infoDict, "leaseExpiresAt"), out double deadline) ||
                    deadline <= Time.realtimeSinceStartupAsDouble)
                    continue;

                roomInfos[kv.Key] = new RoomInfo(roomName, masterName, playerCount, relayJoinCode, deadline);
            }

            return roomInfos;
        }

        private static string SerializeForLog(object value)
        {
            if (value == null)
                return "null";

            try
            {
                return PlayFab.PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer).SerializeObject(value);
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }

        private static string GetStringValue(IDictionary<string, object> values, string key, string fallback)
        {
            if (values.TryGetValue(key, out object value) && value != null)
            {
                string text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return fallback;
        }

        private static int GetIntValue(IDictionary<string, object> values, string key, int fallback)
        {
            if (!values.TryGetValue(key, out object value) || value == null)
                return fallback;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return Mathf.Max(0, (int)longValue);

            if (value is double doubleValue)
                return Mathf.Max(0, Mathf.RoundToInt((float)doubleValue));

            return int.TryParse(value.ToString(), out int parsed) ? Mathf.Max(0, parsed) : fallback;
        }

        public void GetActiveRoomInfos(Action<Dictionary<string, RoomInfo>> callback)
        {
            if (TryReturnCachedRoomInfos(callback))
                return;

            _pendingRoomInfoCallbacks.Add(callback);
            if (_isRoomInfoRequestInFlight)
                return;

            _isRoomInfoRequestInFlight = true;
            _roomInfoRequestStarted = Time.realtimeSinceStartupAsDouble;
            _roomInfoRequestRevision = _roomListSnapshot.Revision;
            unchecked { _roomInfoRequest++; }
            GetActiveRoomInfosFromCloudScript(_roomInfoRequest, _roomInfoRequestStarted);
        }

        private bool TryReturnCachedRoomInfos(Action<Dictionary<string, RoomInfo>> callback)
        {
            if (!_roomListSnapshot.CanReuse(Time.realtimeSinceStartupAsDouble))
                return false;
            var available = CopyLiveRoomInfos();
            NotifyRoomObservers(callback, subscriber => ((Action<Dictionary<string, RoomInfo>>)subscriber)(new Dictionary<string, RoomInfo>(available)));
            return true;
        }

        /// <summary>
        /// 湲곗〈 諛⑹뿉 李몄뿬?⑸땲?? (Shared Group 硫ㅻ쾭 異붽?)
        /// </summary>
        public void JoinRoom(string roomId)
        {
            if (!CanUseRoomService) return;
            if (!RoomIdentity.IsValid(roomId))
            {
                SetRoomFlowState("이전 형식이거나 잘못된 방입니다. 호스트가 방을 다시 만들어 주세요.", false);
                return;
            }
            JoinRoomThroughCloudScript(roomId);
        }

        /// <summary>
        /// 諛??곹깭 ?뺣낫瑜??낅뜲?댄듃?⑸땲??
        /// </summary>
        public void UpdateRoomData(string groupId, string key, string value)
        {
            Debug.LogWarning("[PlayFab] Direct room data writes are disabled. Use the validated CloudScript operation.");
        }

        public async void UpdateCurrentRoomRelayJoinCode(string relayJoinCode, Action<bool> callback = null)
        {
            RoomFlow flow = _activeRoomFlow;
            if (!IsCurrentRoomFlow(flow) || !flow.IsHost || string.IsNullOrWhiteSpace(relayJoinCode))
            { NotifyRoomObservers(callback, subscriber => ((Action<bool>)subscriber)(false)); return; }
            bool success = false;
            try
            {
                ExecuteCloudScriptResult result = await ExecuteRoomMutation(flow, UPDATE_ROOM_RELAY_JOIN_CODE_FUNCTION,
                    new Dictionary<string, object>
                    {
                        { "roomId", flow.Ticket.RoomId }, { "relayJoinCode", relayJoinCode.Trim() }
                    });
                if (IsCurrentRoomFlow(flow) && result != null && result.Error == null)
                {
                    flow.Info = ParseRoomInfoFromCloudScript(result.FunctionResult, flow);
                    _currentRoomInfo = flow.Info;
                    UpdateListedRoom(flow.Ticket.RoomId, flow.Info);
                    success = true;
                    NotifyRoomRegistryChanged(flow);
                }
            }
            catch (Exception) { }
            bool completed = success && IsCurrentRoomFlow(flow);
            NotifyRoomObservers(callback, subscriber => ((Action<bool>)subscriber)(completed));
        }

        public void LeaveCurrentRoom() => LeaveRoomWithNotice(string.Empty);

        private void LeaveRoomWithNotice(string message, bool retainNotice = false)
        {
            uint notification = ++_roomStateNotification;
            RoomFlow flow = _activeRoomFlow;
            flow?.Lease.Stop();
            _roomLifetime.End(flow?.State);
            _activeRoomFlow = null;
            flow?.Cancel();
            ClearCurrentRoomState();
            if (retainNotice) LastRoomNotice = message;
            StopPreviousRoomNetwork();
            ScheduleRoomCleanup(flow);
            // Cleanup and network-stop observers can start another room synchronously.
            if (!_roomServiceDisposed && notification == _roomStateNotification) SetRoomFlowState(message, false);
        }

        // Preserve only this rejected attempt and earlier attempts, never a future successful rejoin.
        public void ForgetCurrentRoomMembership()
        {
            RoomFlow flow = _activeRoomFlow;
            if (flow != null) _roomFlows.PreserveMembership(flow.Ticket);
            _ownedRoomId = null;
            LeaveRoomWithNotice("같은 계정이 이미 방에 연결되어 있습니다.");
        }

        public void NotifyRoomAuthenticationFailed()
        {
            SetRoomFlowState(_networkRoomFlow, "방 참가자 인증에 실패했습니다. 다시 참가해 주세요.", false);
        }

        public void NotifyRoomNetworkDisconnected(bool preserveMembership, bool authenticationFailed)
        {
            RoomFlow flow = _networkRoomFlow;
            _networkRoomFlow = null;
            if (flow == null) return;
            bool current = IsCurrentRoomFlow(flow);
            uint notification = current ? ++_roomStateNotification : _roomStateNotification;
            if (preserveMembership) _roomFlows.PreserveMembership(flow.Ticket);
            if (current)
            {
                flow.Lease.Stop();
                _roomLifetime.End(flow.State);
                _activeRoomFlow = null;
                ClearCurrentRoomState();
                LastRoomNotice = preserveMembership ? "같은 계정이 이미 방에 연결되어 있습니다." :
                    authenticationFailed ? "방 참가자 인증에 실패했습니다. 다시 참가해 주세요." : RoomClosedMessage;
            }
            flow.Cancel();
            ScheduleRoomCleanup(flow);
            if (!current || notification != _roomStateNotification) return;
            SetRoomFlowState(LastRoomNotice, false);
        }

        public void RefreshCurrentRoomInfo(Action<RoomInfo> callback = null)
        {
            RoomFlow flow = _activeRoomFlow;
            if (!IsCurrentRoomFlow(flow) || string.IsNullOrEmpty(_joinedRoomId))
            { NotifyRoomInfo(callback, default); return; }
            GetActiveRoomInfos(rooms =>
            {
                if (!IsCurrentRoomFlow(flow)) { NotifyRoomInfo(callback, default); return; }
                if (rooms.TryGetValue(flow.Ticket.RoomId, out RoomInfo info))
                {
                    flow.Info = info;
                    _currentRoomInfo = info;
                }
                NotifyRoomInfo(callback, flow.Info);
            });
        }

        public void AdminDeleteRoom(string adminKey, string roomId, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                NotifyRoomResult(callback, false, "Admin key is empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                NotifyRoomResult(callback, false, "Room id is empty.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "adminKey", adminKey.Trim() },
                { "roomId", roomId.Trim() }
            };

            ExecuteRoomAdminCloudScript(ADMIN_DELETE_ROOM_FUNCTION, parameters, (ok, message) =>
            {
                if (ok)
                {
                    _lastLoadedRoomInfos.Remove(roomId.Trim());
                    _roomListSnapshot.Invalidate();
                    NotifyRoomRegistryChanged();
                }

                NotifyRoomResult(callback, ok, message);
            });
        }

        public void AdminValidateRoomKey(string adminKey, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                NotifyRoomResult(callback, false, "Admin key is empty.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "adminKey", adminKey.Trim() }
            };

            ExecuteRoomAdminCloudScript(ADMIN_VALIDATE_ROOM_KEY_FUNCTION, parameters, callback);
        }

        public void AdminClearRoomRegistry(string adminKey, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                NotifyRoomResult(callback, false, "Admin key is empty.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "adminKey", adminKey.Trim() }
            };

            ExecuteRoomAdminCloudScript(ADMIN_CLEAR_ROOM_REGISTRY_FUNCTION, parameters, (ok, message) =>
            {
                if (ok)
                {
                    _lastLoadedRoomInfos.Clear();
                    _roomListSnapshot.Invalidate();
                    NotifyRoomRegistryChanged();
                }

                NotifyRoomResult(callback, ok, message);
            });
        }

        private void ExecuteRoomAdminCloudScript(string functionName, Dictionary<string, object> parameters, Action<bool, string> callback)
        {
            _executeCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = functionName,
                    FunctionParameter = parameters,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        string message = FormatCloudScriptError(result);
                        Debug.LogError($"[PlayFab] {functionName} failed: {message}");
                        NotifyRoomResult(callback, false, message);
                        return;
                    }

                    Debug.Log($"[PlayFab] {functionName} completed.");
                    NotifyRoomResult(callback, true, "Completed.");
                },
                error =>
                {
                    string message = error.GenerateErrorReport();
                    Debug.LogError($"[PlayFab] {functionName} request failed: {message}");
                    NotifyRoomResult(callback, false, message);
                });
        }

        private static string FormatCloudScriptError(ExecuteCloudScriptResult result)
        {
            var builder = new StringBuilder();

            if (result.Error != null)
            {
                if (!string.IsNullOrWhiteSpace(result.Error.Error))
                    builder.Append(result.Error.Error).Append(": ");

                builder.Append(result.Error.Message);

                if (!string.IsNullOrWhiteSpace(result.Error.StackTrace))
                    builder.Append("\n").Append(result.Error.StackTrace);
            }

            if (result.Logs != null && result.Logs.Count > 0)
            {
                builder.Append("\nLogs:");
                foreach (var logLine in result.Logs)
                {
                    builder.Append("\n[")
                        .Append(logLine.Level)
                        .Append("] ")
                        .Append(logLine.Message);
                }
            }

            return builder.Length > 0 ? builder.ToString() : "CloudScript failed.";
        }

        private void OnApplicationQuit()
        {
            _roomServiceStopped = true;
            LeaveCurrentRoom();
        }

        private async void ScheduleRoomCleanup(RoomFlow flow)
        {
            if (flow == null || !flow.State.TryQueueCleanup()) return;
            try
            {
                ExecuteCloudScriptResult result = await ExecuteRoomMutation(flow, LEAVE_ROOM_FUNCTION,
                    new Dictionary<string, object> { { "roomId", flow.Ticket.RoomId } }, cleanup: true);
                if (result == null) return;
                if (result.Error != null) throw new InvalidOperationException("Room cleanup failed.");
                flow.State.ConfirmCleanup();
                if (_roomServiceDisposed || !_roomFlows.ShouldCompensate(flow.Ticket, CurrentAccountId) ||
                    !string.Equals(flow.Ticket.AccountId, CurrentAccountId, StringComparison.OrdinalIgnoreCase)) return;
                int count = result.FunctionResult is IDictionary<string, object> values ? GetIntValue(values, "playerCount", 0) : 0;
                string roomId = flow.Ticket.RoomId;
                ApplyRoomCleanupToList(roomId, count, flow.IsHost);
                NotifyRoomRegistryChanged();
            }
            catch (Exception)
            {
                flow.State.ReleaseCleanupQueue();
                if (!_roomServiceDisposed) Debug.LogWarning("[PlayFab] Cancelled room membership cleanup could not be confirmed.");
            }
        }

        private RoomInfo GetRoomInfoFallback(RoomFlow flow)
        {
            RoomInfo session = flow.Info;
            RoomInfo listed = default;
            double now = Time.realtimeSinceStartupAsDouble;
            if (_roomListSnapshot.CanReuse(now) &&
                _lastLoadedRoomInfos.TryGetValue(flow.Ticket.RoomId, out RoomInfo cached) &&
                double.IsFinite(cached.ValidUntil) && cached.ValidUntil > now)
                listed = cached;
            return new RoomInfo(
                !string.IsNullOrWhiteSpace(session.RoomName) ? session.RoomName :
                    !string.IsNullOrWhiteSpace(listed.RoomName) ? listed.RoomName : "Unnamed Room",
                !string.IsNullOrWhiteSpace(session.MasterName) ? session.MasterName :
                    !string.IsNullOrWhiteSpace(listed.MasterName) ? listed.MasterName : "Unknown",
                session.PlayerCount > 0 ? session.PlayerCount : listed.PlayerCount > 0 ? listed.PlayerCount : 1,
                !string.IsNullOrWhiteSpace(session.RelayJoinCode) ? session.RelayJoinCode : listed.RelayJoinCode);
        }

        private string GetCurrentPlayerNickname()
        {
            string nickname = GlobalDataManager.Instance != null
                ? GlobalDataManager.Instance.PlayerNickname
                : null;

            return string.IsNullOrWhiteSpace(nickname) ? "Unknown" : nickname.Trim();
        }

        #endregion

        #region [User Data Sync]

        /// <summary>
        /// ?뚮젅?댁뼱??湲곕낯 ?ㅽ꺈 諛?怨꾩궛???뚯깮 ?ㅽ꺈 ?뺣낫瑜?PlayFab????ν빀?덈떎. (10媛????쒗븳???쇳빐 ??踰덉뿉 ?섎늻???꾩넚)
        /// </summary>
        public void LoadPlayerProfile(Action<PlayerProfileLoadResult> completed)
        {
            if (_roomServiceDisposed || !isActiveAndEnabled)
            {
                NotifyProfileCallback(() => completed?.Invoke(new PlayerProfileLoadResult(
                    ProfileLoadStatus.Failure, error: "Player profile service is inactive.")));
                return;
            }
            _profileRepository.Load(completed);
        }

        public void BeginProfileSession()
        {
            _ownedRoomId = null;
            LeaveCurrentRoom();
            _profileRepository.ResetSession();
        }

        public void SavePlayerStats(StatContainer stats, float atk, float maxHp, float defPercent,
            float pene, float regen, float moveSpd, float atkSpd, Action<bool, string> completed = null)
        {
            // Compatibility wrapper: derived values are recomputed from the saved investment.
            SavePlayerStatPresetData(completed);
        }

        public void SavePlayerStatPresetData(Action<bool, string> completed = null)
        {
            if (_roomServiceDisposed || !isActiveAndEnabled)
            {
                NotifyProfileCallback(() => completed?.Invoke(false, "Player profile service is inactive."));
                return;
            }
            GlobalDataManager data = GlobalDataManager.Instance;
            if (data == null || !data.HasLoadedPlayerStats)
            {
                Debug.LogWarning("[PlayFab] Profile must load successfully before it can be saved.");
                NotifyProfileCallback(() => completed?.Invoke(false, "Player profile has not loaded yet."));
                return;
            }
            _profileRepository.Save(data.CaptureProfileSnapshot(), (success, error) =>
            {
                if (!success) Debug.LogError("[PlayFab] Profile save failed: " + error);
                completed?.Invoke(success, error);
            });
        }

        public void LoadPlayerStats(Action<StatContainer> onLoaded, Action<string> onFailed = null)
        {
            LoadPlayerProfile(result =>
            {
                if (result.Succeeded) onLoaded?.Invoke(result.Profile.Stats);
                else onFailed?.Invoke(result.Error);
            });
        }

        public void LoadCombatRecord(Action<int, int> onLoaded, Action<string> onFailed = null)
        {
            LoadPlayerProfile(result =>
            {
                if (result.Succeeded) onLoaded?.Invoke(result.Kills, result.Deaths);
                else onFailed?.Invoke(result.Error);
            });
        }

        public void SaveCombatRecord(int kills, int deaths)
        {
            if (_roomServiceDisposed || !isActiveAndEnabled) return;
            // Legacy, untrusted personal display data. Competitive rewards require a trusted backend.
            _profileRepository.SaveCombatRecord(kills, deaths, (success, error) =>
            {
                if (!success) Debug.LogError("[PlayFab] Combat record save failed: " + error);
            });
        }

        private static void NotifyProfileCallback(Action callback)
        {
            try { callback(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private double ParseValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0;
            if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 0;
        }

        #endregion
        
        /// <summary>
        /// ?밸━/?뚮젅??湲곕줉??由щ뜑蹂대뱶???낅뜲?댄듃?⑸땲??
        /// </summary>
        public void UpdateStatistics(int points)
        {
            Debug.LogWarning("[PlayFab] Client competitive statistic writes are disabled. Results require authenticated host submission.");
        }

    }
}
