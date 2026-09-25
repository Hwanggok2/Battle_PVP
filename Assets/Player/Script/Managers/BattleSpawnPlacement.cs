using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>Mirror에 등록된 시작점과 서버 플레이어만으로 시작 위치를 배정한다.</summary>
    public sealed class BattleSpawnPlacement
    {
        private readonly List<PlayerManager> _players = new List<PlayerManager>(BattleNetworkManager.PlayerCapacity);

        public void PlacePlayers()
        {
            List<Transform> starts = NetworkManager.startPositions;
            starts.RemoveAll(start => start == null);
            if (starts.Count == 0)
            {
                Debug.LogWarning("[BattleSpawnPlacement] No registered start positions. Players keep their positions.");
                return;
            }

            _players.Clear();
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                if (identity != null && identity.TryGetComponent(out PlayerManager player)) _players.Add(player);
            _players.Sort((left, right) => left.netId.CompareTo(right.netId));
            for (int i = 0; i < _players.Count; i++)
            {
                Transform start = starts[i % starts.Count];
                _players[i].ServerTeleport(start.position, start.rotation);
            }
            _players.Clear();
        }
    }
}
