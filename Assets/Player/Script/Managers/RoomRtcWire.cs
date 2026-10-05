using System;
using System.Text;

namespace BattlePvp.Networking
{
    public static class RoomRtcWire
    {
        // A NaN timestamp can never prefix a valid Mirror batch. The control frame
        // is consumed by this transport, never handed to Mirror's unbatcher.
        private static readonly byte[] Magic = { 0x52, 0x54, 0x43, 0x50, 0x56, 0x50, 0xff, 0x7f };
        public const int MaxSdpBytes = 16000;
        public static byte[] Encode(RoomRtcControl kind, string sdp)
        {
            var text = string.IsNullOrEmpty(sdp) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(sdp);
            if (text.Length > MaxSdpBytes) throw new ArgumentException("RTC description is too large.");
            var bytes = new byte[9 + text.Length];
            Buffer.BlockCopy(Magic, 0, bytes, 0, 8); bytes[8] = (byte)kind;
            Buffer.BlockCopy(text, 0, bytes, 9, text.Length);
            return bytes;
        }
        public static bool IsControl(ArraySegment<byte> bytes)
        {
            if (bytes.Array == null || bytes.Count < 8) return false;
            for (int i = 0; i < 8; i++) if (bytes.Array[bytes.Offset + i] != Magic[i]) return false;
            return true;
        }
        public static bool TryRead(ArraySegment<byte> bytes, out RoomRtcControl kind, out string sdp)
        {
            kind = 0; sdp = null;
            if (!IsControl(bytes) || bytes.Count < 9 || bytes.Count > 9 + MaxSdpBytes) return false;
            kind = (RoomRtcControl)bytes.Array[bytes.Offset + 8];
            if (kind < RoomRtcControl.Offer || kind > RoomRtcControl.Cancel) return false;
            bool description = kind == RoomRtcControl.Offer || kind == RoomRtcControl.Answer;
            if (description != (bytes.Count > 9)) return false;
            if (description) sdp = Encoding.UTF8.GetString(bytes.Array, bytes.Offset + 9, bytes.Count - 9);
            return true;
        }
    }
}
