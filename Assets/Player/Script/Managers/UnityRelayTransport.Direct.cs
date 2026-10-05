using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Mirror;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace BattlePvp.Networking
{
    public sealed partial class UnityRelayTransport
    {
        private NetworkDriver _directServerDriver;
        private NetworkPipeline _directServerPipeline;
        private readonly HashSet<int> _directConnections = new HashSet<int>();
        private readonly RoomLanDiscovery _lanDiscovery = new RoomLanDiscovery();
        private IPEndPoint _directEndpoint;
        private IPEndPoint _publicDirectCandidate;
        private string _clientRelayJoinCode;
        private CancellationTokenSource _clientFallbackCancellation;
        private bool _clientPreparingFallback;
#if !UNITY_WEBGL
        private IPEndPoint _stunServer;
        private RoomStunNetworkInterface _directNetwork;
#endif
        private bool _clientUsingDirect;
        private double _directConnectDeadline;
        private string _lanRoomKey;
        public bool ClientUsingDirect => _clientUsingDirect;
        public int DirectPeerCount => _directConnections.Count + RtcPeerCount;
        public int RelayPeerCount => _serverConnections.Count - _directConnections.Count - RtcPeerCount;
        public long DirectSocketSendFailures
        {
            get
            {
#if !UNITY_WEBGL
                return _directNetwork?.SendFailures ?? 0;
#else
                return 0;
#endif
            }
        }
        public ushort DirectPort => _directServerDriver.IsCreated ? _directServerDriver.GetLocalEndpoint().Port : (ushort)0;
        public string ConnectionRoute => NetworkServer.active ? (DirectPeerCount > 0 ? (RelayPeerCount > 0 ? "mixed" : "direct") : "relay") :
            ClientUsingRtc ? "webrtc_udp" : _clientUsingDirect ? "direct" : "relay";

        public void ConfigureLocalRoom(string roomId, string relayCode) => _lanRoomKey = RoomLanDiscovery.RoomKey(roomId, relayCode);

        public async Task<string> GatherPublicEndpointAsync(CancellationToken cancellation)
        {
#if !UNITY_WEBGL
            var network = _directNetwork;
            if (network == null) return string.Empty;
            double deadline = Time.realtimeSinceStartupAsDouble + 2.5;
            while (network.PublicEndpoint == null && ReferenceEquals(network, _directNetwork) &&
                _directServerDriver.IsCreated && Time.realtimeSinceStartupAsDouble < deadline)
                await Task.Delay(25, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return ReferenceEquals(network, _directNetwork) ? network.PublicEndpoint?.ToString() ?? string.Empty : string.Empty;
#else
            await Task.CompletedTask;
            return string.Empty;
#endif
        }

        private void StartDirectServer()
        {
#if !UNITY_WEBGL
            if (_lanRoomKey == null) return;
            try
            {
                _directNetwork = new RoomStunNetworkInterface(_stunServer);
                _directServerDriver = CreateRoomDriver(false, ref _serverRelayData, out _directServerPipeline, _directNetwork);
                if (_directServerDriver.Bind(NetworkEndpoint.AnyIpv4) != 0 || _directServerDriver.Listen() != 0)
                    throw new InvalidOperationException("Direct UDP bind failed.");
                if (!_lanDiscovery.Start(_lanRoomKey, DirectPort))
                    RoomConnectionDiagnostics.Stage("lan_discovery_unavailable");
                RoomConnectionDiagnostics.Stage("direct_udp_server_ready");
            }
            catch (Exception)
            {
                _lanDiscovery.Dispose();
                if (_directServerDriver.IsCreated) _directServerDriver.Dispose();
                _directNetwork = null;
                RoomConnectionDiagnostics.Stage("direct_udp_unavailable_using_relay");
            }
#endif
        }

        private void CreateClientDriver(bool direct)
        {
            _clientUsingDirect = direct;
            _directConnectDeadline = direct ? Time.realtimeSinceStartupAsDouble + 1.5d : 0d;
            _clientDriver = CreateRoomDriver(!direct, ref _clientRelayData, out _clientReliablePipeline);
            _clientConnection = direct ? _clientDriver.Connect(NetworkEndpoint.Parse(_directEndpoint.Address.ToString(), (ushort)_directEndpoint.Port)) :
                _clientDriver.Connect();
            if (!_clientConnection.IsCreated) _clientConnectFailure = "Room client could not create a connection.";
            else RoomConnectionDiagnostics.Stage(direct ? "direct_udp_connect_started" : "relay_client_driver_created");
        }

        private bool TryAdvanceClientRoute()
        {
            if (!_clientUsingDirect || _clientConnected || !_clientDisconnectPending) return false;
            if (_clientDriver.IsCreated && _clientConnection.IsCreated)
            {
                _clientConnection.Disconnect(_clientDriver);
                _clientDriver.ScheduleUpdate().Complete();
            }
            DisposeClientDriver();
            _clientConnection = default;
            _clientBacklog.Clear();
            _clientConnectFailure = null;
            if (_publicDirectCandidate != null)
            {
                _directEndpoint = _publicDirectCandidate;
                _publicDirectCandidate = null;
                RoomConnectionDiagnostics.Stage("direct_udp_try_public_candidate");
                try { CreateClientDriver(true); }
                catch (Exception) { _clientConnectFailure = "Public UDP initialization failed."; }
                return true;
            }
            _clientUsingDirect = false;
            _directEndpoint = null;
            _directConnectDeadline = 0;
            RoomConnectionDiagnostics.Stage("direct_udp_failed_fallback_relay");
            if (_hasPreparedClientRelay)
            {
                try { CreateClientDriver(false); }
                catch (Exception) { _clientConnectFailure = "Relay fallback initialization failed."; }
            }
            else
            {
                _clientPreparingFallback = true;
                _clientFallbackCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(
                    Math.Max(.1, _clientConnectDeadline - Time.realtimeSinceStartupAsDouble)));
                _ = PrepareFallbackAsync(_clientAttempt, _preparationVersion, _clientFallbackCancellation);
            }
            return true; // Same Mirror attempt; no premature disconnect or membership cleanup.
        }

        private async Task PrepareClientRelayDataAsync(string joinCode, long version, CancellationToken token)
        {
            await ServiceTaskDeadline.WaitAsync(EnsureUnityServicesAsync(), token);
            var allocation = await RunRelayApiWithRetryAsync("JoinAllocationAsync",
                () => Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(joinCode), token);
            var data = allocation.ToRelayServerData(GetRelayConnectionType());
            RequireCurrentPreparation(version, token);
            _clientRelayData = data;
            LastRelayRegion = allocation.Region;
            LastRelayRegionLabel = GetRegionLabel(LastRelayRegion);
            _hasPreparedClientRelay = true;
        }

        private async Task PrepareFallbackAsync(uint attempt, long version, CancellationTokenSource cancellation)
        {
            try
            {
                await PrepareClientRelayDataAsync(_clientRelayJoinCode, version, cancellation.Token);
                if (this == null || attempt != _clientAttempt || !_clientDisconnectPending || cancellation.IsCancellationRequested) return;
                CreateClientDriver(false);
            }
            catch (Exception)
            {
                if (this != null && attempt == _clientAttempt && _clientDisconnectPending)
                    _clientConnectFailure = "Relay fallback preparation failed.";
            }
            finally
            {
                if (ReferenceEquals(_clientFallbackCancellation, cancellation))
                { _clientFallbackCancellation = null; _clientPreparingFallback = false; }
                cancellation.Dispose();
            }
        }

        private ref NetworkDriver ServerDriver(bool direct)
        {
            if (direct) return ref _directServerDriver;
            return ref _serverDriver;
        }

        private static NetworkDriver CreateRoomDriver(bool relay, ref RelayServerData data, out NetworkPipeline pipeline
#if !UNITY_WEBGL
            , RoomStunNetworkInterface network = null
#endif
            )
        {
            var settings = new NetworkSettings();
            try
            {
                // Shared across up to seven peers; leave room for simultaneous fragmented sends and ACKs.
                settings.WithNetworkConfigParameters(sendQueueCapacity: 2048, receiveQueueCapacity: 2048);
                if (relay) settings.WithRelayParameters(serverData: ref data);
                settings.WithFragmentationStageParameters(payloadCapacity: ReliablePacketCapacity);
                settings.WithReliableStageParameters(windowSize: ReliableWindowSize);
                NetworkDriver driver;
#if !UNITY_WEBGL
                if (network != null) driver = NetworkDriver.Create(network.AsInterface().WrapToUnmanaged(), settings);
                else
#endif
                    driver = relay ? CreateRelayDriver(settings) : NetworkDriver.Create(settings);
                try
                {
                    pipeline = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
                    return driver;
                }
                catch { driver.Dispose(); throw; }
            }
            finally { settings.Dispose(); }
        }
    }
}
