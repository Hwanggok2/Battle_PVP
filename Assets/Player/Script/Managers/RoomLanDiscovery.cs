using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BattlePvp.Networking
{
    /// <summary>Local route discovery only. RoomNetworkAuthenticator still controls admission.</summary>
    public sealed class RoomLanDiscovery : IDisposable
    {
        private const int FirstPort = 47770, PortCount = 8, MaxDatagramBytes = 128;
        private UdpClient _listener;
        private string _key;
        private ushort _gamePort;

        public static string RoomKey(string roomId, string relayCode)
        {
            if (!RoomIdentity.IsValid(roomId) || string.IsNullOrWhiteSpace(relayCode)) return null;
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(roomId + "|" + relayCode.Trim())))
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        public bool Start(string key, ushort gamePort)
        {
            Dispose();
#if !UNITY_WEBGL
            if (key == null || gamePort == 0) return false;
            for (int port = FirstPort; port < FirstPort + PortCount; port++)
            {
                var socket = new UdpClient(AddressFamily.InterNetwork);
                try
                {
                    socket.ExclusiveAddressUse = true;
                    IgnoreUnreachablePort(socket.Client);
                    socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    _listener = socket; _key = key; _gamePort = gamePort;
                    return true;
                }
                catch (SocketException) { socket.Dispose(); }
            }
#endif
            return false; // Port/firewall failure never prevents Relay hosting.
        }

        public void Poll()
        {
            if (_listener == null) return;
            try
            {
                // Bound work even if unrelated machines flood the discovery port.
                for (int i = 0; i < 16 && _listener.Available > 0; i++)
                {
                    var sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = _listener.Receive(ref sender);
                    if (!IsLocalAddress(sender.Address) || bytes.Length > MaxDatagramBytes) continue;
                    string[] fields = Encoding.ASCII.GetString(bytes).Split('|');
                    if (fields.Length != 3 || fields[0] != "BPVP1Q" || fields[1] != _key || !IsNonce(fields[2])) continue;
                    byte[] reply = Encoding.ASCII.GetBytes("BPVP1R|" + _key + "|" + fields[2] + "|" + _gamePort.ToString(CultureInfo.InvariantCulture));
                    _listener.Send(reply, reply.Length, sender);
                }
            }
            catch (SocketException) { Dispose(); }
            catch (ObjectDisposedException) { Dispose(); }
        }

        public static async Task<IPEndPoint> FindAsync(string key, CancellationToken cancellation)
        {
#if !UNITY_WEBGL
            if (key == null) return null;
            using var socket = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                IgnoreUnreachablePort(socket.Client);
                socket.EnableBroadcast = true;
                string nonce = Guid.NewGuid().ToString("N");
                byte[] query = Encoding.ASCII.GetBytes("BPVP1Q|" + key + "|" + nonce);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                int nextSend = 0;
                while (timer.ElapsedMilliseconds < 600)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (timer.ElapsedMilliseconds >= nextSend)
                    {
                        nextSend += 200;
                        for (int port = FirstPort; port < FirstPort + PortCount; port++)
                        {
                            socket.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, port));
                            // A denied broadcast must not disable the same-PC route.
                            try { socket.Send(query, query.Length, new IPEndPoint(IPAddress.Broadcast, port)); }
                            catch (SocketException) { }
                        }
                    }
                    for (int i = 0; i < 16 && socket.Available > 0; i++)
                    {
                        var sender = new IPEndPoint(IPAddress.Any, 0);
                        byte[] response = socket.Receive(ref sender);
                        if (TryReadResponse(response, sender.Address, key, nonce, out IPEndPoint endpoint)) return endpoint;
                    }
                    await Task.Delay(20, cancellation);
                }
            }
            catch (SocketException) { }
#else
            await Task.CompletedTask;
#endif
            cancellation.ThrowIfCancellationRequested();
            return null;
        }

        public static bool TryReadResponse(byte[] bytes, IPAddress sender, string key, string nonce, out IPEndPoint endpoint)
        {
            endpoint = null;
            if (bytes == null || bytes.Length > MaxDatagramBytes || key == null || !IsNonce(nonce) || !IsLocalAddress(sender)) return false;
            string[] fields = Encoding.ASCII.GetString(bytes).Split('|');
            if (fields.Length != 4 || fields[0] != "BPVP1R" || fields[1] != key || fields[2] != nonce ||
                !ushort.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0) return false;
            // The responding socket supplies the address, never a hostname/IP supplied in the payload.
            endpoint = new IPEndPoint(sender, port);
            return true;
        }

        private static bool IsNonce(string nonce) => nonce != null && nonce.Length == 32 &&
            Guid.TryParseExact(nonce, "N", out Guid value) && value != Guid.Empty;
        private static bool IsLocalAddress(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            return b[0] == 127 || b[0] == 10 || b[0] == 192 && b[1] == 168 ||
                b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 169 && b[1] == 254;
        }

        private static void IgnoreUnreachablePort(Socket socket)
        {
            // Like the bundled kcp2k server: one unused discovery port must not reset this UDP socket on Windows.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                socket.IOControl(unchecked((int)0x9800000C), new byte[] { 0 }, null);
        }

        public void Dispose()
        {
            _listener?.Dispose(); _listener = null; _key = null; _gamePort = 0;
        }
    }
}
