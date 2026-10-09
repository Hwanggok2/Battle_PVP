using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BattlePvp.Networking;

namespace BattlePvp.Combat
{
    public interface IXpDistributor
    {
        int CalculateXp(int rank, int points);
    }

    public class SimpleXpDistributor : IXpDistributor
    {
        public int CalculateXp(int rank, int points)
        {
            long safePoints = Math.Max(0, points);
            long xp = rank == 1 ? 100 + safePoints * 10 : rank == 2 ? 50 + safePoints * 5 : 20 + safePoints * 2;
            return (int)Math.Min(int.MaxValue, xp);
        }
    }

    public readonly struct MatchTotals
    {
        public readonly int Points;
        public readonly int Deaths;
        public readonly float DamageDealt;
        public readonly float DamageTaken;

        public MatchTotals(int points, int deaths, float dealt, float taken)
        { Points = points; Deaths = deaths; DamageDealt = dealt; DamageTaken = taken; }
    }

    public sealed class MatchParticipantResult
    {
        public string PlayFabId { get; }
        public string PlayerName { get; }
        public uint LastNetId { get; }
        public bool WasConnectedAtEnd { get; }
        public MatchTotals Totals { get; }
        public int Rank { get; }
        // Presentation only. This is not a persisted or authorized backend reward.
        public int ProvisionalXp { get; }
        public IReadOnlyDictionary<string, int> KillsByOpponent { get; }
        public IReadOnlyDictionary<string, int> DeathsByOpponent { get; }

