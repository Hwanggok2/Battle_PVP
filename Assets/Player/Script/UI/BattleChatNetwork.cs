using BattlePvp.Combat;
using BattlePvp.Managers;
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

        public static event System.Action<string, string, double> MessageReceived;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MessageReceived = null;
        }

        public static void EnsureRegistered()
        {
            RegisterClientHandler();
            RegisterServerHandler();
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

        private static void RegisterClientHandler()
        {
            NetworkClient.ReplaceHandler<BattleChatBroadcastMessage>(OnClientChatMessage, false);
        }

        private static void RegisterServerHandler()
        {
            NetworkServer.ReplaceHandler<BattleChatSubmitMessage>(OnServerChatMessage, false);
        }

        private static void OnServerChatMessage(NetworkConnectionToClient conn, BattleChatSubmitMessage message)
        {
            try
            {
                string text = Sanitize(message.Text, MaxMessageLength);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                string sender = ResolveServerPlayerName(conn, message.SenderName);
                var broadcast = new BattleChatBroadcastMessage
                {
                    SenderName = sender,
                    Text = text,
                    ServerTime = NetworkTime.time
                };

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

        private static string ResolveServerPlayerName(NetworkConnectionToClient conn, string fallback)
        {
            if (conn != null && conn.identity != null)
            {
                var score = conn.identity.GetComponent<ScoreSystem>();
                if (score != null && !string.IsNullOrWhiteSpace(score.PlayerName))
                    return Sanitize(score.PlayerName, MaxNameLength);
            }

            string cleanedFallback = Sanitize(fallback, MaxNameLength);
            return string.IsNullOrWhiteSpace(cleanedFallback) ? "Unknown" : cleanedFallback;
        }

        private static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length > maxLength)
                value = value.Substring(0, maxLength);

            return value;
        }
    }
}
