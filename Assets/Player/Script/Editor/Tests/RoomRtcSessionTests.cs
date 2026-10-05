using System;
using System.Collections.Generic;
using BattlePvp.Networking;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class RoomRtcSessionTests
    {
        private sealed class Peer : IRoomRtcPeer
        {
            public string LocalSdp { get; set; } = "test-sdp";
            public bool Ready { get; set; }
            public bool Failed { get; set; }
            public bool Disposed;
            public RoomRtcSend Result = RoomRtcSend.Sent;
            public readonly List<byte> Sent = new List<byte>();
            public event Action<byte[], int> Received;
            public void ApplyRemote(string sdp, bool offer) { }
            public void Poll() { }
            public RoomRtcSend Send(ArraySegment<byte> bytes, int channel)
            { if (Result == RoomRtcSend.Sent) Sent.Add(bytes.Array[bytes.Offset]); return Result; }
            public void Receive(byte value) => Received?.Invoke(new[] { value }, 0);
            public void Dispose() => Disposed = true;
        }
        [Test]
        public void NewPacketsWaitForOldReliableStreamBarrier()
        {
            var peer = new Peer { Ready = true };
            var delivered = new List<byte> { 1 }; // Last old Relay packet.
            var signals = new List<RoomRtcControl>();
            using var session = new RoomRtcSession(peer, true, 0, (kind, _) => signals.Add(kind),
                (data, _) => delivered.Add(data.Array[data.Offset]), _ => Assert.Fail("Unexpected failure"));
            session.Control(RoomRtcControl.Answer, "remote"); session.Control(RoomRtcControl.Ready);
            session.Poll(1);
            Assert.That(session.SendingDirect, Is.True);
            Assert.That(signals, Is.EqualTo(new[] { RoomRtcControl.Offer, RoomRtcControl.Ready, RoomRtcControl.Switch }));
            peer.Receive(3); peer.Receive(4);
            Assert.That(delivered, Is.EqualTo(new byte[] { 1 }));
            delivered.Add(2); // Earlier Relay packet arrives after faster UDP packets.
            session.Control(RoomRtcControl.Switch);
            peer.Receive(5);
            Assert.That(delivered, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.That(session.Active, Is.True);
        }
        [Test]
        public void BackpressurePreservesReliableOrderAndDropsOnlyUnreliable()
        {
            var peer = new Peer { Ready = true, Result = RoomRtcSend.Busy };
            int dropped = 0;
            using var session = new RoomRtcSession(peer, true, 0, (_, __) => { }, (_, __) => { }, _ => Assert.Fail());
            session.Dropped += () => dropped++;
            session.Control(RoomRtcControl.Ready); session.Poll(1); session.Control(RoomRtcControl.Switch);
            session.Send(new ArraySegment<byte>(new byte[] { 1 }), 0, 1);
            session.Send(new ArraySegment<byte>(new byte[] { 2 }), 0, 1);
            session.Send(new ArraySegment<byte>(new byte[] { 99 }), 1, 1);
            Assert.That(dropped, Is.EqualTo(1)); Assert.That(session.BacklogBytes, Is.EqualTo(2));
            peer.Result = RoomRtcSend.Sent; session.Poll(2);
            session.Send(new ArraySegment<byte>(new byte[] { 3 }), 0, 2);
            Assert.That(peer.Sent, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(session.BacklogBytes, Is.Zero);
        }
        [TestCase(false)] [TestCase(true)]
        public void FailureFallsBackOnlyBeforeReliableStreamMoves(bool committed)
        {
            var peer = new Peer { Ready = committed }; bool? fatal = null;
            using var session = new RoomRtcSession(peer, true, 0, (_, __) => { }, (_, __) => { }, value => fatal = value);
            if (committed) { session.Control(RoomRtcControl.Ready); session.Poll(1); }
            peer.Failed = true; session.Poll(2);
            Assert.That(fatal, Is.EqualTo(committed)); Assert.That(peer.Disposed, Is.True);
            Assert.That(session.Ended, Is.True);
        }
        [Test]
        public void NegotiationTimeoutKeepsRelayAndDisposesPeer()
        {
            var peer = new Peer(); bool? fatal = null;
            using var session = new RoomRtcSession(peer, true, 0, (_, __) => { }, (_, __) => { }, value => fatal = value);
            session.Poll(RoomRtcSession.NegotiationSeconds + 1);
            Assert.That(fatal, Is.False); Assert.That(peer.Disposed, Is.True);
        }
        [Test]
        public void ReceiveBufferIsBoundedWhileRelayBarrierIsDelayed()
        {
            var peer = new Peer(); bool? fatal = null;
            using var session = new RoomRtcSession(peer, true, 0, (_, __) => { }, (_, __) => Assert.Fail(), value => fatal = value);
            for (int i = 0; i < 1025; i++) peer.Receive(1);
            Assert.That(session.Ended, Is.True); Assert.That(fatal, Is.False);
        }
        [Test]
        public void ControlWireRejectsInvalidFramesAndRespectsArrayOffset()
        {
            byte[] encoded = RoomRtcWire.Encode(RoomRtcControl.Offer, "sdp");
            var buffer = new byte[encoded.Length + 6]; Array.Copy(encoded, 0, buffer, 3, encoded.Length);
            Assert.That(double.IsNaN(BitConverter.ToDouble(encoded, 0)), Is.True);
            Assert.That(RoomRtcWire.TryRead(new ArraySegment<byte>(buffer, 3, encoded.Length), out var kind, out var sdp), Is.True);
            Assert.That(kind, Is.EqualTo(RoomRtcControl.Offer)); Assert.That(sdp, Is.EqualTo("sdp"));
            Assert.That(RoomRtcWire.TryRead(new ArraySegment<byte>(encoded, 0, 8), out _, out _), Is.False);
            encoded[8] = 99;
            Assert.That(RoomRtcWire.TryRead(new ArraySegment<byte>(encoded), out _, out _), Is.False);
            Assert.That(RoomRtcWire.TryRead(new ArraySegment<byte>(RoomRtcWire.Encode(RoomRtcControl.Ready, "extra")), out _, out _), Is.False);
            Assert.Throws<ArgumentException>(() => RoomRtcWire.Encode(RoomRtcControl.Offer, new string('a', RoomRtcWire.MaxSdpBytes + 1)));
        }
    }
}
