using System;
using System.Collections;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CombatSkillPlanTests
    {
        [TestCase(IdentityType.Monostat, StatKind.CON, 0, JobSkillKind.MonostatConKick, "_monostatConSkillData")]
        [TestCase(IdentityType.Monostat, StatKind.DEF, 0, JobSkillKind.MonostatDefTaunt, "_monostatDefSkillData")]
        [TestCase(IdentityType.Strategist, StatKind.STR, 0, JobSkillKind.StrategistRoll, "_strategistRollSkillData")]
        [TestCase(IdentityType.Strategist, StatKind.STR, 1, JobSkillKind.StrategistPresetChange, "_strategistPresetSkillData")]
        [TestCase(IdentityType.Polymath, StatKind.STR, 0, JobSkillKind.PolymathRoll, "_polymathRollSkillData")]
        [TestCase(IdentityType.Polymath, StatKind.STR, 1, JobSkillKind.PolymathWeaponSwap, "_polymathWeaponSwapSkillData")]
        public void OwnerSelectionAndServerResolutionUseTheSameAssignedAsset(
            IdentityType type, StatKind primary, int slot, JobSkillKind kind, string field)
        {
            GameObject owner = CreateOwner(out PlayerCombat combat, out StatManager stats);
            JobSkillData data = ScriptableObject.CreateInstance<JobSkillData>();
            try
            {
                SetIdentity(stats, new Identity(type, primary));
                Set(data, "_skillKind", kind);
                Set(combat, field, data);
                Set(combat, "_selectedSkillIndex", slot);
                Assert.That(Call(combat, "ResolveSelectedAdvancedSkillData"), Is.SameAs(data));
                Assert.That(Call(combat, "ResolveAdvancedSkillData", (int)kind), Is.SameAs(data));
                Set(data, "_skillKind", (JobSkillKind)999);
                Assert.That(Call(combat, "ResolveSelectedAdvancedSkillData"), Is.Null, "Wrong asset kind is not a usable skill.");
                Assert.That(Call(combat, "ResolveAdvancedSkillData", (int)kind), Is.Null);
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(data); }
        }

        [Test]
        public void UnavailablePolymathPresetStaysUnavailableEvenWithAnAssignedAsset()
        {
            GameObject owner = CreateOwner(out PlayerCombat combat, out StatManager stats);
            JobSkillData data = ScriptableObject.CreateInstance<JobSkillData>();
            try
            {
                SetIdentity(stats, new Identity(IdentityType.Polymath, StatKind.STR));
                Set(data, "_skillKind", JobSkillKind.PolymathPresetChange);
                Set(combat, "_polymathPresetSkillData", data);
                Assert.That(Call(combat, "ResolveAdvancedSkillData", (int)JobSkillKind.PolymathPresetChange), Is.Null);
                Assert.That(Call(combat, "ResolveAssignedAdvancedSkillData", (int)JobSkillKind.PolymathPresetChange), Is.SameAs(data),
                    "Assigned visual/cleanup data is separate from permission to request a skill.");
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(data); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CastCoroutineAppliesTheAcceptedPlanOnlyWhileTheSkillRemainsAvailable(bool changeIdentity)
        {
            GameObject owner = CreateOwner(out PlayerCombat combat, out StatManager stats);
            JobSkillData data = ScriptableObject.CreateInstance<JobSkillData>();
            try
            {
                SetIdentity(stats, new Identity(IdentityType.Monostat, StatKind.DEF));
                Set(data, "_skillKind", JobSkillKind.MonostatDefTaunt);
                Set(data, "_tauntReadyDurationSeconds", 3f);
                Set(combat, "_monostatDefSkillData", data);
                Assert.That(AdvancedSkillPlan.TryBegin(data.SkillKind, default, 0d, 0d, 0.1f, 5f, true, out AdvancedSkillPlan plan), Is.True);
                Set(combat, "_advancedCastingSkillKey", (int)data.SkillKind);
                Set(combat, "_advancedCastCompleteAt", 0d);
                if (changeIdentity) SetIdentity(stats, new Identity(IdentityType.Monostat, StatKind.CON));
                var routine = (IEnumerator)Call(combat, "CoAdvancedSkillCast", plan, data, Vector3.forward);
                Assert.That(routine.MoveNext(), Is.False);
                Assert.That(Get<int>(combat, "_advancedCastingSkillKey"), Is.EqualTo(-1));
                Assert.That(Get<int>(combat, "_advancedActiveSkillKey"), Is.EqualTo(changeIdentity ? -1 : (int)data.SkillKind));
                Assert.That(Get<double>(combat, "_advancedCastCompleteAt"), Is.Zero);
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(data); }
        }

        [TestCase(JobSkillKind.StrategistRoll)]
        [TestCase(JobSkillKind.PolymathRoll)]
        [TestCase(JobSkillKind.MonostatConKick)]
        public void StartAndHitWindowEffectsAreNotRepeatedByCastCompletion(JobSkillKind kind)
        {
            Assert.That(AdvancedSkillPlan.TryBegin(kind, default, 10d, 9.9d, 0.3f, 5f, true, out AdvancedSkillPlan plan), Is.True);
            Assert.That(plan.CanApplyAtFinish(true, true), Is.False);
            Assert.That(plan.ApplyAtStart, Is.EqualTo(kind != JobSkillKind.MonostatConKick));
            Assert.That(plan.HasHitWindow, Is.EqualTo(kind == JobSkillKind.MonostatConKick));
            Assert.That(plan.Execution.Cancel().CooldownUntil, Is.EqualTo(14.9d));
        }

        [Test]
        public void ZeroCastRequiresNoAnimationBeforeApplyingImmediately()
        {
            Assert.That(AdvancedSkillPlan.TryBegin(JobSkillKind.PolymathWeaponSwap, default, 2d, 2d, 0f, 0f, false, out AdvancedSkillPlan instant), Is.True);
            Assert.That(instant.AppliesImmediately, Is.True);
            Assert.That(instant.LocksMovement, Is.False);
            Assert.That(instant.CanApplyAtFinish(true, true), Is.False);
            Assert.That(AdvancedSkillPlan.TryBegin(JobSkillKind.PolymathWeaponSwap, default, 2d, 2d, 0f, 0f, true, out AdvancedSkillPlan animated), Is.True);
            Assert.That(animated.AppliesImmediately, Is.False);
            Assert.That(animated.CanApplyAtFinish(true, true), Is.True);
        }

        [Test]
        public void RejectedPlansAndCancelledCastsCannotBypassCooldown()
        {
            Assert.That(AdvancedSkillPlan.TryBegin(JobSkillKind.MonostatDefTaunt, default, 5d, 5d, 1f, 10f, true, out AdvancedSkillPlan plan), Is.True);
            CombatSkillExecution cancelled = plan.Execution.Cancel();
            Assert.That(AdvancedSkillPlan.TryBegin(plan.Kind, cancelled, 14.99d, 14.99d, 1f, 10f, true, out _), Is.False);
            Assert.That(AdvancedSkillPlan.TryBegin(plan.Kind, cancelled, 15d, 15d, 1f, 10f, true, out _), Is.True);
            Assert.That(AdvancedSkillPlan.TryBegin(plan.Kind, default, 1d, 1d, float.NaN, 10f, true, out AdvancedSkillPlan rejected), Is.False);
            Assert.That(rejected.CanApplyAtFinish(true, true), Is.False);
        }

        [Test]
        public void PresetPlanKeepsReturnInvestmentSeparateFromItemsAndPreservesHealthConversion()
        {
            var from = new StatContainer { STR = new StatSlot { Invested = 30f, Item = 5f } };
            var target = new StatContainer { AGI = new StatSlot { Invested = 30f, Item = 2f } };
            Assert.That(CombatPresetPlan.TryCreate(true, from, target, default, false, out CombatPresetPlan outward), Is.True);
            target.STR.Item = 40f;
            Assert.That(CombatPresetPlan.TryCreate(true, target, outward.Target, outward.ReturnPreset, true, out CombatPresetPlan inward), Is.True);
            Assert.That(inward.Target.STR.Invested, Is.EqualTo(30f));
            Assert.That(inward.Target.STR.Item, Is.EqualTo(5f));
            Assert.That(inward.HasReturnPreset, Is.False);
            Assert.That(CombatPresetPlan.DominantStat(target), Is.EqualTo(StatKind.STR));
            CombatPresetPlan.ResolveVitals(150f, 130f, 100f, 0.5f, 20f, out float hp, out float shield);
            Assert.That(hp, Is.EqualTo(100f));
            Assert.That(shield, Is.EqualTo(50f));
            Assert.That(CombatPresetPlan.TryCreate(true, from, default, outward.ReturnPreset, true, out _), Is.False);
            Assert.That(outward.HasReturnPreset, Is.True);
        }

        private static GameObject CreateOwner(out PlayerCombat combat, out StatManager stats)
        {
            var owner = new GameObject("Skill plan adapter test");
            owner.SetActive(false);
            stats = EditorTestLifecycle.AddNetwork<StatManager>(owner);
            combat = owner.AddComponent<PlayerCombat>();
            Set(combat, "_statManager", stats);
            Set(combat, "_hitboxes", Array.Empty<MeleeHitBox>());
            return owner;
        }

        private static void SetIdentity(StatManager stats, Identity identity) =>
            typeof(StatManager).GetProperty(nameof(StatManager.CurrentIdentity)).SetValue(stats, identity);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
