using UnityEngine;
using BattlePvp.Combat;
using Mirror;

namespace BattlePvp.UI
{
    [DisallowMultipleComponent]
    public sealed class CombatHitFeedback : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color _normalHitColor = Color.white;
        [SerializeField] private Color _headshotColor = new Color(1f, 0.08f, 0.08f, 1f);
        [SerializeField] private Color _poisonDamageColor = new Color(0.25f, 1f, 0.25f, 1f);
        [SerializeField] private Color _reflectDamageColor = new Color(0.25f, 0.65f, 1f, 1f);

        [Header("Opacity")]
        [Range(0f, 1f)] [SerializeField] private float _startOpacity = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float _endOpacity;
        [Range(0f, 1f)] [SerializeField] private float _statusDamageStartOpacity = 0.95f;
        [Range(0f, 1f)] [SerializeField] private float _statusDamageEndOpacity;

        [Header("Hit Animation")]
        [Min(0.01f)] [SerializeField] private float _duration = 0.22f;
        [Range(0.05f, 0.95f)] [SerializeField] private float _growPortion = 0.35f;
        [Range(0f, 50f)] [SerializeField] private float _centerGap = 7f;
        [Range(1f, 60f)] [SerializeField] private float _maximumSpikeLength = 18f;
        [Range(0.5f, 20f)] [SerializeField] private float _spikeWidth = 4f;

        [Header("Poison/Reflect Spike Animation")]
        [Min(0.01f)] [SerializeField] private float _statusDamageDuration = 0.22f;
        [Range(0.05f, 0.95f)] [SerializeField] private float _statusDamageGrowPortion = 0.35f;
        [Range(0f, 80f)] [SerializeField] private float _statusDamageCenterGap = 18f;
        [Range(1f, 100f)] [SerializeField] private float _statusDamageMaximumSpikeLength = 24f;
        [Range(0.5f, 30f)] [SerializeField] private float _statusDamageSpikeWidth = 4f;

        public void Play(bool isHeadshot)
        {
            CombatReticleView view = GetComponent<CombatReticleView>();
            if (view == null)
                return;

            view.InitializeForLocalPlayer(transform);

            Color baseColor = isHeadshot ? _headshotColor : _normalHitColor;
            view.PlayHit(
                baseColor,
                _startOpacity,
                _endOpacity,
                _duration,
                _growPortion,
                _centerGap,
                _maximumSpikeLength,
                _spikeWidth);
        }

        public void PlayStatusDamage(DamageSource source)
        {
            if (source != DamageSource.Poison && source != DamageSource.Thorns)
                return;

            CombatReticleView view = GetComponent<CombatReticleView>();
            if (view == null)
                return;

            view.InitializeForLocalPlayer(transform);

            Color color = source == DamageSource.Poison ? _poisonDamageColor : _reflectDamageColor;
            view.PlayStatusDamage(
                color,
                _statusDamageStartOpacity,
                _statusDamageEndOpacity,
                _statusDamageDuration,
                _statusDamageGrowPortion,
                _statusDamageCenterGap,
                _statusDamageMaximumSpikeLength,
                _statusDamageSpikeWidth);
        }

        public static void PlayStatusDamageForAttacker(DamageSource source, IDamageReceiver attacker)
        {
            if (source != DamageSource.Poison && source != DamageSource.Thorns)
                return;

            if (attacker is not MonoBehaviour attackerMb || attackerMb == null)
                return;

            if (!ShouldPlayForLocalAttacker(attackerMb))
                return;

            CombatHitFeedback feedback = attackerMb.GetComponent<CombatHitFeedback>();
            if (feedback == null)
                feedback = attackerMb.gameObject.AddComponent<CombatHitFeedback>();

            feedback.PlayStatusDamage(source);
        }

        public static void PlayStatusDamageForLocalPlayer(DamageSource source)
        {
            if (source != DamageSource.Poison && source != DamageSource.Thorns)
                return;

            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
                return;

            CombatHitFeedback feedback = localPlayer.GetComponent<CombatHitFeedback>();
            if (feedback == null)
                feedback = localPlayer.gameObject.AddComponent<CombatHitFeedback>();

            feedback.PlayStatusDamage(source);
        }

        private static bool ShouldPlayForLocalAttacker(MonoBehaviour attacker)
        {
            bool networkActive = NetworkClient.active || NetworkServer.active;
            if (!networkActive)
                return true;

            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
                return false;

            if (!attacker.TryGetComponent(out NetworkIdentity attackerIdentity))
                return false;

            return attackerIdentity.netId != 0 && attackerIdentity.netId == localPlayer.netId;
        }
    }
}
