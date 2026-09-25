using System;
using System.Collections.Generic;

namespace BattlePvp.Networking
{
    // Only packets rejected by BeginSend belong here. An EndSend failure may have
    // partially submitted fragments and must terminate the connection instead.
    public sealed class ReliableSendBacklog
    {
        private readonly Queue<(byte[] Bytes, double Enqueued)> _packets = new Queue<(byte[], double)>();
        public const int MaxBytes = 1_048_576;
        public const int MaxPackets = 1024;
        public const double MaxWaitSeconds = 10d;
        public int Count => _packets.Count;
        public int Bytes { get; private set; }

        public bool TryEnqueue(ArraySegment<byte> packet, double now)
        {
            if (packet.Array == null || Count >= MaxPackets || packet.Count > MaxBytes - Bytes) return false;
            var copy = new byte[packet.Count];
            Array.Copy(packet.Array, packet.Offset, copy, 0, packet.Count);
            _packets.Enqueue((copy, now));
            Bytes += copy.Length;
            return true;
        }

        public bool HasExpired(double now) => Count > 0 && now - _packets.Peek().Enqueued >= MaxWaitSeconds;
        public ArraySegment<byte> Peek() => new ArraySegment<byte>(_packets.Peek().Bytes);
        public void RemoveFirst() => Bytes -= _packets.Dequeue().Bytes.Length;
        public void Clear() { _packets.Clear(); Bytes = 0; }
    }
}
