using System;

namespace BattlePvp.Networking
{
    /// <summary>서버 유효 기한을 요청 시작 시각 기준으로 보수적으로 환산하고 호스트 갱신 간격을 관리한다.</summary>
    public sealed class HostRoomLease
    {
        public const double LifetimeSeconds = 60d;
        public const double HeartbeatSeconds = 15d;
        public const double RetrySeconds = 5d;
        private bool _started;
        private bool _confirmed;
        private bool _stopped;
        private bool _requestPending;
        private double _expiresAt;
        private double _nextRequestAt;

        public void BeginRegistration(double now)
        {
            if (_started || _stopped) return;
            _started = true;
            _expiresAt = Finite(now) ? now + LifetimeSeconds : 0d;
        }

        public static bool TryGetDeadline(double requestStarted, double serverNowMs, double expiresAtMs, out double deadline)
        {
            deadline = 0d;
            double remaining = (expiresAtMs - serverNowMs) / 1000d;
            if (!Finite(requestStarted) || !Finite(serverNowMs) || serverNowMs <= 0d || !Finite(expiresAtMs) ||
                remaining <= 0d || remaining > LifetimeSeconds) return false;
            deadline = requestStarted + remaining;
            return Finite(deadline);
        }

        public bool Accept(double requestStarted, double receivedAt, double serverNowMs, double expiresAtMs)
        {
            if (_stopped || !Finite(receivedAt) || receivedAt < requestStarted || HasExpired(receivedAt) ||
                !TryGetDeadline(requestStarted, serverNowMs, expiresAtMs, out double deadline) || deadline <= receivedAt)
                return false;
            _started = true;
            _confirmed = true;
            _requestPending = false;
            _expiresAt = deadline;
            _nextRequestAt = requestStarted + HeartbeatSeconds;
            return true;
        }

        public bool HasExpired(double now) => _started && (now >= _expiresAt || !Finite(now));

        public bool TryBeginHeartbeat(double now)
        {
            if (!_confirmed || _stopped || _requestPending || !Finite(now) || HasExpired(now) || now < _nextRequestAt)
                return false;
            _requestPending = true;
            return true;
        }

        public void Failed(double now)
        {
            if (!Finite(now)) { Stop(); return; }
            _requestPending = false;
            _nextRequestAt = now + RetrySeconds;
        }

        public void Stop() { _stopped = true; _requestPending = false; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
