using System;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Networking;
using Mirror;
using NUnit.Framework;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class RoomDirectTransportTests
    {
        private const string Room = "battle_abc123_11111111111111111111111111111111";
        private const string Nonce = "22222222222222222222222222222222";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Set(UnityRelayTransport t, string field, object value) => typeof(UnityRelayTransport).GetField(field, Private).SetValue(t, value);
        private static T Get<T>(UnityRelayTransport t, string field) => (T)typeof(UnityRelayTransport).GetField(field, Private).GetValue(t);

        [Test]
        public async Task PublicDirectCandidateIsPreparedWithoutJoiningRelay()
        {
            var root = new GameObject("Internet UDP candidate fixture");
            var transport = root.AddComponent<UnityRelayTransport>();
            try
            {
                // An invalid Relay code cannot be accepted by UGS. This path must not call it at all.
                await transport.PrepareClientAsync("not-a-relay-code", CancellationToken.None, null, "203.0.113.1:32853");
                Assert.That(Get<IPEndPoint>(transport, "_directEndpoint")?.ToString(), Is.EqualTo("203.0.113.1:32853"));
                Assert.That(Get<bool>(transport, "_hasPreparedClientRelay"), Is.False);
            }
            finally { transport.Shutdown(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase("127.0.0.1", true)]
        [TestCase("192.168.1.8", true)]
        [TestCase("10.0.0.8", true)]
        [TestCase("172.31.0.1", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("8.8.8.8", false)]
        public void DiscoveryAcceptsOnlyMatchingLocalReplies(string address, bool accepted)
        {
            string key = RoomLanDiscovery.RoomKey(Room, "test-room-code");
            byte[] reply = Encoding.ASCII.GetBytes("BPVP1R|" + key + "|" + Nonce + "|32123");
            Assert.That(RoomLanDiscovery.TryReadResponse(reply, IPAddress.Parse(address), key, Nonce, out var endpoint), Is.EqualTo(accepted));
            if (accepted) Assert.That(endpoint.Port, Is.EqualTo(32123));
            Assert.That(RoomLanDiscovery.TryReadResponse(reply, IPAddress.Loopback, key, Guid.NewGuid().ToString("N"), out _), Is.False);
            Assert.That(RoomLanDiscovery.TryReadResponse(reply, IPAddress.Loopback, RoomLanDiscovery.RoomKey(Room, "different-room-code"), Nonce, out _), Is.False);
            Assert.That(RoomLanDiscovery.TryReadResponse(new byte[1024], IPAddress.Loopback, key, Nonce, out _), Is.False);
            Assert.That(RoomLanDiscovery.RoomKey("invalid-room", "test"), Is.Null);
        }

        [Test]
        public async Task DiscoveryFindsSelectedRoomAndCancellationReleasesItsSocket()
        {
            using var host = new RoomLanDiscovery();
            string key = RoomLanDiscovery.RoomKey(Room, "test-room-code");
            Assert.That(host.Start(key, 32123), Is.True);
            Task<IPEndPoint> find = RoomLanDiscovery.FindAsync(key, CancellationToken.None);
            while (!find.IsCompleted) { host.Poll(); await Task.Delay(5); }
            Assert.That((await find)?.Port, Is.EqualTo(32123));
            using var cancel = new CancellationTokenSource();
            Task<IPEndPoint> missing = RoomLanDiscovery.FindAsync(RoomLanDiscovery.RoomKey(Room, "another-room"), cancel.Token);
            cancel.Cancel();
            try { await missing; Assert.Fail("Cancelled discovery must not return a stale endpoint."); }
            catch (OperationCanceledException) { }
            host.Dispose();
            Assert.That(host.Start(key, 32124), Is.True);
        }

        [Test]
        public void PrimaryAndDirectDriversKeepReliableTrafficAndDisconnectsSeparate()
        {
            var hostObject = new GameObject("Hybrid host fixture");
            var firstObject = new GameObject("Primary client fixture");
            var secondObject = new GameObject("Direct client fixture");
            var host = hostObject.AddComponent<UnityRelayTransport>();
            var first = firstObject.AddComponent<UnityRelayTransport>();
            var second = secondObject.AddComponent<UnityRelayTransport>();
            var settings = new NetworkSettings();
            try
            {
                settings.WithFragmentationStageParameters(payloadCapacity: UnityRelayTransport.ReliablePacketCapacity);
                settings.WithReliableStageParameters(windowSize: 128);
                var primary = NetworkDriver.Create(settings);
                Set(host, "_serverDriver", primary);
                Set(host, "_serverReliablePipeline", primary.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage)));
                Assert.That(primary.Bind(NetworkEndpoint.LoopbackIpv4), Is.Zero);
                Assert.That(primary.Listen(), Is.Zero);
                host.ConfigureLocalRoom(Room, "test-room-code");
                typeof(UnityRelayTransport).GetMethod("StartDirectServer", Private).Invoke(host, null);
                Assert.That(host.DirectPort, Is.Not.Zero);
                int primaryId = 0, directId = 0, clientDisconnects = 0;
                host.OnServerConnectedWithAddress = (id, address) => { if(address.StartsWith("direct:"))directId=id; else primaryId=id; };
                first.OnClientDisconnected = () => clientDisconnects++;
                second.OnClientDisconnected = () => clientDisconnects++;
                Start(first, primary.GetLocalEndpoint().Port);
                Start(second, host.DirectPort);
                Action pump = () => { host.ServerEarlyUpdate(); first.ClientEarlyUpdate(); second.ClientEarlyUpdate(); host.ServerLateUpdate(); first.ClientLateUpdate(); second.ClientLateUpdate(); };
                Wait(pump, () => primaryId > 0 && directId > 0 && first.ClientConnected() && second.ClientConnected());
                Assert.That(primaryId, Is.Not.EqualTo(directId));
                Assert.That(host.DirectPeerCount, Is.EqualTo(1));
                Assert.That(host.RelayPeerCount, Is.EqualTo(1));
                int received = 0;
                byte[] payload = new byte[50000]; for (int i=0;i<payload.Length;i++)payload[i]=(byte)(i%251);
                host.OnServerDataReceived = (id, bytes, channel) => {
                    Assert.That(channel, Is.EqualTo(Channels.Reliable));
                    Assert.That(bytes.Count, Is.EqualTo(payload.Length));
                    host.ServerSend(id, bytes, channel);
                };
                first.OnClientDataReceived = (bytes, channel) => { CollectionAssert.AreEqual(payload, bytes); received++; };
                second.OnClientDataReceived = (bytes, channel) => { CollectionAssert.AreEqual(payload, bytes); received++; };
                first.ClientSend(new ArraySegment<byte>(payload)); second.ClientSend(new ArraySegment<byte>(payload));
                Wait(pump, () => received == 2);
                host.ServerDisconnect(directId);
                Wait(pump, () => clientDisconnects == 1);
                Assert.That(first.ClientConnected(), Is.True);
                Assert.That(second.ClientConnected(), Is.False);
                Assert.That(host.DirectPeerCount, Is.Zero);
                Assert.That(host.RelayPeerCount, Is.EqualTo(1));
            }
            finally
            {
                first.Shutdown(); second.Shutdown(); host.Shutdown(); settings.Dispose();
                UnityEngine.Object.DestroyImmediate(firstObject); UnityEngine.Object.DestroyImmediate(secondObject); UnityEngine.Object.DestroyImmediate(hostObject);
            }
        }

        private static void Start(UnityRelayTransport client, ushort port)
        {
            Set(client, "_hasPreparedClientRelay", true);
            Set(client, "_directEndpoint", new IPEndPoint(IPAddress.Loopback, port));
            client.ClientConnect("room");
        }
        private static void Wait(Action pump, Func<bool> done)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && watch.ElapsedMilliseconds < 4000) { pump(); Thread.Sleep(1); }
            Assert.That(done(), Is.True, "Transport exchange did not complete.");
        }
    }
}
