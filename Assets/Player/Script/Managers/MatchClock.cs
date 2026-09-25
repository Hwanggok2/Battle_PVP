using System;

namespace BattlePvp.Networking
{
    /// <summary>프레임 수나 timeScale에 영향받지 않는 서버 시각 기준 경기 기한.</summary>
    public sealed class MatchClock
    {
        public double EndsAt { get; private set; }
        public bool IsRunning { get; private set; }

        public void Start(double now, double duration)
        {
            if (!double.IsFinite(now) || !double.IsFinite(duration) || duration < 0d ||
                !double.IsFinite(now + duration)) throw new ArgumentOutOfRangeException(nameof(duration));
            EndsAt = now + duration;
            IsRunning = true;
        }

        public double Remaining(double now) => IsRunning && double.IsFinite(now)
            ? Math.Max(0d, EndsAt - now) : 0d;

        public bool TryFinish(double now)
        {
            if (!IsRunning || !double.IsFinite(now) || now < EndsAt) return false;
            IsRunning = false;
            return true;
        }

        public void Stop() => IsRunning = false;
    }
}
