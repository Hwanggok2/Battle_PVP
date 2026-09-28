using BattlePvp.UI;
using Mirror;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Presentation only. Hits enter through server-confirmed feedback; deaths through life-state changes.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip[] _swings;
        [SerializeField] private AudioClip[] _hits;
        [SerializeField] private AudioClip _death;
        private AudioSource _worldSource, _feedbackSource;
        private HealthSystem _health;
        private NetworkIdentity _identity;
        private int _swingIndex, _hitIndex;
        private float _lastHitAt = -1;

        private void Awake()
        {
            _health = GetComponent<HealthSystem>();
            _identity = GetComponent<NetworkIdentity>();
            _worldSource = gameObject.AddComponent<AudioSource>();
            _feedbackSource = gameObject.AddComponent<AudioSource>();
            foreach (var source in new[] { _worldSource, _feedbackSource })
            {
                source.playOnAwake = false; source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2; source.maxDistance = 24;
            }
            _feedbackSource.spatialBlend = 0;
        }
        private void OnEnable() { if (_health != null) _health.OnDied += PlayDeath; }
        private void OnDisable()
        {
            if (_health != null) _health.OnDied -= PlayDeath;
            if (_worldSource != null) _worldSource.Stop();
            if (_feedbackSource != null) _feedbackSource.Stop();
        }
        private bool CanHear => Application.isPlaying && (!NetworkServer.active || NetworkClient.active);
        private bool IsLocal => !NetworkClient.active || (_identity != null && _identity.isLocalPlayer);

        public void PlaySwing()
        {
            if (!CanHear || _swings == null || _swings.Length == 0 || _worldSource == null) return;
            _worldSource.spatialBlend = IsLocal ? 0 : 1;
            _worldSource.PlayOneShot(_swings[_swingIndex++ % _swings.Length], .58f * LocalGameSettings.Current.effects);
        }
        public void PlayConfirmedHit()
        {
            if (!CanHear || !IsLocal || _hits == null || _hits.Length == 0 || _feedbackSource == null ||
                Time.unscaledTime - _lastHitAt < .055f) return;
            _lastHitAt = Time.unscaledTime;
            _feedbackSource.PlayOneShot(_hits[_hitIndex++ % _hits.Length], .7f * LocalGameSettings.Current.effects);
        }
        private void PlayDeath()
        {
            if (!CanHear || _death == null || _worldSource == null) return;
            _worldSource.Stop(); _worldSource.spatialBlend = IsLocal ? 0 : 1;
            _worldSource.PlayOneShot(_death, .7f * LocalGameSettings.Current.effects);
        }
    }
}
