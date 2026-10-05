#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections;
using System.Runtime.InteropServices;
using Unity.WebRTC;
using UnityEngine;

namespace BattlePvp.Networking
{
    public sealed class RoomRtcNativePeer : IRoomRtcPeer
    {
        private readonly MonoBehaviour _owner;
        private readonly RTCPeerConnection _peer;
        private readonly RTCDataChannel[] _channels = new RTCDataChannel[2];
        private readonly byte[] _send = new byte[UnityRelayTransport.ReliablePacketCapacity];
        private GCHandle _pin;
        private Coroutine _description, _stats;
        private bool _disposed, _checkingStats, _udp;
        private double _disconnectedAt;
        public string LocalSdp { get; private set; }
        public bool Failed { get; private set; }
        public bool Ready => !_disposed && !Failed && _udp &&
            _channels[0].ReadyState == RTCDataChannelState.Open && _channels[1].ReadyState == RTCDataChannelState.Open;
        public event Action<byte[], int> Received;

        public RoomRtcNativePeer(MonoBehaviour owner, bool offer)
        {
            _owner = owner;
            var configuration = new RTCConfiguration { iceServers = new[] { new RTCIceServer { urls = new[] { RoomRtcPeer.StunUrl } } } };
            _peer = new RTCPeerConnection(ref configuration);
            try
            {
                _pin = GCHandle.Alloc(_send, GCHandleType.Pinned);
                for (int channel = 0; channel < 2; channel++)
                {
                    int id = channel;
                    var options = new RTCDataChannelInit { negotiated = true, id = id, ordered = id == 0 };
                    if (id == 1) options.maxRetransmits = 0;
                    _channels[id] = _peer.CreateDataChannel(id == 0 ? "reliable" : "unreliable", options);
                    _channels[id].OnMessage = bytes => { if (!_disposed) Received?.Invoke(bytes, id); };
                    _channels[id].OnClose = () => { if (!_disposed) Failed = true; };
                    _channels[id].OnError = error => { if (!_disposed) Failed = true; };
                }
                if (offer) _description = owner.StartCoroutine(Describe(true, null));
            }
            catch { Dispose(); throw; }
        }

        public void ApplyRemote(string sdp, bool offer)
        {
            if (!_disposed && _description == null) _description = _owner.StartCoroutine(Describe(!offer, sdp));
        }

        private IEnumerator Describe(bool offer, string remote)
        {
            if (remote != null)
            {
                var incoming = new RTCSessionDescription { type = offer ? RTCSdpType.Answer : RTCSdpType.Offer, sdp = remote };
                var setRemote = _peer.SetRemoteDescription(ref incoming);
                yield return setRemote;
                if (_disposed) yield break;
                if (setRemote.IsError) { Failed = true; _description = null; yield break; }
                if (offer) { _description = null; yield break; }
            }
            var create = offer ? _peer.CreateOffer() : _peer.CreateAnswer();
            yield return create;
            if (_disposed) yield break;
            if (create.IsError) { Failed = true; _description = null; yield break; }
            var description = create.Desc;
            var setLocal = _peer.SetLocalDescription(ref description);
            yield return setLocal;
            if (_disposed) yield break;
            if (setLocal.IsError) { Failed = true; _description = null; yield break; }
            double deadline = Time.realtimeSinceStartupAsDouble + 3d;
            while (!_disposed && _peer.GatheringState != RTCIceGatheringState.Complete && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            if (!_disposed) LocalSdp = _peer.LocalDescription.sdp;
            _description = null;
        }

        public void Poll()
        {
            if (_disposed) return;
            var state = _peer.ConnectionState;
            if (state == RTCPeerConnectionState.Failed || state == RTCPeerConnectionState.Closed) Failed = true;
            if (state == RTCPeerConnectionState.Disconnected)
            {
                if (_disconnectedAt == 0) _disconnectedAt = Time.realtimeSinceStartupAsDouble;
                if (Time.realtimeSinceStartupAsDouble - _disconnectedAt > 3) Failed = true;
            }
            else _disconnectedAt = 0;
            if (!_udp && !_checkingStats && state == RTCPeerConnectionState.Connected)
                _stats = _owner.StartCoroutine(CheckProtocol());
        }

        private IEnumerator CheckProtocol()
        {
            _checkingStats = true;
            var operation = _peer.GetStats();
            yield return operation;
            if (_disposed) yield break;
            if (!operation.IsError)
            {
                using (var report = operation.Value)
                {
                    foreach (var value in report.Stats.Values)
                    {
                        if (value is RTCTransportStats transport && !string.IsNullOrEmpty(transport.selectedCandidatePairId) &&
                            report.Stats.TryGetValue(transport.selectedCandidatePairId, out var pairValue) && pairValue is RTCIceCandidatePairStats pair &&
                            report.Stats.TryGetValue(pair.localCandidateId, out var candidateValue) && candidateValue is RTCIceCandidateStats candidate)
                        {
                            _udp = candidate.protocol == "udp";
                            if (!_udp) Failed = true; // Do not label an ICE/TCP route as direct UDP.
                            break;
                        }
                    }
                }
            }
            _checkingStats = false; _stats = null;
        }

        public RoomRtcSend Send(ArraySegment<byte> bytes, int channel)
        {
            if (!Ready || bytes.Array == null || channel < 0 || channel > 1 || bytes.Count > _send.Length) return RoomRtcSend.Failed;
            var data = _channels[channel];
            if (data.BufferedAmount + (ulong)bytes.Count > RoomRtcPeer.MaxBufferedBytes) return RoomRtcSend.Busy;
            try
            {
                Array.Copy(bytes.Array, bytes.Offset, _send, 0, bytes.Count);
                data.Send(_pin.AddrOfPinnedObject(), bytes.Count);
                return RoomRtcSend.Sent;
            }
            catch (Exception) { Failed = true; return RoomRtcSend.Failed; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_owner != null && _description != null) _owner.StopCoroutine(_description);
            if (_owner != null && _stats != null) _owner.StopCoroutine(_stats);
            foreach (var channel in _channels) channel?.Dispose();
            _peer.Dispose();
            if (_pin.IsAllocated) _pin.Free();
            Received = null;
        }
    }
}
#endif
