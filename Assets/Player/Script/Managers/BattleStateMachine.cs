using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.UI;
using BattlePvp.Logic;
using TMPro;

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
        [SerializeField] private TMP_Text _damageTakenText;
        [SerializeField] private TMP_Text _damageDealtText;
        [SerializeField] private TMP_Text _winnerText;
        [SerializeField] private TMP_Text _mostKilledByText;
        [SerializeField] private TMP_Text _mostKilledText;
        [SerializeField] private TMP_Text _restartPromptText;
        [SerializeField] private TMP_Text _resultSummaryText;

        [Header("Result UI Labels")]
        [SerializeField] private string _nicknamePrefix = "\uB2C9\uB124\uC784 : ";
        [SerializeField] private string _rankPrefix = "\uC21C\uC704 : ";
        [SerializeField] private string _damageTakenPrefix = "\uBC1B\uC740 \uB370\uBBF8\uC9C0 : ";
        [SerializeField] private string _damageDealtPrefix = "\uC785\uD78C \uB370\uBBF8\uC9C0 : ";
        [SerializeField] private string _winnerPrefix = "\uC2B9\uC790 : ";
        [SerializeField] private string _mostKilledByPrefix = "\uB098\uB97C \uCD5C\uB2E4 \uCC98\uCE58 : ";
        [SerializeField] private string _mostKilledPrefix = "\uB0B4\uAC00 \uCD5C\uB2E4 \uCC98\uCE58 : ";
        [SerializeField] private string _restartPrompt = BattleActionPrompt.ReturnToLobby;

        private Coroutine _activeMatchRoutine;
        private readonly MatchClock _clock = new MatchClock();
        private readonly BattleSpawnPlacement _spawnPlacement = new BattleSpawnPlacement();
        private BattleResultView _resultView;
        private bool _restartRequested;
        private bool _resultPanelVisible;
        public bool IsResultPanelVisible => _resultPanelVisible;
        private MatchLedger _matchLedger;
        private string _matchRoomId;
        public MatchResultSnapshot LastCompletedMatch { get; private set; }

        private void OnDestroy()
        {
            _resultView?.Dispose();
            _resultPanelVisible = false;
            if (Instance == this) Instance = null;
            GameInputController.RefreshCursorState();
        }

        private bool IsReturnWaitingState => gameObject.scene.name == BattleNetworkManager.WaitingScene &&
            NetworkManager.singleton is BattleNetworkManager manager &&
            (manager.IsPreparingReturnWaiting || manager.HasSplitResultRoom);

        private void Awake()
        {
            if (IsReturnWaitingState) return;
            if (Instance == null)
            {
                Instance = this;
                // Apply the default before network state callbacks can show a result.
                HideResultPanel();
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
            if (IsReturnWaitingState) return;
            CurrentState = BattleState.Waiting;

            // 정확히 "Battle" 씬일 때만 매치를 시작합니다. (Battle_waiting 등 오발 방지)
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != "Battle")
                IsLoading = false;

            if (sceneName == "Battle")
            {
                if (NetworkManager.singleton is BattleNetworkManager manager)
                    MatchDuration = manager.SelectedMatchDuration;
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

            // Snapshot the starting roster. Later joins are protected by HealthSystem until ready.
            var startingPlayers = new List<NetworkConnectionToClient>(NetworkServer.connections.Values);
            double readyDeadline = Time.realtimeSinceStartupAsDouble + 15d;
            yield return null;
            // A fresh local host creates its connection after scene objects start.
            var practice = NetworkManager.singleton as BattleNetworkManager;
            while (practice != null && practice.IsPractice && NetworkServer.active &&
                (NetworkServer.localConnection?.identity == null ||
                 !NetworkServer.localConnection.identity.GetComponent<BattlePvp.Stats.StatManager>().HasServerCombatStats))
            {
                if (Time.realtimeSinceStartupAsDouble >= readyDeadline)
                { practice.StopPractice(); yield break; }
                yield return null;
            }
            while (startingPlayers.Count > 0)
            {
                for (int i = startingPlayers.Count - 1; i >= 0; i--)
                {
                    var connection = startingPlayers[i];
                    if (!NetworkServer.connections.TryGetValue(connection.connectionId, out var current) ||
                        !ReferenceEquals(current, connection))
                    {
                        startingPlayers.RemoveAt(i);
                        continue;
                    }
                    var stats = connection.identity != null ? connection.identity.GetComponent<BattlePvp.Stats.StatManager>() : null;
                    if (connection.isReady && stats != null && stats.HasServerCombatStats)
                        startingPlayers.RemoveAt(i);
                    else if (Time.realtimeSinceStartupAsDouble >= readyDeadline)
                    {
                        connection.Disconnect();
                        startingPlayers.RemoveAt(i);
                    }
                }
                if (startingPlayers.Count > 0) yield return null;
            }
            if (!NetworkServer.active) { _activeMatchRoutine = null; yield break; }
            if (practice != null && practice.IsPractice && !practice.PreparePracticeBots())
            { Debug.LogError("[Practice] Not enough safe spawn points."); practice.StopPractice(); yield break; }

            // 2. 스폰 포인트 배치
            try
            {
                if (!_spawnPlacement.PlacePlayers())
                {
                    Debug.LogError("[BattleSpawnPlacement] Not enough safe points to place every player.");
                    if (practice != null && practice.IsPractice) practice.StopPractice();
                    _activeMatchRoutine = null;
                    yield break;
                }

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
            _matchRoomId = practice != null && practice.IsPractice ? practice.PracticeRoomId : PlayFabBattleManager.Instance?.CurrentRoomId;
            if (!RoomIdentity.IsValid(_matchRoomId))
            {
                Debug.LogError("[BattleStateMachine] A verified room is required to start a recorded match.");
                _activeMatchRoutine = null;
                yield break;
            }
            _matchLedger = new MatchLedger();
            // This host-local id identifies a snapshot, not a backend-issued or persisted match.
            _matchLedger.Begin(System.Guid.NewGuid().ToString("N"), _matchRoomId);
            LastCompletedMatch = null;
            var scoreSystems = FindObjectsByType<ScoreSystem>(FindObjectsSortMode.None);
            foreach (var score in scoreSystems)
            {
                if (score != null && score.GetComponent("PlayerManager") != null)
                {
                    score.ResetMatchStats();
                    RegisterMatchParticipant(score);
                }
            }

            _restartRequested = false;
            _resultPanelVisible = false;
            HideResultPanel();
            CurrentState = BattleState.InBattle;
            _clock.Start(NetworkTime.time, float.IsFinite(MatchDuration) ? Mathf.Max(0f, MatchDuration) : 180f);
            RemainingTime = (float)System.Math.Ceiling(_clock.Remaining(NetworkTime.time));
            while (CurrentState == BattleState.InBattle && !_clock.TryFinish(NetworkTime.time))
            {
                RemainingTime = (float)System.Math.Ceiling(_clock.Remaining(NetworkTime.time));
                yield return null;
            }

            // 4. Match End (추후 구현)
            EndMatchOnServer();
            _activeMatchRoutine = null;
        }

        [Server]
        public bool TryRegisterMatchParticipant(ScoreSystem score)
        {
            return CurrentState == BattleState.InBattle && RegisterMatchParticipant(score);
        }

        private bool RegisterMatchParticipant(ScoreSystem score)
        {
            if (_matchLedger == null || !_matchLedger.IsRecording || score == null ||
                score.gameObject.scene != gameObject.scene || score.GetComponent("PlayerManager") == null) return false;
            if (score.AttachToMatch(_matchLedger, _matchRoomId)) return true;
            Debug.LogWarning("[BattleStateMachine] Refusing an unverified or duplicate match participant.");
            score.connectionToClient?.Disconnect();
            return false;
        }

        public void RequestRestartFromInput()
        {
            if (CurrentState != BattleState.MatchEnded || _restartRequested || !_resultPanelVisible) return;
            if (GameInputController.IsTextInputActive || GameInputController.IsSubmitConsumedThisFrame) return;
            GameInputController.ConsumeSubmit();
            _restartRequested = true;
            if (NetworkManager.singleton is BattleNetworkManager manager) manager.ReturnToLobbyAfterMatch();
        }

        public override void OnStopServer()
        {
            _clock.Stop();
            if (_activeMatchRoutine != null) StopCoroutine(_activeMatchRoutine);
            _activeMatchRoutine = null;
            base.OnStopServer();
        }

        public bool IsActiveParticipant(uint netId) => _matchLedger != null && _matchLedger.IsActiveParticipant(netId);

        [Server]
        public void CheckLastParticipant()
        {
            if (CurrentState != BattleState.InBattle || _matchLedger == null ||
                (NetworkManager.singleton is BattleNetworkManager manager && manager.IsPractice) ||
                !_matchLedger.TryGetLastParticipant(out _)) return;
            EndMatchOnServer();
        }

        [Server]
        private void EndMatchOnServer()
        {
            if (CurrentState != BattleState.InBattle || _matchLedger == null) return;
            CurrentState = BattleState.MatchEnded;
            RemainingTime = 0f;
            uint lastParticipant = 0;
            if (!(NetworkManager.singleton is BattleNetworkManager practice && practice.IsPractice))
                _matchLedger.TryGetLastParticipant(out lastParticipant);
            LastCompletedMatch = _matchLedger.Finish(lastParticipant);
            if (NetworkManager.singleton is BattleNetworkManager manager)
                manager.RememberCompletedMatch(LastCompletedMatch);

            var winnerNetIds = new List<uint>();
            var winnerNames = new List<string>();
            foreach (MatchParticipantResult participant in LastCompletedMatch.Participants)
            {
                if (participant.Rank != 1) continue;
                winnerNames.Add(participant.PlayerName);
                if (TryGetConnectedScore(participant, out _)) winnerNetIds.Add(participant.LastNetId);
            }

            var allHealthSystems = FindObjectsByType<HealthSystem>(FindObjectsSortMode.None);
            foreach (var hs in allHealthSystems)
            {
                if (hs == null || hs.gameObject.scene != gameObject.scene) continue;
                hs.isInvincible = true;
                hs.Revive(1f);
            }

            string winnerName = winnerNames.Count > 0 ? string.Join(", ", winnerNames) : "Unknown";
            var rows = new List<BattleResultRow>();
            foreach (var participant in LastCompletedMatch.Participants)
            {
                string character = "기본 캐릭터";
                if (NetworkServer.spawned.TryGetValue(participant.LastNetId, out var avatar))
                {
                    var appearance = avatar.GetComponent<BattlePvp.Characters.PlayerAppearance>();
                    character = BattlePvp.Characters.CharacterCatalog.Instance?.Find(appearance != null ? appearance.SelectedId : "default")?.DisplayName ?? character;
                }
                rows.Add(new BattleResultRow { NetId = participant.LastNetId, Rank = participant.Rank,
                    PlayerName = participant.PlayerName, CharacterName = character, Kills = participant.Totals.Points, Damage = participant.Totals.DamageDealt });
            }
            rows.Sort((a,b) => a.Rank.CompareTo(b.Rank));
            RpcSetFinalStandings(rows.ToArray(), MatchDuration);
            RpcHandleMatchEnded(winnerNetIds.ToArray());
            SendPersonalResults(LastCompletedMatch, winnerName);
            (NetworkManager.singleton as BattleNetworkManager)?.ClearDisconnectedPlayers();
            Debug.Log($"[BattleStateMachine] Match ended. Recorded participants={LastCompletedMatch.Participants.Count}, Winners={winnerNames.Count}");
        }

        [ClientRpc]
        private void RpcSetFinalStandings(BattleResultRow[] rows, float duration)
        {
            GetResultView().SetStandings(rows, NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0, duration);
        }

        [ClientRpc]
        private void RpcHandleMatchEnded(uint[] winnerNetIds)
        {
            var localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null || localPlayer.gameObject.scene != gameObject.scene ||
                (NetworkManager.singleton is BattleNetworkManager manager && manager.IsReturningFromBattle)) return;
            bool isWinner = localPlayer != null && IsWinnerNetId(localPlayer.netId, winnerNetIds);
            Transform spectateTarget = ResolveWinnerSpectateTarget(winnerNetIds);
            // Departed winners remain winners in the result, while the camera needs a live target.
            if (spectateTarget == null && localPlayer != null) spectateTarget = localPlayer.transform;

            if (localPlayer != null)
            {
                var playerManager = localPlayer.GetComponent<PlayerManager>();
                if (playerManager != null)
                    playerManager.EnterMatchEndMode(spectateTarget, isWinner);
            }
        }

        private bool TryGetConnectedScore(MatchParticipantResult participant, out ScoreSystem score)
        {
            score = null;
            if (!participant.WasConnectedAtEnd ||
                !NetworkServer.spawned.TryGetValue(participant.LastNetId, out NetworkIdentity identity) ||
                identity == null) return false;
            var account = identity.connectionToClient?.authenticationData as AuthenticatedRoomPlayer;
            if (NetworkManager.singleton is BattleNetworkManager practice && practice.TryGetPracticeParticipant(identity, out string practiceId))
            {
                score = identity.GetComponent<ScoreSystem>();
                return score != null && practiceId == participant.PlayFabId;
            }
            if (account == null || account.RoomId != _matchRoomId ||
                !string.Equals(account.PlayFabId, participant.PlayFabId, System.StringComparison.OrdinalIgnoreCase))
                return false;
            score = identity.GetComponent<ScoreSystem>();
            return score != null && score.connectionToClient != null;
        }

        [Server]
        private void SendPersonalResults(MatchResultSnapshot result, string winnerName)
        {
            foreach (MatchParticipantResult participant in result.Participants)
            {
                if (!TryGetConnectedScore(participant, out ScoreSystem score) || score.connectionToClient == null) continue;
                result.GetTopOpponent(participant.DeathsByOpponent, out string mostKilledBy, out int mostKilledByCount);
                result.GetTopOpponent(participant.KillsByOpponent, out string mostKilled, out int mostKilledCount);
                bool practice = NetworkManager.singleton is BattleNetworkManager manager && manager.IsPractice;
                score.PresentMatchReward(practice ? 0 : participant.ProvisionalXp, participant.Totals.Points);
                TargetShowPersonalResult(score.connectionToClient, participant.PlayerName, participant.Rank,
                    winnerName, participant.Totals.DamageTaken, participant.Totals.DamageDealt,
                    mostKilledBy, mostKilledByCount, mostKilled, mostKilledCount);
            }
        }

        [TargetRpc]
        private void TargetShowPersonalResult(
            NetworkConnectionToClient target,
            string playerName,
            int rank,
            string winnerName,
            float damageTaken,
            float damageDealt,
            string mostKilledBy,
            int mostKilledByCount,
            string mostKilled,
            int mostKilledCount)
        {
            StartCoroutine(CoShowResultPanel(
                playerName,
                rank,
                winnerName,
                damageTaken,
                damageDealt,
                mostKilledBy,
                mostKilledByCount,
                mostKilled,
                mostKilledCount));
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

        private IEnumerator CoShowResultPanel(
            string playerName,
            int rank,
            string winnerName,
            float damageTaken,
            float damageDealt,
            string mostKilledBy,
            int mostKilledByCount,
            string mostKilled,
            int mostKilledCount)
        {
            _resultPanelVisible = false;
            HideResultPanel();

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, ResultDelaySeconds));

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != gameObject.scene ||
                (NetworkManager.singleton is BattleNetworkManager manager && manager.IsReturningFromBattle)) yield break;

            ShowResultPanel(
                playerName,
                rank,
                winnerName,
                damageTaken,
                damageDealt,
                mostKilledBy,
                mostKilledByCount,
                mostKilled,
                mostKilledCount);
        }

        private void HideResultPanel()
        {
            _resultPanelVisible = false;
            GetResultView().Hide();
            GameInputController.RefreshCursorState();
        }

        public void DismissLocalResult() => HideResultPanel();

        private void ShowResultPanel(
            string playerName, int rank, string winnerName, float damageTaken, float damageDealt,
            string mostKilledBy, int mostKilledByCount, string mostKilled, int mostKilledCount)
        {
            GetResultView().Show(new PersonalBattleResult(playerName, rank, winnerName, damageTaken,
                damageDealt, mostKilledBy, mostKilledByCount, mostKilled, mostKilledCount));
            _resultPanelVisible = true;
            GameInputController.RefreshCursorState();
        }

        private BattleResultView GetResultView()
        {
            if (_resultView != null) return _resultView;
            _resultView = new BattleResultView(new BattleResultBindings
            {
                Panel = _resultPanel, Nickname = _nicknameText, Rank = _rankText,
                DamageTaken = _damageTakenText, DamageDealt = _damageDealtText, Winner = _winnerText,
                MostKilledBy = _mostKilledByText, MostKilled = _mostKilledText,
                RestartPrompt = _restartPromptText, Summary = _resultSummaryText
            }, new BattleResultLabels
            {
                NicknamePrefix = _nicknamePrefix, RankPrefix = _rankPrefix,
                DamageTakenPrefix = _damageTakenPrefix, DamageDealtPrefix = _damageDealtPrefix,
                WinnerPrefix = _winnerPrefix, MostKilledByPrefix = _mostKilledByPrefix,
                MostKilledPrefix = _mostKilledPrefix, RestartPrompt = BattleActionPrompt.ReturnToLobby
            });
            return _resultView;
        }

        [Server]
        public void AnnounceKill(string killerName, string victimName)
        {
            if (CurrentState != BattleState.InBattle)
                return;

            if (string.IsNullOrWhiteSpace(killerName))
                killerName = "Unknown";
            if (string.IsNullOrWhiteSpace(victimName))
                victimName = "Unknown";

            RpcShowKillAnnouncement(killerName, victimName);
        }

        [ClientRpc]
        private void RpcShowKillAnnouncement(string killerName, string victimName)
        {
            KillAnnouncementUI.ShowGlobal(killerName, victimName);
        }

        [Command(requiresAuthority = false)]
        public void CmdRequestRestart(NetworkConnectionToClient sender = null)
        {
            if (CurrentState != BattleState.MatchEnded) return;
            if (NetworkManager.singleton == null) return;
            if (sender == null || !sender.isAuthenticated || sender.identity == null ||
                !(sender.authenticationData is AuthenticatedRoomPlayer account) || account.RoomId != _matchRoomId) return;

            TargetReturnToLobby(sender);
        }

        [TargetRpc]
        private void TargetReturnToLobby(NetworkConnectionToClient target)
        {
            if (NetworkManager.singleton is BattleNetworkManager manager) manager.ReturnToLobbyAfterMatch();
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

    }
}
