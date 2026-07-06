using UnityEngine;

namespace BattlePvp.Combat
{
    [CreateAssetMenu(fileName = "NewEmoteData", menuName = "Combat/Emote Data")]
    public sealed class EmoteData : ScriptableObject
    {
        [SerializeField] private string _displayName = "Emote";
        [SerializeField] private AnimationClip _animationClip;
        [SerializeField] private string _animationStateName = "Taunt Gesture";
        [Min(0)] [SerializeField] private int _animationLayer = 1;
        [SerializeField] private string _fallbackStateName = "New State";
        [SerializeField] private SkillInputLockFlags _inputLockFlags = SkillInputLockFlags.Move | SkillInputLockFlags.Attack | SkillInputLockFlags.Jump;
        [Min(0f)] [SerializeField] private float _lockSeconds = 0f;
        [SerializeField] private AudioClip _useSfx;
        [Range(0f, 1f)] [SerializeField] private float _sfxVolume = 0.9f;

        public string DisplayName => _displayName;
        public AnimationClip AnimationClip => _animationClip;
        public string AnimationStateName => _animationStateName;
        public int AnimationLayer => _animationLayer;
        public string FallbackStateName => _fallbackStateName;
        public SkillInputLockFlags InputLockFlags => _inputLockFlags;
        public float LockSeconds => _lockSeconds;
        public bool LockMovement => (_inputLockFlags & SkillInputLockFlags.Move) != 0;
        public bool LockAttack => (_inputLockFlags & SkillInputLockFlags.Attack) != 0;
        public bool LockJump => (_inputLockFlags & SkillInputLockFlags.Jump) != 0;
        public AudioClip UseSfx => _useSfx;
        public float SfxVolume => _sfxVolume;

        public float ResolveDurationSeconds(float fallbackSeconds = 1.5f)
        {
            if (_lockSeconds > 0f)
                return _lockSeconds;

            if (_animationClip != null && _animationClip.length > 0f)
                return _animationClip.length;

            return Mathf.Max(0.1f, fallbackSeconds);
        }
    }
}
