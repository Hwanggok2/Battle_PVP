using System.Collections.Generic;
using Mirror;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace BattlePvp.Networking
{
    /// <summary>One walkable spawn pool per map. Never wraps onto an occupied spawn.</summary>
    public sealed class BattleSpawnPoints : MonoBehaviour
    {
        public const float MinimumSeparation = 2f;
        private const float Spacing = 2.05f;
        private readonly List<Vector3> _points = new();
        private readonly List<Vector3> _occupied = new();
        private readonly Collider[] _overlaps = new Collider[64];
        private NavMeshSurface _surface;
        public IReadOnlyList<Vector3> Points => _points;

        public static BattleSpawnPoints ForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            Transform environment = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Remodel Environment") { environment = root.transform; break; }
            if (environment == null) return null;
            // Each map owns its data; disabled maps cannot contribute navigation or spawns.
            foreach (Transform child in environment)
                if (child.gameObject.activeInHierarchy) { environment = child; break; }
            var pool = environment.GetComponent<BattleSpawnPoints>();
            if (pool != null) return pool;
            pool = environment.gameObject.AddComponent<BattleSpawnPoints>();
            pool.Build();
            return pool;
        }

        private void Build()
        {
            Physics.SyncTransforms();
            _surface = gameObject.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.Children;
            _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            _surface.overrideVoxelSize = true;
            _surface.voxelSize = .15f;
            _surface.BuildNavMesh();

            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices.Length == 0) return;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (!hasBounds) return;
            var levels = new SortedSet<float>();
            foreach (Vector3 vertex in triangulation.vertices)
                if (bounds.Contains(vertex)) levels.Add(Mathf.Round(vertex.y * 2f) / 2f);
            var anchors = new List<Vector3>();
            foreach (var start in NetworkManager.startPositions)
                if (start != null && start.gameObject.scene == gameObject.scene &&
                    NavMesh.SamplePosition(start.position, out var hit, 2f, NavMesh.AllAreas)) anchors.Add(hit.position);
            var path = new NavMeshPath();
            // Hexagonal rows cover the entire floor, rather than a fixed number of hand-placed starts.
            int row = 0;
            foreach (float level in levels)
            for (float z = bounds.min.z; z <= bounds.max.z; z += Spacing * .8660254f, row++)
            for (float x = bounds.min.x + (row % 2) * Spacing * .5f; x <= bounds.max.x; x += Spacing)
            {
                Vector3 probe = new Vector3(x, level, z);
                if (!NavMesh.SamplePosition(probe, out var hit, .3f, NavMesh.AllAreas) || !bounds.Contains(hit.position)) continue;
                Vector3 point = hit.position + Vector3.up * .06f;
                if (!IsSeparated(point, _points) || !HasClearance(point, .55f, 2.7f)) continue;
                bool reachable = anchors.Count == 0;
                foreach (Vector3 anchor in anchors)
                    if (NavMesh.CalculatePath(anchor, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    { reachable = true; break; }
                if (reachable) _points.Add(point);
            }
            Debug.Log($"[SpawnPoints] {gameObject.name}: {_points.Count} safe points, spacing >= {MinimumSeparation}.");
        }

        public static bool IsSeparated(Vector3 point, IReadOnlyList<Vector3> others)
        {
            for (int i = 0; i < others.Count; i++)
                if ((point - others[i]).sqrMagnitude < MinimumSeparation * MinimumSeparation) return false;
            return true;
        }

        private bool HasClearance(Vector3 point, float radius, float height)
        {
            int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * (radius + .1f),
                point + Vector3.up * Mathf.Max(radius + .1f, height - radius), radius,
                _overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (_overlaps[i] != null && _overlaps[i].GetComponentInParent<PlayerManager>() == null) return false;
            return true;
        }

        public bool TryTake(PlayerManager player, IReadOnlyList<Vector3> reservations, out Pose pose,
            ICollection<PlayerManager> relocating = null)
        {
            pose = default;
            _occupied.Clear();
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && identity.gameObject.scene == gameObject.scene &&
                    identity.TryGetComponent(out PlayerManager other) && other != player &&
                    (relocating == null || !relocating.Contains(other))) _occupied.Add(other.transform.position);
            if (reservations != null) for (int i = 0; i < reservations.Count; i++) _occupied.Add(reservations[i]);
            float radius = .55f, height = 2.7f;
            if (player != null && player.TryGetComponent(out CharacterController controller))
            {
                radius = Mathf.Max(radius, controller.radius * Mathf.Max(player.transform.lossyScale.x, player.transform.lossyScale.z));
                height = Mathf.Max(height, controller.height * player.transform.lossyScale.y);
            }
            float best = -1f;
            int offset = Random.Range(0, Mathf.Max(1, _points.Count));
            for (int i = 0; i < _points.Count; i++)
            {
                Vector3 point = _points[(i + offset) % _points.Count];
                if (!IsSeparated(point, _occupied)) continue;
                float nearest = float.MaxValue;
                foreach (Vector3 occupied in _occupied) nearest = Mathf.Min(nearest, (point - occupied).sqrMagnitude);
                if (nearest <= best || !HasClearance(point, radius, height)) continue;
                Vector3 facing = transform.position - point; facing.y = 0f;
                pose = new Pose(point, facing.sqrMagnitude > .01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
                best = nearest;
            }
            return best >= 0f;
        }

        private void OnDestroy()
        {
            if (_surface == null) return;
            var data = _surface.navMeshData;
            _surface.RemoveData();
            if (data != null) Destroy(data);
        }
    }
}
