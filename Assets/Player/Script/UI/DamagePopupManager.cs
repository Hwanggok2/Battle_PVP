using UnityEngine;

namespace BattlePvp.UI
{
    public class DamagePopupManager : MonoBehaviour
    {
        public static DamagePopupManager Instance { get; private set; }

        [SerializeField] private DamagePopup _popupPrefab;
        [SerializeField] private bool _logPopupSpawns;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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

        private static bool TryShowReceivedDamageOnSceneHud(float damage, Color color)
        {
            PlayerHudView[] views = Resources.FindObjectsOfTypeAll<PlayerHudView>();
            foreach (PlayerHudView view in views)
            {
                if (view == null || !view.gameObject.scene.isLoaded || !view.IsViewVisible)
                    continue;

                if (view.ShowReceivedDamage(damage, color))
                    return true;
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
            if (_popupPrefab == null)
            {
                Debug.LogError("[DamagePopupManager] Popup Prefab is not assigned.");
                return;
            }

            Vector3 spawnPos = applyWorldOffset ? position + new Vector3(0, 0.5f, 0) : position;
            DamagePopup popup = Instantiate(_popupPrefab, spawnPos, Quaternion.identity);
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
