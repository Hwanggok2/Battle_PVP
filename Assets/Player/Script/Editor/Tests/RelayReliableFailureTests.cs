using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Networking;
using Mirror;
using NUnit.Framework;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UtpConnection = Unity.Networking.Transport.NetworkConnection;

namespace BattlePvp.EditorTests
{
    // Uses UTP's in-memory IPC interface: no Relay service, socket, or PlayFab login.
    public sealed class RelayReliableFailureTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(false)]
        [TestCase(true)]
        public void ReliableWindowOverflowDisconnectsInsteadOfContinuingWithLostMessages(bool stopInErrorCallback)
        {
            var root = new GameObject("Relay reliable failure fixture");
            root.SetActive(false);
            var transport = root.AddComponent<UnityRelayTransport>();
            NetworkDriver receiver = default;
            var settings = new NetworkSettings();
            try
            {
                settings.WithReliableStageParameters(windowSize: 1);
                settings.WithFragmentationStageParameters(payloadCapacity: UnityRelayTransport.ReliablePacketCapacity);
                var sender = NetworkDriver.Create(new IPCNetworkInterface(), settings);
                Set(transport, "_serverDriver", sender);
                var pipeline = sender.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
                Set(transport, "_serverReliablePipeline", pipeline);
                Assert.That(sender.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(0)), Is.Zero);
                Assert.That(sender.Listen(), Is.Zero);
                receiver = NetworkDriver.Create(new IPCNetworkInterface(), settings);
                receiver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
                receiver.Connect(sender.GetLocalEndpoint());
                UtpConnection connection = default;
                for (int i = 0; i < 20 && !connection.IsCreated; i++)
                {
                    receiver.ScheduleUpdate().Complete();
                    sender.ScheduleUpdate().Complete();
                    connection = sender.Accept();
                }
                Assert.That(connection.IsCreated, Is.True, "The in-memory peer must connect before saturating reliable sends.");
                Get<Dictionary<int, UtpConnection>>(transport, "_serverConnections")[1] = connection;
                Get<Dictionary<int, ReliableSendBacklog>>(transport, "_serverBacklogs")[1] = new ReliableSendBacklog();
                int failures = 0, disconnects = 0, sent = 0;
                transport.OnServerDataSent = (_, __, ___) => sent++;
                transport.OnServerDisconnected = _ => disconnects++;
                transport.OnServerError = (_, __, ___) =>
                {
                    failures++;
                    if (stopInErrorCallback) transport.ServerStop();
                };
                transport.ServerSend(1, new ArraySegment<byte>(new byte[] { 1 }));
                // No receiver update/ACK: the single reliable slot remains occupied.
                transport.ServerSend(1, new ArraySegment<byte>(new byte[] { 2 }));
                Assert.That(sent, Is.EqualTo(1));
                Assert.That(failures, Is.EqualTo(1));
                Assert.That(Get<Dictionary<int, UtpConnection>>(transport, "_serverConnections"), Is.Empty);
                Assert.That(stopInErrorCallback ? !transport.ServerActive() : disconnects == 1, Is.True,
                    "An unconfirmed EndSend must end the connection or entire transport, including callback reentry.");
                transport.ServerSend(1, new ArraySegment<byte>(new byte[] { 3 }));
                Assert.That(sent, Is.EqualTo(1), "The failed peer cannot continue as though delivery succeeded.");
            }
            finally
            {
                transport.Shutdown();
                if (receiver.IsCreated) receiver.Dispose();
                settings.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    }
}
