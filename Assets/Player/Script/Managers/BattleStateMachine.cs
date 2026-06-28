using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattlePvp.Networking
{
    public enum BattleState { Waiting, PreMatch, Countdown, InBattle, Respawn, MatchEnded }

    /// <summary>
    /// Mirror를 이용해 전장의 상태(State Machine)를 동기화하고 관리하는 클래스입니다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class BattleStateMachine : NetworkBehaviour
    {
        public static BattleStateMachine Instance { get; private set; }

        [SyncVar(hook = nameof(OnStateChanged))]
        public BattleState CurrentState = BattleState.Waiting;

        [SyncVar(hook = nameof(OnRemainingTimeChanged))]
        public float RemainingTime = 0f;

        [SyncVar(hook = nameof(OnIsLoadingChanged))]
        public bool IsLoading = false;

        [Header("Settings")]
        public float PreMatchDuration = 10f;
        public float CountdownDuration = 5f;
        public float MatchDuration = 180f;
        public float RespawnDuration = 5f;
        public float ResultDelaySeconds = 5f;

        [Header("Result UI")]
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private TMP_Text _nicknameText;
        [SerializeField] private TMP_Text _rankText;
        [SerializeField] private TMP_Text _mostKilledByText;
        [SerializeField] private TMP_Text _mostKilledText;
        [SerializeField] private TMP_Text _restartPromptText;
        [SerializeField] private TMP_Text _resultSummaryText;

        private Coroutine _activeMatchRoutine;
        private GameObject _runtimeResultPanel;
        private bool _restartRequested;
        private bool _resultPanelVisible;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogWarning("[BattleStateMachine] 중복 인스턴스 감지됨. 파괴합니다.");
                Destroy(gameObject);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // 클라이언트 접속 시 현재 로딩 상태를 즉시 적용합니다.
            OnIsLoadingChanged(false, IsLoading);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CurrentState = BattleState.Waiting;

            // 정확히 "Battle" 씬일 때만 매치를 시작합니다. (Battle_waiting 등 오발 방지)
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != "Battle")
                IsLoading = false;

            if (sceneName == "Battle")
            {
                Debug.Log("[BattleStateMachine] Battle scene detected. StartMatch 호출.");
                StartMatch();
            }
        }

        [Server]
        public void StartMatch()
        {
            if (CurrentState != BattleState.Waiting) return;
            
            // 이미 매치 흐름이 진행 중이라면 중복 실행 방지
            if (_activeMatchRoutine != null)
            {
                Debug.LogWarning("[BattleStateMachine] 매치 루틴이 이미 실행 중입니다. 중복 실행을 방지합니다.");
                return;
            }

            _activeMatchRoutine = StartCoroutine(MatchFlowRoutine());
        }

        private IEnumerator MatchFlowRoutine()
        {
            Debug.Log("[BattleStateMachine] Match 흐름 시작 (로딩 연출)");
            
            // 1. 모든 클라이언트에게 로딩 화면 켜기 지시 (SyncVar를 통해 늦게 접속한 유저도 처리)
            IsLoading = true;

            // 클라이언트들의 스탯 동기화 대기를 위한 딜레이 (1.5초로 최적화)
            yield return new WaitForSeconds(1.5f);

            // 2. 스폰 포인트 배치
            try
            {
                TeleportPlayersToSpawnPointsOnServer();

            // 3. 모든 플레이어 체력 최대치로 강제 설정 (서버 권한)
            // 약간의 프레임 대기 후 갱신
            var allHealthSystems = FindObjectsByType<HealthSystem>(FindObjectsSortMode.None);
            foreach (var hs in allHealthSystems)
            {
                hs.isInvincible = false;
                hs.RefreshFromStats(keepCurrentHpFlat: false);
                hs.RefillHealth();
            }
            Debug.Log($"[BattleStateMachine] {allHealthSystems.Length}명의 체력을 최대치로 초기화했습니다.");

            // 4. 로딩 화면 끄기
            }
            finally
            {
                IsLoading = false;
            }

            // 3. In-Battle
            var scoreSystems = FindObjectsByType<ScoreSystem>(FindObjectsSortMode.None);
            foreach (var score in scoreSystems)
            {
                if (score != null)
                    score.ResetMatchStats();
            }

            _restartRequested = false;
            _resultPanelVisible = false;
            HideResultPanel();
            CurrentState = BattleState.InBattle;
            RemainingTime = MatchDuration;
            while (RemainingTime > 0)
            {
                yield return new WaitForSeconds(1f);
                RemainingTime--;
            }

            // 4. Match End (추후 구현)
            EndMatchOnServer();
            _activeMatchRoutine = null;
        }

        private void Update()
        {
            if (CurrentState != BattleState.MatchEnded || _restartRequested || !_resultPanelVisible) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                _restartRequested = true;
                CmdRequestRestart();
            }
        }

        [Server]
        private void EndMatchOnServer()
        {
            CurrentState = BattleState.MatchEnded;
            RemainingTime = 0f;

            var allScores = new List<ScoreSystem>(FindObjectsByType<ScoreSystem>(FindObjectsSortMode.None));
            int winnerScore = int.MinValue;
            foreach (var score in allScores)
            {
                if (score == null) continue;
                if (score.CurrentPoints > winnerScore)
                    winnerScore = score.CurrentPoints;
            }

            var winners = new List<ScoreSystem>();
            foreach (var score in allScores)
            {
                if (score != null && score.CurrentPoints == winnerScore)
                    winners.Add(score);
            }

            var allHealthSystems = FindObjectsByType<HealthSystem>(FindObjectsSortMode.None);
            foreach (var hs in allHealthSystems)
            {
                if (hs == null) continue;
                hs.isInvincible = true;
                hs.Revive(1f);
            }

            uint[] winnerNetIds = new uint[winners.Count];
            string winnerName = winners.Count > 0 ? winners[0].PlayerName : "Unknown";
            int displayWinnerScore = winners.Count > 0 ? winnerScore : 0;
            for (int i = 0; i < winners.Count; i++)
            {
                winnerNetIds[i] = winners[i].netId;
            }

            if (winners.Count > 0)
                winners[0].DistributeRewards(allScores);

            RpcHandleMatchEnded(winnerNetIds);
            SendPersonalResults(allScores);
            Debug.Log($"[BattleStateMachine] Match ended. Winners={winners.Count}, Score={displayWinnerScore}");
        }

        [ClientRpc]
        private void RpcHandleMatchEnded(uint[] winnerNetIds)
        {
            var localPlayer = NetworkClient.localPlayer;
            bool isWinner = localPlayer != null && IsWinnerNetId(localPlayer.netId, winnerNetIds);
            Transform spectateTarget = ResolveWinnerSpectateTarget(winnerNetIds);

            if (localPlayer != null)
            {
                var playerManager = localPlayer.GetComponent<PlayerManager>();
                if (playerManager != null)
                    playerManager.EnterMatchEndMode(spectateTarget, isWinner);
            }
        }

        [Server]
        private void SendPersonalResults(List<ScoreSystem> allScores)
        {
            if (allScores == null)
                return;

            for (int i = 0; i < allScores.Count; i++)
            {
                var score = allScores[i];
                if (score == null || score.connectionToClient == null)
                    continue;

                int rank = CalculateRank(score, allScores);
                string playerName = string.IsNullOrEmpty(score.PlayerName) ? "Unknown" : score.PlayerName;
                string mostKilledBy = score.GetMostKilledByEnemyName(allScores);
                string mostKilled = score.GetMostKilledEnemyName(allScores);
                TargetShowPersonalResult(score.connectionToClient, playerName, rank, mostKilledBy, mostKilled);
            }
        }

        private int CalculateRank(ScoreSystem target, IReadOnlyList<ScoreSystem> allScores)
        {
            if (target == null || allScores == null)
                return 0;

            int rank = 1;
            for (int i = 0; i < allScores.Count; i++)
            {
                var score = allScores[i];
                if (score != null && score.CurrentPoints > target.CurrentPoints)
                    rank++;
            }

            return rank;
        }

        [TargetRpc]
        private void TargetShowPersonalResult(NetworkConnectionToClient target, string playerName, int rank, string mostKilledBy, string mostKilled)
        {
            StartCoroutine(CoShowResultPanel(playerName, rank, mostKilledBy, mostKilled));
        }

        private bool IsWinnerNetId(uint netId, uint[] winnerNetIds)
        {
            if (winnerNetIds == null) return false;
            for (int i = 0; i < winnerNetIds.Length; i++)
            {
                if (winnerNetIds[i] == netId)
                    return true;
            }
            return false;
        }

        private Transform ResolveWinnerSpectateTarget(uint[] winnerNetIds)
        {
            if (winnerNetIds == null || winnerNetIds.Length == 0)
                return null;

            int startIndex = Random.Range(0, winnerNetIds.Length);
            for (int i = 0; i < winnerNetIds.Length; i++)
            {
                int index = (startIndex + i) % winnerNetIds.Length;
                if (NetworkClient.spawned.TryGetValue(winnerNetIds[index], out NetworkIdentity identity))
                    return identity.transform;
            }

            return null;
        }

        private IEnumerator CoShowResultPanel(string playerName, int rank, string mostKilledBy, string mostKilled)
        {
            _resultPanelVisible = false;
            HideResultPanel();

            yield return new WaitForSeconds(ResultDelaySeconds);

            ShowResultPanel(playerName, rank, mostKilledBy, mostKilled);
        }

        private void HideResultPanel()
        {
            if (_runtimeResultPanel != null)
            {
                Destroy(_runtimeResultPanel);
                _runtimeResultPanel = null;
            }

            if (_resultPanel != null)
                _resultPanel.SetActive(false);
        }

        private void ShowResultPanel(string playerName, int rank, string mostKilledBy, string mostKilled)
        {
            string resultMessage = BuildResultMessage(playerName, rank, mostKilledBy, mostKilled);
            if (_resultPanel != null)
            {
                ResolveResultTextReferences();
                SetText(_nicknameText, $"Nickname: {playerName}");
                SetText(_rankText, $"Rank: {rank}");
                SetText(_mostKilledByText, $"Most defeated by: {mostKilledBy}");
                SetText(_mostKilledText, $"Most defeated: {mostKilled}");
                SetText(_restartPromptText, "Press Enter to Restart");

                if (_resultSummaryText != null)
                    _resultSummaryText.text = resultMessage;

                _resultPanel.SetActive(true);
                _resultPanelVisible = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("ResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            _runtimeResultPanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _runtimeResultPanel.transform.SetParent(canvas.transform, false);

            var rect = _runtimeResultPanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = _runtimeResultPanel.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.72f);
            image.raycastTarget = true;

            var textObject = new GameObject("ResultText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(_runtimeResultPanel.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(760f, 220f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 34;
            text.color = Color.white;
            text.text = resultMessage;

            _resultPanelVisible = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ResolveResultTextReferences()
        {
            if (_resultPanel == null)
                return;

            var texts = _resultPanel.GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                if (text == null) continue;
                string lowerName = text.name.ToLowerInvariant();

                if (_nicknameText == null && lowerName.Contains("nickname"))
                    _nicknameText = text;
                else if (_rankText == null && lowerName.Contains("rank"))
                    _rankText = text;
                else if (_mostKilledByText == null && (lowerName.Contains("killedby") || lowerName.Contains("killed_by") || lowerName.Contains("killed by") || lowerName.Contains("defeatedby") || lowerName.Contains("defeated_by") || lowerName.Contains("defeated by")))
                    _mostKilledByText = text;
                else if (_mostKilledText == null && (lowerName.Contains("mostkilled") || lowerName.Contains("most_killed") || lowerName.Contains("most killed") || lowerName.Contains("mostdefeated") || lowerName.Contains("most_defeated") || lowerName.Contains("most defeated") || lowerName.Contains("defeated")))
                    _mostKilledText = text;
                else if (_restartPromptText == null && (lowerName.Contains("restart") || lowerName.Contains("prompt")))
                    _restartPromptText = text;
            }

            if (_resultSummaryText == null && _nicknameText == null && _rankText == null && _mostKilledByText == null && _mostKilledText == null && texts.Length > 0)
                _resultSummaryText = texts[0];
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
                text.text = value;
        }

        private string BuildResultMessage(string playerName, int rank, string mostKilledBy, string mostKilled)
        {
            return $"Nickname: {playerName}\nRank: {rank}\nMost defeated by: {mostKilledBy}\nMost defeated: {mostKilled}\n\nPress Enter to Restart";
        }

        [Command(requiresAuthority = false)]
        public void CmdRequestRestart(NetworkConnectionToClient sender = null)
        {
            if (CurrentState != BattleState.MatchEnded) return;
            if (NetworkManager.singleton == null) return;

            _restartRequested = true;
            NetworkManager.singleton.ServerChangeScene("Battle_waiting");
        }

        private void OnIsLoadingChanged(bool oldVal, bool newVal)
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != "Battle")
                newVal = false;

            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.UpdateLoadingOverlay(newVal);
            }
            else if (newVal == true) // 켜야 하는데 아직 HUD가 없다면 대기 후 실행
            {
                StartCoroutine(CoWaitAndShowLoading());
            }
        }

        private IEnumerator CoWaitAndShowLoading()
        {
            float timeout = 5f; // 최대 5초 대기
            while (PlayerHUD.Instance == null && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            
            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.UpdateLoadingOverlay(IsLoading);
            }
        }

        [Server]
        private void TeleportPlayersToSpawnPointsOnServer()
        {
            var starts = FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None);
            if (starts == null || starts.Length == 0)
            {
                Debug.LogWarning("[BattleStateMachine] No NetworkStartPosition found. Players keep current positions.");
                return;
            }

            var players = new List<PlayerManager>(FindObjectsByType<PlayerManager>(FindObjectsSortMode.None));
            players.RemoveAll(player => player == null || player.netIdentity == null);
            players.Sort((a, b) => a.netId.CompareTo(b.netId));

            for (int i = 0; i < players.Count; i++)
            {
                PlayerManager player = players[i];
                Transform start = starts[i % starts.Length].transform;
                SnapPlayerTransform(player.gameObject, start.position, start.rotation);
                RpcSnapPlayerToSpawn(player.netId, start.position, start.rotation);
            }
        }

        [ClientRpc]
        private void RpcSnapPlayerToSpawn(uint playerNetId, Vector3 position, Quaternion rotation)
        {
            if (NetworkClient.spawned.TryGetValue(playerNetId, out NetworkIdentity identity) && identity != null)
                SnapPlayerTransform(identity.gameObject, position, rotation);
        }

        private static void SnapPlayerTransform(GameObject playerObject, Vector3 position, Quaternion rotation)
        {
            if (playerObject == null)
                return;

            CharacterController controller = playerObject.GetComponent<CharacterController>();
            bool wasControllerEnabled = controller != null && controller.enabled;
            if (wasControllerEnabled)
                controller.enabled = false;

            playerObject.transform.SetPositionAndRotation(position, rotation);

            if (wasControllerEnabled)
                controller.enabled = true;
        }

        [ClientRpc]
        private void RpcTeleportToSpawnPoints()
        {
            // 클라이언트에서 자신의 캐릭터를 지정된 스폰 포인트로 이동
            var player = NetworkClient.localPlayer;
            if (player != null)
            {
                // NetworkStartPosition을 찾아 랜덤하게 배치 (간단 구현)
                var starts = FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None);
                if (starts.Length > 0)
                {
                    int index = (int)netId % starts.Length; // 혹은 서버에서 index를 내려주는 방식 추천
                    player.transform.position = starts[index].transform.position;
                    player.transform.rotation = starts[index].transform.rotation;
                }
            }
        }

        private void OnStateChanged(BattleState oldState, BattleState newState)
        {
            Debug.Log($"Battle State Changed: {oldState} -> {newState}");
            if (newState == BattleState.InBattle || newState == BattleState.MatchEnded)
            {
                if (PlayerHUD.Instance != null)
                    PlayerHUD.Instance.UpdateLoadingOverlay(false);
            }
            // UI 업데이트 알림 등을 여기서 수행할 수 있습니다.
            if (BattleTimerUI.Instance != null)
            {
                if (newState == BattleState.Countdown)
                    BattleTimerUI.Instance.UpdateStateMessage("Get Ready!", true);
                else if (newState == BattleState.InBattle)
                    BattleTimerUI.Instance.UpdateStateMessage("", false);
            }
        }

        private void OnRemainingTimeChanged(float oldTime, float newTime)
        {
            if (BattleTimerUI.Instance != null)
            {
                BattleTimerUI.Instance.UpdateTime(newTime);
            }
        }

        #region [Internal Player Logic]

        /// <summary>
        /// 서버에서 플레이어 사망 시 호출하여 리스폰 상태를 관리합니다.
        /// </summary>
        [Server]
        public void OnPlayerKilled(NetworkConnectionToClient targetPlayer)
        {
            StartCoroutine(RespawnRoutine(targetPlayer));
        }

        private IEnumerator RespawnRoutine(NetworkConnectionToClient targetPlayer)
        {
            // 사망 시 처리 (예: 비활성)
            // TargetRpc 등을 통해 해당 클라이언트에게 리스폰 UI 출력 명령
            yield return new WaitForSeconds(RespawnDuration);
            // 리스폰 및 무적 부여 로직 수행
        }

        #endregion
    }
}
