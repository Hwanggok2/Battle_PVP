using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using BattlePvp.Combat;

namespace BattlePvp.Networking
{
    /// <summary>
    /// Mirror의 NetworkManager를 상속받아 배틀 특화 기능을 관리하는 클래스입니다.
    /// 플레이어 생성 로직 및 씬 전환 이벤트를 디버깅하고 제어합니다.
    /// </summary>
    public partial class BattleNetworkManager : NetworkManager
    {
        public const int PlayerCapacity = 8;
        public int RoomCapacity { get; private set; } = PlayerCapacity;
        private readonly HashSet<string> _kickedAccounts = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        public bool IsKicked(string account) => _kickedAccounts.Contains(account);
        public void ApplyRoomCapacity(int capacity)
        {
            if (NetworkServer.active && RoomAdmission.ValidCapacity(capacity, numPlayers)) RoomCapacity = capacity;
        }
        public void BanRoomAccount(string account)
        {
            if (NetworkServer.active) _kickedAccounts.Add(account);
        }
        public byte SelectedBattleMap { get; set; }
        public int SelectedMatchDuration { get; set; } = 180;
        public BattlePvp.Combat.MatchResultSnapshot LastCompletedMatch { get; private set; }
        private readonly Dictionary<string, NetworkIdentity> _disconnectedPlayers =
            new Dictionary<string, NetworkIdentity>(System.StringComparer.OrdinalIgnoreCase);
        private bool _serverStopping;
        private bool _changingServerScene;

        public void RememberCompletedMatch(BattlePvp.Combat.MatchResultSnapshot result)
        {
            if (NetworkServer.active && result != null) LastCompletedMatch = result;
        }
        public override void Awake()
        {
            maxConnections = PlayerCapacity;
            RoomNetworkAuthenticator roomAuthenticator = GetComponent<RoomNetworkAuthenticator>();
            if (roomAuthenticator == null) roomAuthenticator = gameObject.AddComponent<RoomNetworkAuthenticator>();
            roomAuthenticator.enabled = true;
            authenticator = roomAuthenticator;
            base.Awake();
            if (singleton == this && GetComponent<SceneInterestManagement>() == null) gameObject.AddComponent<SceneInterestManagement>();
            if (singleton == this && GetComponent<RoomNetworkTiming>() == null)
                gameObject.AddComponent<RoomNetworkTiming>();
            Debug.Log($"[BattleNetworkManager] Awake - Singleton check: {singleton == this}");
        }

