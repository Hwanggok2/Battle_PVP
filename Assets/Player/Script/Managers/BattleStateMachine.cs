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

        [SyncVar]
        public float RemainingTime = 0f;

        [Header("Settings")]
        public float PreMatchDuration = 10f;
        public float CountdownDuration = 5f;
        public float MatchDuration = 180f;
        public float RespawnDuration = 5f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CurrentState = BattleState.Waiting;
        }

        [Server]
        public void StartMatch()
        {
            if (CurrentState != BattleState.Waiting) return;
            StartCoroutine(MatchFlowRoutine());
        }

        private IEnumerator MatchFlowRoutine()
        {
            // 1. Pre-Match
            CurrentState = BattleState.PreMatch;
            RemainingTime = PreMatchDuration;
            while (RemainingTime > 0)
            {
                yield return new WaitForSeconds(1f);
                RemainingTime--;
            }

            // 2. Countdown
            CurrentState = BattleState.Countdown;
            RemainingTime = CountdownDuration;
            // 여기서 스폰 포인트 계산 및 유저 텔레포트 명령(Rpc)을 보낼 수 있습니다.
            RpcTeleportToSpawnPoints();

            while (RemainingTime > 0)
            {
                yield return new WaitForSeconds(1f);
                RemainingTime--;
            }

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