        internal MatchParticipantResult(string id, string name, uint netId, bool connected, MatchTotals totals,
            int rank, int xp, Dictionary<string, int> kills, Dictionary<string, int> deaths)
        {
            PlayFabId = id; PlayerName = name; LastNetId = netId; WasConnectedAtEnd = connected;
            Totals = totals; Rank = rank; ProvisionalXp = xp;
            KillsByOpponent = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(kills));
            DeathsByOpponent = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(deaths));
        }
    }

    public sealed class MatchResultSnapshot
    {
        public string LocalMatchId { get; }
        public string RoomId { get; }
        public IReadOnlyList<MatchParticipantResult> Participants { get; }

        internal MatchResultSnapshot(string localMatchId, string roomId, MatchParticipantResult[] participants)
        { LocalMatchId = localMatchId; RoomId = roomId; Participants = Array.AsReadOnly(participants); }

        public void GetTopOpponent(IReadOnlyDictionary<string, int> values, out string name, out int count)
        {
            name = "None"; count = 0;
            string topId = null;
            foreach (var pair in values)
                if (pair.Value > count || (pair.Value == count && string.CompareOrdinal(pair.Key, topId) < 0))
                { topId = pair.Key; count = pair.Value; }
            if (topId == null) return;
            name = "Unknown";
            foreach (MatchParticipantResult participant in Participants)
                if (participant.PlayFabId == topId) { name = participant.PlayerName; return; }
        }
    }

    /// <summary>
    /// 신뢰하는 호스트의 한 경기 기록. 검증된 계정 ID를 받아 연결 객체의 파괴와 별개로 보존한다.
    /// 메모리 기록이며 서버 발급 경기 ID, 영속 저장, 보상 트랜잭션을 제공하지 않는다.
    /// </summary>
    public sealed class MatchLedger
    {
        private sealed class Entry
        {
            public string Id;
            public string Name;
            public uint NetId;
            public bool Connected;
            public bool Withdrawn;
            public int Points;
            public int Deaths;
            public float Dealt;
            public float Taken;
            public uint LastDeathSequence;
            public readonly Dictionary<string, int> Kills = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> KilledBy = new Dictionary<string, int>(StringComparer.Ordinal);
            public MatchTotals Totals => new MatchTotals(Points, Deaths, Dealt, Taken);
        }

        private readonly Dictionary<string, Entry> _participants = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<uint, Entry> _connections = new Dictionary<uint, Entry>();
        private string _localMatchId;
        private string _roomId;
        private MatchResultSnapshot _result;
        public bool IsRecording { get; private set; }
        public int ParticipantCount => _participants.Count;

        public void Begin(string localMatchId, string roomId)
        {
            if (IsRecording) throw new InvalidOperationException("Finish the active match before beginning another one.");
            if (string.IsNullOrWhiteSpace(localMatchId)) throw new ArgumentException("A local match id is required.", nameof(localMatchId));
            if (!RoomIdentity.IsValid(roomId)) throw new ArgumentException("A valid room id is required.", nameof(roomId));
            _participants.Clear(); _connections.Clear(); _result = null;
            _localMatchId = localMatchId; _roomId = roomId; IsRecording = true;
        }

        public bool TryAttach(uint netId, string verifiedPlayFabId, string name, out MatchTotals totals)
        {
            totals = default;
            if (!IsRecording || netId == 0 || !RoomIdentity.TryNormalizePlayerId(verifiedPlayFabId, out string id)) return false;
            if (_connections.TryGetValue(netId, out Entry bound))
            {
                if (bound.Id != id) return false;
                bound.Connected = true;
                totals = bound.Totals;
                return true;
            }
            if (_participants.TryGetValue(id, out Entry entry))
            {
                if (entry.Withdrawn || entry.Connected || _connections.ContainsKey(entry.NetId)) return false;
            }
            else
            {
                entry = new Entry { Id = id };
                _participants.Add(id, entry);
            }
            entry.NetId = netId; entry.Connected = true; entry.LastDeathSequence = 0;
            string displayName = NormalizeName(name);
            if (entry.Name == null || displayName != "Unknown") entry.Name = displayName;
            _connections.Add(netId, entry);
            totals = entry.Totals;
            return true;
        }

        public bool Detach(uint netId)
        {
            if (!IsRecording || !_connections.TryGetValue(netId, out Entry entry)) return false;
            entry.Connected = false;
            _connections.Remove(netId);
            return true;
        }

        public bool IsActiveParticipant(uint netId) => IsRecording &&
            _connections.TryGetValue(netId, out Entry entry) && entry.Connected;

        public bool Withdraw(uint netId)
        {
            if (!IsRecording || !_connections.TryGetValue(netId, out Entry entry)) return false;
            entry.Withdrawn = true;
            return Detach(netId);
        }

        public bool TryGetLastParticipant(out uint netId)
        {
            netId = 0;
            // A solo test room must not immediately finish when its match starts.
            if (!IsRecording || ParticipantCount < 2) return false;
            foreach (var entry in _connections.Values)
            {
                if (!entry.Connected) continue;
                if (netId != 0) { netId = 0; return false; }
                netId = entry.NetId;
            }
            return netId != 0;
        }

        // A disconnected body remains a valid damage/kill target and keeps its death sequence.
        public bool SetConnectionState(uint netId, bool connected)
        {
            if (!IsRecording || !_connections.TryGetValue(netId, out Entry entry)) return false;
            entry.Connected = connected;
            return true;
        }

        public bool Rename(uint netId, string name)
        {
            if (!IsRecording || !_connections.TryGetValue(netId, out Entry entry)) return false;
            entry.Name = NormalizeName(name);
            return true;
        }

        public bool TryGetTotals(uint netId, out MatchTotals totals)
        {
            totals = default;
            if (!_connections.TryGetValue(netId, out Entry entry)) return false;
            totals = entry.Totals;
            return true;
        }

        public bool RecordDamage(uint netId, float dealt, float taken)
        {
            if (!IsRecording || !float.IsFinite(dealt) || !float.IsFinite(taken) || dealt < 0f || taken < 0f ||
                !_connections.TryGetValue(netId, out Entry entry)) return false;
            entry.Dealt = (float)Math.Min(float.MaxValue, (double)entry.Dealt + dealt);
            entry.Taken = (float)Math.Min(float.MaxValue, (double)entry.Taken + taken);
            return true;
        }

        public bool RecordKill(uint killerNetId, uint victimNetId, uint deathSequence, bool isDead)
        {
            if (!IsRecording || !isDead || deathSequence == 0 || killerNetId == victimNetId ||
                !_connections.TryGetValue(killerNetId, out Entry killer) ||
                !_connections.TryGetValue(victimNetId, out Entry victim) ||
                (victim.LastDeathSequence != 0 && unchecked((int)(deathSequence - victim.LastDeathSequence)) <= 0) ||
                killer.Points == int.MaxValue || victim.Deaths == int.MaxValue)
                return false;
            victim.LastDeathSequence = deathSequence;
            killer.Points++; victim.Deaths++;
            Increment(killer.Kills, victim.Id); Increment(victim.KilledBy, killer.Id);
            return true;
        }

        public MatchResultSnapshot Finish(uint lastParticipant = 0)
        {
            if (_result != null) return _result;
            if (!IsRecording) return null;
            if (!TryGetLastParticipant(out uint remaining) || lastParticipant != remaining) lastParticipant = 0;
            IsRecording = false;
            var entries = new List<Entry>(_participants.Values);
            entries.Sort((left, right) =>
            {
                int points = right.Points.CompareTo(left.Points);
                if (points != 0) return points;
                int name = string.CompareOrdinal(left.Name, right.Name);
                return name != 0 ? name : string.CompareOrdinal(left.Id, right.Id);
            });
            var rewards = new SimpleXpDistributor();
            var result = new MatchParticipantResult[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                int rank = 1;
                foreach (var other in entries)
                {
                    if (other == entry) continue;
                    if (lastParticipant != 0)
                    {
                        if (entry.NetId != lastParticipant && (other.NetId == lastParticipant || other.Points > entry.Points)) rank++;
                    }
                    else if ((!other.Withdrawn && entry.Withdrawn) ||
                        (other.Withdrawn == entry.Withdrawn && other.Points > entry.Points)) rank++;
                }
                result[i] = new MatchParticipantResult(entry.Id, entry.Name, entry.NetId, entry.Connected,
                    entry.Totals, rank, rewards.CalculateXp(rank, entry.Points), entry.Kills, entry.KilledBy);
            }
            _result = new MatchResultSnapshot(_localMatchId, _roomId, result);
            return _result;
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            string trimmed = name.Trim();
            if (trimmed.Length <= 64) return trimmed;
            int length = 64;
            if (char.IsHighSurrogate(trimmed[length - 1]) && char.IsLowSurrogate(trimmed[length])) length--;
            return trimmed.Substring(0, length);
        }

        private static void Increment(Dictionary<string, int> values, string id)
        { values.TryGetValue(id, out int count); values[id] = count + 1; }
    }
}
