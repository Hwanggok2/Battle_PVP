using System;
using BattlePvp.Combat;

namespace BattlePvp.UI
{
    /// <summary>표시를 잠시 중단해도 다시 시작하지 않는 로컬 사망 대기 시각.</summary>
    public readonly struct PlayerRespawnCountdown
    {
        public bool HasStarted { get; }
        public double ReadyAt { get; }

        public PlayerRespawnCountdown(double startedAt)
        {
            HasStarted = double.IsFinite(startedAt);
            ReadyAt = startedAt + HealthUiLifeRules.RespawnDelaySeconds;
        }

        public static PlayerRespawnCountdown FromServerDeadline(double localNow, double serverNow, double readyAt,
            double receivedServerTime = double.NaN)
        {
            if (!double.IsFinite(localNow) || !double.IsFinite(serverNow) || !double.IsFinite(readyAt)) return default;
            // Reliable spawn/RPC can precede the first unreliable Mirror time snapshot after reconnect.
            if (double.IsFinite(receivedServerTime)) serverNow = Math.Max(serverNow, receivedServerTime);
            double remaining = Math.Clamp(readyAt - serverNow, 0d, HealthUiLifeRules.RespawnDelaySeconds);
            return new PlayerRespawnCountdown(localNow + remaining - HealthUiLifeRules.RespawnDelaySeconds);
        }

        public bool IsReady(double now) => HasStarted && double.IsFinite(now) && now >= ReadyAt;

        public int SecondsRemaining(double now) => !HasStarted || !double.IsFinite(now)
            ? (int)HealthUiLifeRules.RespawnDelaySeconds
            : (int)Math.Clamp(Math.Ceiling(ReadyAt - now), 0d, HealthUiLifeRules.RespawnDelaySeconds);
    }
}
