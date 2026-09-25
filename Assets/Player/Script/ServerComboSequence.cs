using System;

namespace BattlePvp.Combat
{
    /// <summary>Only the server's completed attack can grant a short delayed continuation.</summary>
    public sealed class ServerComboSequence
    {
        private int _nextIndex = -1;
        private double _until;

        public bool CanStart(int requested, bool attacking, int current, float normalizedTime, double now)
        {
            if (requested < 0 || !double.IsFinite(now)) return false;
            if (attacking)
                return float.IsFinite(normalizedTime) && normalizedTime >= 0.8f && requested == current + 1;
            return requested == 0 || (requested == _nextIndex && now <= _until);
        }

        public void Complete(int index, int count, double now)
        {
            _nextIndex = index + 1 < count ? index + 1 : -1;
            _until = now + 0.35d;
        }

        public void Reset() { _nextIndex = -1; _until = 0d; }
    }
}
