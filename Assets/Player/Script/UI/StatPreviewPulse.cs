using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>A restartable pulse keeps the configured scale, even during repeated edits.</summary>
    public sealed class StatPreviewPulse
    {
        private const float Duration = 0.2f;
        private float _elapsed = Duration;
        public Vector3 OriginalScale { get; }
        public bool IsActive => _elapsed < Duration;
        public Vector3 Scale => OriginalScale * Mathf.Lerp(1.2f, 1f, Mathf.Clamp01(_elapsed / Duration));

        public StatPreviewPulse(Vector3 originalScale) => OriginalScale = originalScale;
        public void Restart() => _elapsed = 0f;
        public void Advance(float deltaTime) => _elapsed = Mathf.Min(Duration, _elapsed + Mathf.Max(0f, deltaTime));
        public void Reset() => _elapsed = Duration;
    }
}
