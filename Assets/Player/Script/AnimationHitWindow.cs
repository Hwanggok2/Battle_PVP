using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Clips a sampled animation segment to the authored contact interval.</summary>
    public readonly struct AnimationHitWindow
    {
        public readonly float Start, End;
        public AnimationHitWindow(float start, float end) { Start = start; End = end; }

        public static AnimationHitWindow Melee(Animator animator, int layer)
        {
            foreach (var info in animator.GetCurrentAnimatorClipInfo(layer))
            {
                float start = -1, end = 1;
                foreach (var e in info.clip.events)
                {
                    if (e.functionName == "EnableHitBox") start = e.time / info.clip.length;
                    if (e.functionName == "DisableHitBox" && e.time > 0) end = e.time / info.clip.length;
                }
                if (start >= 0) return new AnimationHitWindow(start, end);
            }
            return new AnimationHitWindow(0, 1);
        }

        public bool Clip(float previous, float current, out float from, out float to)
        {
            from = to = 0;
            if (!float.IsFinite(previous) || !float.IsFinite(current) || current <= previous ||
                current < Start || previous >= End) return false;
            from = Mathf.Clamp01((Start - previous) / (current - previous));
            to = Mathf.Clamp01((End - previous) / (current - previous));
            return to > from;
        }
    }
}
