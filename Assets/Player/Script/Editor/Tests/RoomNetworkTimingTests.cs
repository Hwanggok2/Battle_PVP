using System;
using System.Collections.Generic;
using BattlePvp.Networking;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.EditorTests
{
    public sealed class RoomNetworkTimingTests
    {
        private sealed class CapturedConnection : NetworkConnectionToClient
        {
            public readonly List<(int channel, byte value)> Messages = new List<(int, byte)>();
            public int Packets;
            public CapturedConnection() : base(1) { }
            public void Queue(byte value, int channel) => GetBatchForChannelId(channel).AddMessage(new ArraySegment<byte>(new[] { value }), 1d);
            protected override void SendToTransport(ArraySegment<byte> bytes, int channel)
            {
                Packets++;
                var unbatcher = new Unbatcher();
                Assert.That(unbatcher.AddBatch(bytes), Is.True);
                while (unbatcher.GetNextMessage(out var message, out _)) Messages.Add((channel, message.Array[message.Offset]));
            }
        }

        [Test]
        public void EarlyFlushKeepsChannelOrderAndDoesNotDuplicateLateFlush()
        {
            var previous = Transport.active;
            var root = new GameObject("Early network flush fixture");
            var transport = root.AddComponent<UnityRelayTransport>();
            Transport.active = transport;
            var connection = new CapturedConnection();
            try
            {
                connection.Queue(1, Channels.Reliable); connection.Queue(2, Channels.Reliable);
                connection.Queue(3, Channels.Unreliable); connection.Queue(4, Channels.Unreliable);
                connection.FlushBatches();
                CollectionAssert.AreEqual(new[] { (Channels.Reliable, (byte)1), (Channels.Reliable, (byte)2),
                    (Channels.Unreliable, (byte)3), (Channels.Unreliable, (byte)4) }, connection.Messages);
                int packets = connection.Packets;
                connection.FlushBatches();
                Assert.That(connection.Packets, Is.EqualTo(packets));
                connection.Queue(5, Channels.Reliable);
                connection.FlushBatches();
                Assert.That(connection.Messages.Count, Is.EqualTo(5));
                Assert.That(connection.Messages[4], Is.EqualTo((Channels.Reliable, (byte)5)));
            }
            finally { connection.Cleanup(); transport.Shutdown(); UnityEngine.Object.DestroyImmediate(root); Transport.active = previous; }
        }

        [TestCase(60, 2)]
        [TestCase(30, 4)]
        public void NativeFramePacingPreservesRenderBudgetAndRestoresOfflineRate(int fps, int interval)
        {
            int originalFps = Application.targetFrameRate, originalInterval = OnDemandRendering.renderFrameInterval;
            float physicsStep = Time.fixedDeltaTime;
            int tickRate = NetworkServer.tickRate;
            try
            {
                RoomNetworkTiming.ApplyFrameRate(fps, true);
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
                Assert.That(OnDemandRendering.renderFrameInterval, Is.EqualTo(interval));
                Assert.That(Application.targetFrameRate / OnDemandRendering.renderFrameInterval, Is.EqualTo(fps));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));
                Assert.That(NetworkServer.tickRate, Is.EqualTo(tickRate));
                RoomNetworkTiming.ApplyFrameRate(fps, false);
                Assert.That(Application.targetFrameRate, Is.EqualTo(fps));
                Assert.That(OnDemandRendering.renderFrameInterval, Is.EqualTo(1));
            }
            finally { Application.targetFrameRate = originalFps; OnDemandRendering.renderFrameInterval = originalInterval; }
        }
    }
}
