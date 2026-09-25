using System;

namespace BattlePvp.Networking
{
    /// <summary>방 이름공간 형식만 검사한다. 소유 권한은 CloudScript의 인증된 호출자로 확인한다.</summary>
    public static class RoomIdentity
    {
        private const string Prefix = "battle_";

        public static bool TryCreate(string playFabId, Guid nonce, out string roomId)
        {
            roomId = null;
            if (nonce == Guid.Empty || !TryNormalizePlayerId(playFabId, out string owner)) return false;
            roomId = Prefix + owner + "_" + nonce.ToString("N");
            return true;
        }

        public static bool IsValid(string roomId) => TryGetOwner(roomId, out _);

        public static bool TryNormalizePlayerId(string playFabId, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(playFabId)) return false;
            string value = playFabId.ToLowerInvariant();
            if (!IsLowerHex(value, 0, value.Length)) return false;
            normalized = value;
            return true;
        }

        public static bool TryGetOwner(string roomId, out string owner)
        {
            owner = null;
            if (roomId == null || !roomId.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            int separator = roomId.LastIndexOf('_');
            int ownerLength = separator - Prefix.Length;
            if (ownerLength <= 0 || roomId.Length - separator - 1 != 32 ||
                !IsLowerHex(roomId, Prefix.Length, ownerLength) ||
                !IsLowerHex(roomId, separator + 1, 32)) return false;
            owner = roomId.Substring(Prefix.Length, ownerLength);
            return true;
        }

        private static bool IsLowerHex(string value, int start, int count)
        {
            if (count <= 0) return false;
            for (int i = start; i < start + count; i++)
            {
                char c = value[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            }
            return true;
        }
    }
}
