using Mirror;
using UnityEngine;
using System.Collections.Generic;
using BattlePvp.Networking;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 서버 경기 기록의 점수를 동기화하고 개인 결과를 표시합니다. 영속 보상은 별도 백엔드의 책임입니다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class ScoreSystem : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnPointsChanged))]
        public int CurrentPoints = 0;

        [SyncVar(hook = nameof(OnDeathsChanged))]
        public int CurrentDeaths = 0;

        [SyncVar(hook = nameof(OnNameChanged))]
        public string PlayerName = "Unknown";

        [SyncVar] public float MatchDamageDealt;
        [SyncVar] public float MatchDamageTaken;
        [SyncVar(hook = nameof(OnConnectionChanged))] private bool _isConnected = true;
        public bool IsConnected => _isConnected;

        public static event System.Action<ScoreSystem> OnScoreUpdated;

        public static readonly List<ScoreSystem> ActiveScores = new List<ScoreSystem>();
        public static readonly System.Func<ScoreSystem, int> PointsOf =
            score => score != null ? score.CurrentPoints : int.MinValue;

        public static int CompareForDisplay(ScoreSystem left, ScoreSystem right)
        {
            int points = right.CurrentPoints.CompareTo(left.CurrentPoints);
            if (points != 0) return points;
            int name = string.CompareOrdinal(left.PlayerName, right.PlayerName);
            return name != 0 ? name : left.netId.CompareTo(right.netId);
        }

        public int CurrentKills => CurrentPoints;
        public float KillsPerDeath => CurrentDeaths <= 0 ? CurrentKills : CurrentKills / (float)CurrentDeaths;

        private MatchLedger _matchLedger;

        public override void OnStartServer()
        {
            base.OnStartServer();
            BattleStateMachine.Instance?.TryRegisterMatchParticipant(this);
        }

        public override void OnStopServer()
        {
            _matchLedger?.Detach(netId);
            _matchLedger = null;
            base.OnStopServer();
        }

        [Server]
        public bool AttachToMatch(MatchLedger ledger, string roomId)
        {
            var identity = connectionToClient?.authenticationData as AuthenticatedRoomPlayer;
            string participantId = identity != null && identity.RoomId == roomId ? identity.PlayFabId : null;
            if (NetworkManager.singleton is BattleNetworkManager practice && practice.IsPractice && roomId == practice.PracticeRoomId)
                practice.TryGetPracticeParticipant(netIdentity, out participantId);
            if (participantId == null || ledger == null ||
                !ledger.TryAttach(netId, participantId, PlayerName, out MatchTotals totals))
                return false;
            _matchLedger = ledger;
            ApplyMatchTotals(totals);
            return true;
        }

        [Server]
        public void ServerSetConnected(bool connected)
        {
            _isConnected = connected;
            _matchLedger?.SetConnectionState(netId, connected);
            if (isClient) RefreshConnectedRoster();
        }

        private void OnConnectionChanged(bool previous, bool current) => RefreshConnectedRoster();

        private void RefreshConnectedRoster()
        {
            bool wasListed = ActiveScores.Contains(this);
            int removed = ActiveScores.RemoveAll(score => score == null || score == this || (score.netId == netId && score != this));
            bool shouldList = _isConnected && GetComponent<PlayerManager>() != null;
            if (shouldList) ActiveScores.Add(this);
            // Host SyncVar hook and explicit server update can both call this in one transition.
            if (wasListed != shouldList || removed > (wasListed ? 1 : 0)) OnScoreUpdated?.Invoke(this);
        }

        [Server]
        public void ResetMatchStats()
        {
            // The active ledger owns these values. A public server helper must not diverge from it.
            if (_matchLedger != null && _matchLedger.IsRecording) return;
            _matchLedger = null;
            CurrentPoints = 0;
            CurrentDeaths = 0;
            MatchDamageDealt = 0f;
            MatchDamageTaken = 0f;
        }

        [Server]
        public void RecordDamageDealt(float amount)
        {
            if (_matchLedger != null && _matchLedger.RecordDamage(netId, amount, 0f))
                RefreshMatchTotals();
        }

        [Server]
        public void RecordDamageTaken(float amount)
        {
            if (_matchLedger != null && _matchLedger.RecordDamage(netId, 0f, amount))
                RefreshMatchTotals();
        }

        [Server]
        public void RecordKillAgainst(ScoreSystem victim)
        {
            if (victim == null || victim == this || _matchLedger == null || victim._matchLedger != _matchLedger)
                return;

            HealthSystem victimHealth = victim.GetComponent<HealthSystem>();
            if (victimHealth == null || !_matchLedger.RecordKill(netId, victim.netId,
                victimHealth.DeathSequence, victimHealth.IsDead))
                return;

            RefreshMatchTotals();
            victim.RefreshMatchTotals();

            if (NetworkManager.singleton is BattleNetworkManager practice && practice.IsPractice) return;

            if (connectionToClient != null)
                TargetAddCumulativeKill(connectionToClient);
            if (victim.connectionToClient != null)
                victim.TargetAddCumulativeDeath(victim.connectionToClient);
        }

        private void RefreshMatchTotals()
        {
            if (_matchLedger.TryGetTotals(netId, out MatchTotals totals)) ApplyMatchTotals(totals);
        }

        private void ApplyMatchTotals(MatchTotals totals)
        {
            CurrentPoints = totals.Points;
            CurrentDeaths = totals.Deaths;
            MatchDamageDealt = totals.DamageDealt;
            MatchDamageTaken = totals.DamageTaken;
        }

        [TargetRpc]
        private void TargetAddCumulativeKill(NetworkConnection target)
        {
            ApplyLocalCombatRecordDelta(1, 0);
        }

        [TargetRpc]
        private void TargetAddCumulativeDeath(NetworkConnection target)
        {
            ApplyLocalCombatRecordDelta(0, 1);
        }

        private void ApplyLocalCombatRecordDelta(int killsDelta, int deathsDelta)
        {
            var gdm = BattlePvp.Managers.GlobalDataManager.Instance;
            if (gdm == null)
                return;

            gdm.AddCombatRecord(killsDelta, deathsDelta);

            if (gdm.HasLoadedCombatRecord && PlayFabBattleManager.Instance != null)
                PlayFabBattleManager.Instance.SaveCombatRecord(gdm.CumulativeKills, gdm.CumulativeDeaths);

            OnScoreUpdated?.Invoke(this);
        }

        public static bool TryClaimDeathSequence(uint deathSequence, bool isDead, ref uint lastProcessed)
        {
            if (!isDead || deathSequence == 0 ||
                (lastProcessed != 0 && unchecked((int)(deathSequence - lastProcessed)) <= 0)) return false;
            lastProcessed = deathSequence;
            return true;
        }

        [Server]
        public void SetPlayerName(string newName)
        {
            PlayerName = MatchLedger.NormalizeName(newName);
            _matchLedger?.Rename(netId, PlayerName);
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
            if (GetComponent("PlayerManager") == null)
                return;

            RefreshConnectedRoster();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            ActiveScores.RemoveAll(score => score == null || score == this);
            OnScoreUpdated?.Invoke(this);
        }

        private void OnPointsChanged(int oldVal, int newVal)
        {
            OnScoreUpdated?.Invoke(this);
        }

        private void OnDeathsChanged(int oldVal, int newVal)
        {
            OnScoreUpdated?.Invoke(this);
        }

        private void OnNameChanged(string oldVal, string newVal)
        {
            OnScoreUpdated?.Invoke(this);
        }

        #region [Match Result Logic]

        [Server]
        public void PresentMatchReward(int xp, int points)
        {
            if (connectionToClient != null)
                TargetUpdatePlayFabData(connectionToClient, xp, points);
        }

        [TargetRpc]
        private void TargetUpdatePlayFabData(NetworkConnection target, int xp, int points)
        {
            Debug.Log($"경기 결과: {points}점, 예상 XP {xp} (백엔드 보상 미반영)");
            // Competitive persistence must be submitted by the authenticated trusted host.
            // A TargetRpc is presentation, and cannot authorize client leaderboard writes.
        }

        #endregion
    }
}
