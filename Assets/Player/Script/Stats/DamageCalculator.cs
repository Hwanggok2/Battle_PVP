using System;

namespace BattlePvp.Stats
{
    /// <summary>
    /// 데미지 계산을 위한 순수 로직(유니티 의존 없음).
    /// </summary>
    public sealed class DamageCalculator
    {
        private readonly StatBalanceConfig _balance;

        public DamageCalculator(StatBalanceConfig balance = null)
        {
            _balance = balance != null ? balance : StatBalanceCalculator.Config;
        }

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
            finalEff = Math.Min(finalEff, _balance.DefenseEfficiencyHardCap);
            return Math.Max(0f, finalEff);
        }

        /// <summary>
        /// 가시(Thorns) 반사 데미지:
        /// - 공격자 ATK 계수와 고정 피해는 StatBalanceConfig 기준
        /// - 공격자 MaxHP 상한도 StatBalanceConfig 기준
        /// </summary>
        public float PredictThornsReflectDamage(float attackerAtkPower, float attackerMaxHp)
        {
            float reflect = (attackerAtkPower * _balance.ThornsAttackPowerRatio) + _balance.ThornsFixedDamage;
            float cap = attackerMaxHp * _balance.ThornsAttackerMaxHpCapRatio;
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

