using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Two rigid jaws close using the server timestamp; the base stays on the ground.</summary>
    public sealed class SkillTrapVisual : MonoBehaviour
    {
        public const float CloseSeconds = .16f;
        public const float CloseLifetime = .65f;
        [SerializeField] private Transform _leftJaw, _rightJaw;
        private double _closedAt = double.PositiveInfinity;

        public void Render(double now, double closedAt)
        {
            _closedAt = System.Math.Min(_closedAt, closedAt);
            float age = (float)(now - _closedAt);
            float angle = Mathf.SmoothStep(0, 78, Mathf.Clamp01(age / CloseSeconds));
            if (_leftJaw != null) _leftJaw.localRotation = Quaternion.Euler(0, 0, -angle);
            if (_rightJaw != null) _rightJaw.localRotation = Quaternion.Euler(0, 0, angle);
            // Hold the closed shape before shrinking away, with no runtime material copies.
            float disappear = Mathf.Clamp01((age - .4f) / (CloseLifetime - .4f));
            transform.localScale = Vector3.one * (1 - disappear);
        }

        public bool RenderRemoved(double now)
        {
            Render(now, now);
            return now >= _closedAt + CloseLifetime;
        }
    }
}
