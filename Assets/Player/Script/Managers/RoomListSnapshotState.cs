using System;

namespace BattlePvp.Networking
{
    /// <summary>List freshness and mutation ordering; neither controls the lifetime of a joined room.</summary>
    public sealed class RoomListSnapshotState
    {
        public const double CacheSeconds = 3d;
        public ulong Revision { get; private set; }
        private double _completedAt = double.NegativeInfinity;

        public bool CanReuse(double now) => double.IsFinite(now) && now >= _completedAt && now - _completedAt <= CacheSeconds;

        public bool TryComplete(ulong requestRevision, double now, bool discardUnverified = false)
        {
            // A failed read must still hide all unverified rooms after an intervening mutation.
            if ((!discardUnverified && requestRevision != Revision) || !double.IsFinite(now)) return false;
            _completedAt = now;
            return true;
        }

        public void Invalidate()
        {
            unchecked { Revision++; }
            _completedAt = double.NegativeInfinity;
        }
    }
}
