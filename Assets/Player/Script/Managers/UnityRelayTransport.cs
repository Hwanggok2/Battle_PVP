using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mirror;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UtpNetworkConnection = Unity.Networking.Transport.NetworkConnection;

namespace BattlePvp.Networking
{
    public sealed partial class UnityRelayTransport : Transport
    {
        [SerializeField] private string _connectionType = "udp";

        private const int RelayApiMaxAttempts = 3;
        public const int ReliablePacketCapacity = 60_000;
        public const int PacketBatchThreshold = 1_200;
        private const int ReliableWindowSize = 256; // Absorb a short burst of fragmented room/spawn messages.
        private const int RelayApiBackoffMilliseconds = 500;
        private const int RelayPreparationTimeoutMilliseconds = 30_000;
        public const double ClientConnectTimeoutSeconds = 20d;
        private const string SeoulRelayRegionId = "asia-northeast3";
        private const string TokyoRelayRegionId = "asia-northeast1";

        private NetworkDriver _serverDriver;
        private NetworkDriver _clientDriver;
        private UtpNetworkConnection _clientConnection;
        private NetworkPipeline _serverReliablePipeline;
        private NetworkPipeline _clientReliablePipeline;
        private readonly Dictionary<int, UtpNetworkConnection> _serverConnections = new Dictionary<int, UtpNetworkConnection>();
        private readonly List<int> _connectionIds = new List<int>();
        private readonly List<int> _disconnectedIds = new List<int>();
        private readonly Dictionary<int, ReliableSendBacklog> _serverBacklogs = new Dictionary<int, ReliableSendBacklog>();
        private readonly ReliableSendBacklog _clientBacklog = new ReliableSendBacklog();
        private NativeArray<byte> _sendBuffer;
        private NativeArray<byte> _receiveBuffer;
        private readonly byte[] _receiveBytes = new byte[ReliablePacketCapacity];
        private bool _clientFlushPending = true, _serverFlushPending = true, _directFlushPending = true;
        private bool _isPolling;
        private int _nextConnectionId = 1;

        private RelayServerData _serverRelayData;
        private RelayServerData _clientRelayData;
        private bool _hasPreparedServerRelay;
        private bool _hasPreparedClientRelay;
        private bool _clientConnected;
        private bool _clientDisconnectPending;
        private string _clientConnectFailure;
        private uint _clientAttempt;
        private double _clientConnectDeadline;
        private RelayConnectionStatus _lastClientRelayStatus;
        private long _preparationVersion;
        private CancellationTokenSource _preparationCancellation;
        private static Task _unityServicesReady;

        public string LastJoinCode { get; private set; }
        public string LastRelayRegion { get; private set; }
        public string LastRelayRegionLabel { get; private set; }
        public string ConnectionProtocol => ClientUsingRtc ? "webrtc-udp" : GetRelayConnectionType();
        public bool CollectDiagnostics { get; set; }
        public long SentBytes { get; private set; }
        public long ReceivedBytes { get; private set; }
        public long SendQueueFullCount { get; private set; }
        public long UnreliableSendDropCount { get; private set; }
        public long SendErrorCount { get; private set; }
        public double PollMilliseconds { get; private set; }
        public double FlushMilliseconds { get; private set; }
        public int BacklogBytes
        {
            get { int bytes = _clientBacklog.Bytes + RtcBacklogBytes; foreach (var queue in _serverBacklogs.Values) bytes += queue.Bytes; return bytes; }
        }
        public double OldestBacklogSeconds
        {
            get
            {
                double now = Time.realtimeSinceStartupAsDouble, oldest = _clientBacklog.OldestWaitSeconds(now);
                foreach (var queue in _serverBacklogs.Values) oldest = Math.Max(oldest, queue.OldestWaitSeconds(now));
                return Math.Max(oldest, RtcBacklogAge(now));
            }
        }
        public bool ServerRelayReady => _serverDriver.IsCreated &&
            _serverDriver.GetRelayConnectionStatus() == RelayConnectionStatus.Established;
        public bool ServerRelayFailed => !_serverDriver.IsCreated ||
            _serverDriver.GetRelayConnectionStatus() == RelayConnectionStatus.AllocationInvalid;

