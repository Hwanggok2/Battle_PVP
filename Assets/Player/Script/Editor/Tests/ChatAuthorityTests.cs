using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.UI;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BattlePvp.EditorTests
{
    public sealed class ChatAuthorityTests
    {
        private const int ConnectionId = int.MaxValue - 41;
        private const uint PlayerId = uint.MaxValue - 41;
        private const string Room = "battle_abc123_11111111111111111111111111111111";
        private GameObject _player;
        private TestConnection _connection;
        private NetworkIdentity _identity;
        private ScoreSystem _score;
        private NetworkMessageDelegate _previousHandler;
        private bool _previousRegistered;
        private static Dictionary<ushort, NetworkMessageDelegate> Handlers =>
            (Dictionary<ushort, NetworkMessageDelegate>)typeof(NetworkServer)
                .GetField("handlers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private sealed class TestConnection : NetworkConnectionToClient
        {
            public bool WasDisconnected;
            public TestConnection(int id) : base(id) { }
            public override void Disconnect() => WasDisconnected = true;
        }

        [SetUp]
        public void SetUp()
        {
            Assert.That(NetworkServer.active, Is.False, "Run these fixtures without a live server.");
            Assert.That(NetworkServer.connections.ContainsKey(ConnectionId), Is.False);
            Assert.That(NetworkServer.spawned.ContainsKey(PlayerId), Is.False);
            Handlers.TryGetValue(NetworkMessages.GetId<BattleChatSubmitMessage>(), out _previousHandler);
            _previousRegistered = (bool)typeof(BattleChatNetwork).GetField("_serverRegistered",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            BattleChatNetwork.UnregisterServerHandler();
            _connection = new TestConnection(ConnectionId)
            {
                isAuthenticated = true,
                isReady = true,
                authenticationData = new AuthenticatedRoomPlayer("ABC123", Room)
            };
            _player = new GameObject("Chat authority fixture");
            _player.SetActive(false);
            _identity = _player.AddComponent<NetworkIdentity>();
            _player.AddComponent<PlayerManager>();
            _score = _player.AddComponent<ScoreSystem>();
            SetProperty(_identity, nameof(NetworkIdentity.netId), PlayerId);
            SetProperty(_identity, nameof(NetworkIdentity.connectionToClient), _connection);
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(_score, _identity);
            typeof(NetworkConnection).GetProperty(nameof(NetworkConnection.identity)).SetValue(_connection, _identity);
            _score.PlayerName = "Server name";
            NetworkServer.connections.Add(ConnectionId, _connection);
            NetworkServer.spawned.Add(PlayerId, _identity);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkServer.connections.Remove(ConnectionId);
            NetworkServer.spawned.Remove(PlayerId);
            if (_identity != null) SetProperty(_identity, nameof(NetworkIdentity.connectionToClient), null);
            if (_player != null) UnityEngine.Object.DestroyImmediate(_player);
            BattleChatNetwork.UnregisterServerHandler();
            if (_previousHandler != null)
                Handlers[NetworkMessages.GetId<BattleChatSubmitMessage>()] = _previousHandler;
            typeof(BattleChatNetwork).GetField("_serverRegistered", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, _previousRegistered);
        }

        [Test]
        public void ValidMemberUsesServerNameAndSanitizedText()
        {
            Assert.That(TryBroadcast("  안녕\r\n하세요  ", 10, out BattleChatBroadcastMessage result), Is.True);
            Assert.That(result.SenderName, Is.EqualTo("Server name"));
            Assert.That(result.Text, Is.EqualTo("안녕  하세요"));
            Assert.That(result.ServerTime, Is.EqualTo(10));
            _score.PlayerName = " ";
            Assert.That(TryBroadcast("hello", 10, out result), Is.True);
            Assert.That(result.SenderName, Is.EqualTo("Unknown"), "Never use the submitted forged name as fallback.");
        }

        [Test]
        public void NameAndOutgoingMessageLimitsKeepCompleteSurrogatePairs()
        {
            string emoji = "\U0001F600";
            Assert.That(MatchLedger.NormalizeName("  " + new string('a', 63) + emoji + "  "),
                Is.EqualTo(new string('a', 63)), "The 64-unit ledger limit must not retain half an emoji.");
            Assert.That(MatchLedger.NormalizeName("  " + new string('a', 62) + emoji + "  "),
                Is.EqualTo(new string('a', 62) + emoji), "An exact-fit ledger name stays intact.");

            _score.PlayerName = new string('a', 23) + emoji;
            string exactMessage = new string('b', 118) + emoji;
            Assert.That(TryBroadcast(exactMessage, 10d, out BattleChatBroadcastMessage cutSender), Is.True);
            Assert.That(cutSender.SenderName, Is.EqualTo(new string('a', 23)),
                "The server's existing 24-unit sender limit must not split the authoritative name.");
            Assert.That(cutSender.Text, Is.EqualTo(exactMessage), "A complete 120-unit message is accepted unchanged.");
            _score.PlayerName = new string('a', 22) + emoji;
            Assert.That(TryBroadcast("normal", 11d, out BattleChatBroadcastMessage exactSender), Is.True);
            Assert.That(exactSender.SenderName, Is.EqualTo(new string('a', 22) + emoji));

            MethodInfo sanitize = typeof(BattleChatNetwork).GetMethod("Sanitize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(sanitize.Invoke(null, new object[] { "  " + new string('b', 119) + emoji + "\r\n  ", 120 }),
                Is.EqualTo(new string('b', 119)), "The sending path trims CR/LF before truncating a message at 120 units.");
            Assert.That(sanitize.Invoke(null, new object[] { exactMessage, 120 }), Is.EqualTo(exactMessage));
            Assert.That(sanitize.Invoke(null, new object[] { "  A\r\nB  ", 120 }), Is.EqualTo("A  B"),
                "Existing CR/LF replacement and trim behavior remains unchanged.");
        }

        [TestCase("unauthenticated")]
        [TestCase("not-ready")]
        [TestCase("no-room-authentication")]
        [TestCase("different-room")]
        [TestCase("stale-connection")]
        [TestCase("no-avatar")]
        [TestCase("wrong-owner")]
        [TestCase("unspawned")]
        [TestCase("disconnected-body")]
        public void RejectsConnectionsOutsideTheCurrentConnectedRoster(string reason)
        {
            switch (reason)
            {
                case "unauthenticated": _connection.isAuthenticated = false; break;
                case "not-ready": _connection.isReady = false; break;
                case "no-room-authentication": _connection.authenticationData = null; break;
                case "different-room": _connection.authenticationData = new AuthenticatedRoomPlayer("ABC123", "battle_abc123_22222222222222222222222222222222"); break;
                case "stale-connection": NetworkServer.connections[ConnectionId] = new TestConnection(ConnectionId); break;
                case "no-avatar": typeof(NetworkConnection).GetProperty(nameof(NetworkConnection.identity)).SetValue(_connection, null); break;
                case "wrong-owner": SetProperty(_identity, nameof(NetworkIdentity.connectionToClient), null); break;
                case "unspawned": NetworkServer.spawned.Remove(PlayerId); break;
                case "disconnected-body": typeof(ScoreSystem).GetField("_isConnected", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_score, false); break;
            }
            Assert.That(TryBroadcast("hello", 10, out _), Is.False);
        }

        [Test]
        public void HandlerRegistrationRequiresAuthenticationBeforeReadingPayload()
        {
            BattleChatNetwork.RegisterServerHandler();
            _connection.isAuthenticated = false;
            var reader = new NetworkReader(Array.Empty<byte>());
            LogAssert.Expect(LogType.Warning, new Regex("required authentication"));
            Handlers[NetworkMessages.GetId<BattleChatSubmitMessage>()](_connection, reader, Channels.Reliable);
            Assert.That(_connection.WasDisconnected, Is.True);
            Assert.That(reader.Position, Is.Zero);
        }

        [Test]
        public void BroadcastPathLimitsBurstsAndCleansUpAfterDisconnectAndServerStop()
        {
            for (int i = 0; i < ChatRateLimiter<NetworkConnectionToClient>.BurstCapacity; i++)
                Assert.That(TryBroadcast("hello", 10, out _), Is.True);
            Assert.That(TryBroadcast("hello", 10, out _), Is.False);
            BattleChatNetwork.RegisterServerHandler();
            Assert.That(TryBroadcast("hello", 10, out _), Is.False, "Re-registering cannot refill a sender's budget.");
            Assert.That(TryBroadcast("hello", 11, out _), Is.True);
            Assert.That(TryBroadcast("hello", 11, out _), Is.False);
            BattleChatNetwork.OnServerDisconnected(_connection);
            Assert.That(TryBroadcast("hello", 11, out _), Is.True);
            BattleChatNetwork.UnregisterServerHandler();
            Assert.That(TryBroadcast("hello", 1, out _), Is.True, "New server lifecycle permits a fresh clock and budget.");
        }

        [TestCase(null)]
        [TestCase(" \r\n ")]
        [TestCase("oversized")]
        public void RejectsEmptyOrOversizedPayloads(string text)
        {
            if (text == "oversized") text = new string('a', 121);
            Assert.That(TryBroadcast(text, 10, out _), Is.False);
        }

        private bool TryBroadcast(string text, double time, out BattleChatBroadcastMessage result)
        {
            object[] args = { _connection, Room, new BattleChatSubmitMessage { SenderName = "Forged administrator", Text = text }, time, null };
            bool accepted = (bool)typeof(BattleChatNetwork).GetMethod("TryCreateBroadcast", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);
            result = (BattleChatBroadcastMessage)args[4];
            return accepted;
        }

        private static void SetProperty(NetworkIdentity identity, string property, object value) =>
            typeof(NetworkIdentity).GetProperty(property).SetValue(identity, value);
    }
}
