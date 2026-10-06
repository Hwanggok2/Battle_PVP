using System;
using BattlePvp.Stats;
using UnityEngine;

namespace BattlePvp.Characters
{
    [Serializable]
    public struct CharacterStatModifiers
    {
        [Range(.5f, 1.5f)] public float Health;
        [Range(.5f, 1.5f)] public float Defense;
        [Range(.5f, 1.5f)] public float Attack;
        [Range(.5f, 1.5f)] public float MoveSpeed;
        [Range(.5f, 1.5f)] public float AttackSpeed;

        public static CharacterStatModifiers Baseline => new CharacterStatModifiers(1, 1, 1, 1, 1);
        public CharacterStatModifiers(float health, float defense, float attack, float moveSpeed, float attackSpeed)
        { Health = health; Defense = defense; Attack = attack; MoveSpeed = moveSpeed; AttackSpeed = attackSpeed; }

        // Missing fields in older assets must retain the original character's balance.
        private static float Valid(float value) => float.IsFinite(value) && value > 0 ? Mathf.Clamp(value, .5f, 1.5f) : 1f;
        public CharacterStatModifiers Validated => new CharacterStatModifiers(
            Valid(Health), Valid(Defense), Valid(Attack), Valid(MoveSpeed), Valid(AttackSpeed));

        public DerivedCombatStats Apply(DerivedCombatStats basis)
        {
            var value = Validated;
            return new DerivedCombatStats(basis.AttackPower * value.Attack, basis.PenetrationPercent,
                basis.MaxHp * value.Health, basis.RegenPerSecond,
                Mathf.Min(basis.DefenseEfficiencyPercent * value.Defense, StatBalanceCalculator.Config.DefenseEfficiencyHardCap * 100f),
                basis.DefenseBonusNormalized, basis.MoveSpeed * value.MoveSpeed,
                basis.AttackSpeed * value.AttackSpeed, basis.IncomingDamageMultiplier);
        }
    }
}
