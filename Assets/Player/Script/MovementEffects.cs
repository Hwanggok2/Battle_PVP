using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.Combat
{
    public struct MovementEffectSnapshot
    {
        public int Source;
        public float Multiplier;
        public double Until;
    }

    public struct InputLockSnapshot
    {
        public int Source;
        public SkillInputLockFlags Flags;
        public double Until;
    }

    public sealed class MovementEffects
    {
        private struct Effect
        {
            public int Source;
            public float Multiplier;
            public double Until;
        }

        private readonly List<Effect> _effects = new List<Effect>(4);

        public void Set(int source, float multiplier, float seconds, double now)
        {
            if (!float.IsFinite(multiplier) || !float.IsFinite(seconds) || !double.IsFinite(now))
                return;
            Remove(source);
            if (seconds <= 0f) return;
            _effects.Add(new Effect { Source = source, Multiplier = Mathf.Clamp(multiplier, 0f, 4f), Until = now + seconds });
        }

        public void Remove(int source)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
                if (_effects[i].Source == source) _effects.RemoveAt(i);
        }

        public float Evaluate(double now)
        {
            float multiplier = 1f;
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                if (now >= _effects[i].Until) _effects.RemoveAt(i);
                else multiplier *= _effects[i].Multiplier;
            }
            return Mathf.Clamp(multiplier, 0f, 4f);
        }

        public bool HasSlow(double now, int ignoredSource, int otherIgnoredSource)
        {
            foreach (var effect in _effects)
                if (effect.Until > now && effect.Multiplier < 1f &&
                    effect.Source != ignoredSource && effect.Source != otherIgnoredSource)
                    return true;
            return false;
        }

        public MovementEffectSnapshot[] Capture(double now)
        {
            Evaluate(now);
            var result = new MovementEffectSnapshot[_effects.Count];
            for (int i = 0; i < _effects.Count; i++)
                result[i] = new MovementEffectSnapshot { Source = _effects[i].Source, Multiplier = _effects[i].Multiplier, Until = _effects[i].Until };
            return result;
        }

        public void Restore(MovementEffectSnapshot[] snapshot, double now)
        {
            Clear();
            if (snapshot == null) return;
            foreach (MovementEffectSnapshot effect in snapshot)
                if (effect.Until > now) Set(effect.Source, effect.Multiplier, (float)(effect.Until - now), now);
        }

        public void Clear() => _effects.Clear();
    }

    public sealed class InputLockEffects
    {
        private struct Lock
        {
            public int Source;
            public SkillInputLockFlags Flags;
            public double Until;
        }
        private readonly List<Lock> _locks = new List<Lock>(4);

        public void Set(int source, SkillInputLockFlags flags, float seconds, double now)
        {
            if (float.IsNaN(seconds) || !double.IsFinite(now)) return;
            Remove(source);
            if (seconds <= 0f || flags == SkillInputLockFlags.None) return;
            _locks.Add(new Lock { Source = source, Flags = flags, Until = now + seconds });
        }

        public void Remove(int source)
        {
            for (int i = _locks.Count - 1; i >= 0; i--)
                if (_locks[i].Source == source) _locks.RemoveAt(i);
        }

        public SkillInputLockFlags Evaluate(double now)
        {
            SkillInputLockFlags flags = SkillInputLockFlags.None;
            for (int i = _locks.Count - 1; i >= 0; i--)
                if (now >= _locks[i].Until) _locks.RemoveAt(i);
                else flags |= _locks[i].Flags;
            return flags;
        }

        public InputLockSnapshot[] CaptureForReconnect(double now)
        {
            Evaluate(now);
            var result = new List<InputLockSnapshot>(_locks.Count);
            foreach (Lock entry in _locks)
            {
                // These are reconstructed from authoritative combat state, not old owner predictions.
                if (entry.Source == CombatEffectSources.PredictedCastMovement ||
                    entry.Source == CombatEffectSources.AdvancedSkillInput ||
                    entry.Source == CombatEffectSources.ServerCastMovement) continue;
                result.Add(new InputLockSnapshot { Source = entry.Source, Flags = entry.Flags, Until = entry.Until });
            }
            return result.ToArray();
        }

        public void Restore(InputLockSnapshot[] snapshot, double now)
        {
            if (snapshot == null) return;
            foreach (InputLockSnapshot entry in snapshot)
                if (entry.Until > now) Set(entry.Source, entry.Flags, (float)(entry.Until - now), now);
        }

        public void Clear() => _locks.Clear();
    }
}
