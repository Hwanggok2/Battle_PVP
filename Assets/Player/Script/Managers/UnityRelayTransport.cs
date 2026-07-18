using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
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
        [SerializeField] private int _maxConnections = 8;
        [SerializeField] private string _connectionType = "udp";

        private const int RelayApiMaxAttempts = 3;
        private const int RelayApiBackoffMilliseconds = 500;
        private const string SeoulRelayRegionId = "asia-northeast3";
        private const string TokyoRelayRegionId = "asia-northeast1";

        private NetworkDriver _serverDriver;
        private NetworkDriver _clientDriver;
        private UtpNetworkConnection _clientConnection;
        private NetworkPipeline _serverReliablePipeline;
        private NetworkPipeline _clientReliablePipeline;
        private readonly Dictionary<int, UtpNetworkConnection> _serverConnections = new Dictionary<int, UtpNetworkConnection>();
        private int _nextConnectionId = 1;

        private RelayServerData _serverRelayData;
        private RelayServerData _clientRelayData;
        private bool _hasPreparedServerRelay;
        private bool _hasPreparedClientRelay;
        private bool _clientConnected;

        public string LastJoinCode { get; private set; }
        public string LastRelayRegion { get; private set; }
        public string LastRelayRegionLabel { get; private set; }

        public override bool Available() => true;

        public async Task<string> PrepareHostAsync(int maxConnections)
        {
            await EnsureUnityServicesAsync();

            int relayConnections = Mathf.Max(1, maxConnections - 1);
            Allocation allocation = await CreatePreferredAllocationAsync(relayConnections);
            LastRelayRegion = allocation.Region;
            LastRelayRegionLabel = GetRegionLabel(LastRelayRegion);
            Debug.Log($"[UnityRelayTransport] Relay region: {LastRelayRegionLabel} [{LastRelayRegion}]");
            LastJoinCode = await RunRelayApiWithRetryAsync(
                "GetJoinCodeAsync",
                () => RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId));

            _serverRelayData = allocation.ToRelayServerData(GetRelayConnectionType());
            _hasPreparedServerRelay = true;
            return LastJoinCode;
        }

        private async Task<Allocation> CreatePreferredAllocationAsync(int relayConnections)
        {
            try
            {
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(
                    relayConnections,
                    SeoulRelayRegionId);
                if (IsPreferredRegion(allocation.Region))
                    return allocation;

                Debug.LogWarning(
                    $"[UnityRelayTransport] Seoul request returned unsupported region [{allocation.Region}]. " +
                    "Trying Tokyo.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[UnityRelayTransport] Seoul allocation failed. Trying Tokyo. " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }

            Allocation fallback = await RunRelayApiWithRetryAsync(
                "CreateAllocationAsync (Tokyo)",
                () => RelayService.Instance.CreateAllocationAsync(relayConnections, TokyoRelayRegionId));
            if (!IsPreferredRegion(fallback.Region))
            {
                throw new InvalidOperationException(
                    $"Tokyo request returned unsupported region [{fallback.Region}].");
            }

            return fallback;
        }

        public async Task PrepareClientAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                throw new ArgumentException("Relay join code is empty.", nameof(joinCode));

            await EnsureUnityServicesAsync();

            JoinAllocation allocation = await RunRelayApiWithRetryAsync(
                "JoinAllocationAsync",
                () => RelayService.Instance.JoinAllocationAsync(joinCode.Trim()));
            LastRelayRegion = allocation.Region;
            LastRelayRegionLabel = GetRegionLabel(LastRelayRegion);
            Debug.Log($"[UnityRelayTransport] Joined Relay region: {LastRelayRegionLabel} [{LastRelayRegion}]");
            _clientRelayData = allocation.ToRelayServerData(GetRelayConnectionType());
            _hasPreparedClientRelay = true;
        }

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

        private static async Task EnsureUnityServicesAsync()
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

        private static async Task<T> RunRelayApiWithRetryAsync<T>(string operationName, Func<Task<T>> operation)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= RelayApiMaxAttempts; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < RelayApiMaxAttempts)
                {
                    lastException = ex;
                    int delayMilliseconds = RelayApiBackoffMilliseconds * attempt;
                    Debug.LogWarning(
                        $"[UnityRelayTransport] {operationName} failed on attempt {attempt}/{RelayApiMaxAttempts}. Retrying in {delayMilliseconds}ms. {ex.GetType().Name}: {ex.Message}");
                    await Task.Delay(delayMilliseconds);
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
            if (!_hasPreparedClientRelay)
            {
                OnClientError?.Invoke(TransportError.Unexpected, "Relay client data was not prepared before ClientConnect.");
                OnClientDisconnected?.Invoke();
                return;
            }

            var settings = new NetworkSettings();
            settings.WithRelayParameters(serverData: ref _clientRelayData);
            _clientDriver = CreateRelayDriver(settings);
            _clientReliablePipeline = _clientDriver.CreatePipeline(
                typeof(FragmentationPipelineStage),
                typeof(ReliableSequencedPipelineStage));
            _clientConnection = _clientDriver.Connect();
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
                return;

            Send(_clientDriver, GetClientPipeline(channelId), _clientConnection, segment, channelId, true, 0);
        }

        public override void ClientDisconnect()
        {
            if (_clientDriver.IsCreated && _clientConnection.IsCreated)
            {
                _clientConnection.Disconnect(_clientDriver);
                _clientDriver.ScheduleUpdate().Complete();
            }

            _clientConnected = false;
            _clientConnection = default;
            DisposeClientDriver();
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
            _serverDriver = CreateRelayDriver(settings);
            _serverReliablePipeline = _serverDriver.CreatePipeline(
                typeof(FragmentationPipelineStage),
                typeof(ReliableSequencedPipelineStage));

            if (_serverDriver.Bind(NetworkEndpoint.AnyIpv4) < 0)
            {
                OnServerError?.Invoke(0, TransportError.Unexpected, "Failed to bind Unity Relay transport.");
                return;
            }

            if (_serverDriver.Listen() < 0)
            {
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
            if (_serverDriver.IsCreated && _serverConnections.TryGetValue(connectionId, out UtpNetworkConnection connection))
            {
                connection.Disconnect(_serverDriver);
                _serverDriver.ScheduleUpdate().Complete();
            }

            _serverConnections.Remove(connectionId);
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
            DisposeServerDriver();
        }

        public override int GetMaxPacketSize(int channelId = Channels.Reliable)
        {
            return channelId == Channels.Reliable ? 60_000 : 1_200;
        }

        public override void Shutdown()
        {
            ClientDisconnect();
            ServerStop();
            _hasPreparedClientRelay = false;
            _hasPreparedServerRelay = false;
            LastJoinCode = string.Empty;
            LastRelayRegion = string.Empty;
            LastRelayRegionLabel = string.Empty;
        }

        public override void ClientEarlyUpdate()
        {
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
                return;

            _clientDriver.ScheduleUpdate().Complete();

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
                        _clientConnected = false;
                        _clientConnection = default;
                        OnClientDisconnected?.Invoke();
                        return;
                }

                // Mirror callbacks can synchronously stop the client and dispose the driver.
                if (!_clientDriver.IsCreated || !_clientConnection.IsCreated)
                    return;
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (!_serverDriver.IsCreated)
                return;

            _serverDriver.ScheduleUpdate().Complete();

            UtpNetworkConnection connection;
            while ((connection = _serverDriver.Accept()) != default)
            {
                int connectionId = _nextConnectionId++;
                _serverConnections[connectionId] = connection;
                OnServerConnectedWithAddress?.Invoke(connectionId, ServerGetClientAddress(connectionId));
            }

            var disconnected = new List<int>();
            foreach (var kv in _serverConnections)
            {
                int connectionId = kv.Key;
                UtpNetworkConnection serverConnection = kv.Value;

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
                            disconnected.Add(connectionId);
                            break;
                    }
                }
            }

            foreach (int connectionId in disconnected)
            {
                _serverConnections.Remove(connectionId);
                OnServerDisconnected?.Invoke(connectionId);
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
            int result = driver.BeginSend(pipeline, connection, out DataStreamWriter writer, segment.Count);
            if (result < 0)
            {
                RaiseSendError(client, connectionId, result);
                return;
            }

            var payload = new NativeArray<byte>(segment.Count, Allocator.Temp);
            for (int i = 0; i < segment.Count; i++)
                payload[i] = segment.Array[segment.Offset + i];

            writer.WriteBytes(payload);
            payload.Dispose();

            result = driver.EndSend(writer);
            if (result < 0)
                RaiseSendError(client, connectionId, result);
            else if (client)
                OnClientDataSent?.Invoke(segment, channelId);
            else
                OnServerDataSent?.Invoke(connectionId, segment, channelId);
        }

        private void RaiseSendError(bool client, int connectionId, int result)
        {
            string message = $"Unity Relay send failed with error code {result}.";
            if (client)
                OnClientError?.Invoke(TransportError.Unexpected, message);
            else
                OnServerError?.Invoke(connectionId, TransportError.Unexpected, message);
        }

        private static ArraySegment<byte> ReadPayload(DataStreamReader reader)
        {
            var payload = new NativeArray<byte>(reader.Length, Allocator.Temp);
            reader.ReadBytes(payload);
            byte[] bytes = payload.ToArray();
            payload.Dispose();
            return new ArraySegment<byte>(bytes);
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
