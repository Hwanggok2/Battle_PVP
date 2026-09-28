using BattlePvp.Combat;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>승인된 피해의 표시 규칙. HP 변경과 네트워크 권한은 소유하지 않는다.</summary>
    public static class HealthDamagePresentation
    {
        private static readonly PopupPredictionCache PhysicalPopupCorrelations = new PopupPredictionCache();
        private const float AroundCharacterRadius = 0.65f;
        private const float AroundCharacterHeight = 1.35f;

        public readonly struct Style
        {
            public readonly Color Color;
            public readonly bool UseReceivedDamageHud;
            public readonly bool AroundVictim;
            public readonly bool PlayStatusFeedback;
            public readonly float FontSize;
            public readonly float FontSizeDelta;

            public Style(Color color, bool useReceivedDamageHud, bool aroundVictim,
                bool playStatusFeedback, float fontSize, float fontSizeDelta)
            {
                Color = color;
                UseReceivedDamageHud = useReceivedDamageHud;
                AroundVictim = aroundVictim;
                PlayStatusFeedback = playStatusFeedback;
                FontSize = fontSize;
                FontSizeDelta = fontSizeDelta;
            }
        }

        public static Style ResolveStyle(DamageSource source, bool localVictim, bool localAttacker)
        {
            Color color = source switch
            {
                DamageSource.Poison => new Color(0.25f, 1f, 0.25f, 1f),
                DamageSource.Thorns => new Color(0.25f, 0.65f, 1f, 1f),
                _ => new Color(1f, 0.12f, 0.12f, 1f)
            };
            return new Style(color,
                localVictim && source == DamageSource.Physical,
                localVictim && source != DamageSource.Physical,
                !localVictim && localAttacker,
                localVictim ? 5f : 0f,
                !localVictim && source == DamageSource.Poison ? -16f : 0f);
        }

        public static void ClearCorrelations() => PhysicalPopupCorrelations.Clear();

        public static void ShowLocal(Transform victim, Vector3 position, float amount,
            DamageSource source, uint attackerNetId, uint victimNetId, uint correlationId,
            bool localVictim, bool localAttacker)
        {
            if (localAttacker && source == DamageSource.Physical && correlationId != 0 &&
                !PhysicalPopupCorrelations.TryClaim(attackerNetId, victimNetId, correlationId, Time.unscaledTime))
                return;

            HitImpactVfx.PlayFor(victim, position, source);
            DamagePopupManager popups = DamagePopupManager.Instance;
            if (popups == null) return;

            Style style = ResolveStyle(source, localVictim, localAttacker);
            if (style.UseReceivedDamageHud)
            {
                popups.CreateReceivedDamagePopup(amount, style.Color, style.FontSize, victim.position + Vector3.up);
                return;
            }
            if (style.AroundVictim)
            {
                popups.CreatePopup(GetAroundCharacterPosition(victim), amount, false, style.Color, style.FontSize);
                return;
            }
            if (style.PlayStatusFeedback)
                CombatHitFeedback.PlayStatusDamageForLocalPlayer(source);
            if (style.FontSizeDelta != 0f)
                popups.CreatePopupWithFontDelta(position, amount, false, style.Color, style.FontSizeDelta);
            else
                popups.CreatePopup(position, amount, false, style.Color);
        }

        public static Vector3 ResolvePosition(Transform victim, Vector3 hitPosition, DamageSource source)
        {
            if (source == DamageSource.Poison || source == DamageSource.Thorns)
                return GetAroundCharacterPosition(victim);
            return hitPosition == Vector3.zero ? victim.position + Vector3.up : hitPosition;
        }

        public static Vector3 GetThornsPosition(Transform attacker) =>
            attacker.position + attacker.right * 0.65f + Vector3.up * 1.25f;

        private static Vector3 GetAroundCharacterPosition(Transform victim)
        {
            Vector2 random = Random.insideUnitCircle * AroundCharacterRadius;
            return victim.position + victim.right * random.x + victim.forward * random.y + Vector3.up * AroundCharacterHeight;
        }
    }
}
