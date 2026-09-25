using System;
using System.Collections.Generic;

namespace BattlePvp.Networking
{
    public sealed class ProfileWriteLease
    {
        public string AccountKey { get; }
        internal ProfileWriteLease(string accountKey) { AccountKey = accountKey; }
    }

    /// <summary>
    /// Owns actual writes independently of a repository or its UI callbacks. Single-threaded,
    /// like the PlayFab Unity callback path. An unknown outcome must never expire this lease.
    /// </summary>
    public sealed class ProfileWriteCoordinator
    {
        private readonly Dictionary<string, ProfileWriteLease> _pending =
            new Dictionary<string, ProfileWriteLease>(StringComparer.OrdinalIgnoreCase);
        public int PendingAccounts => _pending.Count;

        public bool IsBlocked(string accountKey, ProfileWriteLease ownedLease = null) =>
            _pending.TryGetValue(accountKey ?? string.Empty, out ProfileWriteLease pending) &&
            !ReferenceEquals(pending, ownedLease);

        public bool TryAcquire(string accountKey, out ProfileWriteLease lease)
        {
            accountKey ??= string.Empty;
            if (_pending.ContainsKey(accountKey)) { lease = null; return false; }
            lease = new ProfileWriteLease(accountKey);
            _pending.Add(accountKey, lease);
            return true;
        }

        public bool Confirm(ProfileWriteLease lease)
        {
            if (lease == null || !_pending.TryGetValue(lease.AccountKey, out ProfileWriteLease actual) ||
                !ReferenceEquals(actual, lease)) return false;
            _pending.Remove(lease.AccountKey);
            return true;
        }
    }
}
