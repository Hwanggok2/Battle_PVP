using System;

namespace BattlePvp.Combat
{
    public readonly struct MovementControlState
    {
        public readonly float Speed;
        public readonly bool MoveLocked;
        public readonly bool JumpLocked;
        public readonly bool CrouchLocked;
        public readonly double JumpLockedSince;
        public readonly double MoveLockedSince;
        public readonly float SpeedBeforeMoveLock;

        public MovementControlState(float speed, bool moveLocked, bool jumpLocked, bool crouchLocked,
            double jumpLockedSince = double.NegativeInfinity, double moveLockedSince = double.NegativeInfinity,
            float speedBeforeMoveLock = 0f)
        {
            Speed = speed; MoveLocked = moveLocked; JumpLocked = jumpLocked; CrouchLocked = crouchLocked;
            JumpLockedSince = jumpLockedSince; MoveLockedSince = moveLockedSince; SpeedBeforeMoveLock = speedBeforeMoveLock;
        }

        public float DistanceBeforeMoveLock(double previousSample, double sampleTime)
        {
            if (!MoveLocked || !double.IsFinite(previousSample) || !double.IsFinite(sampleTime) ||
                !double.IsFinite(MoveLockedSince) || !float.IsFinite(SpeedBeforeMoveLock)) return 0f;
            double elapsed = Math.Clamp(Math.Min(sampleTime, MoveLockedSince) - previousSample, 0d, 0.5d);
            return Math.Max(0f, SpeedBeforeMoveLock) * (float)elapsed;
        }

        internal bool HasSameControls(MovementControlState other) => Speed == other.Speed &&
            MoveLocked == other.MoveLocked && JumpLocked == other.JumpLocked && CrouchLocked == other.CrouchLocked;
    }

    /// <summary>Samples server-owned controls at the packet's time, not at its later arrival time.</summary>
    public sealed class MovementControlHistory
    {
        private readonly struct Entry
        {
            public readonly double Time;
            public readonly MovementControlState State;
            public Entry(double time, MovementControlState state) { Time = time; State = state; }
        }
        private readonly FixedRingBuffer<Entry> _entries = new FixedRingBuffer<Entry>(64);
        private bool _discardedOldest;

        public void Clear() { _entries.Clear(); _discardedOldest = false; }

        public void Record(double now, MovementControlState state)
        {
            if (!double.IsFinite(now) || !float.IsFinite(state.Speed) || state.Speed < 0f) return;
            double jumpStarted = state.JumpLocked ? now : double.NegativeInfinity;
            double moveStarted = state.MoveLocked ? now : double.NegativeInfinity;
            float previousSpeed = 0f;
            if (_entries.Count > 0)
            {
                Entry previous = _entries[_entries.Count - 1];
                if (now < previous.Time || state.HasSameControls(previous.State)) return;
                if (state.JumpLocked && previous.State.JumpLocked) jumpStarted = previous.State.JumpLockedSince;
                if (state.MoveLocked)
                {
                    moveStarted = previous.State.MoveLocked ? previous.State.MoveLockedSince : now;
                    previousSpeed = previous.State.MoveLocked ? previous.State.SpeedBeforeMoveLock : previous.State.Speed;
                }
            }
            if (_entries.Count == _entries.Capacity) _discardedOldest = true;
            _entries.Add(new Entry(now, new MovementControlState(state.Speed, state.MoveLocked,
                state.JumpLocked, state.CrouchLocked, jumpStarted, moveStarted, previousSpeed)));
        }

        public bool TrySample(double sampleTime, out MovementControlState state)
        {
            state = default;
            if (!double.IsFinite(sampleTime) || _entries.Count == 0 ||
                (_discardedOldest && sampleTime < _entries[0].Time)) return false;
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (sampleTime >= _entries[i].Time) { state = _entries[i].State; return true; }
            state = _entries[0].State;
            return true;
        }
    }

