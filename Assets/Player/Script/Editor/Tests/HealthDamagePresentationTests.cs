using BattlePvp.Combat;
using BattlePvp.UI;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class HealthDamagePresentationTests
    {
        [Test]
        public void LifeAnimatorBindingResolvesBeforeHealthAwakeAndKeepsSerializedOverride()
        {
            var root = new GameObject("Life binding before Awake");
            root.SetActive(false);
            try
            {
                var model = new GameObject("Animated model");
                model.transform.SetParent(root.transform);
                Animator childAnimator = model.AddComponent<Animator>();
                HealthSystem health = root.AddComponent<HealthSystem>();
                PropertyInfo binding = typeof(HealthSystem).GetProperty("LifeAnimator", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(binding, Is.Not.Null);
                Assert.That(binding.GetValue(health), Is.SameAs(childAnimator));

                Animator explicitAnimator = root.AddComponent<Animator>();
                typeof(HealthSystem).GetField("_animator", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(health, explicitAnimator);
                Assert.That(binding.GetValue(health), Is.SameAs(explicitAnimator));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(DamageSource.Physical)]
        [TestCase(DamageSource.Poison)]
        [TestCase(DamageSource.Thorns)]
        public void LocalVictimTakesPrecedenceOverAttackerWithoutDoubleFeedback(DamageSource source)
        {
            var style = HealthDamagePresentation.ResolveStyle(source, true, true);
            Assert.That(style.UseReceivedDamageHud, Is.EqualTo(source == DamageSource.Physical));
            Assert.That(style.AroundVictim, Is.EqualTo(source != DamageSource.Physical));
            Assert.That(style.PlayStatusFeedback, Is.False);
            Assert.That(style.FontSize, Is.EqualTo(5f));
            Assert.That(style.FontSizeDelta, Is.Zero);
        }

        [TestCase(DamageSource.Physical)]
        [TestCase(DamageSource.Poison)]
        [TestCase(DamageSource.Thorns)]
        public void ObserversShareWorldPopupStyleButOnlyAttackerReceivesFeedback(DamageSource source)
        {
            var observer = HealthDamagePresentation.ResolveStyle(source, false, false);
            var attacker = HealthDamagePresentation.ResolveStyle(source, false, true);
            Assert.That(observer.UseReceivedDamageHud, Is.False);
            Assert.That(observer.AroundVictim, Is.False);
            Assert.That(observer.PlayStatusFeedback, Is.False);
            Assert.That(attacker.PlayStatusFeedback, Is.True);
            Assert.That(attacker.Color, Is.EqualTo(observer.Color));
            Assert.That(observer.FontSize, Is.Zero);
            Assert.That(observer.FontSizeDelta, Is.EqualTo(source == DamageSource.Poison ? -16f : 0f));
            Assert.That(attacker.FontSizeDelta, Is.EqualTo(observer.FontSizeDelta));
        }

        [Test]
        public void PopupPlacementPreservesHitPointAndVictimFallbackWithoutHealthComponent()
        {
            var victim = new GameObject("Presentation without health");
            try
            {
                victim.transform.position = new Vector3(20f, 3f, -10f);
                Vector3 hit = new Vector3(20.4f, 4f, -10f);
                Assert.That(HealthDamagePresentation.ResolvePosition(victim.transform, hit, DamageSource.Physical), Is.EqualTo(hit));
                Assert.That(HealthDamagePresentation.ResolvePosition(victim.transform, Vector3.zero, DamageSource.Physical),
                    Is.EqualTo(victim.transform.position + Vector3.up));
            }
            finally { Object.DestroyImmediate(victim); }
        }
    }
}
