using BattlePvp.Combat;
using BattlePvp.Managers;
using BattlePvp.Networking;
using Mirror;
using UnityEngine;

namespace BattlePvp.UI
{
    public struct BattleChatSubmitMessage : NetworkMessage
    {
        public string SenderName;
        public string Text;
    }

    public struct BattleChatBroadcastMessage : NetworkMessage
    {
        public string SenderName;
        public string Text;
        public double ServerTime;
    }

    public static class BattleChatNetwork
    {
        private const int MaxNameLength = 24;
        private const int MaxMessageLength = 120;
        private static bool _clientRegistered;
        private static bool _serverRegistered;
        private static readonly ChatRateLimiter<NetworkConnectionToClient> _rateLimiter =
            new ChatRateLimiter<NetworkConnectionToClient>();

        public static event System.Action<string, string, double> MessageReceived;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MessageReceived = null;
            _clientRegistered = _serverRegistered = false;
            _rateLimiter.Clear();
        }

        public static void EnsureRegistered()
        {
            if (NetworkClient.active && !_clientRegistered) RegisterClientHandler();
            if (NetworkServer.active && !_serverRegistered) RegisterServerHandler();
        }

        public static void Send(string text)
        {
            string cleanedText = Sanitize(text, MaxMessageLength);
            if (string.IsNullOrWhiteSpace(cleanedText))
                return;

            EnsureRegistered();

            string sender = "Unknown";
            if (GlobalDataManager.Instance != null && !string.IsNullOrWhiteSpace(GlobalDataManager.Instance.PlayerNickname))
                sender = GlobalDataManager.Instance.PlayerNickname;

            sender = Sanitize(sender, MaxNameLength);

            if (NetworkClient.active && NetworkClient.isConnected && NetworkClient.ready)
            {
                try
                {
                    NetworkClient.Send(new BattleChatSubmitMessage
                    {
                        SenderName = sender,
                        Text = cleanedText
                    });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[BattleChat] Chat send skipped because the network connection is not ready: {ex.Message}");
                }
                return;
            }

            if (NetworkClient.active || NetworkServer.active)
            {
                Debug.LogWarning("[BattleChat] Chat send skipped because the room connection is not ready yet.");
                return;
            }

            MessageReceived?.Invoke(sender, cleanedText, Time.unscaledTimeAsDouble);
        }

        public static void RegisterClientHandler()
        {
            NetworkClient.ReplaceHandler<BattleChatBroadcastMessage>(OnClientChatMessage, false);
            _clientRegistered = true;
        }

        public static void RegisterServerHandler()
        {
            NetworkServer.ReplaceHandler<BattleChatSubmitMessage>(OnServerChatMessage, requireAuthentication: true);
            _serverRegistered = true;
        }

        public static void UnregisterClientHandler()
        {
            NetworkClient.UnregisterHandler<BattleChatBroadcastMessage>();
            _clientRegistered = false;
        }

        public static void UnregisterServerHandler()
        {
            NetworkServer.UnregisterHandler<BattleChatSubmitMessage>();
            _serverRegistered = false;
            _rateLimiter.Clear();
        }

        public static void OnServerDisconnected(NetworkConnectionToClient connection) => _rateLimiter.Remove(connection);

        private static void OnServerChatMessage(NetworkConnectionToClient conn, BattleChatSubmitMessage message)
        {
            try
            {
                if (!TryCreateBroadcast(conn, PlayFabBattleManager.Instance?.CurrentRoomId, message,
                    NetworkTime.time, out BattleChatBroadcastMessage broadcast)) return;
                NetworkServer.SendToAll(broadcast, Channels.Reliable, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BattleChat] Ignored chat message because server handling failed: {ex.Message}");
            }
        }

        private static void OnClientChatMessage(BattleChatBroadcastMessage message)
        {
            MessageReceived?.Invoke(
                Sanitize(message.SenderName, MaxNameLength),
                Sanitize(message.Text, MaxMessageLength),
                message.ServerTime);
        }

        private static bool TryCreateBroadcast(NetworkConnectionToClient conn, string currentRoom,
            BattleChatSubmitMessage message, double now, out BattleChatBroadcastMessage broadcast)
        {
            broadcast = default;
            if (conn == null || !conn.isAuthenticated || !conn.isReady ||
                !(conn.authenticationData is AuthenticatedRoomPlayer account) ||
                !RoomIdentity.IsValid(currentRoom) || account.RoomId != currentRoom ||
                !NetworkServer.connections.TryGetValue(conn.connectionId, out NetworkConnectionToClient current) ||
                !ReferenceEquals(current, conn)) return false;

            NetworkIdentity identity = conn.identity;
            if (identity == null || identity.netId == 0 || identity.connectionToClient != conn ||
                !NetworkServer.spawned.TryGetValue(identity.netId, out NetworkIdentity spawned) || spawned != identity ||
                !identity.TryGetComponent(out PlayerManager _) ||
                !identity.TryGetComponent(out ScoreSystem score) || !score.IsConnected) return false;

            // Apply the budget before sanitizing/allocating attacker-controlled strings.
            if (!_rateLimiter.TryConsume(conn, now) || string.IsNullOrWhiteSpace(message.Text) ||
                message.Text.Length > MaxMessageLength) return false;

            string sender = Sanitize(score.PlayerName, MaxNameLength);
            broadcast = new BattleChatBroadcastMessage
            {
                SenderName = string.IsNullOrWhiteSpace(sender) ? "Unknown" : sender,
                Text = Sanitize(message.Text, MaxMessageLength),
                ServerTime = now
            };
            // SenderName stays in the wire message for compatibility, but is never trusted here.
            return true;
        }

        private static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length > maxLength)
            {
                int length = maxLength;
                if (length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
                value = value.Substring(0, length);
            }

            return value;
        }
    }
}
