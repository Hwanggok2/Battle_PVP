using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

namespace BattlePvp.UI
{
    public class DamagePopupManager : MonoBehaviour
    {
        public static DamagePopupManager Instance { get; private set; }

        [SerializeField] private DamagePopup _popupPrefab;
        [SerializeField] private bool _logPopupSpawns;
        [SerializeField, Min(0)] private int _prewarmCount = 32;
        [SerializeField, Min(1)] private int _maxRetainedPopups = 256;
        private ObjectPool<DamagePopup> _pool;
        private Transform _poolRoot;
        private readonly HashSet<DamagePopup> _active = new HashSet<DamagePopup>();
        private PlayerHudView _fallbackView;
        private float _nextHudSearchTime;
        private bool _isReturningPopups;
        private bool _isDisposing;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }
            if (_popupPrefab == null || _pool != null || _isDisposing) return;
            EnsurePoolRoot();
            _pool = new ObjectPool<DamagePopup>(CreatePooledPopup, null,
                DeactivatePopup, DestroyPooledPopup,
                true, Mathf.Max(1, _prewarmCount), Mathf.Max(1, _maxRetainedPopups));
            var warm = new DamagePopup[Mathf.Clamp(_prewarmCount, 0, _maxRetainedPopups)];
            for (int i = 0; i < warm.Length; i++) warm[i] = _pool.Get();
            foreach (DamagePopup popup in warm)
                if (popup != null) _pool.Release(popup);
        }

        private void EnsurePoolRoot()
        {
            if (_poolRoot != null) return;
            // The scene manager may scale with the selected identity. Popups stay in world space.
            _poolRoot = new GameObject("DamagePopupPool").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_poolRoot.gameObject, gameObject.scene);
        }

        private DamagePopup CreatePooledPopup()
        {
            if (_isDisposing || _popupPrefab == null) return null;
            EnsurePoolRoot();
            DamagePopup popup = Instantiate(_popupPrefab, _poolRoot);
            if (popup == null) return null;
            popup.SetPoolRelease(ReleasePopup, ForgetDestroyedPopup);
            popup.gameObject.SetActive(false);
            return popup;
        }

        private DamagePopup GetLivePopup()
        {
            // ObjectPool cannot remove a destroyed retained object by reference. Consume those
            // slots, with at most one fresh creation attempt even if prefab callbacks destroy it.
            int attempts = _pool.CountInactive + 1;
            while (attempts-- > 0 && !_isDisposing && _pool != null)
            {
                DamagePopup popup = _pool.Get();
                if (popup != null) return popup;
            }
            return null;
        }

        private static void DeactivatePopup(DamagePopup popup)
        {
            if (popup != null) popup.gameObject.SetActive(false);
        }

        private static void DestroyPooledPopup(DamagePopup popup)
        {
            if (popup == null) return;
            popup.SetPoolRelease(null);
            DestroyOwnedObject(popup.gameObject);
        }

        private static void DestroyOwnedObject(GameObject owner)
        {
            if (owner == null) return;
            if (Application.isPlaying) Destroy(owner);
            else DestroyImmediate(owner);
        }

        private void ForgetDestroyedPopup(DamagePopup popup) => _active.Remove(popup);

        private void ReleasePopup(DamagePopup popup)
        {
            if (!_active.Remove(popup)) return;
            if (popup == null) return;
            if (_pool != null && !_isDisposing) _pool.Release(popup);
            else DestroyPooledPopup(popup);
        }

        private void OnDisable() => ReturnActivePopups();

        private void ReturnActivePopups()
        {
            if (_isReturningPopups) return;
            _isReturningPopups = true;
            try
            {
                while (_active.Count > 0)
                {
                    DamagePopup popup;
                    using (var iterator = _active.GetEnumerator())
                    {
                        iterator.MoveNext();
                        popup = iterator.Current;
                    }
                    // Removal belongs to the manager, so an already returned/destroyed popup
                    // cannot leave this loop stuck waiting for a callback that will never run.
                    ReleasePopup(popup);
                }
            }
            finally { _isReturningPopups = false; }
        }

        private void OnDestroy()
        {
            _isDisposing = true;
            if (Instance == this) Instance = null;
            ReturnActivePopups();
            _pool?.Clear();
            _pool = null;
            if (_poolRoot != null) DestroyOwnedObject(_poolRoot.gameObject);
            _poolRoot = null;
        }

        public void CreatePopup(Vector3 position, float damage, bool isCritical = false)
        {
            CreatePopup(position, damage, isCritical, false, Color.white);
        }

        public void CreatePopup(Vector3 position, float damage, bool isCritical, Color color)
        {
            CreatePopup(position, damage, isCritical, true, color);
        }

        public void CreatePopup(Vector3 position, float damage, bool isCritical, Color color, float fontSize)
        {
            CreatePopup(position, damage, isCritical, true, color, fontSize, true);
        }

        public void CreatePopupWithFontDelta(Vector3 position, float damage, bool isCritical, Color color, float fontSizeDelta)
        {
            CreatePopup(position, damage, isCritical, true, color, -1f, true, fontSizeDelta);
        }

        public void CreateReceivedDamagePopup(float damage, Color color, float fontSize, Vector3 fallbackPosition)
        {
            if (PlayerHUD.ShowLocalReceivedDamage(damage, color))
                return;

            if (TryShowReceivedDamageOnSceneHud(damage, color))
                return;

            CreatePopup(fallbackPosition, -Mathf.Abs(damage), false, color, fontSize);
        }

        private bool TryShowReceivedDamageOnSceneHud(float damage, Color color)
        {
            if (_fallbackView != null && _fallbackView.IsViewVisible)
                return _fallbackView.ShowReceivedDamage(damage, color);
            if (Time.unscaledTime < _nextHudSearchTime) return false;
            _nextHudSearchTime = Time.unscaledTime + 1f;
            PlayerHudView[] views = Resources.FindObjectsOfTypeAll<PlayerHudView>();
            foreach (PlayerHudView view in views)
            {
                if (view == null || !view.gameObject.scene.isLoaded || !view.IsViewVisible)
                    continue;

                if (view.ShowReceivedDamage(damage, color))
                {
                    _fallbackView = view;
                    return true;
                }
            }

            return false;
        }

        private void CreatePopup(Vector3 position, float damage, bool isCritical, bool useColorOverride, Color colorOverride)
        {
            CreatePopup(position, damage, isCritical, useColorOverride, colorOverride, -1f, true, 0f);
        }

        private void CreatePopup(Vector3 position, float damage, bool isCritical, bool useColorOverride, Color colorOverride, float fontSizeOverride, bool applyWorldOffset)
        {
            CreatePopup(position, damage, isCritical, useColorOverride, colorOverride, fontSizeOverride, applyWorldOffset, 0f);
        }

        private void CreatePopup(Vector3 position, float damage, bool isCritical, bool useColorOverride, Color colorOverride, float fontSizeOverride, bool applyWorldOffset, float fontSizeDelta)
        {
            if (_isDisposing || _isReturningPopups || !isActiveAndEnabled) return;
            if (_popupPrefab == null || _pool == null)
            {
                Debug.LogError("[DamagePopupManager] Popup Prefab is not assigned.");
                return;
            }

            Vector3 spawnPos = applyWorldOffset ? position + new Vector3(0, 0.5f, 0) : position;
            DamagePopup popup = GetLivePopup();
            if (popup == null) return;
            if (_isDisposing || !isActiveAndEnabled)
            {
                DestroyPooledPopup(popup);
                return;
            }
            _active.Add(popup);
            popup.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
            popup.gameObject.SetActive(true);
            if (popup == null || _isDisposing || !isActiveAndEnabled || !_active.Contains(popup) ||
                !popup.gameObject.activeInHierarchy)
            {
                ReleasePopup(popup);
                return;
            }
            if (useColorOverride)
            {
                if (fontSizeOverride > 0f)
                    popup.Setup(damage, isCritical, colorOverride, fontSizeOverride);
                else if (!Mathf.Approximately(fontSizeDelta, 0f))
                    popup.SetupWithFontDelta(damage, isCritical, colorOverride, fontSizeDelta);
                else
                    popup.Setup(damage, isCritical, colorOverride);
            }
            else
            {
                popup.Setup(damage, isCritical);
            }

            if (_logPopupSpawns)
                Debug.Log($"[DamagePopupManager] Popup spawned at {spawnPos} with damage {damage}");
        }
    }
}
