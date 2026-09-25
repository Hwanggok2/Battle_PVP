using System;

namespace BattlePvp.Networking
{
    public enum AuthenticationOperation { Login, Register }
    public enum AuthenticationStage { Response, Profile, Succeeded, Failed, Cancelled }

    public sealed class AuthenticationAttempt
    {
        public long Id { get; }
        public AuthenticationOperation Operation { get; }
        public string Username { get; }
        public AuthenticationStage Stage { get; internal set; }
        public double Deadline { get; internal set; }
        internal AuthenticationAttempt(long id, AuthenticationOperation operation, string username, double deadline)
        { Id = id; Operation = operation; Username = username; Deadline = deadline; Stage = AuthenticationStage.Response; }
    }

    // Credentials stay outside this state machine. Every SDK request owns a separate context.
    public sealed class AuthenticationAttemptState
    {
        public const double ResponseTimeoutSeconds = 15d;
        public const double ProfileTimeoutSeconds = 16d;
        private long _sequence;
        public AuthenticationAttempt Current { get; private set; }
        public bool IsBusy => Current != null &&
            (Current.Stage == AuthenticationStage.Response || Current.Stage == AuthenticationStage.Profile);

        public AuthenticationAttempt Begin(AuthenticationOperation operation, string username, double now)
        {
            if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            username = username?.Trim() ?? string.Empty;
            if (IsBusy && now < Current.Deadline && Current.Operation == operation && Current.Username == username) return null;
            return Current = new AuthenticationAttempt(++_sequence, operation, username, now + ResponseTimeoutSeconds);
        }

        public bool IsCurrent(AuthenticationAttempt attempt) => attempt != null && ReferenceEquals(Current, attempt);

        public bool ResolveResponse(AuthenticationAttempt attempt, bool success, double now)
        {
            if (!CanResolve(attempt, AuthenticationStage.Response, now)) return false;
            attempt.Stage = success ? (attempt.Operation == AuthenticationOperation.Login ? AuthenticationStage.Profile : AuthenticationStage.Succeeded)
                : AuthenticationStage.Failed;
            if (attempt.Stage == AuthenticationStage.Profile) attempt.Deadline = now + ProfileTimeoutSeconds;
            return true;
        }

        public bool ResolveProfile(AuthenticationAttempt attempt, bool sameSession, double now)
        {
            if (!CanResolve(attempt, AuthenticationStage.Profile, now)) return false;
            // A retryable data-load failure does not invalidate successful authentication.
            attempt.Stage = sameSession ? AuthenticationStage.Succeeded : AuthenticationStage.Failed;
            return true;
        }

        public AuthenticationAttempt Expire(double now, bool sameProfileSession)
        {
            if (!IsBusy || !double.IsFinite(now) || now < Current.Deadline) return null;
            Current.Stage = Current.Stage == AuthenticationStage.Profile && sameProfileSession
                ? AuthenticationStage.Succeeded : AuthenticationStage.Failed;
            return Current;
        }

        public void Cancel()
        {
            if (Current != null) Current.Stage = AuthenticationStage.Cancelled;
        }

        private bool CanResolve(AuthenticationAttempt attempt, AuthenticationStage stage, double now) =>
            IsCurrent(attempt) && attempt.Stage == stage && double.IsFinite(now) && now < attempt.Deadline;
    }

    public sealed class LoginNavigationGate
    {
        private bool _active;
        private bool _consumed;
        private long _request;
        private double _deadline;
        public bool IsPending => _request != 0 && !_consumed;
        public void Activate() { _active = true; _consumed = false; _request = 0; }
        public void Deactivate() { _active = false; _request = 0; }
        public bool Schedule(long request, double now)
        {
            if (!_active || _consumed || IsPending || request <= 0 || !double.IsFinite(now)) return false;
            _request = request;
            _deadline = now + 1.2d;
            return true;
        }
        public bool TryConsume(long approvedRequest, double now)
        {
            if (!_active || !IsPending || !double.IsFinite(now)) return false;
            if (approvedRequest != _request) { _request = 0; return false; }
            if (now < _deadline) return false;
            _consumed = true;
            return true;
        }
    }
}
