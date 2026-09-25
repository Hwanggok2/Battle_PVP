using System;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class CombatPresentationTests
    {
        [Test]
        public void HudPhaseOrderAndDeadlineBoundariesMatchCombatDisplay()
        {
            SkillHudState casting = SkillHudPresenter.Build(Snapshot(true, 12d, 15d, 20d), 10d);
            Assert.That(casting.Phase, Is.EqualTo(SkillHudPhase.Casting));
            Assert.That(casting.RemainingSeconds, Is.EqualTo(2f));
            Assert.That(casting.NormalizedFill, Is.EqualTo(0.5f));
            Assert.That(SkillHudPresenter.Build(Snapshot(true, 10d, 15d, 20d), 10d).Phase,
                Is.EqualTo(SkillHudPhase.Casting), "The casting flag remains authoritative at its deadline.");

            SkillHudState active = SkillHudPresenter.Build(Snapshot(false, 12d, 15d, 20d), 10d);
            Assert.That(active.Phase, Is.EqualTo(SkillHudPhase.Active));
            Assert.That(active.NormalizedFill, Is.EqualTo(1f));
            SkillHudState cooldown = SkillHudPresenter.Build(Snapshot(false, 12d, 15d, 20d), 15d);
            Assert.That(cooldown.Phase, Is.EqualTo(SkillHudPhase.Cooldown));
            Assert.That(cooldown.NormalizedFill, Is.EqualTo(0.5f));
            Assert.That(SkillHudPresenter.Build(Snapshot(false, 12d, 15d, 20d), 20d).Phase,
                Is.EqualTo(SkillHudPhase.Ready));
            Assert.That(SkillHudPresenter.Build(default, 10d).Phase, Is.EqualTo(SkillHudPhase.Hidden));
        }

        [Test]
        public void HudPublicationThrottlesEquivalentValuesButPublishesPhaseChangesImmediately()
        {
            var presenter = new SkillHudPresenter();
            SkillHudState casting = SkillHudPresenter.Build(Snapshot(true, 12d, 15d, 20d), 10d);
            Assert.That(presenter.ShouldPublish(casting, false, 0f), Is.True);
            Assert.That(presenter.ShouldPublish(casting, false, 0.02f), Is.False);
            SkillHudState active = SkillHudPresenter.Build(Snapshot(false, 12d, 15d, 20d), 10d);
            Assert.That(presenter.ShouldPublish(active, false, 0.03f), Is.True);
            Assert.That(presenter.ShouldPublish(active, false, 0.1f), Is.False);
            Assert.That(presenter.ShouldPublish(active, false, 0.14f), Is.True);
            Assert.That(presenter.ShouldPublish(active, true, 0.15f), Is.True);
        }

        [TestCase(IdentityType.Monostat, StatKind.STR, 0, "흡혈")]
        [TestCase(IdentityType.Monostat, StatKind.AGI, 0, "독 바르기")]
        [TestCase(IdentityType.Strategist, StatKind.STR, 1, "프리셋")]
        [TestCase(IdentityType.Polymath, StatKind.STR, 1, "무기")]
        public void HudNamesUseConfiguredValuesOrExistingIdentityFallback(
            IdentityType type, StatKind primary, int selected, string fallback)
        {
            var identity = new Identity(type, primary);
            Assert.That(SkillHudPresenter.ResolveName(identity, selected, " "), Is.EqualTo(fallback));
            Assert.That(SkillHudPresenter.ResolveName(identity, selected, "Custom"), Is.EqualTo("Custom"));
        }

        [TestCase(10d, 9d, 2f, 0f)]
        [TestCase(10d, 11d, 2f, 0.5f)]
        [TestCase(10d, 15d, 2f, 0.98f)]
        public void AnimationSeekKeepsExistingClampedTiming(double started, double now, float length, float expected)
        {
            Assert.That(CombatSkillPresentation.GetNormalizedAnimationTime(started, now, length),
                Is.EqualTo(expected).Within(0.0001f));
            Assert.That(CombatSkillPresentation.GetNormalizedAnimationTime(double.NaN, now, length), Is.Zero);
        }

        [Test]
        public void SwordPresentationRestoresBothModelsWhenTargetOrEffectChanges()
        {
            var owner = new GameObject("Combat presentation owner");
            var first = new GameObject("First sword");
            var second = new GameObject("Second sword");
            Material original = CreateMaterial();
            Material firstEffect = CreateMaterial();
            Material secondEffect = CreateMaterial();
            var presentation = new CombatSkillPresentation(owner, null);
            try
            {
                Renderer firstRenderer = first.AddComponent<MeshRenderer>();
                Renderer secondRenderer = second.AddComponent<MeshRenderer>();
                firstRenderer.sharedMaterials = new[] { original, original };
                secondRenderer.sharedMaterial = original;
                presentation.SetSwordMaterial(first, firstEffect);
                CollectionAssert.AreEqual(new[] { firstEffect, firstEffect }, firstRenderer.sharedMaterials);
                presentation.SetSwordMaterial(first, secondEffect);
                CollectionAssert.AreEqual(new[] { secondEffect, secondEffect }, firstRenderer.sharedMaterials);
                presentation.SetSwordMaterial(second, firstEffect);
                CollectionAssert.AreEqual(new[] { original, original }, firstRenderer.sharedMaterials);
                presentation.Cancel();
                presentation.Cancel();
                Assert.That(secondRenderer.sharedMaterial, Is.SameAs(original));
                Assert.That(owner.GetComponent<PlayerCombat>(), Is.Null);
            }
            finally
            {
                presentation.Dispose();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(firstEffect);
                Object.DestroyImmediate(secondEffect);
            }
        }

        [Test]
        public void AuraPresentationOwnsTimedExpiryShieldFallbackAndDisposal()
        {
            var owner = new GameObject("Aura presentation owner");
            Material str = CreateMaterial();
            Material con = CreateMaterial();
            var settings = new SkillAuraSettings(str, null, con, null, SkillAuraShape.Sphere,
                false, false, Vector3.one, Vector3.zero);
            var presentation = new SkillAuraPresentation(owner.transform, null, settings);
            try
            {
                presentation.ShowTimed(StatKind.STR, 2f, 10d, 20f);
                Transform aura = owner.transform.Find("Strategist_STR_Attack_Aura");
                Assert.That(aura, Is.Not.Null);
                Assert.That(aura.GetComponent<Collider>(), Is.Null);
                Assert.That(aura.GetComponent<Renderer>().sharedMaterial, Is.SameAs(str));
                presentation.Update(12d, 20f);
                Assert.That(aura.GetComponent<Renderer>().sharedMaterial, Is.SameAs(con));
                presentation.Update(12d, 0f);
                Assert.That(aura.gameObject.activeSelf, Is.False);
                presentation.ShowTimed(StatKind.STR, 2f, 20d, 0f);
                presentation.Cancel();
                presentation.Update(20d, 0f);
                Assert.That(aura.gameObject.activeSelf, Is.False);
                presentation.Dispose();
                Assert.That(owner.transform.childCount, Is.Zero);
            }
            finally
            {
                presentation.Dispose();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(str);
                Object.DestroyImmediate(con);
            }
        }

        [TestCase("HandleDied")]
        [TestCase("HandleRevived")]
        [TestCase("OnDisable")]
        public void OwnerLifecycleCancelsActionStateAndPresentationTogether(string lifecycleMethod)
        {
            var owner = new GameObject("Combat lifetime owner");
            owner.SetActive(false);
            var sword = new GameObject("Sword");
            sword.transform.SetParent(owner.transform);
            Material original = CreateMaterial();
            Material effect = CreateMaterial();
            CombatSkillPresentation presentation = null;
            try
            {
                PlayerCombat combat = owner.AddComponent<PlayerCombat>();
                SetField(combat, "_hitboxes", Array.Empty<MeleeHitBox>());
                SetField(combat, "_advancedCastingSkillKey", (int)JobSkillKind.MonostatConKick);
                SetField(combat, "_advancedCastCompleteAt", 100d);
                SetField(combat, "_isCastingMonostatStrSkill", true);
                SetField(combat, "_monostatStrSkillCastCompleteAt", 100d);
                SetField(combat, "_monostatStrSkillActiveUntil", 100d);
                SetField(combat, "_monostatStrSkillCooldownUntil", 120d);
                SetField(combat, "_monostatAgiSkillCooldownUntil", 130d);
                SetField(combat, "_acceptedSkillAnimationKey", (int)JobSkillKind.MonostatConKick);
                SetField(combat, "_acceptedSkillAnimationUntil", 100d);
                SetField(combat, "_acceptedSkillInputFlags", SkillInputLockFlags.Move | SkillInputLockFlags.Attack);
                SetField(combat, "_acceptedSkillInputUntil", 90d);
                GetField<CombatActionLocks>(combat, "_actionLocks").LockUntil(CombatCastChannel.Advanced, 100d);
                SetField(combat, "_serverReportableAttackSequence", 42u);
                SetField(combat, "_nextAttackDamageMultiplier", 2f);
                Renderer renderer = sword.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = original;
                presentation = new CombatSkillPresentation(owner, null);
                presentation.SetSwordMaterial(sword, effect);
                SetField(combat, "_skillPresentation", presentation);

                typeof(PlayerCombat).GetMethod(lifecycleMethod, BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(combat, null);

                Assert.That(GetField<int>(combat, "_advancedCastingSkillKey"), Is.EqualTo(-1));
                Assert.That(GetField<double>(combat, "_advancedCastCompleteAt"), Is.Zero);
                Assert.That(GetField<bool>(combat, "_isCastingMonostatStrSkill"), Is.False);
                Assert.That(GetField<double>(combat, "_monostatStrSkillCastCompleteAt"), Is.Zero);
                Assert.That(GetField<double>(combat, "_monostatStrSkillActiveUntil"), Is.Zero);
                Assert.That(GetField<double>(combat, "_monostatStrSkillCooldownUntil"), Is.EqualTo(120d));
                Assert.That(GetField<double>(combat, "_monostatAgiSkillCooldownUntil"), Is.EqualTo(130d));
                Assert.That(GetField<int>(combat, "_acceptedSkillAnimationKey"), Is.EqualTo(-1));
                Assert.That(GetField<double>(combat, "_acceptedSkillAnimationUntil"), Is.Zero);
                Assert.That(GetField<SkillInputLockFlags>(combat, "_acceptedSkillInputFlags"), Is.EqualTo(SkillInputLockFlags.None));
                Assert.That(GetField<double>(combat, "_acceptedSkillInputUntil"), Is.Zero);
                Assert.That(GetField<CombatActionLocks>(combat, "_actionLocks").IsLocked(0d), Is.False);
                Assert.That(GetField<uint>(combat, "_serverReportableAttackSequence"), Is.Zero);
                Assert.That(GetField<float>(combat, "_nextAttackDamageMultiplier"), Is.EqualTo(1f));
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
            }
            finally
            {
                presentation?.Dispose();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(effect);
            }
        }

        private static SkillHudSnapshot Snapshot(bool casting, double castEnd, double activeEnd, double cooldownEnd)
            => new SkillHudSnapshot("Skill", null, 0, 1, casting, castEnd, activeEnd, cooldownEnd, 4f, 10f);

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            return new Material(shader);
        }

        private static void SetField<T>(PlayerCombat combat, string name, T value)
            => typeof(PlayerCombat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(combat, value);

        private static T GetField<T>(PlayerCombat combat, string name)
            => (T)typeof(PlayerCombat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);
    }
}
