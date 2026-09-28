using UnityEngine;

namespace BattlePvp.Combat
{
    public static class SkillDescription
    {
        public static string Build(JobSkillData data)
        {
            if (data == null) return string.Empty;
            string effect = data.SkillKind switch
            {
                JobSkillKind.MonostatStrLifesteal => $"{data.DurationSeconds:0.#}초 동안 검으로 준 피해의 {data.LifestealRatio * 100:0}%를 체력으로 회복합니다.",
                JobSkillKind.MonostatAgiPoison => $"{data.DurationSeconds:0.#}초 동안 검에 독을 바릅니다. 적중 시 독을 최대 {data.PoisonMaxStacks}회 중첩합니다. 중첩당 초당 {data.PoisonDamagePerStackPerSecond:0.#} 피해, {data.PoisonStackDurationSeconds:0.#}초 지속.",
                JobSkillKind.MonostatConKick => $"전방의 적을 발로 차 공격력의 {data.KickDamageMultiplier * 100:0}% 피해를 주고 {data.KickKnockbackDistance:0.#}m 밀어냅니다. {data.KickSlowDurationSeconds:0.#}초 동안 이동 속도가 감소합니다.",
                JobSkillKind.MonostatDefTaunt => $"다음 검 타격에 도발을 준비합니다. 적중한 적은 {data.TauntDurationSeconds:0.#}초 동안 자신을 향해 이동합니다. 도발 중 받는 피해가 감소하고 피해를 반사합니다.",
                JobSkillKind.StrategistRoll or JobSkillKind.PolymathRoll => $"이동 방향으로 {data.RollDistance:0.#}m 빠르게 이동합니다.",
                JobSkillKind.StrategistPresetChange => "저장한 스탯 프리셋으로 전환하고, 다시 사용하면 원래 배분으로 돌아갑니다. 주 스탯에 따라 추가 효과를 받습니다.\n미설정 기본값: STR 18 · CON 6 · AGI 3 · DEF 3",
                JobSkillKind.PolymathPresetChange => "설정된 스탯 프리셋으로 전환합니다. 미설정 시 STR 18 · CON 6 · AGI 3 · DEF 3을 사용합니다.",
                JobSkillKind.PolymathWeaponSwap => $"검과 활을 교체합니다. 교체 후 {data.WeaponSwapMoveBonusDurationSeconds:0.#}초 동안 이동 속도가 증가하고 다음 공격이 강화됩니다. 활은 공격 버튼을 길게 눌러 충전합니다.",
                _ => string.Empty
            };
            return $"{data.DisplayName}\n\n{effect}\n\n시전 {data.CastSeconds:0.#}초  ·  재사용 {data.CooldownSeconds:0.#}초";
        }
    }
}
