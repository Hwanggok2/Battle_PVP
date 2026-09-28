using System;
using UnityEngine;

namespace BattlePvp.Combat
{
    public static class CombatEffectSources
    {
        public const int BowCharge = 101;
        public const int WeaponSwap = 102;
        public const int StrategistMove = 103;
        public const int KickSlow = 104;
        public const int SkillMoveSlow = 105;
        public const int LegacySkillInput = 201;
        public const int EmoteInput = 202;
        public const int AdvancedSkillInput = 203;
        public const int ServerCastMovement = 204;
        public const int PredictedCastMovement = 205;
        public const int LegacySkillMovement = 206;
        public const int AuthoritativeSkillInput = 207;
    }

    public static class CombatValidation
    {
        private static readonly CombatPhysicsQuery _lineOfSightQuery = new CombatPhysicsQuery();
        public static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        public static bool ContainsPoint(Vector3 center, Vector3 halfExtents, Quaternion rotation,
            Vector3 point, float tolerance)
        {
            if (!IsFinite(point) || !IsFinite(center) || !IsFinite(halfExtents))
                return false;
            Vector3 local = Quaternion.Inverse(rotation) * (point - center);
            tolerance = Mathf.Clamp(tolerance, 0f, 0.35f);
            return Mathf.Abs(local.x) <= halfExtents.x + tolerance &&
                   Mathf.Abs(local.y) <= halfExtents.y + tolerance &&
                   Mathf.Abs(local.z) <= halfExtents.z + tolerance;
        }

        public static bool HasClearPath(Vector3 origin, Vector3 point, Transform attacker, Transform target)
        {
            Vector3 direction = point - origin;
            float distance = direction.magnitude;
            if (!IsFinite(origin) || !IsFinite(point)) return false;
            if (distance <= 0.01f) return true;
            int count = _lineOfSightQuery.Raycast(origin, direction / distance, distance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _lineOfSightQuery.Hits[i];
                Transform root = hit.transform.root;
                if (root != attacker.root && root != target.root)
                    return false;
            }
            return true;
        }
    }

    /// <summary>Server clock owns charge and the single-use permission to spawn one arrow.</summary>
    public sealed class BowShotAuthority
    {
        private double _chargeStartedAt = double.NegativeInfinity;
        private double _shotExpiresAt = double.NegativeInfinity;
        private double _nextChargeAt = double.NegativeInfinity;
        private float _shotMultiplier;
        public bool IsCharging => !double.IsNegativeInfinity(_chargeStartedAt);
        public bool HasPendingShot(double now) => double.IsFinite(now) && now <= _shotExpiresAt && _shotMultiplier > 0f;

        public bool TryBegin(double now)
        {
            if (!double.IsFinite(now) || IsCharging || now < _nextChargeAt || now <= _shotExpiresAt)
                return false;
            _chargeStartedAt = now;
            return true;
        }

        public bool TryRelease(double now, float minimumCharge, float maximumCharge,
            float minimumDamage, float maximumDamage, float releaseLockSeconds, float pendingAnimationSeconds = 0f)
        {
            if (!double.IsFinite(now) || !IsCharging || now < _chargeStartedAt ||
                !float.IsFinite(minimumCharge) || !float.IsFinite(maximumCharge) ||
                !float.IsFinite(minimumDamage) || !float.IsFinite(maximumDamage) ||
                !float.IsFinite(releaseLockSeconds) || !float.IsFinite(pendingAnimationSeconds))
                return false;
            float elapsed = (float)(now - _chargeStartedAt);
            float progress = Mathf.Clamp01((elapsed - Mathf.Max(0f, minimumCharge)) /
                Mathf.Max(0.001f, maximumCharge - minimumCharge));
            _shotMultiplier = Mathf.Lerp(Mathf.Max(0f, minimumDamage), Mathf.Max(minimumDamage, maximumDamage), progress);
            _chargeStartedAt = double.NegativeInfinity;
            _nextChargeAt = now + Mathf.Max(0.1f, releaseLockSeconds);
            // The server supplies the authored draw duration, never the client. Damage was frozen above.
            _shotExpiresAt = now + Mathf.Clamp(pendingAnimationSeconds, 0f, 5f) + Mathf.Max(1f, releaseLockSeconds + 0.5f);
            return true;
        }

        public bool TryConsume(double now, out float multiplier)
        {
            multiplier = 0f;
            if (!double.IsFinite(now) || now > _shotExpiresAt || _shotMultiplier <= 0f)
                return false;
            multiplier = _shotMultiplier;
            _shotMultiplier = 0f;
            _shotExpiresAt = double.NegativeInfinity;
            return true;
        }

        public void Cancel()
        {
            _chargeStartedAt = double.NegativeInfinity;
            _shotExpiresAt = double.NegativeInfinity;
            _shotMultiplier = 0f;
        }
    }

    public sealed class CombatHitPoseHistory
    {
        private const int Capacity = 256;
        private readonly double[] _times = new double[Capacity];
        private readonly Vector3[] _centers = new Vector3[Capacity];
        private readonly Vector3[] _halfExtents = new Vector3[Capacity];
        private readonly Quaternion[] _rotations = new Quaternion[Capacity];
        private int _count;
        private int _next;

        public void Clear() { _count = 0; _next = 0; }

        public void Record(double time, Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            if (!double.IsFinite(time) || !CombatValidation.IsFinite(center)) return;
            _times[_next] = time;
            _centers[_next] = center;
            _halfExtents[_next] = halfExtents;
            _rotations[_next] = rotation;
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }

        public bool Contains(double time, Vector3 point, double timeTolerance = 0.12d)
        {
            if (!double.IsFinite(time) || !CombatValidation.IsFinite(point)) return false;
            for (int i = 0; i < _count; i++)
                if (Math.Abs(_times[i] - time) <= Math.Clamp(timeTolerance, 0d, 0.3d) &&
                    CombatValidation.ContainsPoint(_centers[i], _halfExtents[i], _rotations[i], point, 0.2f))
                    return true;
            return false;
        }
    }

    public sealed class KickHitWindowAuthority
    {
        private uint _sequence;
        private double _startsAt;
        private double _endsAt;
        private bool _opened;
        private bool _active;
        public uint Sequence => _sequence;
        public double EndsAt => _endsAt;

        public void Begin(uint sequence, double startsAt, double endsAt)
        {
            Cancel();
            if (sequence == 0 || !double.IsFinite(startsAt) || !double.IsFinite(endsAt) || endsAt < startsAt)
                return;
            _sequence = sequence;
            _startsAt = startsAt;
            _endsAt = endsAt;
        }

        public bool TryOpen(uint sequence, double now)
        {
            if (_opened || !IsWithinCast(sequence, now)) return false;
            _opened = true;
            _active = true;
            return true;
        }

        public bool CanHit(uint sequence, double now) => _active && IsWithinCast(sequence, now);
        private bool IsWithinCast(uint sequence, double now) => sequence != 0 && sequence == _sequence &&
            double.IsFinite(now) && now >= _startsAt && now <= _endsAt;
        public void Close() => _active = false;
        public void Cancel() { _sequence = 0; _opened = false; _active = false; }
    }

}
