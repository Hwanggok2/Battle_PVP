using System;

namespace BattlePvp.Combat
{
    /// <summary>Pure skill lifetime transitions; PlayerCombat adapts these values to its existing SyncVars.</summary>
    public readonly struct CombatSkillExecution
    {
        public readonly bool IsCasting;
        public readonly double CastCompleteAt;
        public readonly double ActiveUntil;
        public readonly double CooldownUntil;

        public CombatSkillExecution(bool casting, double castCompleteAt, double activeUntil, double cooldownUntil)
        { IsCasting = casting; CastCompleteAt = castCompleteAt; ActiveUntil = activeUntil; CooldownUntil = cooldownUntil; }

        public bool CanBegin(double now) => Finite(now) && !IsCasting && now >= ActiveUntil && now >= CooldownUntil;

        public bool TryBegin(double now, double startedAt, double castSeconds, double cooldownSeconds,
            out CombatSkillExecution next)
        {
            next = this;
            if (!CanBegin(now) || !Finite(startedAt) || !Finite(castSeconds) || castSeconds < 0d ||
                !Finite(cooldownSeconds) || cooldownSeconds < 0d || !Finite(startedAt + castSeconds) ||
                !Finite(startedAt + cooldownSeconds)) return false;
            next = new CombatSkillExecution(true, startedAt + castSeconds, ActiveUntil, startedAt + cooldownSeconds);
            return true;
        }

        public CombatSkillExecution FinishCast(bool clearDeadline = false) =>
            new CombatSkillExecution(false, clearDeadline ? 0d : CastCompleteAt, ActiveUntil, CooldownUntil);

        public bool TryActivate(double now, double duration, out CombatSkillExecution next)
        {
            next = this;
            if (IsCasting || !Finite(now) || !Finite(duration) || duration < 0d || !Finite(now + duration)) return false;
            next = new CombatSkillExecution(false, CastCompleteAt, now + duration, CooldownUntil);
            return true;
        }

        public CombatSkillExecution EndActive() => new CombatSkillExecution(IsCasting, CastCompleteAt, 0d, CooldownUntil);
        public CombatSkillExecution Cancel() => new CombatSkillExecution(false, 0d, 0d, CooldownUntil);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public enum CombatCastChannel { Strength, Agility, Advanced }

    /// <summary>Accepted owner controls survive cast completion and owner changes, using absolute deadlines.</summary>
    public readonly struct CombatOwnerAction
    {
        public readonly int SkillKey;
        public readonly double StartedAt;
        public readonly double AnimationUntil;
        public readonly int InputFlags;
        public readonly double InputUntil;

        public CombatOwnerAction(int skillKey, double startedAt, double animationUntil, int inputFlags, double inputUntil)
        { SkillKey = skillKey; StartedAt = startedAt; AnimationUntil = animationUntil; InputFlags = inputFlags; InputUntil = inputUntil; }

        public static CombatOwnerAction Cancelled => new CombatOwnerAction(-1, 0d, 0d, 0, 0d);
        public static double ResolveRestoreTime(double networkNow, double receivedBatchTime) =>
            Finite(receivedBatchTime) ? Math.Max(networkNow, receivedBatchTime) : networkNow;
        public bool HasAnimation(double now) => SkillKey >= 0 && Finite(now) && Finite(AnimationUntil) && now < AnimationUntil;
        public double RemainingInput(double now) => SkillKey >= 0 && InputFlags != 0 && Finite(now) && Finite(InputUntil)
            ? Math.Max(0d, InputUntil - now) : 0d;

        public static CombatOwnerAction Begin(int skillKey, double startedAt, int inputFlags, double inputSeconds)
        {
            if (skillKey < 0 || !Finite(startedAt) || !Finite(inputSeconds) || inputSeconds < 0d ||
                !Finite(startedAt + inputSeconds)) return Cancelled;
            return new CombatOwnerAction(skillKey, startedAt, 0d, inputFlags, startedAt + inputSeconds);
        }

        public CombatOwnerAction WithAnimation(double duration)
        {
            if (SkillKey < 0 || !Finite(duration) || duration <= 0d || !Finite(StartedAt + duration)) return this;
            return new CombatOwnerAction(SkillKey, StartedAt, StartedAt + duration, InputFlags, InputUntil);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Owner-local predicted locks. Cancelling predictions never modifies server cooldowns.</summary>
    public sealed class CombatActionLocks
    {
        private readonly double[] _deadlines = new double[3];
        private double _animationUntil;

        public void LockUntil(CombatCastChannel channel, double deadline)
        {
            if (!double.IsNaN(deadline) && !double.IsInfinity(deadline)) _deadlines[(int)channel] = deadline;
        }
        public void LockAnimationUntil(double deadline)
        {
            if (!double.IsNaN(deadline) && !double.IsInfinity(deadline)) _animationUntil = deadline;
        }
        public void ReleaseAnimation() => _animationUntil = 0d;
        public bool IsAnimationLocked(double now) => now < _animationUntil;
        public bool IsLocked(double now) => IsAnimationLocked(now) || now < _deadlines[0] || now < _deadlines[1] || now < _deadlines[2];
        public void Cancel() { Array.Clear(_deadlines, 0, _deadlines.Length); ReleaseAnimation(); }
    }
}
