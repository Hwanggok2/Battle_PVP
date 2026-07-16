using UnityEngine;

namespace BattlePvp.UI
{
    [DisallowMultipleComponent]
    public sealed class CombatHitFeedback : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color _normalHitColor = Color.white;
        [SerializeField] private Color _headshotColor = new Color(1f, 0.08f, 0.08f, 1f);

        [Header("Opacity")]
        [Range(0f, 1f)] [SerializeField] private float _startOpacity = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float _endOpacity;

        [Header("Animation")]
        [Min(0.01f)] [SerializeField] private float _duration = 0.22f;
        [Range(0.05f, 0.95f)] [SerializeField] private float _growPortion = 0.35f;
        [Min(0f)] [SerializeField] private float _centerGap = 7f;
        [Min(1f)] [SerializeField] private float _maximumSpikeLength = 18f;
        [Min(0.5f)] [SerializeField] private float _spikeWidth = 4f;

        public void Play(bool isHeadshot)
        {
            CombatReticleView view = GetComponent<CombatReticleView>();
            if (view == null)
                return;

            view.InitializeForLocalPlayer(transform);

            Color baseColor = isHeadshot ? _headshotColor : _normalHitColor;
            float maximumSize = (_centerGap + _maximumSpikeLength + _spikeWidth) * 2f;
            view.PlayHit(baseColor, _startOpacity, _endOpacity, _duration, _growPortion, maximumSize);
        }
    }
}
