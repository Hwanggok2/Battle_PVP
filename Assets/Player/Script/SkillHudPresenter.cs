using BattlePvp.Stats;
using BattlePvp.UI;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>A selected skill's display inputs; the producer retains ownership of gameplay state.</summary>
    public readonly struct SkillHudSnapshot
    {
        public readonly string Name;
        public readonly Sprite Icon;
        public readonly int SelectedIndex, SkillCount;
        public readonly bool IsCasting;
        public readonly double CastCompleteAt, ActiveUntil, CooldownUntil;
        public readonly float CastSeconds, CooldownSeconds;

        public SkillHudSnapshot(string name, Sprite icon, int selectedIndex, int skillCount,
            bool isCasting, double castCompleteAt, double activeUntil, double cooldownUntil,
            float castSeconds, float cooldownSeconds)
        {
            Name = name;
            Icon = icon;
            SelectedIndex = selectedIndex;
            SkillCount = skillCount;
            IsCasting = isCasting;
            CastCompleteAt = castCompleteAt;
            ActiveUntil = activeUntil;
            CooldownUntil = cooldownUntil;
            CastSeconds = castSeconds;
            CooldownSeconds = cooldownSeconds;
        }
    }

    /// <summary>Maps value snapshots to HUD output and owns only notification throttling.</summary>
    public sealed class SkillHudPresenter
    {
        private const float UpdateIntervalSeconds = 0.1f;
        private SkillHudState _lastPublishedState;
        private bool _hasPublishedState;
        private float _nextPublishTime;

        public static string ResolveName(Identity? identity, int selectedIndex, string configuredName)
        {
            if (!string.IsNullOrWhiteSpace(configuredName)) return configuredName;
            if (!identity.HasValue) return string.Empty;
            Identity value = identity.Value;
            if (value.Type == IdentityType.Monostat)
            {
                if (value.PrimaryStat == StatKind.STR) return "흡혈";
                if (value.PrimaryStat == StatKind.AGI) return "독 바르기";
            }
            if (value.Type == IdentityType.Strategist) return selectedIndex == 0 ? "구르기" : "프리셋";
            if (value.Type == IdentityType.Polymath) return selectedIndex == 0 ? "구르기" : "무기";
            return string.Empty;
        }

        public static SkillHudState Build(SkillHudSnapshot snapshot, double now)
        {
            if (snapshot.SkillCount <= 0)
                return new SkillHudState(false, string.Empty, 0, 0, SkillHudPhase.Hidden, 0f, 0f);

            SkillHudPhase phase = SkillHudPhase.Ready;
            float remaining = 0f;
            float fill = 0f;
            if (snapshot.IsCasting)
            {
                phase = SkillHudPhase.Casting;
                remaining = Mathf.Max(0f, (float)(snapshot.CastCompleteAt - now));
                fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, snapshot.CastSeconds));
            }
            else if (now < snapshot.ActiveUntil)
            {
                phase = SkillHudPhase.Active;
                remaining = Mathf.Max(0f, (float)(snapshot.ActiveUntil - now));
                fill = 1f;
            }
            else if (now < snapshot.CooldownUntil)
            {
                phase = SkillHudPhase.Cooldown;
                remaining = Mathf.Max(0f, (float)(snapshot.CooldownUntil - now));
                fill = Mathf.Clamp01(remaining / Mathf.Max(0.001f, snapshot.CooldownSeconds));
            }

            return new SkillHudState(true, snapshot.Name, snapshot.SelectedIndex, snapshot.SkillCount,
                phase, fill, remaining, snapshot.Icon);
        }

        public bool ShouldPublish(SkillHudState state, bool force, float unscaledNow)
        {
            if (!force)
            {
                if (_hasPublishedState && unscaledNow < _nextPublishTime &&
                    IsEquivalent(_lastPublishedState, state))
                    return false;
                _nextPublishTime = unscaledNow + UpdateIntervalSeconds;
            }
            _lastPublishedState = state;
            _hasPublishedState = true;
            return true;
        }

        private static bool IsEquivalent(SkillHudState a, SkillHudState b)
        {
            return a.Visible == b.Visible &&
                   a.Name == b.Name &&
                   a.SelectedIndex == b.SelectedIndex &&
                   a.SkillCount == b.SkillCount &&
                   a.Phase == b.Phase &&
                   a.IconSprite == b.IconSprite &&
                   Mathf.Abs(a.NormalizedFill - b.NormalizedFill) < 0.02f &&
                   Mathf.CeilToInt(a.RemainingSeconds) == Mathf.CeilToInt(b.RemainingSeconds);
        }
    }
}
