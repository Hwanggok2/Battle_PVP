using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Managers
{
    /// <summary>프로필의 적용 요청과 씬 수명을 실제 로컬 플레이어/HUD에 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class LocalPlayerProfileBinding : MonoBehaviour
    {
        private GlobalDataManager _data;
        private StatManager _boundPlayer;

        private void OnEnable()
        {
            _data = GetComponent<GlobalDataManager>();
            if (_data == null) return;
            _data.PlayerBindingRequested += ApplyToLocalPlayer;
            StatManager.LocalChanged += OnLocalPlayerChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnLocalPlayerChanged(StatManager.Local);
        }

        private void OnDisable()
        {
            if (_data != null) _data.PlayerBindingRequested -= ApplyToLocalPlayer;
            StatManager.LocalChanged -= OnLocalPlayerChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            PlayerHUD.UnbindFromPlayer(_boundPlayer);
            _boundPlayer = null;
            _data = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _data.EnsurePlayerStatsLoadedForCurrentScene();
            _data.TryInjectToPlayer();
        }

        private void ApplyToLocalPlayer()
        {
            if (_data == null || !_data.HasLoadedPlayerStats) return;
            StatManager stats = StatManager.Local;
            if (NetworkClient.active)
                stats = NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<StatManager>() : null;
            if (stats == null) return;
            if (!NetworkClient.active || !stats.HasServerStats)
                stats.ApplyStats(_data.SavedStats, recalculateIdentity: true);
            OnLocalPlayerChanged(stats);
        }

        private void OnLocalPlayerChanged(StatManager stats)
        {
            // StatManager owns initial submission; replacement only rebinds presentation here.
            if (!ReferenceEquals(_boundPlayer, stats)) PlayerHUD.UnbindFromPlayer(_boundPlayer);
            _boundPlayer = stats;
            if (stats != null) PlayerHUD.BindToPlayer(stats);
        }
    }
}
