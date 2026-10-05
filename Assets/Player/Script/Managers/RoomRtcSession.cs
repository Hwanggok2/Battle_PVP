using System;
using System.Collections.Generic;

namespace BattlePvp.Networking
{
    public enum RoomRtcControl : byte { Offer = 1, Answer, Ready, Switch, Cancel }

    // Each direction places a barrier behind all its old reliable packets. New RTC
    // packets may arrive first, so keep them bounded until the peer's barrier arrives.
    public sealed class RoomRtcSession : IDisposable
    {
        public const double NegotiationSeconds = 12;
        private readonly IRoomRtcPeer _peer;
        private readonly bool _offer;
        private readonly double _deadline;
        private readonly Action<RoomRtcControl, string> _signal;
        private readonly Action<ArraySegment<byte>, int> _deliver;
        private readonly Action<bool> _ended;
        private readonly ReliableSendBacklog _sendQueue = new ReliableSendBacklog();
        private readonly Queue<(byte[] Bytes, int Channel)> _receiveQueue = new Queue<(byte[], int)>();
        private int _receiveBytes;
        private bool _sdpSent, _remoteDescription, _readySent, _remoteReady, _disposed;
        public bool SendingDirect { get; private set; }
        public bool ReceivingDirect { get; private set; }
        public bool Active => SendingDirect && ReceivingDirect;
        public bool Ended => _disposed;
        public int BacklogBytes => _sendQueue.Bytes;
        public double BacklogAge(double now) => _sendQueue.OldestWaitSeconds(now);
        public event Action<ArraySegment<byte>, int> Sent;
        public event Action Dropped;
        public event Action Backpressure;
        public event Action Activated;
        private bool _activationReported;
        private void ReportActivation()
        {
            if (_disposed || !Active || _activationReported) return;
            _activationReported = true; Activated?.Invoke();
        }

        public RoomRtcSession(IRoomRtcPeer peer, bool offer, double now, Action<RoomRtcControl, string> signal,
            Action<ArraySegment<byte>, int> deliver, Action<bool> ended)
        {
            _peer = peer; _offer = offer; _deadline = now + NegotiationSeconds;
            _signal = signal; _deliver = deliver; _ended = ended;
            _peer.Received += Receive;
        }

        public void Control(RoomRtcControl kind, string sdp = null)
        {
            if (_disposed) return;
            if (kind == RoomRtcControl.Offer || kind == RoomRtcControl.Answer)
            {
                if (_remoteDescription || (kind == RoomRtcControl.Answer) != _offer || string.IsNullOrEmpty(sdp)) { End(); return; }
                _remoteDescription = true; _peer.ApplyRemote(sdp, kind == RoomRtcControl.Offer);
            }
            else if (kind == RoomRtcControl.Ready) _remoteReady = true;
            else if (kind == RoomRtcControl.Switch)
            {
                if (!_remoteReady) { End(); return; }
                ReceivingDirect = true;
                ReportActivation();
                while (!_disposed && _receiveQueue.Count > 0)
                {
                    var packet = _receiveQueue.Dequeue(); _receiveBytes -= packet.Bytes.Length;
                    _deliver(new ArraySegment<byte>(packet.Bytes), packet.Channel);
                }
            }
            else if (kind == RoomRtcControl.Cancel) End(false);
        }

        public void Poll(double now)
        {
            if (_disposed) return;
            _peer.Poll();
            if (_disposed) return;
            if (_peer.Failed || (!Active && now >= _deadline) || _sendQueue.HasExpired(now)) { End(); return; }
            if (!_sdpSent && _peer.LocalSdp != null)
            {
                _sdpSent = true; _signal(_offer ? RoomRtcControl.Offer : RoomRtcControl.Answer, _peer.LocalSdp);
                if (_disposed) return;
            }
            if (_peer.Ready && !_readySent)
            {
                _readySent = true; _signal(RoomRtcControl.Ready, null);
                if (_disposed) return;
            }
            if (_readySent && _remoteReady && !SendingDirect)
            {
                _signal(RoomRtcControl.Switch, null);
                if (_disposed) return;
                SendingDirect = true;
                ReportActivation();
            }
            while (!_disposed && _sendQueue.Count > 0)
            {
                var packet = _sendQueue.Peek();
                var result = _peer.Send(packet, 0);
                if (result == RoomRtcSend.Busy) break;
                if (result == RoomRtcSend.Failed) { End(); return; }
                _sendQueue.RemoveFirst(); Sent?.Invoke(packet, 0);
            }
        }

        public void Send(ArraySegment<byte> bytes, int channel, double now)
        {
            if (_disposed || !SendingDirect) return;
            if (bytes.Array == null || channel < 0 || channel > 1 ||
                bytes.Count > (channel == 0 ? UnityRelayTransport.ReliablePacketCapacity : UnityRelayTransport.PacketBatchThreshold)) { End(); return; }
            if (channel == 0 && _sendQueue.Count > 0)
            { if (!_sendQueue.TryEnqueue(bytes, now)) End(); return; }
            var result = _peer.Send(bytes, channel);
            if (result == RoomRtcSend.Sent) Sent?.Invoke(bytes, channel);
            else if (result == RoomRtcSend.Failed) End();
            else
            {
                Backpressure?.Invoke();
                if (channel == 0) { if (!_sendQueue.TryEnqueue(bytes, now)) End(); }
                else Dropped?.Invoke();
            }
        }

        private void Receive(byte[] bytes, int channel)
        {
            if (_disposed) return;
            if (bytes == null || bytes.Length > (channel == 0 ? UnityRelayTransport.ReliablePacketCapacity : UnityRelayTransport.PacketBatchThreshold) || channel < 0 || channel > 1)
            { End(); return; }
            if (ReceivingDirect) { _deliver(new ArraySegment<byte>(bytes), channel); return; }
            if (_receiveQueue.Count >= 1024 || bytes.Length > ReliableSendBacklog.MaxBytes - _receiveBytes) { End(); return; }
            _receiveQueue.Enqueue((bytes, channel)); _receiveBytes += bytes.Length;
        }

        private void End(bool notifyPeer = true)
        {
            if (_disposed) return;
            bool committed = SendingDirect || ReceivingDirect;
            Dispose();
            if (notifyPeer && !committed) _signal(RoomRtcControl.Cancel, null);
            // Once a reliable stream has moved, loss cannot silently replay an unknown
            // delivery state on Relay. Fail the connection instead of losing commands.
            _ended(committed);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _peer.Received -= Receive; _peer.Dispose();
            _sendQueue.Clear(); _receiveQueue.Clear(); _receiveBytes = 0;
        }
    }
}