        public override void OnStartServer()
        {
            RoomCapacity = PlayerCapacity;
            _kickedAccounts.Clear();
            _serverStopping = false;
            _changingServerScene = false;
            base.OnStartServer();
            ResetMatchReturn();
            RegisterReturnServerHandlers();
            BattlePvp.UI.BattleChatNetwork.RegisterServerHandler();
            Debug.Log("[BattleNetworkManager] Server Started.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RegisterReturnClientHandlers();
            BattlePvp.Combat.HealthSystem.ClearPopupPredictions();
            BattlePvp.UI.BattleChatNetwork.RegisterClientHandler();
            Debug.Log("[BattleNetworkManager] Client Started.");
        }

        /// <summary>
        /// 서버에 플레이어가 추가될 때 호출됩니다.
        /// </summary>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (IsPractice) { AddPracticePlayer(conn); return; }
            // A frozen result has no late-join initialization. Rejoin after the host starts the next round.
            if (BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState == BattleState.MatchEnded)
            {
                Debug.LogWarning("[BattleNetworkManager] The match has ended; join after the next round starts.");
                conn.Disconnect();
                return;
            }
            if (numPlayers >= RoomCapacity || !conn.isAuthenticated || !(conn.authenticationData is AuthenticatedRoomPlayer identity) || IsKicked(identity.PlayFabId) ||
                PlayFabBattleManager.Instance == null || identity.RoomId != PlayFabBattleManager.Instance.CurrentRoomId)
            {
                conn.Disconnect();
                return;
            }
            Debug.Log($"[BattleNetworkManager] OnServerAddPlayer called for connection: {conn.connectionId}");

            if (_disconnectedPlayers.TryGetValue(identity.PlayFabId, out NetworkIdentity retained))
            {
                if (retained == null || retained.connectionToClient != null)
                {
                    conn.Disconnect();
                    return;
                }
                retained.GetComponent<PlayerManager>()?.ServerPrepareReconnect();
                if (!NetworkServer.AddPlayerForConnection(conn, retained.gameObject))
                {
                    retained.GetComponent<PlayerManager>()?.ServerBeginDisconnectedControl();
                    conn.Disconnect();
                    return;
                }
                _disconnectedPlayers.Remove(identity.PlayFabId);
                ScoreSystem score = retained.GetComponent<ScoreSystem>();
                if (score == null || BattleStateMachine.Instance == null ||
                    !BattleStateMachine.Instance.TryRegisterMatchParticipant(score))
                {
                    conn.Disconnect();
                    return;
                }
                score.ServerSetConnected(true);
                retained.GetComponent<PlayerManager>()?.ServerSendReconnectState();
                return;
            }

            // Disconnected bodies reserve their match slot, so cycling accounts cannot create unbounded targets.
            if (numPlayers + _disconnectedPlayers.Count >= RoomCapacity)
            {
                conn.Disconnect();
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("[BattleNetworkManager] Player Prefab is NOT assigned in the Inspector! Spawning failed.");
                return;
            }

            var points = BattleSpawnPoints.ForScene(SceneManager.GetActiveScene());
            if (points == null || !points.TryTake(null, null, out var spawn))
            { Debug.LogError("[BattleNetworkManager] No unoccupied spawn is available."); conn.Disconnect(); return; }
            GameObject player = Instantiate(playerPrefab, spawn.position, spawn.rotation);
            
            player.name = $"Player [ConnId={conn.connectionId}]";

            // 3. 네트워크 상에 플레이어 오브젝트 등록
            NetworkServer.AddPlayerForConnection(conn, player);

            Debug.Log($"[BattleNetworkManager] Player spawned at {spawn.position} for Connection ID: {conn.connectionId}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            BattlePvp.UI.BattleChatNetwork.OnServerDisconnected(conn);
            (authenticator as RoomNetworkAuthenticator)?.OnServerDisconnected(conn);
            Debug.Log($"[BattleNetworkManager] Player disconnected: {conn.connectionId}");
            NetworkIdentity player = conn.identity;
            var account = conn.authenticationData as AuthenticatedRoomPlayer;
            if (NetworkServer.active && !_serverStopping && !_changingServerScene &&
                conn != NetworkServer.localConnection && player != null && account != null &&
                player.TryGetComponent(out BattlePvp.Stats.StatManager stats) && stats.HasServerStats &&
                BattleStateMachine.Instance != null && BattleStateMachine.Instance.CurrentState == BattleState.InBattle &&
                PlayFabBattleManager.Instance != null && account.RoomId == PlayFabBattleManager.Instance.CurrentRoomId)
            {
                player.GetComponent<ScoreSystem>()?.ServerSetConnected(false);
                player.GetComponent<BowAttackController>()?.CancelCharge();
                player.GetComponent<PlayerManager>()?.ServerBeginDisconnectedControl();
                NetworkServer.RemovePlayerForConnection(conn, RemovePlayerOptions.KeepActive);
                _disconnectedPlayers[account.PlayFabId] = player;
            }
            base.OnServerDisconnect(conn);
            _pendingReturns.Remove(conn.connectionId); _movedReturns.Remove(conn.connectionId); _presentedReturns.Remove(conn.connectionId);
            TryFinishWaitingReturn();
        }

        public void ClearDisconnectedPlayers()
        {
            foreach (NetworkIdentity player in _disconnectedPlayers.Values)
                if (player != null) NetworkServer.Destroy(player.gameObject);
            _disconnectedPlayers.Clear();
        }

        public override void ServerChangeScene(string newSceneName)
        {
            if (newSceneName == "Battle" && !CanStartNextRound) return;
            string current = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (newSceneName == "Battle" && (current == "Battle_waiting" || current == "Battle_wait") && !RoomStatReadiness.AllPlayersReady)
            {
                Debug.LogWarning("[BattleNetworkManager] All players must finish stat allocation before starting.");
                return;
            }
            base.ServerChangeScene(newSceneName);
        }

        public override void OnServerChangeScene(string newSceneName)
        {
            _changingServerScene = true;
            ClearDisconnectedPlayers();
            base.OnServerChangeScene(newSceneName);
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            _changingServerScene = false;
            ResetMatchReturn();
            base.OnServerSceneChanged(sceneName);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            RoomConnectionDiagnostics.Record("mirror_connected");
            if (!IsPractice) PlayFabBattleManager.Instance?.NotifyRoomNetworkConnected();
            Debug.Log("[BattleNetworkManager] Client connected to server.");
        }

        public override void OnClientSceneChanged()
        {
            ResetMatchReturn();
            base.OnClientSceneChanged();
        }

        public override void OnClientDisconnect()
        {
            if (IsPractice) { base.OnClientDisconnect(); return; }
            bool preserveMembership = authenticator is RoomNetworkAuthenticator roomAuthenticator &&
                roomAuthenticator.PreserveRoomMembershipOnDisconnect;
            bool authenticationFailed = NetworkClient.connection == null || !NetworkClient.connection.isAuthenticated;
            RoomConnectionDiagnostics.SaveExit(authenticationFailed ? "mirror_disconnected_before_authentication" : "mirror_disconnected");
            PlayFabBattleManager.Instance?.NotifyRoomNetworkDisconnected(preserveMembership, authenticationFailed);
            base.OnClientDisconnect();
            Debug.Log("[BattleNetworkManager] Client disconnected from server.");
        }

        public override void OnClientError(TransportError error, string reason)
        {
            RoomConnectionDiagnostics.Record("client_transport_error_" + error);
            if (!NetworkClient.isConnected)
                PlayFabBattleManager.Instance?.NotifyRoomConnectionFailed(error == TransportError.Timeout);
            Debug.LogWarning($"[BattleNetworkManager] Client transport error: {error}: {reason}");
        }

        public override void OnServerError(NetworkConnectionToClient conn, TransportError error, string reason)
        {
            RoomConnectionDiagnostics.Record("server_transport_error_" + error);
            Debug.LogWarning($"[BattleNetworkManager] Server transport error: {error}: {reason}");
        }

        public override void OnStopServer()
        {
            _serverStopping = true;
            ClearDisconnectedPlayers();
            LastCompletedMatch = null;
            BattlePvp.UI.BattleChatNetwork.UnregisterServerHandler();
            ResetMatchReturn();
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            BattlePvp.Combat.HealthSystem.ClearPopupPredictions();
            BattlePvp.UI.BattleChatNetwork.UnregisterClientHandler();
            _returningFromMatch = false;
            base.OnStopClient();
        }

        public override void OnValidate()
        {
            maxConnections = PlayerCapacity;
            base.OnValidate();
            
            // 인스펙터에서 Online Scene이 비어있으면 경고
            if (string.IsNullOrEmpty(onlineScene))
            {
                Debug.LogWarning("[BattleNetworkManager] Online Scene is empty! Please assign the Battle scene.");
            }
        }
    }
}
