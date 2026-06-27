using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Networking
{
    /// <summary>
    /// Mirror의 NetworkManager를 상속받아 배틀 특화 기능을 관리하는 클래스입니다.
    /// 플레이어 생성 로직 및 씬 전환 이벤트를 디버깅하고 제어합니다.
    /// </summary>
    public class BattleNetworkManager : NetworkManager
    {
        [Header("Battle Settings")]
        [Tooltip("배틀이 시작될 때 활성화할 씬 이름입니다. (Online Scene과 동일하게 설정 권장)")]
        [SerializeField] private string _battleSceneName = "Battle_waiting";

        public override void Awake()
        {
            base.Awake();
            Debug.Log($"[BattleNetworkManager] Awake - Singleton check: {singleton == this}");
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("[BattleNetworkManager] Server Started.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log("[BattleNetworkManager] Client Started.");
        }

        /// <summary>
        /// 서버에 플레이어가 추가될 때 호출됩니다.
        /// </summary>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Debug.Log($"[BattleNetworkManager] OnServerAddPlayer called for connection: {conn.connectionId}");

            if (playerPrefab == null)
            {
                Debug.LogError("[BattleNetworkManager] Player Prefab is NOT assigned in the Inspector! Spawning failed.");
                return;
            }

            // 1. 시작 지점 선택 (NetworkStartPosition 배치된 곳 중 하나)
            Transform startPos = GetStartPosition();
            
            // 2. 위치/회전값 적용하여 생성
            GameObject player = startPos != null
                ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
                : Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            
            player.name = $"Player [ConnId={conn.connectionId}]";

            // 3. 네트워크 상에 플레이어 오브젝트 등록
            NetworkServer.AddPlayerForConnection(conn, player);

            Debug.Log($"[BattleNetworkManager] Player spawned at {(startPos != null ? startPos.position.ToString() : "Origin")} for Connection ID: {conn.connectionId}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[BattleNetworkManager] Player disconnected: {conn.connectionId}");
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            Debug.Log("[BattleNetworkManager] Client connected to server.");
        }

        public override void OnClientDisconnect()
        {
            PlayFabBattleManager.Instance?.LeaveCurrentRoom();
            base.OnClientDisconnect();
            Debug.Log("[BattleNetworkManager] Client disconnected from server.");
        }

        public override void OnValidate()
        {
            base.OnValidate();
            
            // 인스펙터에서 Online Scene이 비어있으면 경고
            if (string.IsNullOrEmpty(onlineScene))
            {
                Debug.LogWarning("[BattleNetworkManager] Online Scene is empty! Please assign the Battle scene.");
            }
        }
    }
}
