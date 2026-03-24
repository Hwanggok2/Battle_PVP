using System;

namespace BattlePvp.Stats
{
    /// <summary>
    /// 데미지 계산을 위한 순수 로직(유니티 의존 없음).
    /// </summary>
    public sealed class DamageCalculator
    {
        // reference-formulae.md 기준
        private const float HardCapDefenseEff = 0.75f; // 75%

        /// <summary>
        /// 최종 데미지 공식:
        /// FinalDamage = 공격력 * (1 - (방어율 * (1 - 관통력/100) / 100))
        /// </summary>
        /// <param name="attackPower">공격력</param>
        /// <param name="currentDefNormalized">CurrentDEF를 0..1로 정규화한 값</param>
        /// <param name="bonusEffNormalized">BonusEff를 0..1로 정규화한 값</param>
        /// <param name="penetrationPercent">관통력(0..100)</param>
        public float PredictFinalDamage(float attackPower, float currentDefNormalized, float bonusEffNormalized, float penetrationPercent)
        {
            float defEff = PredictFinalDefenseEfficiency(currentDefNormalized, bonusEffNormalized);
            float defRatePercent = defEff * 100f; // "방어율"을 퍼센트로 환산
            float pierce01 = penetrationPercent / 100f;

            // defenseRate * (1 - pierce) / 100
            float damageMultiplier = 1f - (defRatePercent * (1f - pierce01) / 100f);
            float finalDamage = attackPower * damageMultiplier;
            return Math.Max(0f, finalDamage);
        }

        /// <summary>
        /// 방어 효율 승산 중첩:
        /// FinalDEF_Eff = 1 - (1 - CurrentDEF) * (1 - BonusEff)
        /// + 방어 상한선(Hard Cap): 0.75
        /// </summary>
        public float PredictFinalDefenseEfficiency(float currentDefNormalized, float bonusEffNormalized)
        {
            float cur = Clamp01(currentDefNormalized);
            float bonus = Clamp01(bonusEffNormalized);
            float finalEff = 1f - (1f - cur) * (1f - bonus);
            finalEff = Math.Min(finalEff, HardCapDefenseEff);
            return Math.Max(0f, finalEff);
        }

        /// <summary>
        /// 가시(Thorns) 반사 데미지:
        /// - 공격자 ATK * 0.15
        /// - 나의 MaxHP * 0.07 상한
        /// </summary>
        public float PredictThornsReflectDamage(float attackerAtkPower, float myMaxHp)
        {
            float reflect = attackerAtkPower * 0.15f;
            float cap = myMaxHp * 0.07f;
            return Math.Max(0f, Math.Min(reflect, cap));
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}

