#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BattlePvp.Networking
{
    public sealed class RoomRtcBrowserPeer : IRoomRtcPeer
    {
        [DllImport("__Internal")] private static extern int BattlePvpRtc_Create(int offer, string stun);
        [DllImport("__Internal")] private static extern void BattlePvpRtc_Remote(int id, string sdp, int offer);
        [DllImport("__Internal")] private static extern int BattlePvpRtc_Poll(int id, byte[] data, int capacity);
        [DllImport("__Internal")] private static extern int BattlePvpRtc_Send(int id, byte[] data, int offset, int count, int channel);
        [DllImport("__Internal")] private static extern void BattlePvpRtc_Close(int id);
        private readonly int _id;
        private readonly byte[] _buffer = new byte[UnityRelayTransport.ReliablePacketCapacity + 1];
        private bool _disposed;
        public string LocalSdp { get; private set; }
        public bool Ready { get; private set; }
        public bool Failed { get; private set; }
        public event Action<byte[], int> Received;
        public RoomRtcBrowserPeer(bool offer) { _id = BattlePvpRtc_Create(offer ? 1 : 0, RoomRtcPeer.StunUrl); Failed = _id == 0; }
        public void ApplyRemote(string sdp, bool offer) { if (!_disposed) BattlePvpRtc_Remote(_id, sdp, offer ? 1 : 0); }
        public void Poll()
        {
            if (_disposed || Failed) return;
            for (int i = 0; i < 256 && !_disposed; i++)
            {
                int count = BattlePvpRtc_Poll(_id, _buffer, _buffer.Length);
                if (count == 0) break;
                if (count < 0) { Failed = true; break; }
                int kind = _buffer[0];
                if (kind == 1) LocalSdp = Encoding.UTF8.GetString(_buffer, 1, count - 1);
                else if (kind == 2) Ready = true;
                else if (kind == 3) { Failed = true; Ready = false; }
                else if (kind == 4 || kind == 5)
                {
                    var bytes = new byte[count - 1];
                    Buffer.BlockCopy(_buffer, 1, bytes, 0, bytes.Length);
                    Received?.Invoke(bytes, kind - 4);
                }
            }
        }
        public RoomRtcSend Send(ArraySegment<byte> bytes, int channel) => _disposed || Failed || !Ready ? RoomRtcSend.Failed :
            (RoomRtcSend)BattlePvpRtc_Send(_id, bytes.Array, bytes.Offset, bytes.Count, channel);
        public void Dispose() { if (_disposed) return; _disposed = true; BattlePvpRtc_Close(_id); Received = null; }
    }
}
#endif
