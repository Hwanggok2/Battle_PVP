using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    /// <summary>Menu buttons live on several canvases; lay out the local player's visible entries together.</summary>
    public sealed class TopMenuLayout : MonoBehaviour
    {
        private sealed class Entry
        {
            public Button Button;
            public int Column;
            public NetworkIdentity Owner;
        }
        private static TopMenuLayout _instance;
        private readonly List<Entry> _entries = new();

        public static void Register(Button button, int column)
        {
            if (!Application.isPlaying) return;
            if (_instance == null) _instance = new GameObject("Top menu layout").AddComponent<TopMenuLayout>();
            foreach (var entry in _instance._entries)
                if (entry.Button == button) return;
            _instance._entries.Add(new Entry { Button = button, Column = column, Owner = button.GetComponentInParent<NetworkIdentity>() });
            _instance._entries.Sort((a, b) => b.Column.CompareTo(a.Column));
        }

        public static bool IsVisible(int column, bool battle, bool dead, bool polymath, bool modalOpen = false)
        {
            if (modalOpen) return false;
            if (!battle) return true;
            switch (column)
            {
                case -1: return dead || polymath;
                case 0: return false;
                case 1:
                case 3: return dead;
                default: return true;
            }
        }

        private void LateUpdate()
        {
            var local = StatManager.Local;
            bool battle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Battle";
            bool dead = local != null && local.GetComponent<HealthSystem>() is { IsDead: true };
            bool polymath = local != null && local.CurrentIdentity.Type == IdentityType.Polymath;
            int visible = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Button == null) { _entries.RemoveAt(i--); continue; }
                bool owns = entry.Owner == null || (!NetworkClient.active && !NetworkServer.active) || entry.Owner.isLocalPlayer;
                bool show = owns && IsVisible(entry.Column, battle, dead, polymath,
                    JobGuidePanel.IsOpen || GameSettingsPanel.IsOpen || BattleExitPanel.IsOpen);
                if (entry.Button.gameObject.activeSelf != show) entry.Button.gameObject.SetActive(show);
                if (!show || !entry.Button.gameObject.activeInHierarchy) continue;
                var canvas = entry.Button.GetComponentInParent<Canvas>();
                if (canvas != null && !canvas.isActiveAndEnabled) continue;
                var rect = (RectTransform)entry.Button.transform;
                var position = new Vector2(-82f - visible++ * 128f, -42f);
                if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            }
            if (!battle) return;
            CharacterSelectionPanel.CloseIfOpen();
            if (!dead) JobGuidePanel.CloseIfOpen();
            if (!dead && !polymath) WeaponSelectionPanel.CloseIfOpen();
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
