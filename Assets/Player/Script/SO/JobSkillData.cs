using UnityEngine;

namespace BattlePvp.Combat
{
    public enum JobSkillKind
    {
        MonostatStrLifesteal = 0,
        MonostatAgiPoison = 1,
        MonostatConKick = 2,
        MonostatDefParry = 3,
        StrategistRoll = 10,
        StrategistPresetChange = 11,
        PolymathRoll = 20,
        PolymathPresetChange = 21,
        PolymathWeaponSwap = 22
    }

    [CreateAssetMenu(fileName = "NewJobSkillData", menuName = "Combat/Job Skill Data")]
    public sealed class JobSkillData : ScriptableObject
    {
        [Header("Common")]
        [SerializeField] private JobSkillKind _skillKind = JobSkillKind.MonostatStrLifesteal;

        [Header("Display")]
        [SerializeField] private string _displayName = "Skill";
        [SerializeField] private Sprite _iconSprite;

        [Header("Timing")]
        [Min(0f)] [SerializeField] private float _castSeconds = 0f;
        [Min(0f)] [SerializeField] private float _durationSeconds = 0f;
        [Min(0f)] [SerializeField] private float _cooldownSeconds = 0f;

        [Header("STR Lifesteal")]
        [Min(0f)] [SerializeField] private float _lifestealRatio = 0f;

        [Header("AGI Poison")]
        [Min(0)] [SerializeField] private int _poisonMaxStacks = 0;
        [Min(0f)] [SerializeField] private float _poisonDamagePerStackPerSecond = 0f;
        [Min(0f)] [SerializeField] private float _poisonStackDurationSeconds = 0f;

        [Header("Audio")]
        [SerializeField] private AudioClip _useSfx;
        [Range(0f, 1f)] [SerializeField] private float _sfxVolume = 0.9f;

        public JobSkillKind SkillKind => _skillKind;
        public string DisplayName => _displayName;
        public Sprite IconSprite => _iconSprite;
        public float CastSeconds => _castSeconds;
        public float DurationSeconds => _durationSeconds;
        public float CooldownSeconds => _cooldownSeconds;
        public float LifestealRatio => _lifestealRatio;
        public int PoisonMaxStacks => _poisonMaxStacks;
        public float PoisonDamagePerStackPerSecond => _poisonDamagePerStackPerSecond;
        public float PoisonStackDurationSeconds => _poisonStackDurationSeconds;
        public AudioClip UseSfx => _useSfx;
        public float SfxVolume => _sfxVolume;
    }
}
