using Mirror;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>Host-selected map is replicated before players enter the shared Battle scene.</summary>
    public sealed class BattleMapSelection : NetworkBehaviour
    {
        public static BattleMapSelection Instance { get; private set; }
        [SerializeField] private GameObject _research;
        [SerializeField] private GameObject _rooftop;
        [SerializeField] private GameObject _spire;
        public const int MapCount = 3;
        public static string MapName(byte map) => map == 2 ? "헬릭스 타워" : map == 1 ? "네온 옥상" : "연구 구역";
        [SyncVar(hook = nameof(OnMapChanged))] private byte _selected;
        public byte Selected => _selected;
        [SyncVar] private int _matchSeconds = 180;
        public int MatchSeconds => _matchSeconds;
        public static event System.Action RoomSettingsChanged;
        [SyncVar(hook = nameof(OnRoomTitleChanged))] private string _roomTitle;
        [SyncVar] private int _roomCapacity = 8;
        [SyncVar] private bool _privateRoom;
        public string RoomTitle => _roomTitle;
        public int RoomCapacity => _roomCapacity;
        public bool PrivateRoom => _privateRoom;
        [Server]
        public void PublishRoomSettings(PlayFabBattleManager.RoomInfo info)
        { _roomCapacity = info.Capacity > 0 ? info.Capacity : 8; _privateRoom = info.IsPrivate; _roomTitle = info.RoomName; RoomSettingsChanged?.Invoke(); }
        private void OnRoomTitleChanged(string previous, string next) => RoomSettingsChanged?.Invoke();

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }
        public override void OnStartServer()
        {
            if (NetworkManager.singleton is BattleNetworkManager manager)
            { _selected = manager.SelectedBattleMap; _matchSeconds = manager.SelectedMatchDuration; }
            if (PlayFabBattleManager.Instance != null) PublishRoomSettings(PlayFabBattleManager.Instance.CurrentRoomInfo);
            Apply();
        }
        public override void OnStartClient() { Apply(); RoomSettingsChanged?.Invoke(); }
        [Server]
        public void Select(byte map)
        {
            if (map >= MapCount || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Battle_waiting") return;
            if (BattlePvp.UI.BattleStartController.IsStarting) return;
            if (BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState != BattleState.Waiting) return;
            _selected = map;
            if (NetworkManager.singleton is BattleNetworkManager manager) manager.SelectedBattleMap = map;
            Apply();
        }
        private void OnMapChanged(byte previous, byte next) => Apply();
        [Server]
        public void SetMatchDuration(int seconds)
        {
            if (seconds != 180 && seconds != 300 && seconds != 600) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Battle_waiting" ||
                BattlePvp.UI.BattleStartController.IsStarting) return;
            if (BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState != BattleState.Waiting) return;
            _matchSeconds = seconds;
            if (NetworkManager.singleton is BattleNetworkManager manager) manager.SelectedMatchDuration = seconds;
        }
        private void Apply()
        {
            if (_research != null) _research.SetActive(_selected == 0);
            if (_rooftop != null) _rooftop.SetActive(_selected == 1);
            if (_spire != null) _spire.SetActive(_selected == 2);
        }
    }
}
