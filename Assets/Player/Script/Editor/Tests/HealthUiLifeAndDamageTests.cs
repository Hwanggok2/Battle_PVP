using System.Reflection;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class HealthUiLifeAndDamageTests
    {
        private GameObject _object;

        [TearDown]
        public void TearDown()
        {
            if (_object != null)
                Object.DestroyImmediate(_object);
        }

        [Test]
        public void ReviveRequestRequiresDeathBattleAndFullDelay()
        {
            double allowedAt = 10d + HealthUiLifeRules.RespawnDelaySeconds;
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, allowedAt - 0.001d, allowedAt, 1f, true), Is.False);
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, allowedAt, allowedAt, 1f, true), Is.True);
            Assert.That(HealthUiLifeRules.CanRequestRevive(false, allowedAt + 1d, allowedAt, 1f, true), Is.False);
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, allowedAt + 1d, allowedAt, 1f, false), Is.False);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(1.001f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ReviveRequestRejectsInvalidRatios(float ratio)
        {
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, 20d, 15d, ratio, true), Is.False);
        }

        [Test]
        public void ReviveRequestRejectsNonFiniteTimes()
        {
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, double.NaN, 15d, 1f, true), Is.False);
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, double.PositiveInfinity, 15d, 1f, true), Is.False);
            Assert.That(HealthUiLifeRules.CanRequestRevive(true, 20d, double.NegativeInfinity, 1f, true), Is.False);
        }

        [Test]
        public void InvincibleDamageDoesNotConsumeShieldOrRaiseHealthEvents()
        {
            HealthSystem health = CreateHealth(100f, 30f);
            health.isInvincible = true;
            int hpEvents = 0;
            int shieldEvents = 0;
            health.HpChanged += (_, __) => hpEvents++;
            health.ShieldChanged += _ => shieldEvents++;

            DamageResult result = health.ApplyDamage(Request(40f));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.HpDamage, Is.Zero);
            Assert.That(result.ShieldDamage, Is.Zero);
            Assert.That(health.CurrentHp, Is.EqualTo(100f));
            Assert.That(health.CurrentShield, Is.EqualTo(30f));
            Assert.That(hpEvents, Is.Zero);
            Assert.That(shieldEvents, Is.Zero);
        }

        [Test]
        public void DamageResultSeparatesShieldAndClampedLethalHpDamage()
        {
            HealthSystem health = CreateHealth(100f, 30f);
            DamageResult shieldHit = health.ApplyDamage(Request(20f));
            Assert.That(shieldHit.Accepted, Is.True);
            Assert.That(shieldHit.HpDamage, Is.Zero);
            Assert.That(shieldHit.ShieldDamage, Is.EqualTo(20f));
            Assert.That(shieldHit.Killed, Is.False);

            DamageResult lethalHit = health.ApplyDamage(Request(500f));
            Assert.That(lethalHit.Accepted, Is.True);
            Assert.That(lethalHit.HpDamage, Is.EqualTo(100f));
            Assert.That(lethalHit.ShieldDamage, Is.EqualTo(10f));
            Assert.That(lethalHit.Killed, Is.True);
            Assert.That(health.IsDead, Is.True);
            Assert.That(health.CurrentHp, Is.Zero);
            Assert.That(health.ApplyDamage(Request(10f)).Accepted, Is.False);
        }

        [Test]
        public void DeathAndReviveTransitionsRemainSingleAcrossTenLives()
        {
            HealthSystem health = CreateHealth(100f);
            int deaths = 0;
            int revives = 0;
            health.OnDied += () => deaths++;
            health.OnRevived += () => revives++;

            for (int life = 1; life <= 10; life++)
            {
                health.ApplyDamage(Request(100f));
                health.ApplyDamage(Request(100f));
                health.Heal(100f);
                health.RefillHealth();
                health.SetCurrentHp(100f);
                Assert.That(health.CurrentHp, Is.Zero, "Only an explicit authoritative revive can restore a dead player.");
                Assert.That(health.DeathSequence, Is.EqualTo((uint)life));
                Assert.That(deaths, Is.EqualTo(life));
                health.Revive();
                health.Revive();
                Assert.That(health.IsDead, Is.False);
                Assert.That(health.CurrentHp, Is.EqualTo(100f));
                Assert.That(revives, Is.EqualTo(life));
            }
        }

        [Test]
        public void LifeSynchronizationHookRestoresObserverStateWithoutDuplicateEvents()
        {
            HealthSystem health = CreateHealth(100f);
            int deaths = 0;
            int revives = 0;
            health.OnDied += () => deaths++;
            health.OnRevived += () => revives++;
            MethodInfo hook = typeof(HealthSystem).GetMethod("OnLifeStateSynced", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(hook, Is.Not.Null);
            SetField(health, "_isDead", true);
            hook.Invoke(health, new object[] { false, true });
            hook.Invoke(health, new object[] { false, true });
            Assert.That(health.IsDead, Is.True);
            SetField(health, "_isDead", false);
            hook.Invoke(health, new object[] { true, false });
            hook.Invoke(health, new object[] { true, false });
            Assert.That(health.IsDead, Is.False);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(revives, Is.EqualTo(1));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-10f)]
        [TestCase(0f)]
        public void InvalidDamageDoesNotChangeHealth(float amount)
        {
            HealthSystem health = CreateHealth(100f);
            Assert.That(health.ApplyDamage(Request(amount)).Accepted, Is.False);
            Assert.That(health.CurrentHp, Is.EqualTo(100f));
        }

        [Test]
        public void DummyLethalResultKeepsDamageBeforeImmediateRespawn()
        {
            _object = new GameObject("HealthUi dummy test");
            _object.SetActive(false);
            DummyHealth dummy = EditorTestLifecycle.AddNetwork<DummyHealth>(_object);
            SetField(dummy, "_maxHp", 100f);
            SetField(dummy, "_currentHp", 40f);
            DamageResult result = dummy.ApplyDamage(Request(500f));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.HpDamage, Is.EqualTo(40f));
            Assert.That(result.Killed, Is.True);
            Assert.That(dummy.CurrentHp, Is.EqualTo(100f));
        }

        private HealthSystem CreateHealth(float hp, float shield = 0f)
        {
            _object = new GameObject("HealthUi health test");
            _object.SetActive(false);
            HealthSystem health = _object.AddComponent<HealthSystem>();
            MethodInfo bind = typeof(Mirror.NetworkIdentity).GetMethod("InitializeNetworkBehaviours",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(bind, Is.Not.Null);
            bind.Invoke(_object.GetComponent<Mirror.NetworkIdentity>(), null);
            Assert.That(health.netIdentity, Is.Not.Null);
            SetField(health, "_maxHp", 100f);
            SetField(health, "_currentHp", hp);
            SetField(health, "_currentShield", shield);
            return health;
        }

        private static DamageRequest Request(float amount) =>
            new DamageRequest(amount, DamageSource.Poison, 0f, null, Vector3.zero);

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
