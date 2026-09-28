using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.Stats;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class RoomPlayerCard : MonoBehaviour
    {
        [SerializeField] private TMP_Text _name, _kickLabel;
        [SerializeField] private Button _kick;
        [SerializeField] private Image _accent;
        private ScoreSystem _player;
        private float _confirmUntil;
        private void Awake() => _kick.onClick.AddListener(Kick);
        public void Bind(ScoreSystem player, bool canEdit)
        {
            if (_player != player) _confirmUntil = 0;
            _player = player;
            UserTextPresentation.SetPlain(_name, UserDisplayText.SingleLine(player.PlayerName, UserDisplayText.NameLimit, "플레이어"));
            bool host = NetworkServer.active && player.connectionToClient == NetworkServer.localConnection;
            _kick.interactable = canEdit && !host;
            _kickLabel.text = host ? "방장" : Time.unscaledTime < _confirmUntil ? "한 번 더 눌러 강퇴" : "강퇴하기";
            var stats = player.GetComponent<StatManager>();
            if (stats != null) _accent.color = StatVfxColor.Resolve(stats.CurrentIdentity, stats.GetStatsCopy(), Color.red, Color.green, Color.yellow, Color.blue);
        }
        private async void Kick()
        {
            if (!_kick.interactable || _player == null || PlayFabBattleManager.Instance == null) return;
            if (Time.unscaledTime >= _confirmUntil) { _confirmUntil = Time.unscaledTime + 3; _kickLabel.text = "한 번 더 눌러 강퇴"; return; }
            _confirmUntil = 0; _kick.interactable = false;
            var panel = GetComponentInParent<WaitingRoomDetails>();
            string error = await PlayFabBattleManager.Instance.KickRoomPlayer(_player.netId);
            if (panel != null) panel.ShowStatus(error ?? "참가자를 내보냈습니다.");
        }
    }
}
