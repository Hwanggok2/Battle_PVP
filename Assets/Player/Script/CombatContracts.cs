using System;

namespace BattlePvp.Combat
{
    /// <summary>
    /// 피해의 출처를 분리해, 가시(Thorns) 재반사 무한루프를 방지합니다.
    /// </summary>
    public enum DamageSource
    {
        Physical = 0,
        Thorns = 1
    }

    /// <summary>
    /// 실제 HP를 보유하고, 외부에서 피해를 받는 컴포넌트가 구현해야 합니다.
    /// </summary>
    public interface IDamageReceiver
    {
        float CurrentHp { get; }
        float MaxHp { get; }

        /// <summary>
        /// 이미 계산된 피해를 적용합니다.
        /// </summary>
        void ApplyDamage(float amount, DamageSource source);
    }

    /// <summary>
    /// (선택) 가시/특수 반응을 위해 공격자 정보를 함께 받는 확장 인터페이스입니다.
    /// </summary>
    public interface IDamageReceiverWithContext : IDamageReceiver
    {
        /// <summary>
        /// 공격자의 ATK(가시 계산용)를 포함해 피해를 적용합니다.
        /// </summary>
        void ApplyDamage(float amount, DamageSource source, float attackerAttackPower, IDamageReceiver attacker);
    }

    /// <summary>
    /// (선택) 가드 파괴를 위한 인터페이스입니다.
    /// </summary>
    public interface IGuard
    {
        bool IsGuarding { get; }
        void BreakGuard();
    }

    /// <summary>
    /// HP/Identity/Overflow 등 HUD에 필요한 이벤트 계약입니다.
    /// </summary>
    public interface IPlayerStatusSource
    {
        event Action<float, float> HpChanged;              // (current, max)
        event Action<bool, float> OverflowChanged;         // (isOverflow, overlapPercent 0..1)
    }
}

