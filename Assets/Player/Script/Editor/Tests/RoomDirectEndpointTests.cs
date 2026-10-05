using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BattlePvp.Networking;
using NUnit.Framework;
using Unity.Networking.Transport;

namespace BattlePvp.EditorTests
{
    public sealed class RoomDirectEndpointTests
    {
        [TestCase("203.0.113.1:32853", true)]
        [TestCase("192.168.1.2:7777", false)]
        [TestCase("127.0.0.1:7777", false)]
        [TestCase("100.64.0.1:7777", false)]
        [TestCase("224.1.1.1:7777", false)]
        [TestCase("example.com:7777", false)]
        [TestCase("203.0.113.1:65536", false)]
        [TestCase("203.0.113.1:80", false)]
        public void InternetCandidateDoesNotRequireLocalSubnet(string value, bool valid) =>
            Assert.That(RoomDirectEndpoint.TryParse(value, out _), Is.EqualTo(valid));

        private static byte[] Reply(byte[] request)
        {
            var reply = new byte[32];
            Array.Copy(request, reply, 20); reply[0] = 1; reply[1] = 1; reply[3] = 12;
            reply[21] = 0x20; reply[23] = 8; reply[25] = 1;
            int port = 32853 ^ 0x2112;
            reply[26] = (byte)(port >> 8); reply[27] = (byte)port;
            reply[28] = 203 ^ 0x21; reply[29] = 0x12; reply[30] = 113 ^ 0xa4; reply[31] = 1 ^ 0x42;
            return reply;
        }

        [Test]
        public void StunRejectsWrongTransactionMalformedLengthAndTruncatedAttribute()
        {
            var request = new byte[20]; request[1] = 1; request[4] = 0x21; request[5] = 0x12; request[6] = 0xa4; request[7] = 0x42;
            Array.Copy(Guid.NewGuid().ToByteArray(), 0, request, 8, 12);
            byte[] reply = Reply(request);
            Assert.That(RoomDirectEndpoint.TryReadStun(reply, reply.Length, request, out var endpoint), Is.True);
            Assert.That(endpoint.ToString(), Is.EqualTo("203.0.113.1:32853"));
            reply[9] ^= 1;
            Assert.That(RoomDirectEndpoint.TryReadStun(reply, reply.Length, request, out _), Is.False);
            reply[9] ^= 1; reply[23] = 64;
            Assert.That(RoomDirectEndpoint.TryReadStun(reply, reply.Length, request, out _), Is.False);
            reply = Reply(request); reply[3] = 8;
            Assert.That(RoomDirectEndpoint.TryReadStun(reply, reply.Length, request, out _), Is.False);
        }

#if !UNITY_WEBGL
        [Test]
        public void StunUsesTheGameSocketAndDoesNotCreateAGameConnection()
        {
            using var stun = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var network = new RoomStunNetworkInterface((IPEndPoint)stun.Client.LocalEndPoint);
            var driver = NetworkDriver.Create(network.AsInterface().WrapToUnmanaged());
            try
            {
                Assert.That(driver.Bind(NetworkEndpoint.LoopbackIpv4), Is.Zero);
                Assert.That(driver.Listen(), Is.Zero);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (network.PublicEndpoint == null && timer.ElapsedMilliseconds < 2000)
                {
                    driver.ScheduleUpdate().Complete(); driver.ScheduleFlushSend().Complete();
                    if (stun.Available > 0)
                    {
                        var sender = new IPEndPoint(IPAddress.Any, 0);
                        byte[] request = stun.Receive(ref sender);
                        Assert.That(sender.Port, Is.EqualTo(driver.GetLocalEndpoint().Port));
                        var reply = Reply(request); stun.Send(reply, reply.Length, sender);
                    }
                    Thread.Sleep(1);
                }
                Assert.That(network.PublicEndpoint?.ToString(), Is.EqualTo("203.0.113.1:32853"));
                Assert.That(driver.Accept().IsCreated, Is.False);
            }
            finally { driver.Dispose(); }
        }
#endif
    }
}
