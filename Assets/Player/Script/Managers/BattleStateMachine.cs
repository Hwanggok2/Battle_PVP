using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.UI;

namespace BattlePvp.Networking
{
    public enum BattleState { Waiting, PreMatch, Countdown, InBattle, Respawn }

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

        private Coroutine _activeMatchRoutine;

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
            RpcTeleportToSpawnPoints();

            // 3. 모든 플레이어 체력 최대치로 강제 설정 (서버 권한)
            // 약간의 프레임 대기 후 갱신
            yield return null;
            var allHealthSystems = FindObjectsByType<HealthSystem>(FindObjectsSortMode.None);
            foreach (var hs in allHealthSystems)
            {
                hs.RefreshFromStats(keepCurrentHpFlat: false);
                hs.RefillHealth();
            }
            Debug.Log($"[BattleStateMachine] {allHealthSystems.Length}명의 체력을 최대치로 초기화했습니다.");

            // 4. 로딩 화면 끄기
            IsLoading = false;

            // 3. In-Battle
            CurrentState = BattleState.InBattle;
            RemainingTime = MatchDuration;
            while (RemainingTime > 0)
            {
                yield return new WaitForSeconds(1f);
                RemainingTime--;
            }

            // 4. Match End (추후 구현)
            Debug.Log("Match Ended!");
            _activeMatchRoutine = null;
        }

        private void OnIsLoadingChanged(bool oldVal, bool newVal)
        {
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
