#if !UNITY_WEBGL
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace BattlePvp.Networking
{
    /// <summary>STUN on the actual game UDP socket, using UTP's supported interface wrapper.</summary>
    public sealed class RoomStunNetworkInterface : INetworkInterface
    {
        // UTP's Windows wrapper currently requires a value type; this adapter shares the socket state.
        public struct Adapter : INetworkInterface
        {
            private readonly RoomStunNetworkInterface _state;
            public Adapter(RoomStunNetworkInterface state) => _state = state;
            public NetworkEndpoint LocalEndpoint => _state.LocalEndpoint;
            public int Initialize(ref NetworkSettings settings, ref int padding) => _state.Initialize(ref settings, ref padding);
            public int Bind(NetworkEndpoint endpoint) => _state.Bind(endpoint);
            public int Listen() => _state.Listen();
            public void Dispose() => _state.Dispose();
            public JobHandle ScheduleReceive(ref ReceiveJobArguments args, JobHandle dep) => _state.ScheduleReceive(ref args, dep);
            public JobHandle ScheduleSend(ref SendJobArguments args, JobHandle dep) => _state.ScheduleSend(ref args, dep);
        }
        public Adapter AsInterface() => new Adapter(this);
        private Socket _socket;
        private NetworkEndpoint _local;
        private readonly Dictionary<NetworkEndpoint, IPEndPoint> _destinations = new Dictionary<NetworkEndpoint, IPEndPoint>();
        private byte[] _receive, _send;
        private int _maxReceive;
        private EndPoint _sender = new IPEndPoint(IPAddress.Any, 0);
        private readonly NetworkEndpoint _server;
        private readonly IPEndPoint _serverSocket;
        private readonly byte[] _request = new byte[20];
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private double _nextRequest;
        private int _attempt;
        public IPEndPoint PublicEndpoint { get; private set; }
        public NetworkEndpoint LocalEndpoint => _local;
        public long SendFailures { get; private set; }

        public RoomStunNetworkInterface(IPEndPoint server)
        {
            _server = server == null ? default : NetworkEndpoint.Parse(server.Address.ToString(), (ushort)server.Port);
            _serverSocket = server;
            _request[1] = 1; _request[4] = 0x21; _request[5] = 0x12; _request[6] = 0xa4; _request[7] = 0x42;
            var nonce = new byte[12]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            Array.Copy(nonce, 0, _request, 8, 12);
        }

        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            var config = settings.GetNetworkConfigParameters();
            _receive = new byte[config.maxMessageSize + 1]; _send = new byte[config.maxMessageSize];
            _maxReceive = Math.Min(config.receiveQueueCapacity, 256);
            return 0;
        }
        public int Bind(NetworkEndpoint endpoint)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) {
                    Blocking = false, ExclusiveAddressUse = true, SendBufferSize = 1024 * 1024, ReceiveBufferSize = 1024 * 1024
                };
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    _socket.IOControl(unchecked((int)0x9800000C), new byte[] { 0 }, null);
                _socket.Bind(new IPEndPoint(IPAddress.Parse(endpoint.ToFixedStringNoPort().ToString()), endpoint.Port));
                var bound = (IPEndPoint)_socket.LocalEndPoint;
                _local = NetworkEndpoint.Parse(bound.Address.ToString(), (ushort)bound.Port);
                return 0;
            }
            catch (SocketException) { Dispose(); return -1; }
        }
        public int Listen() => _socket != null ? 0 : -1;
        public void Dispose() { _socket?.Dispose(); _socket = null; _destinations.Clear(); }

        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            dep.Complete();
            if (_socket == null) return default;
            for (int i = 0; i < _maxReceive; i++)
            {
                int count;
                try
                {
                    if (!_socket.Poll(0, SelectMode.SelectRead)) break; // Do not allocate WouldBlock exceptions every frame.
                    count = _socket.ReceiveFrom(_receive, 0, _receive.Length, SocketFlags.None, ref _sender);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock) break;
                    if (e.SocketErrorCode == SocketError.MessageSize || e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    arguments.ReceiveResult.ErrorCode = e.ErrorCode;
                    break;
                }
                if (_serverSocket != null && _serverSocket.Equals(_sender))
                {
                    if (RoomDirectEndpoint.TryReadStun(_receive, count, _request, out var endpoint)) PublicEndpoint = endpoint;
                    continue; // STUN replies never enter the UTP connection handshake.
                }
                if (count <= 0 || count >= _receive.Length) continue;
                if (!arguments.ReceiveQueue.EnqueuePacket(out var packet)) break;
                if (count > packet.BytesAvailableAtEnd) { packet.Drop(); continue; }
                for (int b = 0; b < count; b++) packet.AppendToPayload(_receive[b]);
                var sender = (IPEndPoint)_sender;
                packet.EndpointRef = NetworkEndpoint.Parse(sender.Address.ToString(), (ushort)sender.Port);
            }
            return default;
        }

        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            dep.Complete();
            if (_socket == null) return default;
            for (int i = 0; i < arguments.SendQueue.Count; i++)
            {
                var packet = arguments.SendQueue[i];
                if (packet.Length == 0) continue;
                if (!_destinations.TryGetValue(packet.EndpointRef, out var endpoint))
                {
                    endpoint = new IPEndPoint(IPAddress.Parse(packet.EndpointRef.ToFixedStringNoPort().ToString()), packet.EndpointRef.Port);
                    if (_destinations.Count >= 32) _destinations.Clear();
                    _destinations[packet.EndpointRef] = endpoint;
                }
                if (packet.Length <= _send.Length)
                {
                    for (int b = 0; b < packet.Length; b++) _send[b] = packet.GetPayloadDataRef<byte>(b);
                    try { _socket.SendTo(_send, 0, packet.Length, SocketFlags.None, endpoint); }
                    catch (SocketException) { SendFailures++; } // UTP's reliable pipeline retransmits; never duplicate here.
                }
                else SendFailures++;
                packet.Drop();
            }
            double now = _clock.Elapsed.TotalSeconds;
            // 3 bounded acquisition attempts; an established mapping gets a 15-second keepalive.
            if (_server.IsValid && now >= _nextRequest && (_attempt < 3 || PublicEndpoint != null))
            {
                try { _socket.SendTo(_request, _serverSocket); }
                catch (SocketException) { }
                _attempt++;
                _nextRequest = now + (PublicEndpoint == null ? .5 * Math.Pow(2, _attempt - 1) : 15);
            }
            return default;
        }

        public static async Task<IPEndPoint> ResolveAsync(CancellationToken cancellation)
        {
            try
            {
                Task<IPAddress[]> dns = Dns.GetHostAddressesAsync("stun.l.google.com");
                if (await Task.WhenAny(dns, Task.Delay(1500, cancellation)) != dns)
                {
                    // Observe a later DNS failure; it must not mutate a newer room attempt.
                    _ = dns.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    cancellation.ThrowIfCancellationRequested();
                    return null;
                }
                foreach (var address in await dns)
                    if (RoomDirectEndpoint.IsPublic(address)) return new IPEndPoint(address, 19302);
            }
            catch (SocketException) { }
            return null;
        }
    }
}
#endif