        public override bool Available() => true;

        public async Task<string> PrepareHostAsync(int maxConnections, CancellationToken cancellation = default)
        {
            CancellationTokenSource preparation = BeginPreparation(cancellation);
            long version = _preparationVersion;
            CancellationToken token = preparation.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                await ServiceTaskDeadline.WaitAsync(EnsureUnityServicesAsync(), token);
                int relayConnections = BattleNetworkManager.PlayerCapacity - 1;
                Allocation allocation = await RunRelayApiWithRetryAsync("CreateAllocationAsync (QoS)",
                    () => RelayService.Instance.CreateAllocationAsync(relayConnections), token);
                string joinCode = await RunRelayApiWithRetryAsync("GetJoinCodeAsync",
                    () => RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId), token);
                RelayServerData serverData = allocation.ToRelayServerData(GetRelayConnectionType());
#if !UNITY_WEBGL
                var stunServer = await RoomStunNetworkInterface.ResolveAsync(token);
#endif
                RequireCurrentPreparation(version, token);
                // Commit only after every stage succeeds. Detached SDK tasks never write these fields.
                _serverRelayData = serverData;
#if !UNITY_WEBGL
                _stunServer = stunServer;
#endif
                LastJoinCode = joinCode;
                LastRelayRegion = allocation.Region;
                LastRelayRegionLabel = GetRegionLabel(LastRelayRegion);
                _hasPreparedServerRelay = true;
                return joinCode;
            }
            finally { FinishPreparation(preparation); }
        }

        public async Task PrepareClientAsync(string joinCode, CancellationToken cancellation = default, string roomId = null, string publicEndpoint = null)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                throw new ArgumentException("Relay join code is empty.", nameof(joinCode));
            CancellationTokenSource preparation = BeginPreparation(cancellation);
            long version = _preparationVersion;
            CancellationToken token = preparation.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                var directEndpoint = await RoomLanDiscovery.FindAsync(RoomLanDiscovery.RoomKey(roomId, joinCode), token);
                System.Net.IPEndPoint publicCandidate = null;
#if !UNITY_WEBGL
                RoomDirectEndpoint.TryParse(publicEndpoint, out publicCandidate);
