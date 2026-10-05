using System;
using UnityEngine;

namespace BattlePvp.Networking
{
    public enum RoomRtcSend { Sent, Busy, Failed }

    // Data only: no camera, microphone, media tracks or TURN credentials.
    public interface IRoomRtcPeer : IDisposable
    {
        string LocalSdp { get; }
        bool Ready { get; }
        bool Failed { get; }
        event Action<byte[], int> Received;
        void ApplyRemote(string sdp, bool offer);
        void Poll();
        RoomRtcSend Send(ArraySegment<byte> bytes, int channel);
    }

    public static class RoomRtcPeer
    {
        public const int MaxBufferedBytes = 262144;
        public const string StunUrl = "stun:stun.l.google.com:19302";
        public static IRoomRtcPeer Create(MonoBehaviour owner, bool offer)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new RoomRtcBrowserPeer(offer);
#else
            return new RoomRtcNativePeer(owner, offer);
#endif
        }
    }
}
