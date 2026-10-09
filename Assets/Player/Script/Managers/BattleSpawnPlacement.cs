using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>Reserve a complete non-overlapping layout before moving any player.</summary>
    public sealed class BattleSpawnPlacement
    {
        private readonly List<PlayerManager> _players = new List<PlayerManager>(BattleNetworkManager.PlayerCapacity);

        public bool PlacePlayers()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var points = BattleSpawnPoints.ForScene(scene);
            if (points == null) return false;
            _players.Clear();
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                if (identity != null && identity.gameObject.scene == scene &&
                    identity.TryGetComponent(out PlayerManager player)) _players.Add(player);
            _players.Sort((left, right) => left.netId.CompareTo(right.netId));
            var reservations = new List<Vector3>(_players.Count);
            var poses = new List<Pose>(_players.Count);
            for (int i = 0; i < _players.Count; i++)
            {
                if (!points.TryTake(_players[i], reservations, out var pose, _players))
                { _players.Clear(); return false; }
                reservations.Add(pose.position);
                poses.Add(pose);
            }
            for (int i = 0; i < _players.Count; i++) _players[i].ServerTeleport(poses[i].position, poses[i].rotation);
            _players.Clear();
            return true;
        }
    }
}
