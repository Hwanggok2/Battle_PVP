using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BattlePvp.Networking
{
    public sealed partial class UnityRelayTransport
    {
        public bool AllowRtcUpgrade { get; set; } = true;
        private RoomRtcSession _clientRtc;
        private bool _clientRtcAttempted;
        private readonly Dictionary<int, RoomRtcSession> _serverRtc = new Dictionary<int, RoomRtcSession>();
        private readonly HashSet<int> _serverRtcAttempted = new HashSet<int>();
        private readonly List<int> _rtcPollIds = new List<int>(8);
        public bool ClientUsingRtc => _clientRtc != null && !_clientRtc.Ended && _clientRtc.Active;
        public int RtcPeerCount
        {
            get { int count = 0; foreach (var peer in _serverRtc.Values) if (!peer.Ended && peer.Active) count++; return count; }
        }
        public string GameplayRouteLabel
        {
            get
            {
                if (!NetworkServer.active)
                    return ClientUsingRtc ? "WebRTC UDP" : _clientUsingDirect ? "Direct UDP" : "Relay " + LastRelayRegionLabel;
                if (RelayPeerCount == 0 && RtcPeerCount > 0) return DirectPeerCount > RtcPeerCount ? "UDP + WebRTC" : "WebRTC UDP";
                if (RelayPeerCount == 0 && DirectPeerCount > 0) return "Direct UDP";
                return (DirectPeerCount > 0 ? "Direct + " : "") + "Relay " + LastRelayRegionLabel;
            }
        }

        // Called only after the authenticated host advertises this transport version.
        // Older hosts do not advertise it; older clients ignore the extra auth result.
        public void EnableRtcUpgrade()
        {
            if (!AllowRtcUpgrade || _clientRtcAttempted || _clientUsingDirect || !_clientConnected || NetworkServer.active ||
                NetworkClient.connection == null || !NetworkClient.connection.isAuthenticated) return;
            _clientRtcAttempted = true;
            try { _clientRtc = CreateRtc(true, 0, true); }
            catch (Exception) { RoomConnectionDiagnostics.Stage("webrtc_unavailable_keep_relay"); }
        }

        private RoomRtcSession CreateRtc(bool client, int id, bool offer)
        {
            var session = new RoomRtcSession(RoomRtcPeer.Create(this, offer), offer, Time.realtimeSinceStartupAsDouble,
                (kind, sdp) => SendRtcControl(client, id, kind, sdp),
                (bytes, channel) =>
                {
                    ReceivedBytes += bytes.Count;
                    if (client) OnClientDataReceived?.Invoke(bytes, channel);
                    else OnServerDataReceived?.Invoke(id, bytes, channel);
                },
                committed =>
                {
                    RoomConnectionDiagnostics.Stage(committed ? "webrtc_active_connection_lost" : "webrtc_unavailable_keep_relay");
                    if (committed) FailConnection(client, id, "Direct RTC connection was lost.");
                });
            session.Sent += (bytes, channel) =>
            {
                SentBytes += bytes.Count;
                if (client) OnClientDataSent?.Invoke(bytes, channel);
                else OnServerDataSent?.Invoke(id, bytes, channel);
            };
            session.Dropped += () => UnreliableSendDropCount++;
            session.Backpressure += () => SendQueueFullCount++;
            session.Activated += () => RoomConnectionDiagnostics.Stage(client ? "webrtc_udp_gameplay_active" : "webrtc_udp_peer_active");
            RoomConnectionDiagnostics.Stage("webrtc_negotiation_started");
            return session;
        }

        private void SendRtcControl(bool client, int id, RoomRtcControl kind, string sdp)
        {
            byte[] bytes;
            try { bytes = RoomRtcWire.Encode(kind, sdp); }
            catch (ArgumentException) { FailConnection(client, id, "Invalid RTC control size."); return; }
            if (client)
            {
                if (_clientDriver.IsCreated && _clientConnection.IsCreated)
                    Send(_clientDriver, _clientReliablePipeline, _clientConnection, new ArraySegment<byte>(bytes), Channels.Reliable, true, 0);
            }
            else if (_serverDriver.IsCreated && _serverConnections.TryGetValue(id, out var connection))
                Send(_serverDriver, _serverReliablePipeline, connection, new ArraySegment<byte>(bytes), Channels.Reliable, false, id);
        }

        private void ReceiveRoomData(bool client, int id, ArraySegment<byte> bytes, int channel)
        {
            if (!RoomRtcWire.IsControl(bytes))
            {
                // Old unreliable packets can arrive after the barrier; discard them.
                // All old reliable packets necessarily arrived before that barrier.
                var session = client ? _clientRtc : (_serverRtc.TryGetValue(id, out var rtc) ? rtc : null);
                if (session != null && session.ReceivingDirect && channel == Channels.Unreliable) return;
                if (client) OnClientDataReceived?.Invoke(bytes, channel);
                else OnServerDataReceived?.Invoke(id, bytes, channel);
                return;
            }
            bool authenticated = client ? NetworkClient.connection != null && NetworkClient.connection.isAuthenticated :
                NetworkServer.connections.TryGetValue(id, out var peer) && peer.isAuthenticated;
            if (!authenticated || channel != Channels.Reliable || !AllowRtcUpgrade ||
                !RoomRtcWire.TryRead(bytes, out var kind, out string sdp)) return;
            if (client) _clientRtc?.Control(kind, sdp);
            else
            {
                if (!_serverRtc.TryGetValue(id, out var session))
                {
                    if (kind != RoomRtcControl.Offer || _directConnections.Contains(id) || _serverRtcAttempted.Contains(id) || _serverRtc.Count >= 7) return;
                    _serverRtcAttempted.Add(id);
                    try { _serverRtc[id] = session = CreateRtc(false, id, false); }
                    catch (Exception) { SendRtcControl(false, id, RoomRtcControl.Cancel, null); return; }
                }
                session.Control(kind, sdp);
            }
        }

        private void PollRtc(bool client)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // The SDK pumps all peers together; never pay its wait once per player.
            if (client ? _clientRtc != null && !_clientRtc.Ended : _serverRtc.Count > 0)
                Unity.WebRTC.WebRTC.ExecutePendingTasks(0);
#endif
            double now = Time.realtimeSinceStartupAsDouble;
            if (client)
            {
                var session = _clientRtc;
                if (session == null || session.Ended) return;
                session.Poll(now);
            }
            else
            {
                _rtcPollIds.Clear(); _rtcPollIds.AddRange(_serverRtc.Keys);
                foreach (int id in _rtcPollIds)
                {
                    if (!_serverRtc.TryGetValue(id, out var session) || session.Ended) continue;
                    session.Poll(now);
                }
            }
        }
        private void ClearClientRtc() { _clientRtc?.Dispose(); _clientRtc = null; _clientRtcAttempted = false; }
        private int RtcBacklogBytes
        {
            get { int bytes = _clientRtc?.BacklogBytes ?? 0; foreach (var session in _serverRtc.Values) bytes += session.BacklogBytes; return bytes; }
        }
        private double RtcBacklogAge(double now)
        {
            double age = _clientRtc?.BacklogAge(now) ?? 0;
            foreach (var session in _serverRtc.Values) age = Math.Max(age, session.BacklogAge(now));
            return age;
        }
        private void ClearServerRtc(int id)
        {
            if (_serverRtc.Remove(id, out var session)) session.Dispose();
            _serverRtcAttempted.Remove(id);
        }
        private void ClearServerRtc()
        {
            foreach (var session in _serverRtc.Values) session.Dispose();
            _serverRtc.Clear(); _serverRtcAttempted.Clear();
        }
    }
}
