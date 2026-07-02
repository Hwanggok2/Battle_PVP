using UnityEngine;

namespace BattlePvp.Combat
{
    [CreateAssetMenu(fileName = "NewJobSkillData", menuName = "Combat/Job Skill Data")]
    public sealed class JobSkillData : ScriptableObject
    {
        [Header("Display")]
        [SerializeField] private string _displayName = "Skill";
        [SerializeField] private Sprite _iconSprite;

        [Header("Timing")]
        [Min(0f)] [SerializeField] private float _castSeconds = 0f;
        [Min(0f)] [SerializeField] private float _durationSeconds = 0f;
        [Min(0f)] [SerializeField] private float _cooldownSeconds = 0f;

        [Header("Effect Values")]
        [Min(0f)] [SerializeField] private float _lifestealRatio = 0f;

        [Header("Audio")]
        [SerializeField] private AudioClip _useSfx;
        [Range(0f, 1f)] [SerializeField] private float _sfxVolume = 0.9f;

        public string DisplayName => _displayName;
        public Sprite IconSprite => _iconSprite;
        public float CastSeconds => _castSeconds;
        public float DurationSeconds => _durationSeconds;
        public float CooldownSeconds => _cooldownSeconds;
        public float LifestealRatio => _lifestealRatio;
        public AudioClip UseSfx => _useSfx;
        public float SfxVolume => _sfxVolume;
    }
}
