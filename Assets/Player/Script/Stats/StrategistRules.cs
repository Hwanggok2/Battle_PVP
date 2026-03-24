namespace BattlePvp.Stats
{
    /// <summary>
    /// 전략가(Strategist) 전용 전투 규칙을 담는 순수 로직.
    /// </summary>
    public sealed class StrategistRules
    {
        // reference-formulae.md 기준 고정값
        private const float UnprotectedDurationSeconds = 1.5f;
        private const float UnprotectedDamageMultiplier = 1.2f;

        private const float GlitchOverflowHpPerSecondRate = 0.10f; // NewMaxHP * 0.10 / sec

        /// <summary>
        /// 무방비 패널티:
        /// 1.5초간 IncomingDamage * 1.2 적용.
        /// </summary>
        /// <param name="timeSincePenaltyStartSeconds">패널티 시작 후 경과 시간</param>
        public float GetIncomingDamageMultiplier(float timeSincePenaltyStartSeconds)
        {
            return timeSincePenaltyStartSeconds <= UnprotectedDurationSeconds
                ? UnprotectedDamageMultiplier
                : 1f;
        }

        /// <summary>
        /// Glitch Overflow:
        /// CurrentHP > NewMaxHP 이면 초당 NewMaxHP * 0.10 만큼 HP 감소.
        /// </summary>
        public float TickOverflow(float currentHP, float newMaxHP, float deltaTimeSeconds)
        {
            if (newMaxHP <= 0f) return currentHP;
            if (currentHP <= newMaxHP) return currentHP;
            float dec = newMaxHP * GlitchOverflowHpPerSecondRate * deltaTimeSeconds;
            float next = currentHP - dec;
            return next < 0f ? 0f : next;
        }
    }
}

