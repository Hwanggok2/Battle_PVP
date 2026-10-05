using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class HudVisibilitySettings : MonoBehaviour
    {
        [SerializeField] private bool _showInWaitingRoom;
        public bool ShowInWaitingRoom
        {
            get => _showInWaitingRoom;
            set { _showInWaitingRoom = value; Apply(); }
        }
        private void OnEnable() { LocalGameSettings.Changed += Apply; SceneManager.sceneLoaded += SceneLoaded; Apply(); }
        private void OnDisable() { LocalGameSettings.Changed -= Apply; SceneManager.sceneLoaded -= SceneLoaded; }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) => Apply();
        private void Apply()
        {
            var group = GetComponent<CanvasGroup>();
            string scene = SceneManager.GetActiveScene().name;
            group.alpha = (scene == "Battle" || (_showInWaitingRoom && scene == "Battle_waiting"))
                ? LocalGameSettings.Current.hudOpacity : 0;
            group.blocksRaycasts = false; group.interactable = false;
            transform.localScale = Vector3.one * LocalGameSettings.Current.hudScale;
        }
    }
}
