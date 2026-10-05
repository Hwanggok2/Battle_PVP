using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BattlePvp.Networking
{
    public static class RoomDirectEndpoint
    {
        // Public IPv4 candidates are separate from LAN discovery and never contain a hostname.
        public static bool TryParse(string value, out IPEndPoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrEmpty(value) || value.Length > 21) return false;
            string[] parts = value.Split(':');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
                address.ToString() != parts[0] || !IsPublic(address) ||
                !ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port < 1024) return false;
            endpoint = new IPEndPoint(address, port);
            return true;
        }

        public static bool IsPublic(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
                !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) && !(b[0] == 169 && b[1] == 254) &&
                !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168) &&
                !(b[0] == 198 && (b[1] == 18 || b[1] == 19));
        }

        // RFC 8489 Binding success, IPv4 XOR-MAPPED-ADDRESS. Caller also checks the STUN source.
        public static bool TryReadStun(byte[] bytes, int length, byte[] request, out IPEndPoint endpoint)
        {
            endpoint = null;
            if (bytes == null || request == null || request.Length != 20 || length < 20 || length > bytes.Length || length > 576 ||
                bytes[0] != 1 || bytes[1] != 1 || ((bytes[2] << 8) | bytes[3]) != length - 20 || (length & 3) != 0) return false;
            for (int i = 4; i < 20; i++) if (bytes[i] != request[i]) return false;
            IPEndPoint candidate = null;
            for (int at = 20; at < length;)
            {
                if (at + 4 > length) return false;
                int type = bytes[at] << 8 | bytes[at + 1], size = bytes[at + 2] << 8 | bytes[at + 3];
                int next = at + 4 + ((size + 3) & ~3);
                if (next > length) return false;
                if (type == 0x20 && size == 8 && bytes[at + 5] == 1)
                {
                    int port = ((bytes[at + 6] << 8) | bytes[at + 7]) ^ 0x2112;
                    var address = new IPAddress(new[] { (byte)(bytes[at + 8] ^ 0x21), (byte)(bytes[at + 9] ^ 0x12),
                        (byte)(bytes[at + 10] ^ 0xa4), (byte)(bytes[at + 11] ^ 0x42) });
                    if (port >= 1024 && IsPublic(address)) candidate = new IPEndPoint(address, port);
                }
                at = next;
            }
            endpoint = candidate;
            return endpoint != null;
        }
    }
}
