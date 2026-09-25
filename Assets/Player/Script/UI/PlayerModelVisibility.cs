using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>사망 연출이 변경한 표시/충돌 상태만 원래 값으로 복구한다.</summary>
    public sealed class PlayerModelVisibility
    {
        private readonly Transform _root;
        private readonly Dictionary<Renderer, bool> _renderers = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Collider, bool> _colliders = new Dictionary<Collider, bool>();

        public PlayerModelVisibility(Transform root) => _root = root;

        public void Hide()
        {
            if (_root == null) return;
            foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))
            {
                if (!_renderers.ContainsKey(renderer)) _renderers.Add(renderer, renderer.enabled);
                renderer.enabled = false;
            }
            foreach (Collider collider in _root.GetComponentsInChildren<Collider>(true))
            {
                // CharacterController의 생명/이동 게이트는 PlayerManager가 소유한다.
                if (collider is CharacterController) continue;
                if (!_colliders.ContainsKey(collider)) _colliders.Add(collider, collider.enabled);
                collider.enabled = false;
            }
        }

        public void Restore()
        {
            foreach (var entry in _renderers)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            foreach (var entry in _colliders)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            _renderers.Clear();
            _colliders.Clear();
        }
    }
}
