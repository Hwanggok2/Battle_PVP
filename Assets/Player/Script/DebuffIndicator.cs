using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>One shared downward-arrow group, driven by authoritative status rather than cast effects.</summary>
    [DisallowMultipleComponent]
    public sealed class DebuffIndicator : MonoBehaviour
    {
        private HealthSystem _health;
        private DummyHealth _dummy;
        private ExpandedSkillController _skills;
        private PlayerManager _movement;
        private PlayerCombat _combat;
        private IDamageReceiver _target;
        private readonly List<PlayerCombat> _poisonSources = new();
        private SkillBuffSymbols _symbols;
        private double Now => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        private bool Authority => _health != null ? _health.isServer || (!NetworkServer.active && !NetworkClient.active) :
            _dummy != null && (_dummy.isServer || (!NetworkServer.active && !NetworkClient.active));

        private void Awake()
        {
            _symbols ??= new SkillBuffSymbols();
            _health = GetComponent<HealthSystem>(); _dummy = GetComponent<DummyHealth>();
            _skills = GetComponent<ExpandedSkillController>(); _movement = GetComponent<PlayerManager>();
            _combat = GetComponent<PlayerCombat>(); _target = _health != null ? _health : (IDamageReceiver)_dummy;
        }

        internal void TrackPoison(PlayerCombat source)
        {
            if (Authority && source != null && !_poisonSources.Contains(source)) _poisonSources.Add(source);
        }

        private void LateUpdate()
        {
            bool alive = _target != null && _target.CurrentHp > 0 && (_health == null || !_health.IsDead);
            if (Authority)
            {
                bool poison = false;
                for (int i = _poisonSources.Count - 1; i >= 0; i--)
                {
                    var source = _poisonSources[i];
                    // Query the actual stack owner: cancellation/expiry and multiple attackers stay consistent.
                    if (!alive || source == null || !source.isActiveAndEnabled || source.PoisonExpiresAt(_target) <= Now)
                        _poisonSources.RemoveAt(i);
                    else poison = true;
                }
                bool active = alive && (poison || (_skills != null && _skills.HasDebuff) ||
                    (_movement != null && _movement.HasMovementDebuff) || (_combat != null && _combat.IsServerTaunted) ||
                    (_dummy != null && _dummy.HasControlDebuff));
                _health?.SetDebuffPresentation(active); _dummy?.SetDebuffPresentation(active);
            }
            bool concealed = _skills != null && _skills.IsStealthed && !_skills.Owner;
            bool debuffed = _health != null ? _health.HasDebuff : _dummy != null && _dummy.HasDebuff;
            _symbols.TickDebuff(transform, Now, alive && debuffed && !concealed, _skills != null && _skills.IsStealthed ? .5f : 1f);
        }

        private void OnDisable()
        {
            _poisonSources.Clear();
            _health?.SetDebuffPresentation(false); _dummy?.SetDebuffPresentation(false);
            _symbols?.Dispose();
        }
        private void OnDestroy() => _symbols?.Dispose();
    }
}
