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
        [Min(0f)] [SerializeField] private float _lockSeconds = 0f;
        [SerializeField] private bool _lockMovement = true;
        [SerializeField] private bool _lockAttack = true;
        [SerializeField] private bool _lockJump = false;
        [SerializeField] private AudioClip _useSfx;
        [Range(0f, 1f)] [SerializeField] private float _sfxVolume = 0.9f;

        public string DisplayName => _displayName;
        public AnimationClip AnimationClip => _animationClip;
        public string AnimationStateName => _animationStateName;
        public int AnimationLayer => _animationLayer;
        public string FallbackStateName => _fallbackStateName;
        public float LockSeconds => _lockSeconds;
        public bool LockMovement => _lockMovement;
        public bool LockAttack => _lockAttack;
        public bool LockJump => _lockJump;
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
