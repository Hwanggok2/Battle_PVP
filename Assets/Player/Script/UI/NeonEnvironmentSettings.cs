using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattlePvp.UI
{
    public sealed class NeonEnvironmentSettings : MonoBehaviour
    {
        private Light[] _lights;
        private Volume _volume;
        private VolumeProfile _runtimeProfile;
        private void Awake() { _lights = GetComponentsInChildren<Light>(true); _volume = GetComponent<Volume>(); }
        private void OnEnable() { LocalGameSettings.Changed += Apply; Apply(); }
        private void OnDisable() => LocalGameSettings.Changed -= Apply;
        private void OnDestroy()
        {
            if (_runtimeProfile == null) return;
            foreach (var component in _runtimeProfile.components) if (component != null) Destroy(component);
            Destroy(_runtimeProfile);
        }
        private void Apply()
        {
            foreach (var light in _lights) if (light != null && light.type == LightType.Point) light.enabled = LocalGameSettings.Current.quality > 0;
            if (_volume == null) return;
            var profile = _runtimeProfile != null ? _runtimeProfile : (_runtimeProfile = _volume.profile);
            if (profile.TryGet(out Bloom bloom)) bloom.active = LocalGameSettings.Current.quality > 0;
            if (!profile.TryGet(out ColorAdjustments color)) color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(Mathf.Log(LocalGameSettings.Current.brightness, 2));
        }
    }
}
