using System;

namespace BattlePvp.Combat
{
    /// <summary>Server-owned displacement clock and bounded owner input during temporary movement authority.</summary>
    public sealed class ServerForcedMotion
    {
        public bool IsActive { get; private set; }
        public float DirectionX { get; private set; }
        public float DirectionZ { get; private set; }
        public float Speed { get; private set; }
        public double EndsAt { get; private set; }
        public float InputX { get; private set; }
        public float InputZ { get; private set; }
        private double _steppedUntil;
        private double _lastInputSample = double.NegativeInfinity;
        private double _inputUntil;
        private double _jumpUntil;
        private uint _lastJumpSequence;

        public bool TryBegin(float x, float z, float distance, float duration, double now)
        {
            if (!float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(distance) ||
                !float.IsFinite(duration) || !double.IsFinite(now) || distance <= 0f || duration <= 0f)
                return false;
            double length = Math.Sqrt((double)x * x + (double)z * z);
            float speed = distance / duration;
            if (length < 0.00001d || !float.IsFinite(speed) || !double.IsFinite(now + duration)) return false;
            DirectionX = (float)(x / length);
            DirectionZ = (float)(z / length);
            Speed = speed;
            _steppedUntil = now;
            EndsAt = now + duration;
            IsActive = true;
            ResetOwnerInput();
            return true;
        }

        // Consume the interval once. A hitch does not delete the final part of a short knockback.
        public double TakeStep(double now)
        {
            if (!IsActive || !double.IsFinite(now) || now <= _steppedUntil) return 0d;
            double end = Math.Min(now, EndsAt);
            double step = Math.Max(0d, end - _steppedUntil);
            _steppedUntil = Math.Max(_steppedUntil, end);
            return step;
        }

        public bool HasFinished => IsActive && _steppedUntil >= EndsAt;
        public bool AcceptsOwnerPose(uint epoch, uint currentEpoch) => !IsActive && epoch == currentEpoch;

        public bool TrySetOwnerInput(float x, float z, uint jumpSequence, double sampleTime, double now)
        {
            if (!IsActive || !float.IsFinite(x) || !float.IsFinite(z) ||
                !double.IsFinite(sampleTime) || !double.IsFinite(now) || sampleTime <= _lastInputSample ||
                sampleTime < now - 0.5d || sampleTime > now + 0.05d || (double)x * x + (double)z * z > 1.1025d)
                return false;
            double length = Math.Max(1d, Math.Sqrt((double)x * x + (double)z * z));
            InputX = (float)(x / length);
            InputZ = (float)(z / length);
            _lastInputSample = sampleTime;
            _inputUntil = now + 0.25d;
            if (jumpSequence != 0u && CombatRequestSequences.IsNewer(jumpSequence, _lastJumpSequence))
            {
                _lastJumpSequence = jumpSequence;
                _jumpUntil = now + 0.15d;
            }
            return true;
        }

        public bool HasInput(double now) => double.IsFinite(now) && now < _inputUntil;
        public bool TryConsumeJump(double now, bool permittedAndGrounded)
        {
            if (!permittedAndGrounded || !double.IsFinite(now) || now >= _jumpUntil) return false;
            _jumpUntil = 0d;
            return true;
        }

        public void ResetOwnerInput()
        {
            InputX = InputZ = 0f;
            _lastInputSample = double.NegativeInfinity;
            _inputUntil = _jumpUntil = 0d;
            _lastJumpSequence = 0u;
        }

        public void Cancel()
        {
            IsActive = false;
            ResetOwnerInput();
        }
    }
}
