using System;
using System.Collections.Generic;

namespace BattlePvp.Combat
{
    public readonly struct PoisonTick<TTarget, TPosition>
    {
        public readonly TTarget Target;
        public readonly TPosition HitPosition;
        public readonly float Damage;
        public PoisonTick(TTarget target, TPosition hitPosition, float damage)
        { Target = target; HitPosition = hitPosition; Damage = damage; }
    }

    /// <summary>Owns stacks and expiry. The caller supplies target validity and applies the resulting damage.</summary>
    public sealed class PoisonStackCollection<TTarget, TPosition> where TTarget : class
    {
        private sealed class Stack
        {
            public TTarget Target;
            public TPosition HitPosition;
            public int Count;
            public double ExpiresAt;
        }

        private readonly List<Stack> _stacks = new List<Stack>();
        public int Count => _stacks.Count;

        public bool Add(TTarget target, TPosition hitPosition, double now, double duration, int maximumStacks)
        {
            if (target == null || !Finite(now) || !Finite(duration) || duration <= 0d ||
                maximumStacks <= 0 || !Finite(now + duration)) return false;
            Stack stack = null;
            for (int i = 0; i < _stacks.Count; i++)
                if (ReferenceEquals(_stacks[i].Target, target)) { stack = _stacks[i]; break; }
            if (stack == null) { stack = new Stack { Target = target }; _stacks.Add(stack); }
            if (now >= stack.ExpiresAt) stack.Count = 0;
            stack.Count = (int)Math.Min((long)stack.Count + 1L, maximumStacks);
            stack.HitPosition = hitPosition;
            stack.ExpiresAt = now + duration;
            return true;
        }

        public void CollectTicks(double now, float damagePerStack, Func<TTarget, bool> isValid,
            List<PoisonTick<TTarget, TPosition>> ticks)
        {
            ticks.Clear();
            if (!Finite(now)) return;
            bool validDamage = !float.IsNaN(damagePerStack) && !float.IsInfinity(damagePerStack) && damagePerStack > 0f;
            for (int i = _stacks.Count - 1; i >= 0; i--)
            {
                Stack stack = _stacks[i];
                if (now >= stack.ExpiresAt || !isValid(stack.Target)) { _stacks.RemoveAt(i); continue; }
                if (validDamage)
                {
                    float damage = (float)Math.Min(float.MaxValue, (double)stack.Count * damagePerStack);
                    ticks.Add(new PoisonTick<TTarget, TPosition>(stack.Target, stack.HitPosition, damage));
                }
            }
        }

        public void Clear() => _stacks.Clear();
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
