using BattlePvp.Combat;
using BattlePvp.Stats;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Networking
{
    public partial class BattleNetworkManager
    {
        public const int MaxPracticeBots = 15;
        public bool IsPractice { get; private set; }
        public int PracticeBotCount { get; private set; }
        public string PracticeRoomId { get; private set; }
        private bool _practiceBotsCreated;
        private PracticeBotSetup[] _practiceSetups;

        public bool StartPractice(int bots, byte map)
        {
            if (bots < 0 || bots > MaxPracticeBots || NetworkServer.active || NetworkClient.active) return false;
            var setups = new PracticeBotSetup[bots];
            for (int i = 0; i < bots; i++) setups[i] = PracticeBotSetup.Randomized();
            return StartPractice(setups, map);
        }

        public bool StartPractice(IReadOnlyList<PracticeBotSetup> bots, byte map)
        {
            if (NetworkServer.active || NetworkClient.active || bots == null || bots.Count > MaxPracticeBots ||
                map >= BattleMapSelection.MapCount || playerPrefab == null) return false;
            var snapshot = new PracticeBotSetup[bots.Count];
            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i] == null || !bots[i].IsValid) return false;
                snapshot[i] = bots[i].Copy();
            }
            _practiceSetups = snapshot;
            IsPractice = true;
            PracticeBotCount = snapshot.Length;
            _practiceBotsCreated = false;
            RoomIdentity.TryCreate("0", System.Guid.NewGuid(), out var roomId);
            PracticeRoomId = roomId;
            SelectedBattleMap = map;
            SelectedMatchDuration = 180;
            // The Lobby scene supplies a fresh manager after StopHost completes.
            transport = GetComponent<PracticeTransport>() ?? gameObject.AddComponent<PracticeTransport>();
            Transport.active = transport;
            authenticator = null;
            onlineScene = "Assets/Scenes/Battle.unity";
            offlineScene = "Assets/Scenes/Lobby.unity";
            StartHost();
            return NetworkServer.active;
        }

        public void StopPractice()
        {
            if (!IsPractice || !NetworkServer.active) return;
            StopHost();
        }

        private void AddPracticePlayer(NetworkConnectionToClient connection)
        {
            if (connection != NetworkServer.localConnection || !(connection is LocalConnectionToClient))
            { connection.Disconnect(); return; }
            var points = BattleSpawnPoints.ForScene(SceneManager.GetActiveScene());
            if (points == null || !points.TryTake(null, null, out var pose))
            { Debug.LogError("[Practice] No safe player spawn."); StopPractice(); return; }
            var player = Instantiate(playerPrefab, pose.position, pose.rotation);
            NetworkServer.AddPlayerForConnection(connection, player);
            var stats = player.GetComponent<StatManager>();
            if (!stats.HasServerCombatStats) stats.TryApplyServerPreset(DefaultPracticeStats());
        }

        public bool PreparePracticeBots()
        {
            if (!IsPractice || !NetworkServer.active) return true;
            if (_practiceBotsCreated) return true;
            var points = BattleSpawnPoints.ForScene(SceneManager.GetActiveScene());
            if (points == null) return false;
            // Reserve the entire wave before spawning/initializing any visual or network callbacks.
            var reserved = new List<Vector3>(PracticeBotCount);
            var poses = new List<Pose>(PracticeBotCount);
            for (int i = 0; i < PracticeBotCount; i++)
            {
                if (!points.TryTake(null, reserved, out var pose)) return false;
                reserved.Add(pose.position);
                poses.Add(pose);
            }
            for (int i = 0; i < PracticeBotCount; i++)
            {
                var pose = poses[i];
                var bot = Instantiate(playerPrefab, pose.position, pose.rotation);
                bot.name = $"Practice AI {i + 1:00}";
                bot.AddComponent<PracticeBot>();
                NetworkServer.Spawn(bot);
                if (!bot.GetComponent<PracticeBot>().InitializeLoadout(_practiceSetups[i]))
                { Debug.LogError($"[Practice] AI {i + 1} setup could not be applied."); return false; }
                bot.GetComponent<ScoreSystem>().SetPlayerName($"AI {i + 1:00}");
            }
            _practiceBotsCreated = true;
            return true;
        }

        public static StatContainer DefaultPracticeStats() => new StatContainer
        {
            STR = new StatSlot { Invested = 8 }, CON = new StatSlot { Invested = 8 },
            AGI = new StatSlot { Invested = 7 }, DEF = new StatSlot { Invested = 7 }
        };

        public bool TryGetPracticeParticipant(NetworkIdentity identity, out string id)
        {
            id = null;
            if (!IsPractice || !NetworkServer.active || identity == null || identity.netId == 0) return false;
            if (identity.connectionToClient != NetworkServer.localConnection && identity.GetComponent<PracticeBot>() == null) return false;
            // Local ledger only; these IDs are never submitted to the account/room service.
            id = identity.netId.ToString("x");
            return true;
        }
    }
}