    /// <summary>Vertical flight budget survives a lock or owner change; only a new launch needs permission.</summary>
    public readonly struct MovementFlightState
    {
        public readonly float GroundHeight;
        public readonly double GroundTime;
        public readonly bool WasGrounded;
        public readonly float JumpBudget;

        public MovementFlightState(float height, double time, bool grounded, float jumpBudget)
        { GroundHeight = height; GroundTime = time; WasGrounded = grounded; JumpBudget = jumpBudget; }

        public MovementFlightState Rebase(float height, double now, bool grounded) => grounded
            ? new MovementFlightState(height, now, true, 0f) : new MovementFlightState(GroundHeight, GroundTime, false, JumpBudget);

        public bool TryAccept(float previousY, float nextY, float horizontalDistance, double previousSample,
            double sampleTime, float jumpHeight, float gravity, bool grounded, float maxSlopeDegrees,
            bool jumpLocked, double jumpLockedSince, out MovementFlightState next)
        {
            next = this;
            if (!float.IsFinite(previousY) || !float.IsFinite(nextY) || !float.IsFinite(horizontalDistance) ||
                !double.IsFinite(previousSample) || !double.IsFinite(sampleTime) || !float.IsFinite(jumpHeight) ||
                !float.IsFinite(gravity) || !float.IsFinite(maxSlopeDegrees)) return false;
            float safeGravity = Math.Max(0.1f, gravity);
            float budget = WasGrounded ? Math.Max(0f, jumpHeight) : JumpBudget;
            bool startingFlight = !grounded && WasGrounded;
            if (startingFlight && jumpLocked && !StartedBeforeLock(previousSample, sampleTime, nextY,
                    budget, safeGravity, jumpLockedSince))
            {
                budget = 0f;
                if (nextY > previousY + 0.05f && nextY > GroundHeight + 0.05f) return false;
            }
            double groundTime = startingFlight
                ? sampleTime - Math.Min(0.1d, Math.Max(0d, sampleTime - previousSample)) : GroundTime;
            float flightTime = Math.Max(0f, (float)(sampleTime - groundTime));
            float descent = Math.Max(0f, flightTime - (float)Math.Sqrt(2f * budget / safeGravity) - 0.3f);
            float allowedHeight = GroundHeight + budget + 0.35f - 0.5f * safeGravity * descent * descent;
            if (!grounded && nextY > allowedHeight) return false;
            float elapsed = Math.Max(0.001f, (float)(sampleTime - previousSample));
            float maximumFall = (float)Math.Sqrt(2f * budget * safeGravity) + safeGravity * (flightTime + 0.5f);
            if (previousY - nextY > maximumFall * elapsed + 0.5f) return false;
            if (grounded && nextY - previousY > horizontalDistance *
                Math.Tan(Math.Clamp(maxSlopeDegrees, 0f, 70f) * Math.PI / 180d) + 0.5f) return false;
            next = grounded ? new MovementFlightState(nextY, sampleTime, true, 0f)
                : new MovementFlightState(GroundHeight, groundTime, false, budget);
            return true;
        }

        private bool StartedBeforeLock(double previousSample, double sampleTime, float height, float jumpHeight,
            float gravity, double lockedSince)
        {
            if (!double.IsFinite(lockedSince) || previousSample >= lockedSince || sampleTime > lockedSince + 0.5d || jumpHeight <= 0f)
                return false;
            float rise = height - GroundHeight;
            if (rise <= 0f || rise > jumpHeight) return false;
            double launchSpeed = Math.Sqrt(2d * jumpHeight * gravity);
            double age = (launchSpeed - Math.Sqrt(Math.Max(0d, launchSpeed * launchSpeed - 2d * gravity * rise))) / gravity;
            double launchTime = sampleTime - age;
            return launchTime >= Math.Max(previousSample - 0.01d, GroundTime - 0.000001d) && launchTime <= lockedSince;
        }
    }

    public static class MovementReconnectTiming
    {
        public static double Remaining(double networkNow, double receivedBatchTime, double until)
        {
            if (!double.IsFinite(networkNow) || !double.IsFinite(until)) return 0d;
            double now = double.IsFinite(receivedBatchTime) ? Math.Max(networkNow, receivedBatchTime) : networkNow;
            return Math.Max(0d, until - now);
        }
    }
}
