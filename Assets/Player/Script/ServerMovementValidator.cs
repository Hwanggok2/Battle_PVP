using System;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Server-clock movement budget. Packet count never grants extra distance.</summary>
    public sealed class ServerMovementValidator
    {
        public Vector3 Position { get; private set; }
        public double LastSampleTime { get; private set; }
        public double LastPositionTime { get; private set; }
        private double _budgetTime;
        private float _credit;
        private MovementFlightState _flight;
        private bool _airJumpUsed;
        private Vector3 _forcedDirection;
        private float _forcedRemaining;
        private double _forcedUntil;
        private float _verticalVelocity;

        public void Reset(Vector3 position, double now)
        {
            Position = position;
            LastSampleTime = now - 0.5d;
            LastPositionTime = now;
            _budgetTime = now;
            _credit = 0.5f;
            _flight = new MovementFlightState(position.y, now, true, 0f);
            _forcedRemaining = 0f;
            _verticalVelocity = 0f;
            _airJumpUsed = false;
        }

        public void AuthorizeForcedMove(Vector3 direction, float distance, float duration, double now)
        {
            if (!CombatValidation.IsFinite(direction) || !float.IsFinite(distance) ||
                !float.IsFinite(duration) || distance <= 0f || duration <= 0f)
                return;
            direction.y = 0f;
            _forcedDirection = direction.normalized;
            _forcedRemaining = distance;
            _forcedUntil = now + duration + 0.5d;
        }

        /// <summary>Ownership handoff rebases packet credit without granting a new midair jump.</summary>
        public void Rebase(Vector3 position, double now, bool grounded)
        {
            Position = position;
            LastSampleTime = now - 0.5d;
            LastPositionTime = now;
            _budgetTime = now;
            _credit = 0.5f;
            _forcedRemaining = 0f;
            _flight = _flight.Rebase(position.y, now, grounded);
            if (grounded) _airJumpUsed = false;
        }

        public bool TryAccept(Vector3 position, Quaternion rotation, double sampleTime, double now,
            float speed, float jumpHeight, float gravity, bool grounded, float maxSlopeDegrees,
            bool movementLocked = false, bool jumpLocked = false, double jumpLockedSince = double.NegativeInfinity,
            float distanceBeforeMoveLock = 0f)
        {
            if (!CombatValidation.IsFinite(position) || !IsValidRotation(rotation) ||
                !double.IsFinite(sampleTime) || !double.IsFinite(now) ||
                sampleTime <= LastSampleTime || sampleTime < now - 0.5d || sampleTime > now + 0.05d ||
                !float.IsFinite(speed) || speed < 0f || !float.IsFinite(distanceBeforeMoveLock) || distanceBeforeMoveLock < 0f)
                return false;

            Vector3 horizontal = position - Position;
            horizontal.y = 0f;
            float credit = Mathf.Min(_credit + speed * (float)Math.Max(0d, now - _budgetTime),
                speed * 0.5f + 0.5f);
            if (movementLocked) credit = Mathf.Min(_credit, 0.05f) + distanceBeforeMoveLock;
            float forcedUsed = now <= _forcedUntil
                ? Mathf.Clamp(Vector3.Dot(horizontal, _forcedDirection), 0f, _forcedRemaining)
                : 0f;
            float chargedDistance = (horizontal - _forcedDirection * forcedUsed).magnitude;
            if (chargedDistance > credit + 0.0001f) return false;

            if (!_flight.TryAccept(Position.y, position.y, horizontal.magnitude, LastSampleTime, sampleTime,
                    jumpHeight, gravity, grounded, maxSlopeDegrees, jumpLocked, jumpLockedSince, out MovementFlightState flight)) return false;

            double verticalElapsed = sampleTime - LastPositionTime;
            if (verticalElapsed > 0.0001d)
                _verticalVelocity = Mathf.Clamp((position.y - Position.y) / (float)verticalElapsed, -50f, 50f);
            Position = position;
            LastSampleTime = sampleTime;
            LastPositionTime = Math.Max(LastPositionTime, sampleTime);
            _credit = Mathf.Max(0f, credit - chargedDistance);
            _budgetTime = now;
            _forcedRemaining -= forcedUsed;
            _flight = flight;
            if (grounded) _airJumpUsed = false;
            return true;
        }

        public bool TryBeginAirJump(float height, double now)
        {
            if (_airJumpUsed || _flight.WasGrounded || !float.IsFinite(height) || height <= 0 ||
                !double.IsFinite(now) || now < LastSampleTime) return false;
            _airJumpUsed = true;
            _flight = new MovementFlightState(Position.y, now, false, height);
            return true;
        }

        public float GetServerVerticalVelocity(float gravity)
        {
            if (_flight.WasGrounded) return -0.5f;
            float heightRemaining = Mathf.Max(0f, _flight.GroundHeight + _flight.JumpBudget - Position.y);
            float maximumRiseSpeed = Mathf.Sqrt(2f * Mathf.Max(0.1f, gravity) * heightRemaining);
            return Mathf.Min(_verticalVelocity, maximumRiseSpeed);
        }

        public void BeginServerJump(float jumpHeight, double now)
        {
            if (!float.IsFinite(jumpHeight) || jumpHeight < 0f || !double.IsFinite(now)) return;
            _flight = new MovementFlightState(Position.y, now, false, Mathf.Max(0f, jumpHeight));
        }

        // Only CharacterController results from the trusted server enter this path.
        public void CommitServerMove(Vector3 position, double now, bool grounded, float verticalVelocity)
        {
            if (!CombatValidation.IsFinite(position) || !double.IsFinite(now) || !float.IsFinite(verticalVelocity)) return;
            Position = position;
            LastSampleTime = LastPositionTime = _budgetTime = now;
            _credit = 0.05f;
            _forcedRemaining = 0f;
            _verticalVelocity = verticalVelocity;
            _flight = _flight.Rebase(position.y, now, grounded);
            if (grounded) _airJumpUsed = false;
        }

        public static bool IsValidRotation(Quaternion rotation)
        {
            if (!float.IsFinite(rotation.x) || !float.IsFinite(rotation.y) ||
                !float.IsFinite(rotation.z) || !float.IsFinite(rotation.w)) return false;
            float norm = rotation.x * rotation.x + rotation.y * rotation.y +
                         rotation.z * rotation.z + rotation.w * rotation.w;
            return norm >= 0.5f && norm <= 1.5f;
        }
    }
}
