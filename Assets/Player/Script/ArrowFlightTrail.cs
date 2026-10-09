using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>Only records travelled positions; the complete stroke fades together after flight ends.</summary>
    public sealed class ArrowFlightTrail : MonoBehaviour
    {
        public const float FadeSeconds = .5f;
        private const int MaximumPoints = 256;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private LineRenderer _line;
        private MaterialPropertyBlock _properties;
        private Color _color;
        private Vector3 _previous, _last;
        private float _finishedAt = -1f;
        public bool IsFinished => _finishedAt >= 0f;

        public static ArrowFlightTrail Acquire(Material material, Color color, Vector3 origin, UnityEngine.SceneManagement.Scene scene = default)
        {
            var go = CombatVisualPool.Rent(null, Vector3.zero, Quaternion.identity, scene);
            var trail = go.GetComponent<ArrowFlightTrail>() ?? go.AddComponent<ArrowFlightTrail>();
            trail.Initialize(material, color, origin);
            go.SetActive(true);
            return trail;
        }

        public void Initialize(Material material, Color color, Vector3 origin)
        {
            _color = color; _color.a = .65f;
            _finishedAt = -1f;
            _properties ??= new MaterialPropertyBlock();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.enabled = !BattlePvp.UI.LocalGameSettings.Current.hideVfx;
            _line.sharedMaterial = material;
            _line.useWorldSpace = true;
            _line.startWidth = _line.endWidth = .035f;
            _line.startColor = _line.endColor = Color.white;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.lightProbeUsage = LightProbeUsage.Off;
            _line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _line.positionCount = 1;
            _line.SetPosition(0, origin);
            _previous = _last = origin;
            ApplyOpacity(1f);
        }

        public void Sample(Vector3 position)
        {
            if (IsFinished || _line == null || !CombatValidation.IsFinite(position) ||
                (position - _last).sqrMagnitude < .000001f) return;
            int count = _line.positionCount;
            // Straight flight needs two endpoints, independent of FPS. Preserve bends if flight changes direction.
            bool straight = count >= 2 && Vector3.Dot((_last - _previous).normalized, (position - _last).normalized) > .99999f;
            if (straight || count >= MaximumPoints) _line.SetPosition(count - 1, position);
            else
            {
                _previous = _last;
                _line.positionCount = count + 1;
                _line.SetPosition(count, position);
            }
            _last = position;
        }

        public void Finish(Vector3 finalPosition)
        {
            if (IsFinished) return;
            // An observer can be ahead of the server impact notification. Trim that final segment.
            if (_line != null && _line.positionCount >= 2 && CombatValidation.IsFinite(finalPosition) &&
                ((finalPosition - _previous).sqrMagnitude < .000001f ||
                 Vector3.Dot((finalPosition - _previous).normalized, (_last - _previous).normalized) > .999f) &&
                (finalPosition - _previous).sqrMagnitude <= (_last - _previous).sqrMagnitude)
                _line.SetPosition(_line.positionCount - 1, finalPosition);
            else Sample(finalPosition);
            _finishedAt = Time.time;
        }

        public static float OpacityAfter(float elapsed) => Mathf.Clamp01(1f - elapsed / FadeSeconds);

        private void Update()
        {
            if (_line != null) _line.enabled = !BattlePvp.UI.LocalGameSettings.Current.hideVfx;
            if (!IsFinished) return;
            float opacity = OpacityAfter(Time.time - _finishedAt);
            ApplyOpacity(opacity);
            if (opacity <= 0f) CombatVisualPool.Return(gameObject);
        }

        private void ApplyOpacity(float opacity)
        {
            Color color = _color; color.a *= opacity;
            _properties.SetColor(BaseColor, color);
            _line.SetPropertyBlock(_properties);
        }
    }
}
