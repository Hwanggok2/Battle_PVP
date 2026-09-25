using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using BattlePvp.Networking;
using Mirror;
using NUnit.Framework;
using Unity.Networking.Transport;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class RelayPollingTests
    {
        [Test]
        public void VoluntaryDisconnectNotifiesOnceAfterDisposingTheClient()
        {
            WithLoopback((transport, connectionId) =>
            {
                int clientNotifications = 0;
                int serverNotifications = 0;
                transport.OnClientDisconnected = () =>
                {
                    clientNotifications++;
                    Assert.That(transport.ClientConnected(), Is.False);
                    Assert.That(GetField<NetworkDriver>(transport, "_clientDriver").IsCreated, Is.False);
                    transport.ClientDisconnect();
                    transport.ClientEarlyUpdate();
                };
                transport.OnServerDisconnected = id =>
                {
                    Assert.That(id, Is.EqualTo(connectionId));
                    serverNotifications++;
                };
                transport.ClientDisconnect();
                Assert.That(clientNotifications, Is.EqualTo(1));
                PumpUntil(transport, () => serverNotifications == 1);
                transport.ClientDisconnect();
                transport.Shutdown();
                Assert.That(clientNotifications, Is.EqualTo(1));
                Assert.That(serverNotifications, Is.EqualTo(1));
            });
        }

        [Test]
        public void ServerInitiatedDisconnectNotifiesBothSidesOnce()
        {
            WithLoopback((transport, connectionId) =>
            {
                int serverNotifications = 0;
                int clientNotifications = 0;
                transport.OnServerDisconnected = id =>
                {
                    Assert.That(id, Is.EqualTo(connectionId));
                    serverNotifications++;
                    transport.ServerDisconnect(id);
                };
                transport.OnClientDisconnected = () =>
                {
                    clientNotifications++;
                    transport.ClientDisconnect();
                };
                transport.ServerDisconnect(connectionId);
                Assert.That(serverNotifications, Is.EqualTo(1));
                PumpUntil(transport, () => clientNotifications == 1);
                transport.ServerDisconnect(connectionId);
                transport.ClientDisconnect();
                Assert.That(serverNotifications, Is.EqualTo(1));
                Assert.That(clientNotifications, Is.EqualTo(1));
            });
        }

        [Test]
        public void ConnectionFailureIsDeferredAndShutdownReentryDoesNotDuplicateItsNotification()
        {
            var instance = new GameObject("Transport disconnect regression");
            var transport = instance.AddComponent<UnityRelayTransport>();
            try
            {
                int errors = 0;
                int disconnected = 0;
                transport.OnClientError = (error, message) =>
                {
                    errors++;
                    transport.ClientDisconnect();
                };
                transport.OnClientDisconnected = () =>
                {
                    disconnected++;
                    transport.Shutdown();
                    transport.ClientEarlyUpdate();
                    transport.ServerEarlyUpdate();
                };
                transport.Shutdown();
                Assert.That(disconnected, Is.Zero, "An idle transport has no attempt to terminate.");
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    transport.ClientConnect("relay");
                    Assert.That(errors, Is.EqualTo(attempt - 1), "Mirror must finish creating its connection before failure callbacks.");
                    transport.ClientEarlyUpdate();
                    Assert.That(errors, Is.EqualTo(attempt));
                    Assert.That(disconnected, Is.EqualTo(attempt));
                    transport.ClientDisconnect();
                    transport.ClientEarlyUpdate();
                    Assert.That(disconnected, Is.EqualTo(attempt));
                }
                transport.ClientConnect("relay");
                transport.ClientDisconnect();
                transport.ClientEarlyUpdate();
                Assert.That(errors, Is.EqualTo(2), "Cancelling before the first poll must discard the pending error.");
                Assert.That(disconnected, Is.EqualTo(3));
            }
            finally
            {
                transport.Shutdown();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RetryStartedInsideFailureCallbackSurvivesPreviousAttemptCleanup()
        {
            var instance = new GameObject("Transport callback retry regression");
            var transport = instance.AddComponent<UnityRelayTransport>();
            try
            {
                int errors = 0;
                int disconnected = 0;
                transport.OnClientError = (error, message) =>
                {
                    errors++;
                    transport.ClientDisconnect();
                };
                transport.OnClientDisconnected = () =>
                {
                    disconnected++;
                    if (disconnected == 1) transport.ClientConnect("relay");
                };
                transport.ClientConnect("relay");
                transport.ClientEarlyUpdate();
                Assert.That(errors, Is.EqualTo(1));
                Assert.That(disconnected, Is.EqualTo(1));
                transport.ClientEarlyUpdate();
                Assert.That(errors, Is.EqualTo(2), "The first failure must not discard the retry's pending error.");
                Assert.That(disconnected, Is.EqualTo(2));
                transport.ClientEarlyUpdate();
                transport.ClientDisconnect();
                Assert.That(disconnected, Is.EqualTo(2));
            }
            finally
            {
                transport.Shutdown();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ReceiveCallbackCannotReenterEitherPollOrOverwriteItsPayload()
        {
            WithLoopback((transport, connectionId) =>
            {
                var received = new List<byte>();
                bool handlingServerData = false;
                bool gotReply = false;
                transport.OnClientDataReceived = (data, channel) =>
                {
                    Assert.That(handlingServerData, Is.False, "Client poll must wait for server callback completion.");
                    Assert.That(data.Array[data.Offset], Is.EqualTo(9));
                    gotReply = true;
                };
                transport.OnServerDataReceived = (id, data, channel) =>
                {
                    Assert.That(handlingServerData, Is.False, "Server poll must not recursively deliver another packet.");
                    handlingServerData = true;
                    byte original = data.Array[data.Offset];
                    transport.ServerEarlyUpdate();
                    transport.ClientEarlyUpdate();
                    Assert.That(data.Array[data.Offset], Is.EqualTo(original));
                    received.Add(original);
                    handlingServerData = false;
                };
                transport.ServerSend(connectionId, new ArraySegment<byte>(new byte[] { 9 }), Channels.Unreliable);
                transport.ClientSend(new ArraySegment<byte>(new byte[] { 1 }), Channels.Unreliable);
                transport.ClientSend(new ArraySegment<byte>(new byte[] { 2 }), Channels.Unreliable);
                transport.ServerLateUpdate();
                transport.ClientLateUpdate();
                PumpUntil(transport, () => received.Count == 2 && gotReply);
                Assert.That(received, Is.EqualTo(new byte[] { 1, 2 }));
            });
        }

        [Test]
        public void ShutdownInsideReceiveCallbackEndsPollingWithoutUsingDisposedDrivers()
        {
            WithLoopback((transport, connectionId) =>
            {
                bool received = false;
                transport.OnServerDataReceived = (id, data, channel) =>
                {
                    transport.Shutdown();
                    transport.ServerEarlyUpdate();
                    transport.ClientEarlyUpdate();
                    received = true;
                };
                transport.ClientSend(new ArraySegment<byte>(new byte[] { 1 }), Channels.Unreliable);
                transport.ClientLateUpdate();
                PumpUntil(transport, () => received);
                Assert.That(transport.ServerActive(), Is.False);
                Assert.That(transport.ClientConnected(), Is.False);
                transport.ServerEarlyUpdate();
                transport.ClientEarlyUpdate();
            });
        }

        // Local UTP sockets exercise the real polling implementation without Relay credentials.
        // Reflection only supplies drivers normally created by the Relay preparation path.
        private static void WithLoopback(Action<UnityRelayTransport, int> body)
        {
            var instance = new GameObject("Transport polling regression");
            var transport = instance.AddComponent<UnityRelayTransport>();
            try
            {
                NetworkDriver server = NetworkDriver.Create();
                SetField(transport, "_serverDriver", server);
                Assert.That(server.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(0)), Is.Zero);
                Assert.That(server.Listen(), Is.Zero);
                NetworkDriver client = NetworkDriver.Create();
                SetField(transport, "_clientDriver", client);
                SetField(transport, "_clientConnection", client.Connect(server.GetLocalEndpoint()));
                SetField(transport, "_clientDisconnectPending", true);
                int connectionId = 0;
                transport.OnServerConnectedWithAddress = (id, address) => connectionId = id;
                PumpUntil(transport, () => connectionId != 0 && transport.ClientConnected());
                body(transport, connectionId);
            }
            finally
            {
                transport.Shutdown();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void PumpUntil(UnityRelayTransport transport, Func<bool> completed)
        {
            var timer = Stopwatch.StartNew();
            while (!completed() && timer.ElapsedMilliseconds < 2000)
            {
                transport.ServerEarlyUpdate();
                transport.ClientEarlyUpdate();
                transport.ServerLateUpdate();
                transport.ClientLateUpdate();
                Thread.Sleep(1);
            }
            Assert.That(completed(), Is.True, "Local UTP connection did not complete within 2 seconds.");
        }

        private static void SetField<T>(UnityRelayTransport transport, string name, T value)
        {
            typeof(UnityRelayTransport).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(transport, value);
        }

        private static T GetField<T>(UnityRelayTransport transport, string name) =>
            (T)typeof(UnityRelayTransport).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transport);
    }
}
