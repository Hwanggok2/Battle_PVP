using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;

namespace BattlePvp.Networking
{
    public static class RoomStatReadiness
    {
        public static bool AllPlayersReady
        {
            get { GetProgress(out int ready, out int total); return total > 0 && ready == total; }
        }

        public static void GetProgress(out int ready, out int total)
        {
            if (NetworkServer.active)
                CountReady(NetworkServer.connections.Values, out ready, out total);
            else
            {
                total = StatManager.Local != null ? 1 : 0;
                ready = !NetworkClient.active && StatManager.Local != null &&
                    StatValidation.IsCompletePreset(StatManager.Local.GetStatsCopy()) ? 1 : 0;
            }
        }

        public static void CountReady(IEnumerable<NetworkConnectionToClient> players, out int ready, out int total)
        {
            ready = total = 0;
            foreach (var connection in players)
            {
                total++;
                if (IsReady(connection)) ready++;
            }
        }

        public static List<string> GetPendingPlayerNames()
        {
            var names = new List<string>();
            if (NetworkServer.active)
            {
                foreach (var connection in NetworkServer.connections.Values)
                {
                    if (IsReady(connection)) continue;
                    string name = connection?.identity != null ? connection.identity.GetComponent<ScoreSystem>()?.PlayerName : null;
                    if (string.IsNullOrWhiteSpace(name) || name == "Unknown")
                        name = connection != null ? $"플레이어 {connection.connectionId + 1}" : "접속 중인 플레이어";
                    if (connection == null || !connection.isAuthenticated || !connection.isReady || connection.identity == null)
                        name += " (접속 준비 중)";
                    names.Add(name);
                }
            }
            else if (!NetworkClient.active && StatManager.Local != null && !StatValidation.IsCompletePreset(StatManager.Local.GetStatsCopy()))
            {
                string name = StatManager.Local.GetComponent<ScoreSystem>()?.PlayerName;
                names.Add(string.IsNullOrWhiteSpace(name) || name == "Unknown" ? "내 플레이어" : name);
            }
            return names;
        }

        private static bool IsReady(NetworkConnectionToClient connection) => connection != null &&
            connection.isAuthenticated && connection.isReady && connection.identity != null &&
            connection.identity.TryGetComponent<StatManager>(out var stats) && stats.IsAllocationComplete;
    }
}
