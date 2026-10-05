using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Networking
{
    /// <summary>Flush received-message responses early; keep rendering at the selected FPS.</summary>
    [DisallowMultipleComponent]
    public sealed class RoomNetworkTiming : MonoBehaviour
    {
        private readonly List<NetworkConnectionToClient> _peers = new List<NetworkConnectionToClient>(8);
        private bool _active;
        public static bool LowLatencyActive => (NetworkServer.active || NetworkClient.active) && Transport.active is UnityRelayTransport;
        private void OnEnable()
        {
            NetworkLoop.OnEarlyUpdate += FlushResponses;
            NetworkLoop.OnLateUpdate += ReceiveBeforeFinalSend;
        }
        private void OnDisable()
        {
            NetworkLoop.OnEarlyUpdate -= FlushResponses;
            NetworkLoop.OnLateUpdate -= ReceiveBeforeFinalSend;
            _active = false;
            ApplyFrameRate(BattlePvp.UI.LocalGameSettings.Current.fps, false);
        }

        private void ReceiveBeforeFinalSend()
        {
            if (!Application.isPlaying || Transport.active is not UnityRelayTransport transport) return;
            // Process packets that arrived during game updates before the final send of this frame.
            // Mirror handlers, authentication, channel order and fixed physics stepping are unchanged.
            if (NetworkServer.active) transport.ServerEarlyUpdate();
            if (NetworkClient.active) transport.ClientEarlyUpdate();
            FlushResponses();
        }
        private void Update()
        {
            bool active = LowLatencyActive;
            if (active == _active) return;
            _active = active;
            ApplyFrameRate(BattlePvp.UI.LocalGameSettings.Current.fps, active);
        }

        public static void ApplyFrameRate(int renderFps, bool active)
        {
            renderFps = renderFps == 30 ? 30 : 60;
#if UNITY_WEBGL
            active = false;
#endif
            // Snapshot send rate and fixed physics step are unchanged. Native input/network responses run at 120 Hz.
            Application.targetFrameRate = active ? 120 : renderFps;
            OnDemandRendering.renderFrameInterval = active ? 120 / renderFps : 1;
        }

        private void FlushResponses()
        {
            if (!Application.isPlaying || Transport.active is not UnityRelayTransport transport) return;
            _peers.Clear();
            if (NetworkServer.active)
            {
                foreach (var peer in NetworkServer.connections.Values)
                    if (peer is not LocalConnectionToClient) _peers.Add(peer);
                foreach (var peer in _peers)
                    if (NetworkServer.active && NetworkServer.connections.TryGetValue(peer.connectionId, out var current) && ReferenceEquals(current, peer))
                        peer.FlushBatches();
                transport.ServerLateUpdate();
            }
            if (NetworkClient.isConnected && NetworkClient.connection is not LocalConnectionToServer)
            {
                NetworkClient.connection.FlushBatches();
                transport.ClientLateUpdate();
            }
        }
    }
}
