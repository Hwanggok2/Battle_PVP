using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CombatExecutionStateTests
    {
        [Test]
        public void CastCompletionAndEffectExpiryPreserveTheOriginalCooldown()
        {
            var state = default(CombatSkillExecution);
            Assert.That(state.TryBegin(10d, 9.9d, 0.7d, 35d, out state), Is.True);
            Assert.That(state.CastCompleteAt, Is.EqualTo(10.6d).Within(0.00001d));
            Assert.That(state.TryBegin(11d, 11d, 0.7d, 35d, out _), Is.False);
            Assert.That(state.TryActivate(10.6d, 10d, out _), Is.False);
            state = state.FinishCast();
            Assert.That(state.TryActivate(10.6d, 10d, out state), Is.True);
            Assert.That(state.ActiveUntil, Is.EqualTo(20.6d));
            state = state.EndActive();
            Assert.That(state.CanBegin(44.899d), Is.False);
            Assert.That(state.CanBegin(44.9d), Is.True);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1d)]
        public void InvalidDurationsCannotPartiallyConsumeOrResetAnExistingCooldown(double duration)
        {
            var state = new CombatSkillExecution(false, 0d, 0d, 5d);
            Assert.That(state.TryBegin(5d, 5d, duration, 30d, out CombatSkillExecution rejected), Is.False);
            Assert.That(rejected.CooldownUntil, Is.EqualTo(5d));
            Assert.That(state.TryBegin(5d, 5d, 1d, duration, out rejected), Is.False);
            Assert.That(rejected.IsCasting, Is.False);
            Assert.That(state.TryActivate(5d, duration, out rejected), Is.False);
            Assert.That(rejected.ActiveUntil, Is.Zero);
        }

        [Test]
        public void CancellingAnAcceptedCastOrBuffClearsActionsButNeverRefundsItsCooldown()
        {
            var state = new CombatSkillExecution(true, 11d, 20d, 45d).Cancel();
            Assert.That(state.IsCasting, Is.False);
            Assert.That(state.CastCompleteAt, Is.Zero);
            Assert.That(state.ActiveUntil, Is.Zero);
            Assert.That(state.CooldownUntil, Is.EqualTo(45d));
            Assert.That(state.Cancel().CooldownUntil, Is.EqualTo(45d));
            Assert.That(state.CanBegin(44.99d), Is.False);
            Assert.That(state.CanBegin(45d), Is.True);
        }

        [Test]
        public void PredictedLocksExpireIndependentlyAndCancellationAlsoStopsTheAnimationLock()
        {
            var locks = new CombatActionLocks();
            locks.LockUntil(CombatCastChannel.Strength, 2d);
            locks.LockUntil(CombatCastChannel.Advanced, 5d);
            Assert.That(locks.IsLocked(2d), Is.True);
            Assert.That(locks.IsLocked(5d), Is.False);
            locks.SetAnimationLocked(true);
            Assert.That(locks.IsLocked(50d), Is.True);
            locks.Cancel();
            Assert.That(locks.IsLocked(0d), Is.False);
            locks.LockUntil(CombatCastChannel.Agility, double.NaN);
            Assert.That(locks.IsLocked(0d), Is.False);
        }

        [Test]
        public void AcceptedPostCastAnimationAndInputLocksKeepIndependentAbsoluteDeadlines()
        {
            CombatOwnerAction action = CombatOwnerAction.Begin(2, 10d, 7, 0.75d).WithAnimation(1.333d);
            Assert.That(action.SkillKey, Is.EqualTo(2));
            Assert.That(action.StartedAt, Is.EqualTo(10d));
            Assert.That(action.HasAnimation(10.5d), Is.True, "The 0.3 second cast may already have finished.");
            Assert.That(action.InputFlags, Is.EqualTo(7));
            Assert.That(action.RemainingInput(10.5d), Is.EqualTo(0.25d));
            Assert.That(action.RemainingInput(10.75d), Is.Zero);
            Assert.That(action.HasAnimation(10.75d), Is.True);
            Assert.That(action.HasAnimation(action.AnimationUntil), Is.False);
            Assert.That(CombatOwnerAction.Cancelled.RemainingInput(10d), Is.Zero);
            Assert.That(CombatOwnerAction.Cancelled.HasAnimation(10d), Is.False);
        }

        [Test]
        public void AcceptedOwnerControlsRejectInvalidDeadlinesAndDoNotRequireAnAnimationForInputLocks()
        {
            CombatOwnerAction action = CombatOwnerAction.Begin(22, 10d, 1, 0.15d);
            Assert.That(action.HasAnimation(10d), Is.False);
            Assert.That(action.RemainingInput(10d), Is.EqualTo(0.15d).Within(0.00001d));
            Assert.That(action.WithAnimation(double.NaN).HasAnimation(10d), Is.False);
            Assert.That(action.WithAnimation(double.PositiveInfinity).HasAnimation(10d), Is.False);
            Assert.That(CombatOwnerAction.Begin(2, double.NaN, 7, 1d).SkillKey, Is.EqualTo(-1));
            Assert.That(CombatOwnerAction.Begin(2, 10d, 7, -1d).SkillKey, Is.EqualTo(-1));
            Assert.That(CombatOwnerAction.Begin(2, double.MaxValue, 7, double.MaxValue).SkillKey, Is.EqualTo(-1));
        }

        [Test]
        public void FirstReliableSpawnUsesItsBatchTimeBeforeTheFirstUnreliableTimeSnapshot()
        {
            CombatOwnerAction action = CombatOwnerAction.Begin(2, 1999d, 7, 0.75d).WithAnimation(1.333d);
            double restoreNow = CombatOwnerAction.ResolveRestoreTime(0d, 2000d);
            Assert.That(restoreNow, Is.EqualTo(2000d));
            Assert.That(action.HasAnimation(restoreNow), Is.True);
            Assert.That(action.RemainingInput(restoreNow), Is.Zero);
            Assert.That(action.HasAnimation(CombatOwnerAction.ResolveRestoreTime(0d, 2001d)), Is.False);
            Assert.That(CombatOwnerAction.ResolveRestoreTime(2002d, 2000d), Is.EqualTo(2002d));
            Assert.That(CombatOwnerAction.ResolveRestoreTime(2002d, double.NaN), Is.EqualTo(2002d));
        }

        [Test]
        public void PoisonRefreshCapsStacksAndRetainsReverseTargetTickOrderUntilExactExpiry()
        {
            var stacks = new PoisonStackCollection<object, int>();
            var ticks = new List<PoisonTick<object, int>>();
            object first = new object();
            object second = new object();
            Assert.That(stacks.Add(first, 1, 10d, 3d, 2), Is.True);
            stacks.Add(second, 2, 10d, 3d, 2);
            stacks.Add(first, 3, 11d, 3d, 2);
            stacks.Add(first, 4, 12d, 3d, 2);
            stacks.CollectTicks(12d, 2f, _ => true, ticks);
            Assert.That(ticks.Count, Is.EqualTo(2));
            Assert.That(ticks[0].Target, Is.SameAs(second));
            Assert.That(ticks[1].Target, Is.SameAs(first));
            Assert.That(ticks[1].Damage, Is.EqualTo(4f));
            Assert.That(ticks[1].HitPosition, Is.EqualTo(4));
            stacks.CollectTicks(13d, 2f, _ => true, ticks);
            Assert.That(ticks.Count, Is.EqualTo(1));
            stacks.CollectTicks(15d, 2f, _ => true, ticks);
            Assert.That(ticks, Is.Empty);
            Assert.That(stacks.Count, Is.Zero);
        }

        [Test]
        public void PoisonRejectsInvalidInputsPrunesDeadTargetsAndClampsDamageOverflow()
        {
            var stacks = new PoisonStackCollection<object, int>();
            var ticks = new List<PoisonTick<object, int>>();
            object target = new object();
            Assert.That(stacks.Add(target, 0, double.NaN, 3d, 2), Is.False);
            Assert.That(stacks.Add(target, 0, 0d, -1d, 2), Is.False);
            Assert.That(stacks.Add(target, 0, 0d, 3d, 0), Is.False);
            Assert.That(stacks.Add(null, 0, 0d, 3d, 2), Is.False);
            stacks.Add(target, 0, 0d, 3d, 2);
            stacks.Add(target, 0, 0d, 3d, 2);
            stacks.CollectTicks(1d, float.MaxValue, _ => true, ticks);
            Assert.That(ticks[0].Damage, Is.EqualTo(float.MaxValue));
            stacks.CollectTicks(1d, float.NaN, _ => true, ticks);
            Assert.That(ticks, Is.Empty);
            Assert.That(stacks.Count, Is.EqualTo(1));
            stacks.CollectTicks(2d, 2f, _ => false, ticks);
            Assert.That(stacks.Count, Is.Zero);
            Assert.That(ticks, Is.Empty);
        }

        [TestCase("CoMonostatStrSkill", "_isCastingMonostatStrSkill", "_monostatStrSkillCooldownUntil", "_monostatStrSkillActiveUntil")]
        [TestCase("CoMonostatAgiSkill", "_isCastingMonostatAgiSkill", "_monostatAgiSkillCooldownUntil", "_monostatAgiSkillActiveUntil")]
        public void ActualCastRoutineRejectsMissingIdentityWithoutRefundingCooldown(
            string routineName, string castingField, string cooldownField, string activeField)
        {
            var owner = new GameObject("Rejected combat cast");
            owner.SetActive(false);
            try
            {
                PlayerCombat combat = owner.AddComponent<PlayerCombat>();
                Set(combat, "_hitboxes", Array.Empty<MeleeHitBox>());
                Set(combat, castingField, true);
                Set(combat, cooldownField, 100d);
                var routine = (IEnumerator)typeof(PlayerCombat).GetMethod(routineName,
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(combat, null);
                Assert.That(routine.MoveNext(), Is.False);
                Assert.That(Get<bool>(combat, castingField), Is.False);
                Assert.That(Get<double>(combat, cooldownField), Is.EqualTo(100d));
                Assert.That(Get<double>(combat, activeField), Is.Zero);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void PoisonReappliedAtExpiryStartsWithOneStackBeforeTheNextTimerTick()
        {
            var stacks = new PoisonStackCollection<object, int>();
            var ticks = new List<PoisonTick<object, int>>();
            object target = new object();
            for (int i = 0; i < 3; i++) stacks.Add(target, 0, 0d, 6d, 3);
            stacks.Add(target, 1, 6d, 6d, 3);
            stacks.CollectTicks(6d, 2f, _ => true, ticks);
            Assert.That(ticks.Count, Is.EqualTo(1));
            Assert.That(ticks[0].Damage, Is.EqualTo(2f));
            Assert.That(ticks[0].HitPosition, Is.EqualTo(1));
            stacks.CollectTicks(12d, 2f, _ => true, ticks);
            Assert.That(stacks.Count, Is.Zero);
        }

        [TestCase("HandleDied", 1)]
        [TestCase("HandleRevived", 1)]
        [TestCase("OnDisable", 0)]
        public void OwnerDeathAndRevivalPreserveAppliedPoisonWhileDisposalClearsIt(string lifecycle, int expectedCount)
        {
            var owner = new GameObject("Poison lifetime owner");
            owner.SetActive(false);
            try
            {
                PlayerCombat combat = owner.AddComponent<PlayerCombat>();
                Set(combat, "_hitboxes", Array.Empty<MeleeHitBox>());
                var stacks = Get<PoisonStackCollection<IDamageReceiver, Vector3>>(combat, "_poisonStacks");
                stacks.Add(new PoisonTarget(), Vector3.zero, 0d, 6d, 3);
                typeof(PlayerCombat).GetMethod(lifecycle, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(combat, null);
                Assert.That(stacks.Count, Is.EqualTo(expectedCount));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void OwnerRebindSeedsRequestsFromRetainedServerWithoutRollingBackInFlightRequests()
        {
            var owner = new GameObject("Rebound combat owner");
            owner.SetActive(false);
            try
            {
                PlayerCombat combat = owner.AddComponent<PlayerCombat>();
                Set(combat, "_hitboxes", Array.Empty<MeleeHitBox>());
                Set(combat, "_lastServerAttackSequence", 42u);
                Set(combat, "_lastServerSkillSequence", uint.MaxValue);
                MethodInfo restore = typeof(PlayerCombat).GetMethod("RestoreLocalRequestSequences",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                restore.Invoke(combat, null);
                Assert.That(Get<uint>(combat, "_nextLocalAttackSequence"), Is.EqualTo(42u));
                Assert.That(Get<uint>(combat, "_nextLocalSkillSequence"), Is.EqualTo(uint.MaxValue));
                Assert.That(CombatRequestSequences.Next(uint.MaxValue), Is.EqualTo(1u));
                Set(combat, "_nextLocalAttackSequence", 43u);
                Set(combat, "_nextLocalSkillSequence", 1u);
                restore.Invoke(combat, null);
                Assert.That(Get<uint>(combat, "_nextLocalAttackSequence"), Is.EqualTo(43u));
                Assert.That(Get<uint>(combat, "_nextLocalSkillSequence"), Is.EqualTo(1u));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        private static void Set<T>(PlayerCombat combat, string name, T value) =>
            typeof(PlayerCombat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(combat, value);
        private static T Get<T>(PlayerCombat combat, string name) =>
            (T)typeof(PlayerCombat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);

        private sealed class PoisonTarget : IDamageReceiver
        {
            public float CurrentHp => 100f;
            public float MaxHp => 100f;
            public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition) { }
        }
    }
}
