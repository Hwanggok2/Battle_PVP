using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CombatMovementAndQueryTests
    {
        [TestCase(false, 0f, 0f)]
        [TestCase(true, 0f, 10f)]
        [TestCase(true, 10f, 0f)]
        public void GuardBreakWaitsForAcceptedDamage(bool accepted, float hpDamage, float shieldDamage)
        {
            var attacker = new GameObject("Combat guard result attacker");
            var defender = new GameObject("Combat guard result defender");
            AttackData attack = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                StatManager attackerStats = attacker.AddComponent<StatManager>();
                var stats = new StatContainer();
                stats.STR.Invested = 30f;
                attackerStats.ApplyLocalSceneStats(stats);
                Assert.That(attackerStats.CurrentIdentity.Type, Is.EqualTo(IdentityType.Monostat));
                StatManager defenderStats = defender.AddComponent<StatManager>();
                AttackProcessor processor = attacker.AddComponent<AttackProcessor>();
                typeof(AttackProcessor).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(processor, null);
                attack.damage = 1f;
                var receiver = new GuardedReceiver(new DamageResult(accepted, hpDamage, shieldDamage));

                processor.ProcessHit(attack, defenderStats, receiver, Vector3.zero, receiver);

                Assert.That(receiver.RequestedDamage, Is.GreaterThan(0f));
                Assert.That(receiver.WasGuardingDuringDamage, Is.True);
                Assert.That(receiver.IsGuarding, Is.EqualTo(!accepted));
            }
            finally
            {
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(attacker);
                Object.DestroyImmediate(defender);
            }
        }

        private sealed class GuardedReceiver : IDamageReceiverWithResult, IGuard
        {
            private readonly DamageResult _result;
            public float CurrentHp => 100f;
            public float MaxHp => 100f;
            public bool IsGuarding { get; private set; } = true;
            public bool WasGuardingDuringDamage { get; private set; }
            public float RequestedDamage { get; private set; }

            public GuardedReceiver(DamageResult result) { _result = result; }
            public void BreakGuard() { IsGuarding = false; }
            public DamageResult ApplyDamage(DamageRequest request)
            {
                RequestedDamage = request.Amount;
                WasGuardingDuringDamage = IsGuarding;
                return _result;
            }
            public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition)
                => throw new InvalidOperationException("Expected result-aware damage path.");
            public void ApplyDamage(float amount, DamageSource source, float attackerAttackPower,
                IDamageReceiver attacker, Vector3 hitPosition)
                => throw new InvalidOperationException("Expected result-aware damage path.");
        }

        [Test]
        public void ChargeEndingPreservesBuffAndSlowUntilTheirOwnExpiry()
        {
            var effects = new MovementEffects();
            effects.Set(CombatEffectSources.WeaponSwap, 1.5f, 10f, 0d);
            effects.Set(CombatEffectSources.BowCharge, 0.5f, 5f, 0d);
            effects.Set(CombatEffectSources.KickSlow, 0.4f, 3f, 0d);
            Assert.That(effects.Evaluate(1d), Is.EqualTo(0.3f).Within(0.0001f));
            effects.Remove(CombatEffectSources.BowCharge);
            Assert.That(effects.Evaluate(1d), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(effects.Evaluate(3d), Is.EqualTo(1.5f));
            effects.Set(CombatEffectSources.WeaponSwap, float.NaN, 2f, 3d);
            Assert.That(effects.Evaluate(4d), Is.EqualTo(1.5f));
            Assert.That(effects.Evaluate(10d), Is.EqualTo(1f));
        }

        [Test]
        public void InputLockRemovalAndExpiryKeepOtherOwnersLocked()
        {
            var locks = new InputLockEffects();
            locks.Set(CombatEffectSources.EmoteInput, SkillInputLockFlags.Attack, 10f, 0d);
            locks.Set(CombatEffectSources.ServerCastMovement, SkillInputLockFlags.Move, float.PositiveInfinity, 0d);
            locks.Set(CombatEffectSources.PredictedCastMovement, SkillInputLockFlags.Move, 2f, 0d);
            locks.Remove(CombatEffectSources.PredictedCastMovement);
            Assert.That(locks.Evaluate(3d), Is.EqualTo(SkillInputLockFlags.Attack | SkillInputLockFlags.Move));
            locks.Remove(CombatEffectSources.ServerCastMovement);
            Assert.That(locks.Evaluate(3d), Is.EqualTo(SkillInputLockFlags.Attack));
            Assert.That(locks.Evaluate(10d), Is.EqualTo(SkillInputLockFlags.None));
        }

        [Test]
        public void RingWrapAndFrontDiscardPreserveSnapshotOrder()
        {
            var ring = new FixedRingBuffer<int>(8);
            var reference = new List<int>();
            for (int value = 0; value < 100; value++)
            {
                ring.Add(value);
                reference.Add(value);
                if (reference.Count > 8) reference.RemoveAt(0);
                if (value % 7 == 0)
                {
                    ring.TrimToCount(4);
                    while (reference.Count > 4) reference.RemoveAt(0);
                }
                Assert.That(ring.Count, Is.EqualTo(reference.Count));
                for (int i = 0; i < reference.Count; i++) Assert.That(ring[i], Is.EqualTo(reference[i]));
            }
            ring.Clear();
            Assert.That(ring.Count, Is.Zero);
            ring.Add(123);
            Assert.That(ring[0], Is.EqualTo(123));
            Assert.Throws<ArgumentOutOfRangeException>(() => { int ignored = ring[1]; });
        }

        [Test]
        public void FullOverlapAndRayBuffersRetryWithoutLosingColliders()
        {
            var objects = new List<GameObject>();
            var query = new CombatPhysicsQuery();
            Vector3 origin = new Vector3(15000f, 15000f, 15000f);
            try
            {
                for (int i = 0; i < 48; i++)
                {
                    var box = new GameObject("Combat query test " + i);
                    box.transform.position = origin + Vector3.forward * (i + 1);
                    box.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
                    objects.Add(box);
                }
                Physics.SyncTransforms();
                Assert.That(query.OverlapBox(origin + Vector3.forward * 25f,
                    new Vector3(1f, 1f, 25f), Quaternion.identity), Is.EqualTo(48));
                Collider[] warmedColliders = query.Colliders;
                Assert.That(query.OverlapBox(origin + Vector3.forward * 25f,
                    new Vector3(1f, 1f, 25f), Quaternion.identity), Is.EqualTo(48));
                Assert.That(query.Colliders, Is.SameAs(warmedColliders));
                Assert.That(query.Raycast(origin, Vector3.forward, 50f), Is.EqualTo(48));
                RaycastHit[] warmedHits = query.Hits;
                Assert.That(query.Raycast(origin, Vector3.forward, 50f), Is.EqualTo(48));
                Assert.That(query.Hits, Is.SameAs(warmedHits));
            }
            finally { foreach (GameObject item in objects) Object.DestroyImmediate(item); }
        }

        [Test]
        public void HidingAndRestoringCharacterDoesNotEnableInactiveAttackColliders()
        {
            var root = new GameObject("Combat visibility test");
            try
            {
                PlayerManager manager = root.AddComponent<PlayerManager>();
                var attackObject = new GameObject("Inactive attack collider");
                attackObject.transform.SetParent(root.transform);
                BoxCollider attack = attackObject.AddComponent<BoxCollider>();
                attack.enabled = false;
                var bodyObject = new GameObject("Active body collider");
                bodyObject.transform.SetParent(root.transform);
                BoxCollider body = bodyObject.AddComponent<BoxCollider>();
                var visibility = new BattlePvp.UI.PlayerModelVisibility(root.transform);
                visibility.Hide();
                Assert.That(body.enabled, Is.False);
                visibility.Restore();
                Assert.That(body.enabled, Is.True);
                Assert.That(attack.enabled, Is.False);
                manager.SetInputLock(CombatEffectSources.ServerCastMovement, SkillInputLockFlags.Move, 60f);
                manager.SetInputLock(CombatEffectSources.EmoteInput, SkillInputLockFlags.Attack, 60f);
                typeof(PlayerManager).GetMethod("CancelMovementActions", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, null);
                Assert.That(manager.IsSkillInputLocked(SkillInputLockFlags.Move | SkillInputLockFlags.Attack), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void BodyCacheRefreshesWhenNestedModelAddsANewCollider()
        {
            var root = new GameObject("Combat body cache refresh test");
            try
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform);
                Type historyType = typeof(PlayerCombat).Assembly.GetType("ServerPoseHistory", true);
                Component history = root.AddComponent(historyType);
                historyType.GetMethod("RefreshBodyParts").Invoke(history, null);
                var limb = new GameObject("New nested hitbox");
                limb.transform.SetParent(model.transform);
                HitBodyPart part = limb.AddComponent<HitBodyPart>();
                Collider collider = limb.AddComponent<BoxCollider>();
                object[] arguments = { collider, null, null, null };
                historyType.GetMethod("TryResolveHitCollider").Invoke(history, arguments);
                Assert.That(arguments[1], Is.SameAs(part));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
