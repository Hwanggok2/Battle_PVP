using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Combat
{
    /// <summary>Scene-owned presentation objects only. NetworkIdentity objects never enter this pool.</summary>
    public sealed class CombatVisualPool : MonoBehaviour
    {
        private const int CapacityPerPrefab = 32;
        private static readonly Dictionary<int, CombatVisualPool> Pools = new();
        private readonly Dictionary<int, Stack<GameObject>> _free = new();
        private readonly Dictionary<GameObject, int> _leased = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Pools.Clear();

        public static GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Scene scene = default)
        {
            if (!Application.isPlaying)
                return prefab != null ? Instantiate(prefab, position, rotation) : new GameObject("Arrow flight trail");
            if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
            if (!Pools.TryGetValue(scene.handle, out var pool) || pool == null)
            {
                pool = new GameObject("Combat visual pool").AddComponent<CombatVisualPool>();
                SceneManager.MoveGameObjectToScene(pool.gameObject, scene);
                Pools[scene.handle] = pool;
            }
            int key = prefab != null ? prefab.GetInstanceID() : 0;
            if (!pool._free.TryGetValue(key, out var free)) pool._free[key] = free = new Stack<GameObject>();
            GameObject value = null;
            while (free.Count > 0 && value == null) value = free.Pop();
            if (value == null)
            {
                value = prefab != null ? Instantiate(prefab, pool.transform) : new GameObject("Arrow flight trail");
                value.transform.SetParent(pool.transform, false);
            }
            value.SetActive(false);
            value.transform.SetPositionAndRotation(position, rotation);
            pool._leased[value] = key;
            return value;
        }

        public static void Return(GameObject value)
        {
            if (value == null) return;
            var pool = value.GetComponentInParent<CombatVisualPool>();
            if (pool != null && pool._leased.Remove(value, out int key))
            {
                value.SetActive(false);
                var free = pool._free[key];
                if (free.Count < CapacityPerPrefab) { free.Push(value); return; }
            }
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            if (Pools.TryGetValue(gameObject.scene.handle, out var pool) && pool == this) Pools.Remove(gameObject.scene.handle);
            _leased.Clear(); _free.Clear();
        }
    }
}
