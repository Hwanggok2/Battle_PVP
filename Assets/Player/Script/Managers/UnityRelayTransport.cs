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
    public sealed class UnityRelayTransport : Transport
    {
        [SerializeField] private string _connectionType = "udp";

        private const int RelayApiMaxAttempts = 3;
        public const int ReliablePacketCapacity = 60_000;
        public const int PacketBatchThreshold = 1_200;
        private const int ReliableWindowSize = 128; // Room for all fragments of one declared 60 KB packet.
        private const int RelayApiBackoffMilliseconds = 500;
        private const int RelayPreparationTimeoutMilliseconds = 30_000;
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
        private long _preparationVersion;
        private CancellationTokenSource _preparationCancellation;
        private static Task _unityServicesReady;

        public string LastJoinCode { get; private set; }
        public string LastRelayRegion { get; private set; }
        public string LastRelayRegionLabel { get; private set; }
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
                Allocation allocation = await CreatePreferredAllocationAsync(relayConnections, token);
                string joinCode = await RunRelayApiWithRetryAsync("GetJoinCodeAsync",
                    () => RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId), token);
                RelayServerData serverData = allocation.ToRelayServerData(GetRelayConnectionType());
                RequireCurrentPreparation(version, token);
                // Commit only after every stage succeeds. Detached SDK tasks never write these fields.
                _serverRelayData = serverData;
                LastJoinCode = joinCode;
                LastRelayRegion = allocation.Region;
                LastRelayRegionLabel = GetRegionLabel(LastRelayRegion);
                _hasPreparedServerRelay = true;
                return joinCode;
            }
            finally { FinishPreparation(preparation); }
        }

        private async Task<Allocation> CreatePreferredAllocationAsync(int relayConnections, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                Allocation allocation = await ServiceTaskDeadline.WaitAsync(
                    RelayService.Instance.CreateAllocationAsync(relayConnections, SeoulRelayRegionId), token);
                if (IsPreferredRegion(allocation.Region))
                    return allocation;

                Debug.LogWarning(
                    $"[UnityRelayTransport] Seoul request returned unsupported region [{allocation.Region}]. " +
                    "Trying Tokyo.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[UnityRelayTransport] Seoul allocation failed. Trying Tokyo. " +
                    $"{ex.GetType().Name}");
            }

            Allocation fallback = await RunRelayApiWithRetryAsync(
                "CreateAllocationAsync (Tokyo)",
                () => RelayService.Instance.CreateAllocationAsync(relayConnections, TokyoRelayRegionId), token);
            if (!IsPreferredRegion(fallback.Region))
            {
                throw new InvalidOperationException(
                    $"Tokyo request returned unsupported region [{fallback.Region}].");
            }

            return fallback;
        }

        public async Task PrepareClientAsync(string joinCode, CancellationToken cancellation = default)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                throw new ArgumentException("Relay join code is empty.", nameof(joinCode));
            CancellationTokenSource preparation = BeginPreparation(cancellation);
            long version = _preparationVersion;
            CancellationToken token = preparation.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                await ServiceTaskDeadline.WaitAsync(EnsureUnityServicesAsync(), token);
                JoinAllocation allocation = await RunRelayApiWithRetryAsync("JoinAllocationAsync",
                    () => RelayService.Instance.JoinAllocationAsync(joinCode.Trim()), token);
                RelayServerData clientData = allocation.ToRelayServerData(GetRelayConnectionType());
                RequireCurrentPreparation(version, token);
                _clientRelayData = clientData;
                LastRelayRegion = allocation.Region;
                LastRelayRegionLabel = GetRegionLabel(LastRelayRegion);
                _hasPreparedClientRelay = true;
            }
            finally { FinishPreparation(preparation); }
        }

        private CancellationTokenSource BeginPreparation(CancellationToken cancellation)
        {
            CancelPendingPreparation();
            _hasPreparedClientRelay = false;
            _hasPreparedServerRelay = false;
            LastJoinCode = LastRelayRegion = LastRelayRegionLabel = string.Empty;
            var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            preparation.CancelAfter(RelayPreparationTimeoutMilliseconds);
            _preparationCancellation = preparation;
            return preparation;
        }

        public void CancelPendingPreparation()
        {
            _preparationVersion++;
            CancellationTokenSource pending = _preparationCancellation;
            _preparationCancellation = null;
            pending?.Cancel();
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

        private void OnDestroy() => CancelPendingPreparation();

        private static bool IsPreferredRegion(string regionId)
        {
            return string.Equals(regionId, SeoulRelayRegionId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(regionId, TokyoRelayRegionId, StringComparison.OrdinalIgnoreCase);
        }

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
                    await Task.Delay(delayMilliseconds, token);
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
            _clientDisconnectPending = true;
            _clientConnectFailure = null;
            if (!_hasPreparedClientRelay)
            {
                _clientConnectFailure = "Relay client data was not prepared before ClientConnect.";
                return;
            }

            try
            {
                var settings = new NetworkSettings();
                settings.WithRelayParameters(serverData: ref _clientRelayData);
                settings.WithFragmentationStageParameters(payloadCapacity: ReliablePacketCapacity);
                settings.WithReliableStageParameters(windowSize: ReliableWindowSize);
                _clientDriver = CreateRelayDriver(settings);
                _clientReliablePipeline = _clientDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(ReliableSequencedPipelineStage));
                _clientConnection = _clientDriver.Connect();
            }
            catch (Exception)
            {
                _clientConnectFailure = "Relay client transport initialization failed.";
            }
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
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
            bool notify = _clientDisconnectPending;
            _clientDisconnectPending = false;
            _clientConnectFailure = null;
            _clientConnected = false;
            _clientBacklog.Clear();
            _clientConnection = default;
            DisposeClientDriver();
            // Mirror's callback calls Shutdown -> ClientDisconnect again. Commit cleanup first.
            if (notify) OnClientDisconnected?.Invoke();
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

            var settings = new NetworkSettings();
            settings.WithRelayParameters(serverData: ref _serverRelayData);
            settings.WithFragmentationStageParameters(payloadCapacity: ReliablePacketCapacity);
            settings.WithReliableStageParameters(windowSize: ReliableWindowSize);
            _serverDriver = CreateRelayDriver(settings);
            _serverReliablePipeline = _serverDriver.CreatePipeline(
                typeof(FragmentationPipelineStage),
                typeof(ReliableSequencedPipelineStage));

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
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (!_serverDriver.IsCreated || !_serverConnections.TryGetValue(connectionId, out UtpNetworkConnection connection))
                return;

            Send(_serverDriver, GetServerPipeline(channelId), connection, segment, channelId, false, connectionId);
        }

        public override void ServerDisconnect(int connectionId)
        {
            if (!_serverConnections.TryGetValue(connectionId, out UtpNetworkConnection connection)) return;
            _serverConnections.Remove(connectionId);
            _serverBacklogs.Remove(connectionId);
            try
            {
                if (_serverDriver.IsCreated && connection.IsCreated)
                {
                    connection.Disconnect(_serverDriver);
                    _serverDriver.ScheduleUpdate().Complete();
                }
            }
            finally { OnServerDisconnected?.Invoke(connectionId); }
        }

        public override string ServerGetClientAddress(int connectionId) => $"relay:{connectionId}";

        public override void ServerStop()
        {
            bool disconnectedAny = false;
            foreach (UtpNetworkConnection connection in _serverConnections.Values)
            {
                if (connection.IsCreated && _serverDriver.IsCreated)
                {
                    connection.Disconnect(_serverDriver);
                    disconnectedAny = true;
                }
            }

            if (disconnectedAny && _serverDriver.IsCreated)
                _serverDriver.ScheduleUpdate().Complete();

            _serverConnections.Clear();
            _serverBacklogs.Clear();
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
            try { PollClient(); }
            finally { _isPolling = false; }
        }

        private void PollClient()
        {
            if (_clientConnectFailure != null)
            {
                uint failedAttempt = _clientAttempt;
                string failure = _clientConnectFailure;
                _clientConnectFailure = null;
                // Mirror creates its connection after ClientConnect returns, so fail on the next poll.
                try { OnClientError?.Invoke(TransportError.Unexpected, failure); }
                finally { if (_clientAttempt == failedAttempt) FinishClientDisconnect(); }
                return;
            }
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
                return;

            _clientDriver.ScheduleUpdate().Complete();
            if (_clientDriver.GetRelayConnectionStatus() == RelayConnectionStatus.AllocationInvalid)
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
                        OnClientConnected?.Invoke();
                        break;
                    case NetworkEvent.Type.Data:
                        OnClientDataReceived?.Invoke(
                            ReadPayload(reader),
                            ResolveReceivedChannel(pipeline, _clientReliablePipeline));
                        break;
                    case NetworkEvent.Type.Disconnect:
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
            try { PollServer(); }
            finally { _isPolling = false; }
        }

        private void PollServer()
        {
            if (!_serverDriver.IsCreated)
                return;

            _serverDriver.ScheduleUpdate().Complete();
            if (_serverDriver.GetRelayConnectionStatus() == RelayConnectionStatus.AllocationInvalid)
            {
                // The room service observes this terminal state and stops advertising the host.
                return;
            }

            UtpNetworkConnection connection;
            while ((connection = _serverDriver.Accept()) != default)
            {
                int connectionId = _nextConnectionId++;
                _serverConnections[connectionId] = connection;
                _serverBacklogs[connectionId] = new ReliableSendBacklog();
                OnServerConnectedWithAddress?.Invoke(connectionId, ServerGetClientAddress(connectionId));
                if (!_serverDriver.IsCreated) return;
            }

            _disconnectedIds.Clear();
            _connectionIds.Clear();
            _connectionIds.AddRange(_serverConnections.Keys);
            foreach (int connectionId in _connectionIds)
            {
                if (!_serverConnections.TryGetValue(connectionId, out UtpNetworkConnection serverConnection))
                    continue;
                if (_serverBacklogs.TryGetValue(connectionId, out ReliableSendBacklog backlog))
                    FlushBacklog(backlog, _serverDriver, _serverReliablePipeline, serverConnection, false, connectionId);
                if (!_serverDriver.IsCreated) return;
                if (!_serverConnections.ContainsKey(connectionId)) continue;

                NetworkEvent.Type eventType;
                while ((eventType = _serverDriver.PopEventForConnection(
                           serverConnection,
                           out DataStreamReader reader,
                           out NetworkPipeline pipeline)) != NetworkEvent.Type.Empty)
                {
                    switch (eventType)
                    {
                        case NetworkEvent.Type.Data:
                            OnServerDataReceived?.Invoke(
                                connectionId,
                                ReadPayload(reader),
                                ResolveReceivedChannel(pipeline, _serverReliablePipeline));
                            break;
                        case NetworkEvent.Type.Disconnect:
                            _disconnectedIds.Add(connectionId);
                            break;
                    }
                    // A Mirror callback may synchronously disconnect or shut down the server.
                    if (!_serverDriver.IsCreated) return;
                    if (!_serverConnections.ContainsKey(connectionId)) break;
                }
            }

            foreach (int connectionId in _disconnectedIds)
            {
                _serverBacklogs.Remove(connectionId);
                if (_serverConnections.Remove(connectionId)) OnServerDisconnected?.Invoke(connectionId);
            }
        }

        public override void ClientLateUpdate()
        {
            if (_clientDriver.IsCreated)
                _clientDriver.ScheduleFlushSend().Complete();
        }

        public override void ServerLateUpdate()
        {
            if (_serverDriver.IsCreated)
                _serverDriver.ScheduleFlushSend().Complete();
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

        private NetworkPipeline GetServerPipeline(int channelId)
        {
            return channelId == Channels.Reliable ? _serverReliablePipeline : NetworkPipeline.Null;
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
                if (result == (int)Unity.Networking.Transport.Error.StatusCode.NetworkSendQueueFull) return false;
                FailConnection(client, connectionId, $"Relay BeginSend failed ({result}).");
                return true;
            }

            if (!_sendBuffer.IsCreated)
                _sendBuffer = new NativeArray<byte>(ReliablePacketCapacity, Allocator.Persistent);
            NativeArray<byte>.Copy(segment.Array, segment.Offset, _sendBuffer, 0, segment.Count);
            writer.WriteBytes(_sendBuffer.GetSubArray(0, segment.Count));

            result = driver.EndSend(writer);
            if (result < 0)
            {
                if (channelId == Channels.Reliable)
                    FailConnection(client, connectionId, $"Relay reliable EndSend failed ({result}); delivery is unconfirmed.");
                else RaiseSendError(client, connectionId, result);
            }
            else if (client)
                OnClientDataSent?.Invoke(segment, channelId);
            else
                OnServerDataSent?.Invoke(connectionId, segment, channelId);
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
                if (client ? !_clientDriver.IsCreated : !_serverDriver.IsCreated || !_serverConnections.ContainsKey(connectionId)) return;
                if (backlog.Count > 0) backlog.RemoveFirst();
            }
        }

        private void FailConnection(bool client, int connectionId, string message)
        {
            if (client)
            {
                uint attempt = _clientAttempt;
                try { OnClientError?.Invoke(TransportError.Unexpected, message); }
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
            if (!_receiveBuffer.IsCreated)
                _receiveBuffer = new NativeArray<byte>(ReliablePacketCapacity, Allocator.Persistent);
            reader.ReadBytes(_receiveBuffer.GetSubArray(0, reader.Length));
            NativeArray<byte>.Copy(_receiveBuffer, 0, _receiveBytes, 0, reader.Length);
            // Mirror's Unbatcher.AddBatch copies this segment before the callback returns.
            return new ArraySegment<byte>(_receiveBytes, 0, reader.Length);
        }

        private void DisposeClientDriver()
        {
            if (_clientDriver.IsCreated)
                _clientDriver.Dispose();
        }

        private void DisposeServerDriver()
        {
            if (_serverDriver.IsCreated)
                _serverDriver.Dispose();
        }
    }
}
