using Mirror;
using UnityEngine;
using System.Collections.Generic;
using BattlePvp.Networking;
using System.Linq;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 점수와 XP 보상을 관리하는 인터페이스입니다. 추후 공식을 유연하게 변경할 수 있도록 디자인했습니다.
    /// </summary>
    public interface IXpDistributor
    {
        int CalculateXp(int rank, int points);
    }

    public class SimpleXpDistributor : IXpDistributor
    {
        public int CalculateXp(int rank, int points)
        {
            if (rank == 1) return 100 + (points * 10);
            if (rank == 2) return 50 + (points * 5);
            return 20 + (points * 2);
        }
    }

    /// <summary>
    /// 플레이어의 킬 점수를 동기화하고 경기 종료 후 PlayFab에 결과를 반영하는 클래스입니다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class ScoreSystem : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnPointsChanged))]
        public int CurrentPoints = 0;

        [SyncVar(hook = nameof(OnNameChanged))]
        public string PlayerName = "Unknown";

        public static event System.Action<ScoreSystem> OnScoreUpdated;

        public static readonly List<ScoreSystem> ActiveScores = new List<ScoreSystem>();

        private IXpDistributor _xpDistributor = new SimpleXpDistributor();

        [Server]
        public void AddPoint(int amount)
        {
            CurrentPoints += amount;
        }

        /// <summary>
        /// 클라이언트에서 서버로 점수 추가를 요청합니다.
        /// </summary>
        [Command]
        public void CmdAddPoint(int amount)
        {
            AddPoint(amount);
        }

        [Server]
        public void SetPlayerName(string newName)
        {
            PlayerName = newName;
        }

        /// <summary>
        /// 클라이언트에서 서버로 닉네임 설정을 요청합니다.
        /// </summary>
        [Command]
        public void CmdSetPlayerName(string newName)
        {
            SetPlayerName(newName);
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            // GlobalDataManager에 저장된 닉네임을 서버로 전송
            var gdm = BattlePvp.Managers.GlobalDataManager.Instance;
            if (gdm != null && !string.IsNullOrEmpty(gdm.PlayerNickname))
            {
                CmdSetPlayerName(gdm.PlayerNickname);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!ActiveScores.Contains(this))
                ActiveScores.Add(this);
            OnScoreUpdated?.Invoke(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (ActiveScores.Contains(this))
                ActiveScores.Remove(this);
            OnScoreUpdated?.Invoke(this);
        }

        private void OnPointsChanged(int oldVal, int newVal)
        {
            OnScoreUpdated?.Invoke(this);
        }

        private void OnNameChanged(string oldVal, string newVal)
        {
            OnScoreUpdated?.Invoke(this);
        }

        #region [Match Result Logic]

        /// <summary>
        /// 서버에서 호출하여 모든 생존자의 랭킹을 산출하고 XP를 분배합니다.
        /// </summary>
        [Server]
        public void DistributeRewards(List<ScoreSystem> allPlayers)
        {
            var sortedList = allPlayers.OrderByDescending(p => p.CurrentPoints).ToList();

            for (int i = 0; i < sortedList.Count; i++)
            {
                int rank = i + 1;
                int points = sortedList[i].CurrentPoints;
                int xp = _xpDistributor.CalculateXp(rank, points);

                // PlayFab에 점수(리더보드용) 및 XP 업데이트 호출을 요청할 수 있습니다.
                TargetUpdatePlayFabData(sortedList[i].connectionToClient, xp, points);
            }
        }

        [TargetRpc]
        private void TargetUpdatePlayFabData(NetworkConnection target, int xp, int points)
        {
            Debug.Log($"경기 결과: {xp} XP 획득, {points} 점 기록!");
            PlayFabBattleManager.Instance.UpdateStatistics(points);
            // XP는 Internal User Data에 관리하므로, 클라이언트에서 업데이트 요청을 보낼 수도 있고
            // 보안을 위해서는 CloudScript 혹은 서버에서 직접 처리해야 합니다.
        }

        #endregion
    }
}
