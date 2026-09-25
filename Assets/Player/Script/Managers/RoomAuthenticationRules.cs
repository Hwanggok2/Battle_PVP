using System;
using System.Collections.Generic;

namespace BattlePvp.Networking
{
    public sealed class AuthenticatedRoomPlayer
    {
        public string PlayFabId { get; }
        public string RoomId { get; }

        public AuthenticatedRoomPlayer(string playFabId, string roomId)
        {
            if (!RoomAuthenticationRules.TryNormalizeAccountId(playFabId, out string normalized) || !RoomIdentity.IsValid(roomId))
                throw new ArgumentException("A valid authenticated room identity is required.");
            PlayFabId = normalized;
            RoomId = roomId;
        }
    }

    public static class RoomAuthenticationRules
    {
        public const double TimeoutSeconds = 30d;
        public const int MaximumAccountIdLength = 64;

        public static bool TryNormalizeAccountId(string value, out string normalized)
        {
            normalized = null;
            return value != null && value.Length <= MaximumAccountIdLength &&
                RoomIdentity.TryNormalizePlayerId(value, out normalized);
        }

        public static bool IsChallenge(string value) => value != null && value.Length == 32 &&
            Guid.TryParseExact(value, "N", out Guid parsed) && parsed != Guid.Empty &&
            value == parsed.ToString("N");

        public static bool IsBeforeDeadline(double now, double deadline) =>
            double.IsFinite(now) && double.IsFinite(deadline) && now < deadline;

        public static bool IsLocalOwner(string roomId, string playerId) =>
            TryNormalizeAccountId(playerId, out string normalized) &&
            RoomIdentity.TryGetOwner(roomId, out string owner) && owner == normalized;

        public static bool MatchesProof(string roomId, string challenge, string playerId,
            string actualRoomId, string actualChallenge, string actualPlayerId) =>
            RoomIdentity.IsValid(roomId) && IsChallenge(challenge) &&
            TryNormalizeAccountId(playerId, out string normalized) &&
            TryNormalizeAccountId(actualPlayerId, out string actualNormalized) &&
            roomId == actualRoomId && challenge == actualChallenge && normalized == actualNormalized;
    }

    /// <summary>Only verified accounts may reserve a connection. Entries survive acceptance until disconnect.</summary>
    public sealed class RoomAuthenticationReservations<T> where T : class
    {
        private readonly Dictionary<string, T> _connections = new Dictionary<string, T>(StringComparer.Ordinal);
        public int Count => _connections.Count;

        public bool TryReserve(string playerId, T connection)
        {
            if (connection == null || !RoomAuthenticationRules.TryNormalizeAccountId(playerId, out string normalized)) return false;
            if (_connections.TryGetValue(normalized, out T existing)) return ReferenceEquals(existing, connection);
            _connections.Add(normalized, connection);
            return true;
        }

        public void Release(string playerId, T connection)
        {
            if (RoomAuthenticationRules.TryNormalizeAccountId(playerId, out string normalized) &&
                _connections.TryGetValue(normalized, out T existing) && ReferenceEquals(existing, connection))
                _connections.Remove(normalized);
        }

        public void Clear() => _connections.Clear();
    }
}
