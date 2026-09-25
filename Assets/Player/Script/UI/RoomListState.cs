using System;
using System.Collections.Generic;
using BattlePvp.Networking;

namespace BattlePvp.UI
{
    // Owns request lifetime and row identity independently of the Unity hierarchy.
    public sealed class RoomListState
    {
        private readonly Dictionary<string, PlayFabBattleManager.RoomInfo> _rooms =
            new Dictionary<string, PlayFabBattleManager.RoomInfo>(StringComparer.Ordinal);
        private readonly Comparison<string> _compare;
        private uint _request;
        private bool _active;
        public IReadOnlyDictionary<string, PlayFabBattleManager.RoomInfo> Rooms => _rooms;

        public RoomListState() { _compare = CompareRooms; }

        public void SetActive(bool active)
        {
            _active = active;
            unchecked { _request++; }
        }

        public uint BeginRequest() { unchecked { return ++_request; } }

        public bool Apply(uint request, IReadOnlyDictionary<string, PlayFabBattleManager.RoomInfo> rooms,
            List<string> removed, List<string> changed)
        {
            removed.Clear();
            changed.Clear();
            if (!_active || request != _request || rooms == null) return false;
            foreach (string id in _rooms.Keys)
                if (!rooms.ContainsKey(id)) removed.Add(id);
            foreach (string id in removed) _rooms.Remove(id);
            foreach (var entry in rooms)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                if (!_rooms.TryGetValue(entry.Key, out var previous) || !SameDisplay(previous, entry.Value))
                    changed.Add(entry.Key);
                _rooms[entry.Key] = entry.Value;
            }
            return true;
        }

        public void CopyOrderedIds(List<string> destination)
        {
            destination.Clear();
            destination.AddRange(_rooms.Keys);
            destination.Sort(_compare);
        }

        private int CompareRooms(string left, string right)
        {
            int title = string.CompareOrdinal(_rooms[left].RoomName, _rooms[right].RoomName);
            return title != 0 ? title : string.CompareOrdinal(left, right);
        }

        private static bool SameDisplay(PlayFabBattleManager.RoomInfo left, PlayFabBattleManager.RoomInfo right) =>
            left.RoomName == right.RoomName && left.MasterName == right.MasterName &&
            left.PlayerCount == right.PlayerCount;
    }
}