#endif
                RequireCurrentPreparation(version, token);
                _clientRelayJoinCode = joinCode.Trim();
                _directEndpoint = directEndpoint ?? publicCandidate;
                _publicDirectCandidate = directEndpoint != null && !directEndpoint.Equals(publicCandidate) ? publicCandidate : null;
                if (_directEndpoint != null)
                {
                    RoomConnectionDiagnostics.Stage("direct_udp_prepared");
                    return; // Do not join the Relay allocation unless direct UDP is unavailable.
                }
                await PrepareClientRelayDataAsync(_clientRelayJoinCode, version, token);
                RoomConnectionDiagnostics.Stage("relay_client_prepared_" + GetRelayConnectionType());
            }
            finally { FinishPreparation(preparation); }
        }

        private CancellationTokenSource BeginPreparation(CancellationToken cancellation)
        {
            CancelPendingPreparation();
            _hasPreparedClientRelay = false;
            _hasPreparedServerRelay = false;
            _directEndpoint = null;
            _publicDirectCandidate = null;
            _clientRelayJoinCode = null;
            _lanRoomKey = null;
            LastJoinCode = LastRelayRegion = LastRelayRegionLabel = string.Empty;
            var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            UnityRealtimeTimer.CancelAfter(preparation, RelayPreparationTimeoutMilliseconds);
            _preparationCancellation = preparation;
            return preparation;
        }

        public void CancelPendingPreparation()
        {
            _preparationVersion++;
            CancellationTokenSource pending = _preparationCancellation;
            _preparationCancellation = null;
            pending?.Cancel();
            _clientFallbackCancellation?.Cancel();
            _clientFallbackCancellation = null;
            _clientPreparingFallback = false;
        }

        private void RequireCurrentPreparation(long version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (this == null || version != _preparationVersion) throw new OperationCanceledException();
        }

        private void FinishPreparation(CancellationTokenSource preparation)
        {
            if (ReferenceEquals(_preparationCancellation, preparation)) _preparationCancellation = null;
            preparation.Dispose();
        }

        private void OnDestroy() { CancelPendingPreparation(); _lanDiscovery.Dispose(); }

        private static string GetRegionLabel(string regionId)
        {
            if (string.IsNullOrWhiteSpace(regionId))
                return string.Empty;

            if (string.Equals(regionId, SeoulRelayRegionId, StringComparison.OrdinalIgnoreCase))
                return "Seoul";
            if (string.Equals(regionId, TokyoRelayRegionId, StringComparison.OrdinalIgnoreCase))
                return "Tokyo";
            return regionId;
        }

        private static Task EnsureUnityServicesAsync()
        {
            // Initialization/sign-in mutates shared SDK state. Never overlap it after a caller times out.
            if (_unityServicesReady == null || _unityServicesReady.IsFaulted || _unityServicesReady.IsCanceled)
                _unityServicesReady = InitializeUnityServicesAsync();
            else if (_unityServicesReady.IsCompleted &&
                (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn))
                _unityServicesReady = InitializeUnityServicesAsync();
            return _unityServicesReady;
        }

        private static async Task InitializeUnityServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private string GetRelayConnectionType()
        {
#if UNITY_WEBGL
            // Relay only permits WebSocket Secure while compiling for WebGL.
            // This also applies to Editor Play Mode with WebGL selected as the build target.
            return "wss";
#else
            // Recover safely from legacy serialized values such as "UDP".
            string connectionType = _connectionType?.Trim().ToLowerInvariant();
            return connectionType == "wss" ? "wss" : "udp";
#endif
        }

        private static async Task<T> RunRelayApiWithRetryAsync<T>(string operationName, Func<Task<T>> operation,
            CancellationToken token)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= RelayApiMaxAttempts; attempt++)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    return await ServiceTaskDeadline.WaitAsync(operation(), token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (attempt < RelayApiMaxAttempts)
                {
                    lastException = ex;
                    int delayMilliseconds = RelayApiBackoffMilliseconds * attempt;
                    Debug.LogWarning(
                        $"[UnityRelayTransport] {operationName} failed on attempt {attempt}/{RelayApiMaxAttempts}. Retrying in {delayMilliseconds}ms. {ex.GetType().Name}");
                    await UnityRealtimeTimer.DelayAsync(delayMilliseconds, token);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            throw lastException ?? new InvalidOperationException($"{operationName} failed.");
        }

        public override bool ClientConnected() => _clientConnected;

        public override void ClientConnect(string address)
        {
            unchecked { _clientAttempt++; }
            _clientConnected = false;
            _clientDisconnectPending = true;
            _clientConnectDeadline = Time.realtimeSinceStartupAsDouble + ClientConnectTimeoutSeconds;
            _lastClientRelayStatus = RelayConnectionStatus.NotEstablished;
            _clientConnectFailure = null;
            RoomConnectionDiagnostics.Stage("relay_client_connect_started");
            if (!_hasPreparedClientRelay && _directEndpoint == null)
            {
                _clientConnectFailure = "Relay client data was not prepared before ClientConnect.";
                return;
            }

            try
            {
                CreateClientDriver(_directEndpoint != null);
            }
            catch (Exception)
            {
                _clientConnectFailure = "Relay client transport initialization failed.";
            }
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (_clientRtc != null && _clientRtc.SendingDirect && !_clientRtc.Ended)
            { _clientRtc.Send(segment, channelId, Time.realtimeSinceStartupAsDouble); return; }
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
                return;

            Send(_clientDriver, GetClientPipeline(channelId), _clientConnection, segment, channelId, true, 0);
        }

        public override void ClientDisconnect()
        {
            try
            {
                if (_clientDriver.IsCreated && _clientConnection.IsCreated)
                {
                    _clientConnection.Disconnect(_clientDriver);
                    _clientDriver.ScheduleUpdate().Complete();
                }
            }
            finally { FinishClientDisconnect(); }
        }

        private void FinishClientDisconnect()
        {
            ClearClientRtc();
            bool notify = _clientDisconnectPending;
            _clientDisconnectPending = false;
            _clientConnectFailure = null;
            _clientConnected = false;
            _clientConnectDeadline = 0d;
            _directConnectDeadline = 0d;
            _clientUsingDirect = false;
            _directEndpoint = null;
            _publicDirectCandidate = null;
            _clientFallbackCancellation?.Cancel();
            _clientFallbackCancellation = null;
            _clientPreparingFallback = false;
            _clientBacklog.Clear();
            _clientConnection = default;
            DisposeClientDriver();
            // Mirror's callback calls Shutdown -> ClientDisconnect again. Commit cleanup first.
            if (notify) OnClientDisconnected?.Invoke();
        }

        // This also runs when an interrupted network loop is no longer polling the driver.
        // Use wall time so a low frame rate/timeScale cannot extend an abandoned join indefinitely.
        private new void Update() => CheckClientConnectTimeout(Time.realtimeSinceStartupAsDouble);

        private bool CheckClientConnectTimeout(double now)
        {
            if (_directConnectDeadline > 0 && now >= _directConnectDeadline && TryAdvanceClientRoute()) return false;
            if (!_clientDisconnectPending || _clientConnected || _clientConnectDeadline <= 0d || now < _clientConnectDeadline)
                return false;
            RoomConnectionDiagnostics.Stage("relay_client_connect_timeout");
            FailConnection(true, 0, "Relay game connection did not complete within 20 seconds.", TransportError.Timeout);
            return true;
        }

        public override Uri ServerUri() => new Uri("relay://localhost");

        public override bool ServerActive() => _serverDriver.IsCreated;

        public override void ServerStart()
        {
            if (!_hasPreparedServerRelay)
            {
                OnServerError?.Invoke(0, TransportError.Unexpected, "Relay server data was not prepared before ServerStart.");
                return;
            }

            _serverDriver = CreateRoomDriver(true, ref _serverRelayData, out _serverReliablePipeline);

            if (_serverDriver.Bind(NetworkEndpoint.AnyIpv4) < 0)
            {
                DisposeServerDriver();
                OnServerError?.Invoke(0, TransportError.Unexpected, "Failed to bind Unity Relay transport.");
                return;
            }

            if (_serverDriver.Listen() < 0)
            {
                DisposeServerDriver();
                OnServerError?.Invoke(0, TransportError.Unexpected, "Failed to listen on Unity Relay transport.");
                return;
            }
            StartDirectServer();
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (_serverRtc.TryGetValue(connectionId, out var rtc) && rtc.SendingDirect && !rtc.Ended)
            { rtc.Send(segment, channelId, Time.realtimeSinceStartupAsDouble); return; }
            bool direct = _directConnections.Contains(connectionId);
            ref NetworkDriver driver = ref ServerDriver(direct);
            if (!driver.IsCreated || !_serverConnections.TryGetValue(connectionId, out UtpNetworkConnection connection))
                return;

            Send(driver, channelId == Channels.Reliable ? (direct ? _directServerPipeline : _serverReliablePipeline) : NetworkPipeline.Null,
                connection, segment, channelId, false, connectionId);
        }

        public override void ServerDisconnect(int connectionId)
        {
            ClearServerRtc(connectionId);
            if (!_serverConnections.TryGetValue(connectionId, out UtpNetworkConnection connection)) return;
            bool direct = _directConnections.Remove(connectionId);
            ref NetworkDriver driver = ref ServerDriver(direct);
            _serverConnections.Remove(connectionId);
            _serverBacklogs.Remove(connectionId);
            try
            {
                if (driver.IsCreated && connection.IsCreated)
                {
                    connection.Disconnect(driver);
                    driver.ScheduleUpdate().Complete();
                }
            }
            finally { OnServerDisconnected?.Invoke(connectionId); }
        }

        public override string ServerGetClientAddress(int connectionId) =>
            (_directConnections.Contains(connectionId) ? "direct:" : "relay:") + connectionId;

        public override void ServerStop()
        {
            ClearServerRtc();
            bool disconnectedAny = false;
            _lanDiscovery.Dispose();
            foreach (var pair in _serverConnections)
            {
                ref NetworkDriver driver = ref ServerDriver(_directConnections.Contains(pair.Key));
                if (pair.Value.IsCreated && driver.IsCreated)
                {
                    pair.Value.Disconnect(driver);
                    disconnectedAny = true;
                }
            }

            if (disconnectedAny && _serverDriver.IsCreated)
                _serverDriver.ScheduleUpdate().Complete();
            if (disconnectedAny && _directServerDriver.IsCreated) _directServerDriver.ScheduleUpdate().Complete();

            _serverConnections.Clear();
            _serverBacklogs.Clear();
            _directConnections.Clear();
            if (_directServerDriver.IsCreated) _directServerDriver.Dispose();
#if !UNITY_WEBGL
            _directNetwork = null;
#endif
            DisposeServerDriver();
        }

        public override int GetMaxPacketSize(int channelId = Channels.Reliable)
        {
            return channelId == Channels.Reliable ? ReliablePacketCapacity : PacketBatchThreshold;
        }

        public override int GetBatchThreshold(int channelId = Channels.Reliable) => PacketBatchThreshold;

        public override void Shutdown()
        {
            CancelPendingPreparation();
            ClientDisconnect();
            ServerStop();
            if (_sendBuffer.IsCreated) _sendBuffer.Dispose();
            if (_receiveBuffer.IsCreated) _receiveBuffer.Dispose();
            _hasPreparedClientRelay = false;
            _hasPreparedServerRelay = false;
            LastJoinCode = string.Empty;
            LastRelayRegion = string.Empty;
            LastRelayRegionLabel = string.Empty;
        }

        public override void ClientEarlyUpdate()
        {
            if (_isPolling) return;
            _isPolling = true;
            double started = CollectDiagnostics ? Time.realtimeSinceStartupAsDouble : 0d;
            try { PollClient(); PollRtc(true); }
            finally { if (started > 0d) PollMilliseconds += (Time.realtimeSinceStartupAsDouble - started) * 1000d; _isPolling = false; }
        }

        private void PollClient()
        {
            if (CheckClientConnectTimeout(Time.realtimeSinceStartupAsDouble)) return;
            if (_clientPreparingFallback) return;
            if (_clientConnectFailure != null)
            {
                if (TryAdvanceClientRoute()) return;
                uint failedAttempt = _clientAttempt;
                string failure = _clientConnectFailure;
                _clientConnectFailure = null;
                // Mirror creates its connection after ClientConnect returns, so fail on the next poll.
                try { OnClientError?.Invoke(TransportError.Unexpected, failure); }
                finally { if (_clientAttempt == failedAttempt) FinishClientDisconnect(); }
                return;
            }
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
            {
                if (_clientDisconnectPending)
                    FailConnection(true, 0, "Relay client has no valid driver or connection.");
                return;
            }

            _clientFlushPending = true;
            _clientDriver.ScheduleUpdate().Complete();
            RelayConnectionStatus status = _clientDriver.GetRelayConnectionStatus();
            if (status != _lastClientRelayStatus)
            {
                _lastClientRelayStatus = status;
                RoomConnectionDiagnostics.Stage("relay_client_status_" + status);
            }
            if (status == RelayConnectionStatus.AllocationInvalid)
            {
                FailConnection(true, 0, "Relay allocation is no longer valid.");
                return;
            }
            FlushBacklog(_clientBacklog, _clientDriver, _clientReliablePipeline, _clientConnection, true, 0);

            while (_clientDriver.IsCreated && _clientConnection.IsCreated)
            {
                NetworkEvent.Type eventType = _clientConnection.PopEvent(
                    _clientDriver,
                    out DataStreamReader reader,
                    out NetworkPipeline pipeline);
                if (eventType == NetworkEvent.Type.Empty)
                    return;

                switch (eventType)
                {
                    case NetworkEvent.Type.Connect:
                        _clientConnected = true;
                        _clientConnectDeadline = 0d;
                        _directConnectDeadline = 0d;
                        RoomConnectionDiagnostics.Stage(_clientUsingDirect ? "direct_udp_transport_connected" : "relay_client_transport_connected");
                        OnClientConnected?.Invoke();
                        break;
                    case NetworkEvent.Type.Data:
                        ReceiveRoomData(true, 0,
                            ReadPayload(reader),
                            ResolveReceivedChannel(pipeline, _clientReliablePipeline));
                        break;
                    case NetworkEvent.Type.Disconnect:
                        if (TryAdvanceClientRoute()) return;
                        RoomConnectionDiagnostics.Stage("relay_client_disconnected_code_" + (reader.Length > 0 ? reader.ReadByte() : 0));
                        FinishClientDisconnect();
                        return;
                }

                // Mirror callbacks can synchronously stop the client and dispose the driver.
                if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
                    return;
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (_isPolling) return;
            _isPolling = true;
            double started = CollectDiagnostics ? Time.realtimeSinceStartupAsDouble : 0d;
            try { _lanDiscovery.Poll(); PollServer(false); PollServer(true); PollRtc(false); }
            finally { if (started > 0d) PollMilliseconds += (Time.realtimeSinceStartupAsDouble - started) * 1000d; _isPolling = false; }
        }

        private void PollServer(bool direct)
        {
            ref NetworkDriver driver = ref ServerDriver(direct);
            NetworkPipeline reliable = direct ? _directServerPipeline : _serverReliablePipeline;
            if (!driver.IsCreated)
                return;

            if (direct) _directFlushPending = true; else _serverFlushPending = true;
            driver.ScheduleUpdate().Complete();
            if (!direct && driver.GetRelayConnectionStatus() == RelayConnectionStatus.AllocationInvalid)
            {
                // The room service observes this terminal state and stops advertising the host.
                return;
            }

            UtpNetworkConnection connection;
            while ((connection = driver.Accept()) != default)
            {
                int connectionId = _nextConnectionId++;
                _serverConnections[connectionId] = connection;
                if (direct) _directConnections.Add(connectionId);
                _serverBacklogs[connectionId] = new ReliableSendBacklog();
                RoomConnectionDiagnostics.Stage(direct ? "direct_udp_peer_accepted" : "relay_server_peer_accepted");
                OnServerConnectedWithAddress?.Invoke(connectionId, ServerGetClientAddress(connectionId));
                if (!driver.IsCreated) return;
            }

            _disconnectedIds.Clear();
            _connectionIds.Clear();
            _connectionIds.AddRange(_serverConnections.Keys);
            foreach (int connectionId in _connectionIds)
            {
                if (_directConnections.Contains(connectionId) != direct) continue;
                if (!_serverConnections.TryGetValue(connectionId, out UtpNetworkConnection serverConnection))
                    continue;
                if (_serverBacklogs.TryGetValue(connectionId, out ReliableSendBacklog backlog))
                    FlushBacklog(backlog, driver, reliable, serverConnection, false, connectionId);
                if (!driver.IsCreated) return;
                if (!_serverConnections.ContainsKey(connectionId)) continue;

                NetworkEvent.Type eventType;
                while ((eventType = driver.PopEventForConnection(
                           serverConnection,
                           out DataStreamReader reader,
                           out NetworkPipeline pipeline)) != NetworkEvent.Type.Empty)
                {
                    switch (eventType)
                    {
                        case NetworkEvent.Type.Data:
                            ReceiveRoomData(false, connectionId,
                                ReadPayload(reader),
                                ResolveReceivedChannel(pipeline, reliable));
                            break;
                        case NetworkEvent.Type.Disconnect:
                            _disconnectedIds.Add(connectionId);
                            break;
                    }
                    // A Mirror callback may synchronously disconnect or shut down the server.
                    if (!driver.IsCreated) return;
                    if (!_serverConnections.ContainsKey(connectionId)) break;
                }
            }

            foreach (int connectionId in _disconnectedIds)
            {
                ClearServerRtc(connectionId);
                _serverBacklogs.Remove(connectionId);
                _directConnections.Remove(connectionId);
                if (_serverConnections.Remove(connectionId)) OnServerDisconnected?.Invoke(connectionId);
            }
        }

        public override void ClientLateUpdate()
        {
            if (_clientDriver.IsCreated && _clientFlushPending)
            {
                double started = CollectDiagnostics ? Time.realtimeSinceStartupAsDouble : 0d;
                _clientDriver.ScheduleFlushSend().Complete();
                _clientFlushPending = false;
                if (CollectDiagnostics) FlushMilliseconds += (Time.realtimeSinceStartupAsDouble - started) * 1000d;
            }
        }

        public override void ServerLateUpdate()
        {
            double started = CollectDiagnostics ? Time.realtimeSinceStartupAsDouble : 0d;
            if (_directServerDriver.IsCreated && _directFlushPending)
            { _directServerDriver.ScheduleFlushSend().Complete(); _directFlushPending = false; }
            if (_serverDriver.IsCreated && _serverFlushPending)
            { _serverDriver.ScheduleFlushSend().Complete(); _serverFlushPending = false; }
            if (CollectDiagnostics) FlushMilliseconds += (Time.realtimeSinceStartupAsDouble - started) * 1000d;
        }

        private NetworkPipeline GetClientPipeline(int channelId)
        {
            return channelId == Channels.Reliable ? _clientReliablePipeline : NetworkPipeline.Null;
        }

        private static NetworkDriver CreateRelayDriver(NetworkSettings settings)
        {
#if UNITY_WEBGL
            return NetworkDriver.Create(new WebSocketNetworkInterface(), settings);
#else
            return NetworkDriver.Create(settings);
#endif
        }

        private static int ResolveReceivedChannel(NetworkPipeline pipeline, NetworkPipeline reliablePipeline)
        {
            return pipeline == reliablePipeline ? Channels.Reliable : Channels.Unreliable;
        }

        private void Send(
            NetworkDriver driver,
            NetworkPipeline pipeline,
            UtpNetworkConnection connection,
            ArraySegment<byte> segment,
            int channelId,
            bool client,
            int connectionId)
        {
            if (segment.Array == null || segment.Count > GetMaxPacketSize(channelId))
            {
                FailConnection(client, connectionId, "Relay packet exceeds the supported size.");
                return;
            }
            ReliableSendBacklog backlog = null;
            if (channelId == Channels.Reliable)
            {
                if (client) backlog = _clientBacklog;
                else _serverBacklogs.TryGetValue(connectionId, out backlog);
                if (backlog != null && backlog.Count > 0)
                {
                    QueueReliable(backlog, segment, client, connectionId);
                    return;
                }
            }
            if (!TrySend(driver, pipeline, connection, segment, channelId, client, connectionId) && backlog != null)
                QueueReliable(backlog, segment, client, connectionId);
        }

        // False means BeginSend did not consume the packet and a retry is safe.
        private bool TrySend(NetworkDriver driver, NetworkPipeline pipeline, UtpNetworkConnection connection,
            ArraySegment<byte> segment, int channelId, bool client, int connectionId)
        {
            int result = driver.BeginSend(pipeline, connection, out DataStreamWriter writer, segment.Count);
            if (result < 0)
            {
                if (result == (int)Unity.Networking.Transport.Error.StatusCode.NetworkSendQueueFull)
                {
                    SendQueueFullCount++;
                    if (channelId != Channels.Reliable) UnreliableSendDropCount++;
                    return false;
                }
                SendErrorCount++;
                FailConnection(client, connectionId, $"Relay BeginSend failed ({result}).");
                return true;
            }

            if (!_sendBuffer.IsCreated)
                _sendBuffer = new NativeArray<byte>(ReliablePacketCapacity, Allocator.Persistent);
            NativeArray<byte>.Copy(segment.Array, segment.Offset, _sendBuffer, 0, segment.Count);
            writer.WriteBytes(_sendBuffer.GetSubArray(0, segment.Count));

            result = driver.EndSend(writer);
            // Every driver update (including ACKs/keepalives) and send invalidates the flush.
            // Repeated timing hooks with no intervening work need no additional job completion.
            if (client) _clientFlushPending = true;
            else if (_directConnections.Contains(connectionId)) _directFlushPending = true;
            else _serverFlushPending = true;
            if (result < 0)
            {
                SendErrorCount++;
                if (channelId != Channels.Reliable) UnreliableSendDropCount++;
                if (channelId == Channels.Reliable)
                    FailConnection(client, connectionId, $"Relay reliable EndSend failed ({result}); delivery is unconfirmed.");
                else RaiseSendError(client, connectionId, result);
            }
            else
            {
                SentBytes += segment.Count;
                if (client) OnClientDataSent?.Invoke(segment, channelId);
                else OnServerDataSent?.Invoke(connectionId, segment, channelId);
            }
            return true;
        }

        private void QueueReliable(ReliableSendBacklog backlog, ArraySegment<byte> segment, bool client, int connectionId)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (backlog.HasExpired(now) || !backlog.TryEnqueue(segment, now))
                FailConnection(client, connectionId, "Relay reliable send backlog exceeded its time/size limit.");
        }

        private void FlushBacklog(ReliableSendBacklog backlog, NetworkDriver driver, NetworkPipeline pipeline,
            UtpNetworkConnection connection, bool client, int connectionId)
        {
            if (backlog.HasExpired(Time.realtimeSinceStartupAsDouble))
            {
                FailConnection(client, connectionId, "Relay reliable send backlog timed out.");
                return;
            }
            while (backlog.Count > 0)
            {
                if (!TrySend(driver, pipeline, connection, backlog.Peek(), Channels.Reliable, client, connectionId)) return;
                // Send callbacks may synchronously shut down and clear the queue/driver.
                if (client ? !_clientDriver.IsCreated : !ServerDriver(_directConnections.Contains(connectionId)).IsCreated || !_serverConnections.ContainsKey(connectionId)) return;
                if (backlog.Count > 0) backlog.RemoveFirst();
            }
        }

        private void FailConnection(bool client, int connectionId, string message, TransportError error = TransportError.Unexpected)
        {
            // These messages are generated here from transport status codes, never SDK credentials.
            RoomConnectionDiagnostics.Record((client ? "client_relay_failure: " : "server_relay_failure: ") + message);
            if (client)
            {
                uint attempt = _clientAttempt;
                try { OnClientError?.Invoke(error, message); }
                finally { if (attempt == _clientAttempt) ClientDisconnect(); }
            }
            else
            {
                try { OnServerError?.Invoke(connectionId, TransportError.Unexpected, message); }
                finally { ServerDisconnect(connectionId); }
            }
        }

        private void RaiseSendError(bool client, int connectionId, int result)
        {
            string message = $"Unity Relay send failed with error code {result}.";
            if (client)
                OnClientError?.Invoke(TransportError.Unexpected, message);
            else
                OnServerError?.Invoke(connectionId, TransportError.Unexpected, message);
        }

        private ArraySegment<byte> ReadPayload(DataStreamReader reader)
        {
            ReceivedBytes += reader.Length;
            if (!_receiveBuffer.IsCreated)
                _receiveBuffer = new NativeArray<byte>(ReliablePacketCapacity, Allocator.Persistent);
            reader.ReadBytes(_receiveBuffer.GetSubArray(0, reader.Length));
            NativeArray<byte>.Copy(_receiveBuffer, 0, _receiveBytes, 0, reader.Length);
            // Mirror's Unbatcher.AddBatch copies this segment before the callback returns.
            return new ArraySegment<byte>(_receiveBytes, 0, reader.Length);
        }

        private void DisposeClientDriver()
        {
            _clientFlushPending = true;
            if (_clientDriver.IsCreated)
                _clientDriver.Dispose();
        }

        private void DisposeServerDriver()
        {
            _serverFlushPending = _directFlushPending = true;
            if (_serverDriver.IsCreated)
                _serverDriver.Dispose();
        }
    }
}
