using BattlePvp.UI;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>A short, reusable flash at a confirmed physical impact. Never predicts damage.</summary>
    public sealed class HitImpactVfx : MonoBehaviour
    {
        [SerializeField] private Mesh _square;
        [SerializeField] private Material _material;
        private const float Duration = .14f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _properties;
        private Light _light;
        private Vector3 _position;
        private float _born = -10f;

        public static void PlayFor(Transform victim, Vector3 position, DamageSource source)
        {
            if (LocalGameSettings.Current.hideVfx || !Application.isPlaying || source != DamageSource.Physical || victim == null ||
                (NetworkServer.active && !NetworkClient.active)) return;
            var effect = victim.GetComponent<HitImpactVfx>();
            if (effect != null) effect.Play(position);
        }

        private void Play(Vector3 position)
        {
            if (_square == null || _material == null) return;
            _position = position; _born = Time.unscaledTime;
            if (_light == null)
            {
                var child = new GameObject("Impact Glow"); child.transform.SetParent(transform, false);
                _light = child.AddComponent<Light>(); _light.type = LightType.Point;
                _light.color = new Color(.25f, .85f, 1f); _light.range = 1.8f;
                _light.shadows = LightShadows.None; _light.enabled = false;
            }
        }

        private void LateUpdate()
        {
            float t = (Time.unscaledTime - _born) / Duration;
            if (LocalGameSettings.Current.hideVfx || t < 0 || t >= 1 || _square == null || _material == null)
            { if (_light != null) _light.enabled = false; return; }
            var camera = Camera.main;
            if (camera == null) return;
            float fade = (1 - t) * (1 - t);
            Quaternion rotation = camera.transform.rotation;
            Vector3 center = _position - camera.transform.forward * .06f;
            _properties ??= new MaterialPropertyBlock();
            _properties.SetColor(BaseColor, new Color(.35f, 1.3f, 1.8f, fade * .8f));
            var parameters = new RenderParams(_material) { matProps = _properties,
                worldBounds = new Bounds(center, Vector3.one), shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (int i = 0; i < 4; i++)
            {
                Quaternion ray = rotation * Quaternion.Euler(0, 0, 45 + i * 90);
                Vector3 offset = ray * Vector3.up * (.025f + t * .09f);
                Graphics.RenderMesh(parameters, _square, 0,
                    Matrix4x4.TRS(center + offset, ray, new Vector3(.022f * (1 - t), .1f * (1 - t), 1)));
            }
            if (_light != null)
            {
                _light.transform.position = center;
                _light.intensity = 1.4f * fade;
                _light.enabled = LocalGameSettings.Current.quality > 0;
            }
        }

        private void OnDisable() { _born = -10; if (_light != null) _light.enabled = false; }
    }
}
