using System;
using System.Linq;
using System.Reflection;
using BattlePvp.Characters;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class WeaponCombatTests
    {
        private GameObject _attacker, _defender;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        private static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private|BindingFlags.Public).Invoke(target,args);
        private static GameObject Player()
        {
            var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            p.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            EditorTestLifecycle.BindNetwork(p);
            EditorTestLifecycle.Invoke(p.GetComponent<PlayerCombat>(),"Awake");
            EditorTestLifecycle.Invoke(p.GetComponent<WeaponLoadout>(),"Awake");
            Set(p.GetComponent<HealthSystem>(),"_currentHp",100f);
            p.GetComponent<Animator>().fireEvents=false; p.GetComponent<Animator>().Rebind(); p.GetComponent<Animator>().Update(0);
            return p;
        }
        [SetUp] public void Setup() { _attacker=Player(); _defender=Player(); _attacker.transform.position=Vector3.forward; }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(_attacker); Object.DestroyImmediate(_defender); }
        private PlayerCombat Attacker=>_attacker.GetComponent<PlayerCombat>();
        private PlayerCombat Defender=>_defender.GetComponent<PlayerCombat>();
        private void Equip(GameObject player,MeleeWeaponKind kind)=>Call(player.GetComponent<WeaponLoadout>(),"Apply",kind);

        [Test]
        public void FinisherKeepsItsReportIndexThroughTheClosingSweepThenStartsAFreshCombo()
        {
            Equip(_attacker,MeleeWeaponKind.Greatsword);
            Call(Attacker,"StartAttack",2,false,Vector3.forward,false);
            Call(Attacker,"StopCombo",true);
            Assert.That(Attacker.IsAttackActive,Is.False);
            Assert.That(typeof(PlayerCombat).GetField("currentComboIndex",Private).GetValue(Attacker),Is.EqualTo(2),
                "A closing-frame hit must be reported as the third strike, not the first.");
            Call(Attacker,"StartAttack",0,false,Vector3.forward,false);
            Assert.That(Attacker.IsAttackActive,Is.True);
            Assert.That(typeof(PlayerCombat).GetField("currentComboIndex",Private).GetValue(Attacker),Is.EqualTo(0));
        }

        [TestCase(.4f)] [TestCase(.525f)] [TestCase(1.5f)]
        public void ServerReportWindowIncludesTheWholeGreatswordFinisherAndNetworkGrace(float speed)
        {
            Equip(_attacker,MeleeWeaponKind.Greatsword);
            Set(Attacker,"_currentAttackSpeed",speed);
            Set(Attacker,"isAttacking",true);
            Call(Attacker,"PlayAttackAnimation",2);
            var active=typeof(Mirror.NetworkServer).GetProperty("active",BindingFlags.Static|BindingFlags.Public);
            bool previous=Mirror.NetworkServer.active;
            try
            {
                active.SetValue(null,true);
                double started=Mirror.NetworkTime.time;
                Call(Attacker,"RegisterServerAcceptedAttack",7u,2);
                double expires=(double)typeof(PlayerCombat).GetField("_serverAttackReportExpiresAt",Private).GetValue(Attacker);
                float clipSeconds=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Remodel/Weapons/Greatsword3.anim").length/speed;
                Assert.That(expires-started,Is.GreaterThanOrEqualTo(clipSeconds+.5d),
                    "A slow third strike must still be reportable when its late contact reaches the server.");
            }
            finally { active.SetValue(null,previous); }
        }

        private float BeginAxeSwing()
        {
            Call(Attacker,"StartAttack",0,false,Vector3.forward,false);
            Assert.That(Attacker.IsAttackActive,Is.True);
            return _attacker.GetComponent<Animator>().speed;
        }

        private void HitWithAxe()
        {
            var processor=_attacker.GetComponent<AttackProcessor>(); Call(processor,"Awake");
            Set(_defender.GetComponent<HealthSystem>(),"_currentHp",100f);
            processor.ProcessHit(WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe).Attacks[0],
                _defender.GetComponent<BattlePvp.Stats.StatManager>(),_defender.GetComponent<HealthSystem>(),Vector3.up);
        }

        [Test] public void AxeHitImmediatelyAcceleratesRecoveryAndNextSwingWithoutChangingStats()
        {
            Equip(_attacker,MeleeWeaponKind.Axe);
            float baseline=(float)Call(Attacker,"ResolveCurrentAttackSpeed");
            Assert.That(BeginAxeSwing(),Is.EqualTo(baseline).Within(.0001f));
            var animator=_attacker.GetComponent<Animator>();
            animator.Play("Weapon_AxeChop",1,.36f); animator.Update(0);
            float phase=animator.GetCurrentAnimatorStateInfo(1).normalizedTime;
            HitWithAxe(); HitWithAxe(); // multiple targets/hit notifications cannot stack charges
            Assert.That(animator.speed,Is.EqualTo(baseline*2f).Within(.0001f),"The current swing/recovery must accelerate at contact.");
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).normalizedTime,Is.EqualTo(phase),"Do not restart or skip the animation.");
            Assert.That((float)Call(Attacker,"ResolveCurrentAttackSpeed"),Is.EqualTo(baseline),"Character attack-speed stats/buffs are unchanged.");
            float clipLength=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Remodel/Weapons/AxeChop.anim").length;
            animator.Update(.1f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).normalizedTime-phase,Is.EqualTo(.1f*baseline*2f/clipLength).Within(.001f),"Actual motion must advance at 2x speed.");
            Call(Attacker,"CancelCurrentAttack");
            Assert.That(BeginAxeSwing(),Is.EqualTo(baseline*2f).Within(.0001f));
            Call(Attacker,"CancelCurrentAttack"); // missed swing consumed the charge
            Assert.That(BeginAxeSwing(),Is.EqualTo(baseline).Within(.0001f));
        }

        [Test] public void ConsecutiveAxeHitsSustainDoubleSpeedWithoutStacking()
        {
            Equip(_attacker,MeleeWeaponKind.Axe);
            float baseline=BeginAxeSwing();
            for(int i=0;i<3;i++)
            {
                HitWithAxe();
                Assert.That(_attacker.GetComponent<Animator>().speed,Is.EqualTo(baseline*2f).Within(.0001f));
                Call(Attacker,"CancelCurrentAttack");
                Assert.That(BeginAxeSwing(),Is.EqualTo(baseline*2f).Within(.0001f));
            }
        }

        [Test] public void AxeKeepsAcceleratedRecoveryUntilTheEndOfTheClip()
        {
            Equip(_attacker,MeleeWeaponKind.Axe);
            float baseline=BeginAxeSwing(); HitWithAxe();
            var animator=_attacker.GetComponent<Animator>();
            animator.Play("Weapon_AxeChop",1,.96f); animator.Update(0);
            var monitor=(System.Collections.IEnumerator)Call(Attacker,"CoComboMonitor",0);
            Assert.That(monitor.MoveNext(),Is.True); Assert.That(monitor.MoveNext(),Is.True);
            Assert.That(monitor.MoveNext(),Is.True);
            Assert.That(Attacker.IsAttackActive,Is.True,"The last 5% is still axe recovery.");
            Assert.That(animator.speed,Is.EqualTo(baseline*2f).Within(.0001f));
            animator.Play("Weapon_AxeChop",1,1f); animator.Update(0);
            Assert.That(monitor.MoveNext(),Is.False);
            Assert.That(Attacker.IsAttackActive,Is.False);
            Assert.That(animator.speed,Is.EqualTo(1f));
        }

        [Test] public void RejectedAxeInputDoesNotConsumeTheEarnedFollowup()
        {
            Equip(_attacker,MeleeWeaponKind.Axe); HitWithAxe();
            Call(Attacker,"StartAttack",3,false,Vector3.forward,false); // axe has no thrust
            Assert.That(Attacker.IsAttackActive,Is.False);
            Assert.That(BeginAxeSwing(),Is.EqualTo((float)Call(Attacker,"ResolveCurrentAttackSpeed")*2f).Within(.0001f));
        }

        [TestCase(false)] [TestCase(true)] public void AxeFollowupClearsOnWeaponChangeOrCombatReset(bool reset)
        {
            Equip(_attacker,MeleeWeaponKind.Axe); HitWithAxe();
            if(reset) Call(Attacker,"CancelAllCombatActions");
            else { Equip(_attacker,MeleeWeaponKind.Sword); Equip(_attacker,MeleeWeaponKind.Axe); }
            Assert.That(BeginAxeSwing(),Is.EqualTo((float)Call(Attacker,"ResolveCurrentAttackSpeed")).Within(.0001f));
        }

        [Test] public void ShieldBlockAndSkillDamageDoNotGrantTheAxeFollowup()
        {
            Equip(_attacker,MeleeWeaponKind.Axe); Equip(_defender,MeleeWeaponKind.SwordShield);
            Call(Defender,"SetWeaponGuard",true); HitWithAxe();
            Set(Attacker,"_recoilUntil",0d);
            var processor=_attacker.GetComponent<AttackProcessor>();
            processor.ProcessSkillHit(1,_defender.GetComponent<BattlePvp.Stats.StatManager>(),_defender.GetComponent<HealthSystem>(),Vector3.up);
            Assert.That(BeginAxeSwing(),Is.EqualTo((float)Call(Attacker,"ResolveCurrentAttackSpeed")).Within(.0001f));
        }

        [TestCase(false,0f,0f,false)] [TestCase(true,0f,10f,true)] [TestCase(true,10f,0f,true)]
        public void AxeFollowupRequiresAcceptedMeleeDamageIncludingAbsorption(bool accepted,float hp,float shield,bool boosted)
        {
            Equip(_attacker,MeleeWeaponKind.Axe);
            var processor=_attacker.GetComponent<AttackProcessor>(); Call(processor,"Awake");
            processor.ProcessHit(WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe).Attacks[0],
                _defender.GetComponent<BattlePvp.Stats.StatManager>(),new AxeDamageReceiver(new DamageResult(accepted,hp,shield)),Vector3.up);
            Assert.That(BeginAxeSwing(),Is.EqualTo((float)Call(Attacker,"ResolveCurrentAttackSpeed")*(boosted?2f:1f)).Within(.0001f));
        }

        private sealed class AxeDamageReceiver : IDamageReceiverWithResult
        {
            private readonly DamageResult _result;
            public float CurrentHp=>100; public float MaxHp=>100;
            public float RequestedDamage { get; private set; }
            public AxeDamageReceiver(DamageResult result) { _result=result; }
            public DamageResult ApplyDamage(DamageRequest request) { RequestedDamage=request.Amount; return _result; }
            public void ApplyDamage(float amount,DamageSource source,Vector3 position)=>throw new InvalidOperationException();
            public void ApplyDamage(float amount,DamageSource source,float power,IDamageReceiver attacker,Vector3 position)=>throw new InvalidOperationException();
        }

        [Test] public void RemoteAxeAnimationUsesAcceptedServerSpeedAndIgnoresDuplicateStarts()
        {
            Equip(_attacker,MeleeWeaponKind.Axe);
            Call(Attacker,"ReceiveRemoteAttackStart",0,Vector3.forward,10u,1.44f);
            Assert.That(_attacker.GetComponent<Animator>().speed,Is.EqualTo(1.44f));
            Call(Attacker,"ReceiveRemoteAttackStart",0,Vector3.forward,10u,1f);
            Assert.That(_attacker.GetComponent<Animator>().speed,Is.EqualTo(1.44f));
        }

        [Test] public void ConfirmedRecoverySpeedReachesReplicasAndCannotLeakIntoANewSwingOrIdle()
        {
            Equip(_attacker,MeleeWeaponKind.Axe);
            var animator=_attacker.GetComponent<Animator>();
            Call(Attacker,"ReceiveRemoteAttackStart",0,Vector3.forward,10u,1f);
            animator.Play("Weapon_AxeChop",1,.4f); animator.Update(0);
            Call(Attacker,"ReceiveAxeHitAnimationSpeed",10u,2f);
            Call(Attacker,"ReceiveAxeHitAnimationSpeed",10u,2f);
            Call(Attacker,"ResolveAttackRequest",10u,true,1f); // earlier start acknowledgement
            Assert.That(animator.speed,Is.EqualTo(2f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).normalizedTime,Is.EqualTo(.4f).Within(.001f));
            Call(Attacker,"ReceiveRemoteAttackStart",0,Vector3.forward,11u,1f);
            Call(Attacker,"ReceiveAxeHitAnimationSpeed",10u,2f);
            Assert.That(animator.speed,Is.EqualTo(1f));
            Call(Attacker,"CancelCurrentAttack");
            Call(Attacker,"ReceiveAxeHitAnimationSpeed",11u,2f);
            HitWithAxe();
            Assert.That(animator.speed,Is.EqualTo(1f),"A late accepted hit must leave idle/walking at their regular speed.");
        }

        [Test] public void MeleeDamageBuildsThroughCombosAndAxeExceedsEvenTheGreatswordFinisher()
        {
            var processor=_attacker.GetComponent<AttackProcessor>(); Call(processor,"Awake");
            var stats=_defender.GetComponent<BattlePvp.Stats.StatManager>();
            float Damage(MeleeWeaponKind kind,int index)
            {
                Equip(_attacker,kind);
                var receiver=new AxeDamageReceiver(new DamageResult(true,1,0));
                processor.ProcessHit(WeaponCatalog.Instance.Find(kind).Attacks[index],stats,receiver,Vector3.up);
                return receiver.RequestedDamage;
            }
            var sword=Enumerable.Range(0,3).Select(i=>Damage(MeleeWeaponKind.Sword,i)).ToArray();
            var shield=Enumerable.Range(0,3).Select(i=>Damage(MeleeWeaponKind.SwordShield,i)).ToArray();
            var great=Enumerable.Range(0,3).Select(i=>Damage(MeleeWeaponKind.Greatsword,i)).ToArray();
            float axe=Damage(MeleeWeaponKind.Axe,0);
            Assert.That(sword[0],Is.GreaterThan(0));
            foreach(var combo in new[]{sword,shield,great})
                for(int i=1;i<combo.Length;i++) Assert.That(combo[i],Is.GreaterThan(combo[i-1]),"Each later hit is stronger.");
            Assert.That(great[0],Is.GreaterThan(sword[2]),"Even the first greatsword hit exceeds the sword finisher.");
            Assert.That(great[0],Is.EqualTo(sword[0]*1.2f).Within(.001f));
            Assert.That(axe,Is.GreaterThan(great[2]),"The strongest combo hit must remain weaker than the axe.");
            Assert.That(axe,Is.EqualTo(sword[0]*1.5f).Within(.001f));
            float riposte1=Damage(MeleeWeaponKind.Greatsword,4), riposte2=Damage(MeleeWeaponKind.Greatsword,5);
            Assert.That(riposte1,Is.GreaterThan(sword[2]));
            Assert.That(riposte2,Is.GreaterThan(riposte1).And.LessThan(axe));
        }

        [TestCase(.5f)] [TestCase(1f)] [TestCase(2f)]
        public void BasicAttackPlaybackIsFastestWithSwordThenGreatswordThenAxe(float statSpeedMultiplier)
        {
            Set(Attacker,"_attackSpeedBonusMultiplier",statSpeedMultiplier);
            Set(Attacker,"_attackSpeedBonusUntil",double.MaxValue);
            var animator=_attacker.GetComponent<Animator>();
            float Duration(MeleeWeaponKind kind,int index)
            {
                Equip(_attacker,kind);
                Call(Attacker,"StartAttack",index,false,Vector3.forward,false);
                Assert.That(Attacker.IsAttackActive,Is.True);
                // Sample normalized progress after the entry blend so both the
                // controller's state speed and accepted stat speed are exercised.
                animator.Play(WeaponCatalog.Instance.Find(kind).Attacks[index].animationName,1,0);
                animator.Update(0); animator.Update(.05f);
                float duration=.05f/animator.GetCurrentAnimatorStateInfo(1).normalizedTime;
                Call(Attacker,"CancelCurrentAttack");
                return duration;
            }
            var sword=Enumerable.Range(0,3).Select(i=>Duration(MeleeWeaponKind.Sword,i)).ToArray();
            var shield=Enumerable.Range(0,3).Select(i=>Duration(MeleeWeaponKind.SwordShield,i)).ToArray();
            var great=Enumerable.Range(0,3).Select(i=>Duration(MeleeWeaponKind.Greatsword,i)).ToArray();
            float axe=Duration(MeleeWeaponKind.Axe,0);
            Assert.That(sword.Min(),Is.GreaterThan(0));
            Assert.That(sword.Max(),Is.LessThan(great.Min()));
            Assert.That(shield.Max(),Is.LessThan(great.Min()));
            Assert.That(great.Max(),Is.LessThan(axe));
            Assert.That(Duration(MeleeWeaponKind.Sword,3),Is.LessThan(great.Min()),"The one-handed thrust is also faster.");
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void SwordWithoutShieldDealsTenPercentMoreMeleeDamage(int attackIndex)
        {
            var processor=_attacker.GetComponent<AttackProcessor>(); Call(processor,"Awake");
            var stats=_defender.GetComponent<BattlePvp.Stats.StatManager>();
            float Damage(MeleeWeaponKind kind,int index)
            {
                Equip(_attacker,kind);
                var receiver=new AxeDamageReceiver(new DamageResult(true,1,0));
                processor.ProcessHit(WeaponCatalog.Instance.Find(kind).Attacks[index],stats,receiver,Vector3.up);
                return receiver.RequestedDamage;
            }
            float shield=Damage(MeleeWeaponKind.SwordShield,attackIndex==3?0:attackIndex);
            Assert.That(shield,Is.GreaterThan(0));
            Assert.That(Damage(MeleeWeaponKind.Sword,attackIndex),Is.EqualTo(shield*1.1f).Within(.001f));
        }

        [Test] public void EquippingASwordDoesNotIncreaseSkillOrProjectileDamage()
        {
            var processor=_attacker.GetComponent<AttackProcessor>(); Call(processor,"Awake");
            var stats=_defender.GetComponent<BattlePvp.Stats.StatManager>();
            float baseline=0;
            foreach(var kind in new[]{MeleeWeaponKind.SwordShield,MeleeWeaponKind.Sword,MeleeWeaponKind.Axe})
            {
                Equip(_attacker,kind);
                var receiver=new AxeDamageReceiver(new DamageResult(true,1,0));
                processor.ProcessSkillHit(1,stats,receiver,Vector3.up);
                if(kind==MeleeWeaponKind.SwordShield) baseline=receiver.RequestedDamage;
                Assert.That(receiver.RequestedDamage,Is.EqualTo(baseline).Within(.001f));
            }
        }

        [Test] public void OwnerSpeedCorrectionPreservesAnimationPhaseAndIgnoresOldReplies()
        {
            Equip(_attacker,MeleeWeaponKind.Axe); BeginAxeSwing(); Set(Attacker,"_currentAttackSequence",10u);
            var animator=_attacker.GetComponent<Animator>(); animator.Play("Weapon_AxeChop",1,.25f); animator.Update(0);
            Call(Attacker,"ResolveAttackRequest",10u,true,1.44f);
            Assert.That(animator.speed,Is.EqualTo(1.44f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).normalizedTime,Is.EqualTo(.25f).Within(.001f));
            Call(Attacker,"ResolveAttackRequest",9u,true,1f);
            Assert.That(animator.speed,Is.EqualTo(1.44f));
            Call(Attacker,"CancelCurrentAttack"); Call(Attacker,"ResolveAttackRequest",10u,true,1.44f);
            Assert.That(animator.speed,Is.EqualTo(1f),"Late confirmation must not accelerate idle locomotion.");
        }

        [TestCase(MeleeWeaponKind.Sword,false)] [TestCase(MeleeWeaponKind.Axe,false)]
        [TestCase(MeleeWeaponKind.SwordShield,true)] [TestCase(MeleeWeaponKind.Greatsword,true)]
        public void OnlyShieldAndGreatswordCanGuard(MeleeWeaponKind kind,bool expected)
        { Equip(_defender,kind); Assert.That(Call(Defender,"SetWeaponGuard",true),Is.EqualTo(expected)); }

        [Test] public void ShieldStopsFrontalMeleeAndInterruptsTheAttacker()
        {
            Equip(_defender,MeleeWeaponKind.SwordShield);
            Assert.That(Call(Defender,"SetWeaponGuard",true),Is.True);
            Set(Attacker,"isAttacking",true);
            Assert.That(Call(Defender,"TryBlockMelee",Attacker),Is.True);
            Assert.That(Attacker.IsAttackActive,Is.False);
            Assert.That(Attacker.IsWeaponRecoiling,Is.True);
            Assert.That(Call(Attacker,"IsSkillCastingOrAttackLocked"),Is.True);
            double until=(double)typeof(PlayerCombat).GetField("_recoilUntil",Private).GetValue(Attacker);
            Assert.That(until-Time.timeAsDouble,Is.EqualTo(.975d).Within(.002d),"Shield stagger must last 1.5 times the original .65 seconds.");
            Set(Attacker,"_recoilUntil",Time.timeAsDouble-1);
            Assert.That(Attacker.IsWeaponRecoiling,Is.False);
        }
        [TestCase(90)] [TestCase(180)] public void ShieldDoesNotBlockSideOrRearHits(float degrees)
        {
            Equip(_defender,MeleeWeaponKind.SwordShield); Call(Defender,"SetWeaponGuard",true);
            _attacker.transform.position=Quaternion.Euler(0,degrees,0)*Vector3.forward;
            Assert.That(Call(Defender,"TryBlockMelee",Attacker),Is.False);
            Assert.That(Attacker.IsWeaponRecoiling,Is.False);
        }
        [Test] public void HeldBladeContactGrantsOneTimedRiposteButBodyContactDoesNot()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            Set(Defender,"_guardStartedAt",Time.timeAsDouble-1);
            Call(Defender,"SetWeaponGuard",true);
            Assert.That(Call(Defender,"TryBlockMelee",Attacker),Is.False);
            Assert.That(Call(Defender,"TryParryBlade",Attacker),Is.True);
            double until=(double)typeof(PlayerCombat).GetField("_recoilUntil",Private).GetValue(Attacker);
            Assert.That(until-Time.timeAsDouble,Is.EqualTo(.65d).Within(.002d),"Shield tuning must not also lengthen sword parry recoil.");
            Assert.That(Defender.IsRiposteReady,Is.True); Assert.That(Defender.IsWeaponGuarding,Is.False);
            Assert.That(Call(Defender,"WeaponAttackAllowed",4),Is.True);
            Set(Defender,"_riposteUntil",Time.timeAsDouble-1);
            Assert.That(Call(Defender,"WeaponAttackAllowed",4),Is.False);
        }
        [Test] public void ChangingWeaponsClearsGuardRiposteAndAttackState()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            Call(Defender,"TryParryBlade",Attacker); Equip(_defender,MeleeWeaponKind.Axe);
            Assert.That(Defender.IsRiposteReady||Defender.IsWeaponGuarding||Defender.IsAttackActive,Is.False);
            Assert.That(Call(Defender,"WeaponAttackAllowed",3),Is.False);
            Assert.That(Call(Defender,"CanContinueWeaponCombo",0),Is.False);
        }

        [TestCase(true,false)] [TestCase(false,false)] [TestCase(false,true)]
        public void ParryRequiresBladeContactStrictlyBeforeTheBody(bool bladeFirst,bool simultaneous)
        {
            foreach(var collider in _defender.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            var animator=_defender.GetComponent<Animator>(); animator.Play("Weapon_SwordGuard",1,0); animator.Update(0);
            Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
            var surface=_defender.GetComponentInChildren<WeaponParrySurface>().GetComponent<BoxCollider>();
            Physics.SyncTransforms();
            Vector3 bladePoint=surface.bounds.center;
            var body=new GameObject("Body contact",typeof(BoxCollider),typeof(HitBodyPart));
            body.transform.SetParent(_defender.transform);
            body.transform.position=simultaneous ? bladePoint : bladePoint+Vector3.back;
            body.GetComponent<BoxCollider>().size=Vector3.one*.12f;
            var hitbox=_attacker.GetComponentInChildren<MeleeHitBox>(true); Call(hitbox,"Awake");
            EditorTestLifecycle.Invoke(_attacker.GetComponent<AttackProcessor>(),"Awake");
            hitbox.GetComponent<BoxCollider>().center=Vector3.zero;
            hitbox.GetComponent<BoxCollider>().size=Vector3.one*.04f;
            hitbox.SetAttackData(WeaponCatalog.Instance.Find(MeleeWeaponKind.Sword).Attacks[0]);
            Set(Attacker,"isAttacking",true); hitbox.EnableHitBox(); Physics.SyncTransforms();
            float hp=_defender.GetComponent<HealthSystem>().CurrentHp;
            Call(hitbox,"ProcessBoxOverlap",bladeFirst ? bladePoint : body.transform.position,Quaternion.identity);
            Call(hitbox,"ProcessBoxOverlap",bladeFirst ? body.transform.position : bladePoint,Quaternion.identity);
            Assert.That(Defender.IsRiposteReady,Is.EqualTo(bladeFirst));
            Assert.That(Attacker.IsWeaponRecoiling,Is.EqualTo(bladeFirst));
            if(bladeFirst) Assert.That(_defender.GetComponent<HealthSystem>().CurrentHp,Is.EqualTo(hp));
            else Assert.That(_defender.GetComponent<HealthSystem>().CurrentHp,Is.LessThan(hp));
            Call(Defender,"SetWeaponGuard",false); Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
            Assert.That(surface.enabled,Is.False);
        }
        [Test] public void RiposteSecondHitCannotBeStartedWithoutTheFirst()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword);
            Assert.That(Call(Defender,"WeaponAttackAllowed",5),Is.False);
            Set(Defender,"isAttacking",true); Set(Defender,"currentComboIndex",4);
            Assert.That(Call(Defender,"WeaponAttackAllowed",5),Is.True);
            Assert.That(Call(Defender,"CanContinueWeaponCombo",4),Is.True);
            Assert.That(Call(Defender,"CanContinueWeaponCombo",5),Is.False);
        }
        [Test] public void AnotherHitboxCannotParryAfterThisAttackAlreadyDamagedTheBody()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            Assert.That(Attacker.TryRegisterHitTarget(_defender.GetComponent<HealthSystem>()),Is.True);
            Assert.That(Call(Defender,"TryParryBlade",Attacker),Is.False);
            Assert.That(Attacker.IsWeaponRecoiling,Is.False);
        }
        [TestCase("default")] [TestCase("security-officer")] [TestCase("megumi")] [TestCase("casual-1")] [TestCase("picochan")] [TestCase("brute")]
        public void AllWeaponsFollowTheVisibleBladeAndUseTheirOwnHitBounds(string character)
        {
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            var definition=CharacterCatalog.Instance.Find(character);
            Assert.That(skin.Apply(definition,out var error),Is.True,error); skin.SyncPose();
            foreach(MeleeWeaponKind kind in Enum.GetValues(typeof(MeleeWeaponKind)))
            {
                Equip(_defender,kind); skin.SyncPose(); Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
                var entry=WeaponCatalog.Instance.Find(kind); var blade=_defender.GetComponentInChildren<MeleeHitBox>(true);
                var source=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(blade);
                Assert.That(source.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(entry.Mesh));
                Assert.That(blade.GetComponent<BoxCollider>().center,Is.EqualTo(entry.HitCenter));
                Assert.That(blade.GetComponent<BoxCollider>().size,Is.EqualTo(entry.HitSize));
                Assert.That(entry.Materials.All(m=>m!=null),Is.True);
            }
        }
        [Test] public void EveryWeaponAttackHasAPlayableStateAndOneDamageWindow()
        {
            var animator=_defender.GetComponent<Animator>();
            foreach(var entry in WeaponCatalog.Instance.Weapons) foreach(var attack in entry.Attacks.Where(a=>a!=null))
            {
                Assert.That(animator.HasState(1,Animator.StringToHash(attack.animationName)),Is.True,attack.name);
                animator.Play(attack.animationName,1,0); animator.Update(0);
                var clip=animator.GetCurrentAnimatorClipInfo(1)[0].clip;
                Assert.That(clip.humanMotion,Is.True,clip.name);
                Assert.That(clip.events.Count(e=>e.functionName=="EnableHitBox"),Is.EqualTo(1));
                Assert.That(AnimationHitWindow.Melee(animator,1).End,Is.GreaterThan(AnimationHitWindow.Melee(animator,1).Start));
            }
        }
        [Test] public void LateJoinReceivesWeaponAndGuardWithoutClearingSerializedCombatState()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            var writer=new Mirror.NetworkWriter(); Defender.OnSerialize(writer,true);
            Attacker.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()),true);
            writer=new Mirror.NetworkWriter(); _defender.GetComponent<WeaponLoadout>().OnSerialize(writer,true);
            var replica=_attacker.GetComponent<WeaponLoadout>();
            replica.OnDeserialize(new Mirror.NetworkReader(writer.ToArraySegment()),true); replica.OnStartClient();
            Assert.That(replica.Selected,Is.EqualTo(MeleeWeaponKind.Greatsword));
            Assert.That(Attacker.IsWeaponGuarding,Is.True);
        }
        [Test] public void GuardReleaseDoesNotReplaceAnAttackStartedInTheSameFrame()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword);
            Set(Defender,"_shownGuard",true); Set(Defender,"isAttacking",true);
            var animator=_defender.GetComponent<Animator>(); animator.Play("Weapon_Riposte1",1,.1f); animator.Update(0);
            Call(Defender,"UpdateWeaponCombat"); animator.Update(.02f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Weapon_Riposte1"),Is.True);
            Assert.That(animator.IsInTransition(1),Is.False);
        }

        [TestCase("default")] [TestCase("security-officer")] [TestCase("megumi")]
        [TestCase("casual-1")] [TestCase("picochan")] [TestCase("brute")]
        public void ShieldFollowsUpperBodyAimWhileGuarding(string character)
        {
            var scene=_defender.scene; string previousName=scene.name; scene.name="Battle_waiting";
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            try
            {
                var definition=CharacterCatalog.Instance.Find(character);
                Assert.That(definition,Is.Not.Null,character);
                Assert.That(skin.Apply(definition,out var error),Is.True,error);
                Equip(_defender,MeleeWeaponKind.SwordShield);
                Assert.That(Call(Defender,"SetWeaponGuard",true),Is.True);
                animator.Play("Weapon_ShieldGuard",1,.5f); animator.Update(0);
                skin.SyncPose();
                var loadout=_defender.GetComponent<WeaponLoadout>(); Call(loadout,"SyncVisual");
                var shield=_defender.transform.Find("Equipped shield");
                Quaternion neutral=shield.rotation;
                foreach(float pitch in new[]{-45f,45f})
                {
                    Call(Defender,"RestoreMeleeAimPose");
                    animator.Play("Weapon_ShieldGuard",1,.5f); animator.Update(0);
                    Set(Defender,"_networkLookPitch",pitch); Set(Defender,"_lookPitch",pitch);
                    Set(Defender,"_lookPoseWeight",1f);
                    Call(Defender,"UpdateMeleeAimPose"); skin.SyncPose(); Call(loadout,"SyncVisual");
                    Assert.That((float)typeof(PlayerCombat).GetField("_lookPoseWeight",Private).GetValue(Defender),Is.EqualTo(1f),"Guard must retain aim weight.");
                    // Humanoid retargeting changes the native wrist a few degrees; it must still follow the complete pitch.
                    Assert.That(Quaternion.Angle(Quaternion.AngleAxis(pitch,_defender.transform.right)*neutral,shield.rotation),Is.LessThan(5f),character+" / "+pitch);
                    Assert.That(Defender.IsWeaponGuarding,Is.True);
                    Assert.That(Call(Defender,"IsSkillCastingOrAttackLocked"),Is.True,"Aiming must not unlock attacks.");
                }
            }
            finally { scene.name=previousName; }
        }
        [Test] public void WeaponMenuIsModalAndEscapeReturnsToGameplay()
        {
            var scene=_defender.scene; string previousName=scene.name; scene.name="Battle_waiting";
            var local=typeof(BattlePvp.Stats.StatManager).GetProperty("Local"); var previousLocal=local.GetValue(null);
            local.SetValue(null,_defender.GetComponent<BattlePvp.Stats.StatManager>());
            var input=new GameObject("Input").AddComponent<BattlePvp.Logic.GameInputController>(); EditorTestLifecycle.Invoke(input,"Awake");
            var ui=new GameObject("Weapon menu",typeof(RectTransform));
            try
            {
                var panel=ui.AddComponent<BattlePvp.UI.WeaponSelectionPanel>(); EditorTestLifecycle.Invoke(panel,"Awake");
                Call(panel,"Toggle"); Assert.That(BattlePvp.UI.WeaponSelectionPanel.IsOpen,Is.True);
                Assert.That(BattlePvp.Logic.GameInputController.IsPaused,Is.True);
                BattlePvp.Logic.GameInputController.HandleEscape();
                Assert.That(BattlePvp.UI.WeaponSelectionPanel.IsOpen,Is.False);
            }
            finally { Object.DestroyImmediate(ui); Object.DestroyImmediate(input.gameObject); local.SetValue(null,previousLocal); scene.name=previousName; }
        }
        [Test] public void ConfirmedMeleeBlockPreventsHealthDamageAndRearAttackStillHurts()
        {
            Equip(_defender,MeleeWeaponKind.SwordShield); Call(Defender,"SetWeaponGuard",true);
            var processor=_attacker.GetComponent<AttackProcessor>(); EditorTestLifecycle.Invoke(processor,"Awake");
            var target=_defender.GetComponent<HealthSystem>();
            var stats=_defender.GetComponent<BattlePvp.Stats.StatManager>();
            var attack=WeaponCatalog.Instance.Find(MeleeWeaponKind.Sword).Attacks[0];
            float before=target.CurrentHp;
            processor.ProcessHit(attack,stats,target,_defender.transform.position+Vector3.up);
            Assert.That(target.CurrentHp,Is.EqualTo(before)); Assert.That(Attacker.IsWeaponRecoiling,Is.True);
            Set(Attacker,"_recoilUntil",0d); _attacker.transform.position=Vector3.back;
            processor.ProcessHit(attack,stats,target,_defender.transform.position+Vector3.up);
            Assert.That(target.CurrentHp,Is.LessThan(before));
        }
        [Test] public void EveryNewAttackTrackMatchesTheRenderedWeaponWhenAimingUpAndDown()
        {
            var animator=_attacker.GetComponent<Animator>();
            var blade=_attacker.GetComponentInChildren<MeleeHitBox>(true).transform;
            var aim=new MeleeAimPose(_attacker.transform,animator);
            foreach(var entry in WeaponCatalog.Instance.Weapons)
            foreach(var attack in entry.Attacks.Where(a=>a!=null && a.animationName.StartsWith("Weapon_")))
            foreach(float pitch in new[]{-45f,0f,45f})
            {
                Assert.That(attack.motionSamples,Has.Length.EqualTo(241));
                Equip(_attacker,entry.Kind);
                animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),entry.Kind==MeleeWeaponKind.Greatsword ? 1 : 0);
                aim.Restore(); animator.Play("Movement",0,0); animator.Play(attack.animationName,1,.6f); animator.Update(0);
                Vector3 direction=Quaternion.AngleAxis(pitch,_attacker.transform.right)*_attacker.transform.forward;
                aim.ApplyCalibrated(direction,aim.SelectReference(attack,1.4f),.6f,1,direction,attack.maxAimCalibration,attack.aimInRootSpace,attack.aimCrossingPhase);
                if(entry.TwoHanded) WeaponLoadout.FitTwoHandedGrip(animator,blade,entry);
                Assert.That(MeleeMotionSample.TryEvaluate(attack,_attacker.transform,.6f,direction*1.4f,direction,1,out var sample),Is.True);
                Assert.That(Vector3.Distance(sample.position,blade.position),Is.LessThan(.003f),attack.name);
                Assert.That(Quaternion.Angle(sample.rotation,blade.rotation),Is.LessThan(.2f),attack.name);
            }
        }

        private static Vector3 Palm(Animator animator,bool left) => (Vector3)typeof(CharacterSkin).Assembly
            .GetType("BattlePvp.Characters.CharacterEquipmentVisual").GetMethod("PalmCenter",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{animator,left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand});

        [TestCase("default",MeleeWeaponKind.Greatsword)]
        [TestCase("brute",MeleeWeaponKind.Greatsword)]
        [TestCase("security-officer",MeleeWeaponKind.Greatsword)]
        [TestCase("casual-1",MeleeWeaponKind.Greatsword)]
        [TestCase("megumi",MeleeWeaponKind.Greatsword)]
        [TestCase("picochan",MeleeWeaponKind.Greatsword)]
        [TestCase("default",MeleeWeaponKind.Sword)]
        [TestCase("brute",MeleeWeaponKind.Sword)]
        [TestCase("security-officer",MeleeWeaponKind.Sword)]
        [TestCase("casual-1",MeleeWeaponKind.Sword)]
        [TestCase("megumi",MeleeWeaponKind.Sword)]
        [TestCase("picochan",MeleeWeaponKind.Sword)]
        [TestCase("default",MeleeWeaponKind.Axe)]
        [TestCase("brute",MeleeWeaponKind.Axe)]
        [TestCase("security-officer",MeleeWeaponKind.Axe)]
        [TestCase("casual-1",MeleeWeaponKind.Axe)]
        [TestCase("megumi",MeleeWeaponKind.Axe)]
        [TestCase("picochan",MeleeWeaponKind.Axe)]
        public void CalibratedStrikesCrossTheCameraRayOnEveryBody(string character,MeleeWeaponKind kind)
        {
            _defender.transform.SetPositionAndRotation(new Vector3(2,0,-3),Quaternion.Euler(0,37,0));
            _defender.transform.localScale=Vector3.one*1.15f;
            Equip(_defender,kind);
            var animator=_defender.GetComponent<Animator>(); animator.SetLayerWeight(5,kind==MeleeWeaponKind.Greatsword ? 1 : 0);
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            var aim=new MeleeAimPose(_defender.transform,visible);
            var entry=WeaponCatalog.Instance.Find(kind);
            var hitbox=_defender.GetComponentInChildren<MeleeHitBox>(true);
            var blade=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(hitbox);
            foreach(var attack in entry.Attacks.Where(a=>a!=null && (entry.TwoHanded || a.animationName=="Weapon_Thrust"))) foreach(float pitch in new[]{-40f,0f,40f})
            {
                Set(Defender,"_meleeAnimationData",attack);
                Call(Defender,"UpdateMeleeAimPose");
                Call(Defender,"UpdateVisualMeleeAimPose",visible);
                Assert.That(typeof(PlayerCombat).GetField("_meleeAimAnimator",Private).GetValue(Defender),Is.SameAs(visible),"Correct the final avatar after retargeting.");
                Call(Defender,"RestoreMeleeAimPose");
                float crossing=attack.CrossingPhase(visible.avatar);
                aim.Restore(); animator.Play("Movement",0,0); animator.Play(attack.animationName,1,crossing); animator.Update(0); skin.SyncPose();
                Vector3 look=Quaternion.AngleAxis(pitch,_defender.transform.right)*_defender.transform.forward;
                // Eye-level camera ray, including the third-person camera's setback.
                var ray=new Ray(visible.GetBoneTransform(HumanBodyBones.Head).position-look*.6f,look);
                float reach=aim.ReferenceVector(aim.ReferencePoint(attack),attack.aimInRootSpace).magnitude;
                Vector3 direction=(MeleeAimPose.ReachablePoint(ray,aim.Pivot,reach)-aim.Pivot).normalized;
                aim.ApplyCalibrated(direction,aim.SelectReference(attack,reach),crossing,1,look,attack.maxAimCalibration,attack.aimInRootSpace,crossing);
                // Match the runtime order: native aim, equipment, then grip support.
                var follower=_defender.GetComponentInChildren<CharacterPoseFollower>();
                if(follower!=null) Call(typeof(CharacterPoseFollower).GetField("_equipment",Private).GetValue(follower),"Sync");
                Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
                Vector3 a=blade.TransformPoint(entry.BladeBase)-ray.origin,b=blade.TransformPoint(entry.BladeTip)-ray.origin;
                Vector3 segment=Vector3.ProjectOnPlane(b-a,look);
                float t=Mathf.Clamp01(-Vector3.Dot(Vector3.ProjectOnPlane(a,look),segment)/segment.sqrMagnitude);
                Assert.That(Vector3.ProjectOnPlane(Vector3.Lerp(a,b,t),look).magnitude,Is.LessThan(.015f),character+" / "+attack.name+" / "+pitch);
                Assert.That(MeleeMotionSample.TryEvaluate(attack,_defender.transform,crossing,direction*reach,look,1,out var pose,visible.avatar),Is.True);
                Assert.That(Vector3.Distance(pose.position,blade.position),Is.LessThan(.012f),"Server sweep must match the visible blade.");
                Assert.That(Quaternion.Angle(pose.rotation,blade.rotation),Is.LessThan(1f));
            }
            aim.Restore();
        }

        [TestCase("default")] [TestCase("security-officer")] [TestCase("megumi")]
        [TestCase("casual-1")] [TestCase("picochan")] [TestCase("brute")]
        public void BothHandsRemainOnTheHandleThroughReadyAndAllTwoHandedAttacks(string character)
        {
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            var loadout=_defender.GetComponent<WeaponLoadout>();
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true);
            foreach(var entry in WeaponCatalog.Instance.Weapons.Where(w=>w.TwoHanded))
            {
                Equip(_defender,entry.Kind);
                var states=entry.Attacks.Where(a=>a!=null).Select(a=>a.animationName).Concat(string.IsNullOrEmpty(entry.ReadyState) ? Array.Empty<string>() : new[]{entry.ReadyState});
                if(entry.Kind==MeleeWeaponKind.Greatsword) states=states.Concat(new[]{"Weapon_SwordGuard"});
                foreach(string state in states)
                foreach(float phase in new[]{0f,.15f,.3f,.5f,.7f,.9f})
                {
                    bool ready=state==entry.ReadyState;
                    bool attack=!ready && state!="Weapon_SwordGuard";
                    Set(Defender,"isAttacking",attack);
                    animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),attack && entry.Kind==MeleeWeaponKind.Greatsword ? 1 : 0);
                    animator.Play("Movement",0,0); animator.Play(state,1,phase); animator.Update(0);
                    skin.SyncPose(); Call(loadout,"SyncVisual");
                    var weapon=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(blade);
                    string context=character+" / "+state+" / "+phase;
                    var rightGrip=entry.Kind==MeleeWeaponKind.Axe ? new Vector3(0,0,weapon.InverseTransformPoint(Fist(visible,false)).z) : entry.RightGrip;
                    Assert.That(Vector3.Distance(Fist(visible,false),weapon.TransformPoint(rightGrip)),Is.LessThan(.035f),context+" right");
                    Assert.That(Vector3.Distance(Fist(visible,true),weapon.TransformPoint(WeaponLoadout.SupportGrip(visible,animator,entry,weapon))),Is.LessThan(.035f),context+" left");
                    if(ready)
                    {
                        // The stance is in front of the torso, which can sit behind the
                        // player root; smaller characters need a proportionate distance.
                        Vector3 midpoint=_defender.transform.InverseTransformVector((Palm(visible,true)+Palm(visible,false))*.5f-
                            visible.GetBoneTransform(HumanBodyBones.Chest).position);
                        Assert.That(midpoint.z,Is.GreaterThan(.18f*CharacterPoseFollower.GetViewScale(_defender.transform)),context+" ready must be in front of torso");
                        Assert.That(Mathf.Abs(midpoint.x),Is.LessThan(.18f),context+" ready must be centered");
                    }
                }
            }
        }

        [TestCase("default")] [TestCase("security-officer")] [TestCase("megumi")]
        [TestCase("casual-1")] [TestCase("picochan")] [TestCase("brute")]
        public void AxeKeepsTheAuthoredDominantArmAndSupportGripThroughoutTheSwing(string character)
        {
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            Equip(_defender,MeleeWeaponKind.Axe); Set(Defender,"isAttacking",true);
            animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),0);
            var hand=visible.GetBoneTransform(HumanBodyBones.RightHand);
            var elbow=visible.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var loadout=_defender.GetComponent<WeaponLoadout>();
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true);
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe);
            for(int i=0;i<=120;i++)
            {
                animator.Play("Movement",0,0); animator.Play("Weapon_AxeChop",1,i/120f); animator.Update(0); skin.SyncPose();
                Vector3 wrist=hand.position, joint=elbow.position; Quaternion rotation=hand.rotation;
                Call(loadout,"SyncVisual");
                var weapon=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(blade);
                string context=character+" / phase "+i+"/120";
                Assert.That(Vector3.Distance(hand.position,wrist),Is.LessThan(.001f),context);
                Assert.That(Vector3.Distance(elbow.position,joint),Is.LessThan(.001f),context);
                Assert.That(Quaternion.Angle(hand.rotation,rotation),Is.LessThan(.1f),context);
                Assert.That(Vector3.Distance(Fist(visible,false),weapon.TransformPoint(entry.RightGrip)),Is.LessThan(.01f),context);
                Vector3 support=weapon.InverseTransformPoint(Fist(visible,true));
                Assert.That(new Vector2(support.x,support.y).magnitude,Is.LessThan(.035f),context+" support hand on shaft");
                Assert.That(support.z,Is.GreaterThan(entry.Mesh.bounds.min.z),context+" support hand above handle end");
            }
        }

        private static Vector3 Fist(Animator animator, bool left)
        {
            var knuckle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var tip = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
            return knuckle != null && tip != null ? (knuckle.position+tip.position)*.5f : Palm(animator,left);
        }

        [Test] public void SwitchingBackToOneHandedSwordRestoresItsAuthoredMountRotation()
        {
            var blade = _defender.GetComponentInChildren<MeleeHitBox>(true).transform;
            Quaternion mount = blade.localRotation;
            Equip(_defender, MeleeWeaponKind.Greatsword);
            var animator = _defender.GetComponent<Animator>();
            animator.Play("Weapon_SwordGuard",1,.5f); animator.Update(0);
            Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
            Assert.That(Quaternion.Angle(mount, blade.localRotation), Is.GreaterThan(10f));
            Equip(_defender, MeleeWeaponKind.Sword);
            Assert.That(Quaternion.Angle(mount, blade.localRotation), Is.LessThan(.01f));
        }

        [TestCase("default")] [TestCase("brute")] [TestCase("security-officer")]
        [TestCase("megumi")] [TestCase("casual-1")] [TestCase("picochan")]
        public void GreatswordGripPreservesBothAuthoredArmsWithoutFlipping(string character)
        {
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            var loadout=_defender.GetComponent<WeaponLoadout>();
            var bones=new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand};
            foreach(var entry in WeaponCatalog.Instance.Weapons.Where(w=>w.Kind==MeleeWeaponKind.Greatsword))
            {
                Equip(_defender,entry.Kind); Set(Defender,"isAttacking",true);
                animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),1);
                foreach(var attack in entry.Attacks.Where(a=>a!=null))
                {
                    Quaternion[] previous=null;
                    // Include the interpolation midpoints, not only baked keyframes.
                    for(int frame=0;frame<=240;frame++)
                    {
                        animator.Play("Movement",0,0); animator.Play(attack.animationName,1,frame/240f); animator.Update(0);
                        skin.SyncPose();
                        var sourceArms=bones.Select(b=>visible.GetBoneTransform(b).rotation).ToArray();
                        Call(loadout,"SyncVisual");
                        bool supportCorrection=CharacterCatalog.Instance.Find(character).WristDrivenGreatswordFinisher &&
                            attack.animationName=="Weapon_Greatsword3";
                        for(int b=supportCorrection?3:0;b<bones.Length;b++) Assert.That(Quaternion.Angle(sourceArms[b],visible.GetBoneTransform(bones[b]).rotation),
                            Is.LessThan(.06f),character+" / "+attack.animationName+" must retain authored dominant arm: "+bones[b]);
                        var rotations=bones.Select(b=>visible.GetBoneTransform(b).localRotation).ToArray();
                        if(previous!=null) for(int i=0;i<bones.Length;i++)
                            Assert.That(Quaternion.Angle(previous[i],rotations[i]),Is.LessThan(35f),character+" / "+attack.animationName+" / "+frame+" / "+bones[i]);
                        previous=rotations;

                    }
                }
            }
        }

        [TestCase("default")] [TestCase("brute")] [TestCase("security-officer")]
        [TestCase("megumi")] [TestCase("casual-1")] [TestCase("picochan")]
        public void ShieldKeepsTheFistBehindItsBackSurfaceAndSwordGuardIsHorizontal(string character)
        {
            var animator = _defender.GetComponent<Animator>();
            using var skin = new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character), out var error), Is.True, error);
            var visible = (Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            var loadout = _defender.GetComponent<WeaponLoadout>();
            Equip(_defender, MeleeWeaponKind.SwordShield);
            animator.Play("Weapon_ShieldGuard",1,.5f); animator.Update(0); skin.SyncPose(); Call(loadout,"SyncVisual");
            var shield = _defender.transform.Find("Equipped shield");
            Assert.That(shield.InverseTransformPoint(Fist(visible,true)).x, Is.LessThan(WeaponCatalog.Instance.ShieldMesh.bounds.min.x-.01f), character+" hand must be inside the shield");
            Assert.That(shield.InverseTransformPoint(visible.GetBoneTransform(HumanBodyBones.LeftLowerArm).position).x,
                Is.LessThan(WeaponCatalog.Instance.ShieldMesh.bounds.min.x-.04f), character+" forearm must stay behind the shield");
            Equip(_defender, MeleeWeaponKind.Greatsword);
            animator.Play("Weapon_SwordGuard",1,.5f); animator.Update(0); skin.SyncPose(); Call(loadout,"SyncVisual");
            var hitbox = _defender.GetComponentInChildren<MeleeHitBox>(true);
            var weapon = (Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(hitbox);
            Assert.That(Mathf.Abs(Vector3.Dot(weapon.forward,_defender.transform.up)), Is.LessThan(.16f), character+" horizontal blade");
            Assert.That(Fist(visible,false).y, Is.GreaterThan(visible.GetBoneTransform(HumanBodyBones.Chest).position.y-.10f), character+" chest-height guard");
        }

        [TestCase("Greatsword1", "Great Sword Slash combo", 0f, .34f)]
        [TestCase("Greatsword2", "Great Sword Slash combo", .34f, .66f)]
        [TestCase("Greatsword3", "Great Sword Slash combo", .66f, 1f)]
        [TestCase("Riposte1", "Great Sword Slash combo", .18f, .34f)]
        [TestCase("Riposte2", "Great Sword Slash combo", .40f, .60f)]
        [TestCase("AxeChop", "KevinIglesias/HumanM@Attack2H01", 0f, 1f)]
        public void TwoHandedAttacksRetainTheirSourceMuscleCurves(string name,string take,float from,float to)
        {
            var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Remodel/Weapons/Source/"+(take.Contains('/') ? take : "Mixamo/"+take)+".fbx")
                .OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Remodel/Weapons/"+name+".anim");
            foreach(var binding in AnimationUtility.GetCurveBindings(source).Where(b=>b.type==typeof(Animator) &&
                (b.propertyName.Contains("Arm") || b.propertyName.Contains("Hand") || b.propertyName.Contains("Leg") ||
                 b.propertyName.StartsWith("Spine") || b.propertyName.Contains("Chest"))))
            {
                var original=AnimationUtility.GetEditorCurve(source,binding);
                var actual=AnimationUtility.GetEditorCurve(clip,binding);
                Assert.That(actual,Is.Not.Null,binding.propertyName);
                for(int frame=0;frame<=120;frame++)
                    Assert.That(actual.Evaluate(clip.length*frame/120f),
                        Is.EqualTo(original.Evaluate(source.length*Mathf.Lerp(from,to,frame/120f))).Within(.002f),name+" / "+binding.propertyName+" / "+frame);
            }
            Assert.That(AnimationUtility.GetCurveBindings(clip).Any(b=>b.type==typeof(WeaponLoadout)),Is.False,
                "A generated weapon path must not replace the source animation.");
        }

        [Test] public void AnimationOnlyDownloadsReuseTheMatchingSourceAvatar()
        {
            var avatar=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Brute/Source/Brute.fbx").GetComponent<Animator>().avatar;
            var paths=AssetDatabase.FindAssets("t:Model",new[]{"Assets/Remodel/Weapons/Source/Mixamo"}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            Assert.That(paths,Has.Length.EqualTo(9));
            foreach(string path in paths)
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.avatarSetup,Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther),path);
                Assert.That(importer.sourceAvatar,Is.SameAs(avatar),path);
            }
        }

        [Test] public void TwoHandedAttacksRetainFootworkWithoutMovingThePlayerRoot()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Set(Defender,"isAttacking",true);
            var animator=_defender.GetComponent<Animator>();
            animator.Play("Weapon_Greatsword1",1,.1f); animator.Update(0);
            Call(Defender,"UpdateWeaponCombat"); animator.Update(0);
            int layer=animator.GetLayerIndex("Weapon Footwork");
            Assert.That(animator.GetLayerWeight(layer),Is.EqualTo(1f));
            var first=animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
            var hip=animator.GetBoneTransform(HumanBodyBones.Hips).rotation;
            animator.Play("Weapon_Greatsword1",1,.8f); animator.Update(0);
            Assert.That(Quaternion.Angle(hip,animator.GetBoneTransform(HumanBodyBones.Hips).rotation),Is.GreaterThan(10));
            Assert.That(Vector3.Distance(first,animator.GetBoneTransform(HumanBodyBones.RightFoot).position),Is.GreaterThan(.05f));
            Assert.That(_defender.transform.position,Is.EqualTo(Vector3.zero));
            Set(Defender,"isAttacking",false); animator.Play("Weapon_GreatswordReady",1,0); animator.Update(0);
            Call(Defender,"UpdateWeaponCombat");
            Assert.That(animator.GetLayerWeight(layer),Is.Zero,"Moving with a ready weapon must retain locomotion.");
        }

        [TestCase("default")] [TestCase("brute")] [TestCase("security-officer")]
        [TestCase("megumi")] [TestCase("casual-1")] [TestCase("picochan")]
        public void GreatswordGuardFollowsLiveAimInsteadOfThePreviousSwing(string character)
        {
            var scene=_defender.scene; string previousName=scene.name; scene.name="Battle_waiting";
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            try
            {
                Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
                Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
                var loadout=_defender.GetComponent<WeaponLoadout>();
                Quaternion neutral=Quaternion.identity;
                foreach(bool crouch in new[]{false,true})
                foreach(float pitch in new[]{0f,-70f,-45f,45f,70f,85f,0f})
                {
                    Set(_defender.GetComponent<PlayerManager>(),"isCrouching",crouch);
                    Call(Defender,"RestoreMeleeAimPose");
                    animator.SetBool("IsCrouching",crouch);
                    animator.Play(crouch ? "Crouch Walk" : "New State",2,.5f);
                    animator.Play("Movement",0,0); animator.Play("Weapon_SwordGuard",1,.5f); animator.Update(0);
                    Set(Defender,"_lookPoseWeight",1f); Set(Defender,"_networkLookPitch",pitch); Set(Defender,"_lookPitch",pitch);
                    Set(Defender,"_meleeAimWeight",1f); Set(Defender,"_meleeAimDirection",Vector3.back);
                    Call(Defender,"UpdateMeleeAimPose"); skin.SyncPose();
                    var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
                    Call(Defender,"UpdateVisualMeleeAimPose",visible);
                    var wrists=new[]{visible.GetBoneTransform(HumanBodyBones.LeftHand),visible.GetBoneTransform(HumanBodyBones.RightHand)};
                    var authoredWrists=wrists.Select(w=>w.localRotation).ToArray();
                    var shoulders=new[]{visible.GetBoneTransform(HumanBodyBones.LeftUpperArm),visible.GetBoneTransform(HumanBodyBones.RightUpperArm)};
                    var authoredShoulders=shoulders.Select(s=>s.localRotation).ToArray();
                    Call(loadout,"SyncVisual");
                    var blade=_defender.GetComponentInChildren<MeleeHitBox>(true);
                    var weapon=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(blade);
                    if(pitch==0) neutral=weapon.rotation;
                    Assert.That(Quaternion.Angle(Quaternion.AngleAxis(pitch,Vector3.right)*neutral,weapon.rotation),Is.LessThan(7f),character+" / "+pitch);
                    Assert.That((float)typeof(PlayerCombat).GetField("_meleeAimWeight",Private).GetValue(Defender),Is.Zero);
                    Assert.That(Defender.IsWeaponGuarding,Is.True);
                    // A fixed eye-height target passed the old camera-ray assertion
                    // while folding both arms behind a downward-aimed torso.
                    for(int side=0;side<2;side++)
                    {
                        var elbow=visible.GetBoneTransform(side==0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                        float flexion=180f-Vector3.Angle(shoulders[side].position-elbow.position,wrists[side].position-elbow.position);
                        string label=character+" pitch="+pitch+" crouch="+crouch+" side="+side;
                        Assert.That(flexion,Is.InRange(9.9f,145.1f),label+" elbow flexion");
                        Assert.That(Quaternion.Angle(authoredWrists[side],wrists[side].localRotation),Is.LessThanOrEqualTo(40.1f),label+" wrist correction");
                        Assert.That(Quaternion.Angle(authoredShoulders[side],shoulders[side].localRotation),Is.LessThan(35f),label+" preserve authored shoulder");
                        Vector3 look=Quaternion.AngleAxis(pitch,Vector3.right)*Vector3.forward;
                        Assert.That(Vector3.Dot(wrists[side].position-shoulders[side].position,look),Is.GreaterThan(0),label+" hands stay in front of aimed shoulders");
                    }
                }
            }
            finally { scene.name=previousName; }
        }

        [TestCase(.01f)] [TestCase(.4f)] [TestCase(10f)]
        public void GuardArmSolverKeepsTheElbowBentAndUsesAReachableWrist(float reach)
        {
            var arm=new GameObject("Test upper arm").transform; arm.SetParent(_defender.transform,false);
            var elbow=new GameObject("Test elbow").transform; elbow.SetParent(arm,false); elbow.localPosition=Vector3.down*.3f;
            var hand=new GameObject("Test wrist").transform; hand.SetParent(elbow,false); hand.localPosition=Vector3.down*.3f;
            Vector3 target=arm.position+Vector3.forward*reach;
            WeaponLoadout.SolveArm(arm,elbow,hand,target,arm.position+Vector3.down,10f,145f);
            float flexion=180f-Vector3.Angle(arm.position-elbow.position,hand.position-elbow.position);
            Assert.That(flexion,Is.InRange(9.9f,145.1f));
            Assert.That(Vector3.Distance(arm.position,elbow.position),Is.EqualTo(.3f).Within(.0001f));
            Assert.That(Vector3.Distance(elbow.position,hand.position),Is.EqualTo(.3f).Within(.0001f));
            Assert.That(Vector3.Cross(hand.position-arm.position,Vector3.forward).magnitude,Is.LessThan(.0001f));
            Assert.That(elbow.position.y,Is.LessThan(arm.position.y),"The elbow must retain its downward bend, never flip upward.");
        }

        [TestCase("megumi")] [TestCase("security-officer")] [TestCase("picochan")]
        public void NativeFinisherKeepsTheSourceWristSweepAndBakedEffectAttached(string character)
        {
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            var frameMethod=typeof(CharacterSkin).Assembly.GetType("BattlePvp.Characters.CharacterEquipmentVisual")
                .GetMethod("HandFrame",BindingFlags.Static|BindingFlags.NonPublic);
            Quaternion Frame(Animator rig)=>(Quaternion)frameMethod.Invoke(null,new object[]{rig,HumanBodyBones.RightHand,0f});
            Equip(_defender,MeleeWeaponKind.Greatsword); Set(Defender,"isAttacking",true);
            animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),1);
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Greatsword);
            var attack=entry.Attacks[2]; var aim=new MeleeAimPose(_defender.transform,visible);
            var blade=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(_defender.GetComponentInChildren<MeleeHitBox>(true));
            foreach(float pitch in new[]{-40f,0f,40f}) for(int i=1;i<240;i++)
            {
                float phase=i/240f;
                aim.Restore(); animator.Play("Movement",0,0); animator.Play(attack.animationName,1,phase); animator.Update(0); skin.SyncPose();
                Vector3 look=Quaternion.AngleAxis(pitch,Vector3.right)*Vector3.forward;
                aim.ApplyCalibrated(look,aim.SelectReference(attack,1.4f),phase,1,look,attack.maxAimCalibration,true,attack.CrossingPhase(visible.avatar));
                Vector3 wristSweep=Frame(visible)*Quaternion.Inverse(Frame(animator))*(Fist(animator,false)-Fist(animator,true));
                Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
                string context=character+" pitch="+pitch+" phase="+phase;
                if(phase>=.12f && phase<=.6f)
                    Assert.That(Vector3.Angle(blade.forward,wristSweep),Is.LessThan(.1f),context+" blade must follow the source wrist, not converging fists");
                Assert.That(Vector3.Distance(Fist(visible,true),blade.TransformPoint(WeaponLoadout.SupportGrip(visible,animator,entry,blade))),Is.LessThan(.035f),context+" support grip");
                Assert.That(MeleeMotionSample.TryEvaluate(attack,_defender.transform,phase,look*1.4f,look,1,out var sample,visible.avatar),Is.True);
                Assert.That(Vector3.Distance(sample.position,blade.position),Is.LessThan(.012f),context+" effect position");
                Assert.That(Quaternion.Angle(sample.rotation,blade.rotation),Is.LessThan(1f),context+" effect rotation");
            }
            aim.Restore();
        }

        [TestCase(MeleeWeaponKind.Greatsword,0)] [TestCase(MeleeWeaponKind.Greatsword,1)]
        [TestCase(MeleeWeaponKind.Greatsword,2)] [TestCase(MeleeWeaponKind.Axe,0)]
        public void WeaponTrailSurvivesTheReadyToAttackCrossfade(MeleeWeaponKind kind,int attack)
        {
            var scene=_defender.scene; string previous=scene.name; scene.name="Lobby";
            try
            {
                Equip(_defender,kind);
                var animator=_defender.GetComponent<Animator>();
                var fx=_defender.GetComponent<BlockAttackVfx>(); Call(fx,"Awake");
                Set(Defender,"_currentAttackSpeed",1f); Set(Defender,"isAttacking",true);
                Call(Defender,"PlayAttackAnimation",attack);
                Assert.That(typeof(BlockAttackVfx).GetField("_animationState",Private).GetValue(fx),
                    Is.EqualTo(animator.GetNextAnimatorStateInfo(1).fullPathHash));
                var loadout=_defender.GetComponent<WeaponLoadout>();
                for(int frame=0;frame<20;frame++)
                {
                    animator.Update(1f/60); Call(loadout,"SyncVisual"); Call(fx,"UpdateBladeTrail",frame/60f);
                }
                Assert.That((bool)typeof(BlockAttackVfx).GetField("_emitting",Private).GetValue(fx),Is.True);
                var vertices=(System.Collections.Generic.List<Vector3>)typeof(BlockAttackVfx).GetField("_bladeVertices",Private).GetValue(fx);
                Assert.That(vertices.Count,Is.GreaterThan(4),"The corrected weapon must produce a visible ribbon after the blend.");
            }
            finally { scene.name=previous; }
        }

        [TestCase(-45f)] [TestCase(0f)] [TestCase(45f)]
        public void GreatswordFinisherCannotTwistTheWaistAroundToReachTheCrosshair(float pitch)
        {
            Equip(_defender,MeleeWeaponKind.Greatsword);
            var animator=_defender.GetComponent<Animator>(); var aim=new MeleeAimPose(_defender.transform,animator);
            var data=WeaponCatalog.Instance.Find(MeleeWeaponKind.Greatsword).Attacks[2];
            var spine=animator.GetBoneTransform(HumanBodyBones.Spine);
            Quaternion look=Quaternion.AngleAxis(pitch,Vector3.right);
            for(int frame=0;frame<=120;frame++)
            {
                aim.Restore(); animator.Play("Movement",0,0); animator.Play(data.animationName,1,frame/120f); animator.Update(0);
                Quaternion before=spine.rotation;
                aim.ApplyCalibrated(look*Vector3.forward,aim.SelectReference(data,1.4f),frame/120f,1,look*Vector3.forward,data.maxAimCalibration,data.aimInRootSpace,data.aimCrossingPhase);
                Assert.That(Quaternion.Angle(look*before,spine.rotation),Is.LessThan(65f),"The authored hip turn must not be inverted: phase "+frame/120f);
            }
            aim.Restore();
        }

        [Test] public void AxeEntryBlendsFromReadyAndSamplesTheIncomingAttackWindow()
        {
            Equip(_defender,MeleeWeaponKind.Axe);
            var animator=_defender.GetComponent<Animator>();
            animator.Play("New State",1,0); animator.Update(0);
            Set(Defender,"_currentAttackSpeed",1f); Set(Defender,"isAttacking",true);
            Call(Defender,"PlayAttackAnimation",0);
            Assert.That(animator.IsInTransition(1),Is.True);
            Assert.That(animator.GetNextAnimatorStateInfo(1).IsName("Weapon_AxeChop"),Is.True);
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true);
            Assert.That(typeof(MeleeHitBox).GetField("_sampleState",Private).GetValue(blade),
                Is.EqualTo(animator.GetNextAnimatorStateInfo(1).fullPathHash));
            var window=(AnimationHitWindow)typeof(MeleeHitBox).GetField("_animationWindow",Private).GetValue(blade);
            Assert.That(window.Start,Is.EqualTo(.325f).Within(.001f));
            animator.Update(.12f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Weapon_AxeChop"),Is.True);
            Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("Weapon Footwork")),Is.Zero,
                "Axe attacks must keep the locomotion legs.");
        }

        [Test] public void ThrustTurnsTheTorsoPullsTheElbowBackAndExtendsForward()
        {
            var animator=_defender.GetComponent<Animator>();
            animator.Play("Weapon_Thrust",1,.2f); animator.Update(0);
            Vector3 wind=animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
            Vector3 windPalm=Palm(animator,false);
            Quaternion chest=animator.GetBoneTransform(HumanBodyBones.Chest).rotation;
            animator.Play("Weapon_Thrust",1,.7f); animator.Update(0);
            Assert.That(wind.z,Is.LessThan(-.1f));
            Assert.That(Palm(animator,false).z-windPalm.z,Is.GreaterThan(.6f));
            Assert.That(Quaternion.Angle(chest,animator.GetBoneTransform(HumanBodyBones.Chest).rotation),Is.GreaterThan(15f));
        }

        [TestCase("default")] [TestCase("brute")] [TestCase("security-officer")]
        [TestCase("casual-1")] [TestCase("megumi")] [TestCase("picochan")]
        public void ThrustSupportUsesReachableArmWithoutCollapsingTheShoulderOrMovingTheBlade(string character)
        {
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            Equip(_defender,MeleeWeaponKind.Sword); Set(Defender,"isAttacking",true);
            var blade=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(_defender.GetComponentInChildren<MeleeHitBox>(true));
            var bones=new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand};
            Quaternion[] previous=null;
            Vector3 startingSupport=Vector3.zero;
            for(int i=0;i<=120;i++)
            {
                float phase=i/120f;
                animator.Play("Movement",0,0); animator.Play("Weapon_Thrust",1,phase); animator.Update(0); skin.SyncPose();
                var rotations=bones.Select(b=>visible.GetBoneTransform(b).rotation).ToArray();
                var positions=bones.Select(b=>visible.GetBoneTransform(b).position).ToArray();
                var bladePose=new Pose(blade.position,blade.rotation);
                Vector3 beforeSupport=Fist(visible,true);
                Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
                string context=character+" / "+phase;
                Vector3 leftPosition=_defender.transform.InverseTransformPoint(Fist(visible,true));
                if(i==0) startingSupport=leftPosition;
                if(phase<=.45f)
                {
                    float chestZ=_defender.transform.InverseTransformPoint(visible.GetBoneTransform(HumanBodyBones.Chest).position).z;
                    Assert.That(leftPosition.z,Is.GreaterThan(chestZ-.03f),context+" support hand prepares in front, not behind the torso");
                    Assert.That(Vector3.Distance(beforeSupport,Fist(visible,true)),Is.LessThan(.001f),context+" do not pull the hand onto the retracted sword");
                }
                if(i==54)
                    Assert.That(leftPosition.x-startingSupport.x,Is.GreaterThan(.05f),context+" support hand moves across toward the right");
                var clavicle=visible.GetBoneTransform(HumanBodyBones.LeftShoulder);
                var opposite=visible.GetBoneTransform(HumanBodyBones.RightShoulder);
                var upperArm=visible.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Vector3 outward=(clavicle.position-opposite.position).normalized;
                Assert.That(Vector3.Dot((upperArm.position-clavicle.position).normalized,outward),Is.GreaterThanOrEqualTo(.70f),
                    context+" shoulder must retain its width instead of collapsing into the chest");
                for(int b=0;b<bones.Length;b++)
                {
                    Assert.That(Quaternion.Angle(rotations[b],visible.GetBoneTransform(bones[b]).rotation),Is.LessThan(.03f),context+" dominant arm");
                    Assert.That(Vector3.Distance(positions[b],visible.GetBoneTransform(bones[b]).position),Is.LessThan(.001f),context+" dominant arm");
                }
                Assert.That(Vector3.Distance(bladePose.position,blade.position),Is.LessThan(.001f),context+" blade");
                Assert.That(Quaternion.Angle(bladePose.rotation,blade.rotation),Is.LessThan(.03f),context+" blade");
                // Contact is possible only within the avatar's real arm reach.
                // The previous unconditional grip-distance assertion accepted an
                // inward-folded clavicle as a way to reach an impossible target.
                var elbow=visible.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var hand=visible.GetBoneTransform(HumanBodyBones.LeftHand);
                float upper=Vector3.Distance(upperArm.position,elbow.position), lower=Vector3.Distance(elbow.position,hand.position);
                var frameArgs=new object[]{visible,HumanBodyBones.RightHand,0f};
                typeof(CharacterSkin).Assembly.GetType("BattlePvp.Characters.CharacterEquipmentVisual")
                    .GetMethod("HandFrame",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,frameArgs);
                float handSize=(float)frameArgs[2];
                Vector3 support=Fist(visible,false)-blade.forward*Mathf.Clamp(handSize*.7f,.035f,.07f);
                Vector3 targetWrist=support-(Fist(visible,true)-hand.position);
                float reach=Mathf.Sqrt(upper*upper+lower*lower+2*upper*lower*Mathf.Cos(8f*Mathf.Deg2Rad))-.001f;
                float unavoidableGap=Mathf.Max(0,Vector3.Distance(upperArm.position,targetWrist)-reach);
                if(phase>=.62f && phase<=.82f)
                    Assert.That(Vector3.Distance(Fist(visible,true),support),Is.LessThan(unavoidableGap+.004f),
                        context+" support hand joins the pommel during extension without stretching the arm");
                var current=new[]{visible.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation,
                    visible.GetBoneTransform(HumanBodyBones.LeftLowerArm).localRotation,visible.GetBoneTransform(HumanBodyBones.LeftHand).localRotation};
                if(previous!=null) for(int b=0;b<current.Length;b++)
                    Assert.That(Quaternion.Angle(previous[b],current[b]),Is.LessThan(35f),context+" support-arm continuity "+b);
                previous=current;
            }
        }

        [Test] public void AxeWindsBehindTheShoulderAndCutsForwardWithTheImportedWrist()
        {
            Equip(_defender,MeleeWeaponKind.Axe); Set(Defender,"isAttacking",true);
            var animator=_defender.GetComponent<Animator>(); var loadout=_defender.GetComponent<WeaponLoadout>();
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true).transform;
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe);
            animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),0);
            animator.Play("Weapon_AxeChop",1,.28f); animator.Update(0); Call(loadout,"SyncVisual");
            Vector3 wind=blade.TransformPoint(entry.BladeTip);
            animator.Play("Weapon_AxeChop",1,.36f); animator.Update(0);
            Quaternion wrist=animator.GetBoneTransform(HumanBodyBones.RightHand).rotation;
            Call(loadout,"SyncVisual");
            Vector3 strike=blade.TransformPoint(entry.BladeTip);
            Assert.That(wind.z,Is.LessThan(-.5f),"Authored shoulder wind-up.");
            Assert.That(strike.z,Is.GreaterThan(1f),"Cut travels forward.");
            Assert.That(wind.y-strike.y,Is.GreaterThan(.3f),"The upper-body cut descends without the original leg crouch.");
            Assert.That(Quaternion.Angle(wrist,animator.GetBoneTransform(HumanBodyBones.RightHand).rotation),Is.LessThan(.01f),
                "Do not add a procedural wrist flick to this imported take.");
        }

        [Test] public void WeaponReadyPoseReturnsAfterAttackAndDoesNotOverrideBowOrSkills()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); var animator=_defender.GetComponent<Animator>();
            animator.Play("New State",1,0); animator.Update(0); Call(Defender,"UpdateWeaponReadyPose"); animator.Update(.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Weapon_GreatswordReady"),Is.True);
            Set(Defender,"_isCastingMonostatStrSkill",true); Call(Defender,"UpdateWeaponReadyPose"); animator.Update(.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("New State"),Is.True);
            Set(Defender,"_isCastingMonostatStrSkill",false); Set(Defender,"_isBowEquipped",true);
            animator.Play("Bow_AimHold",1,.3f); animator.Update(0); Call(Defender,"UpdateWeaponReadyPose"); animator.Update(.05f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Bow_AimHold"),Is.True);
        }
        [Test] public void ShieldRaiseSettlesIntoGuardAndReleaseReturnsToLocomotion()
        {
            Equip(_defender,MeleeWeaponKind.SwordShield);
            var animator=_defender.GetComponent<Animator>();
            Call(Defender,"SetWeaponGuard",true); Call(Defender,"UpdateWeaponCombat"); animator.Update(.09f);
            for(int i=0;i<6;i++) animator.Update(.1f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Weapon_ShieldGuard"),Is.True);
            Call(Defender,"SetWeaponGuard",false); Call(Defender,"UpdateWeaponCombat");
            for(int i=0;i<6;i++) animator.Update(.1f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("New State"),Is.True);
            Assert.That(Defender.IsWeaponGuarding,Is.False);
            // Releasing during the raise must not let its exit transition restore guard.
            Call(Defender,"SetWeaponGuard",true); Call(Defender,"UpdateWeaponCombat"); animator.Update(.03f);
            Call(Defender,"SetWeaponGuard",false); Call(Defender,"UpdateWeaponCombat");
            for(int i=0;i<6;i++) animator.Update(.1f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("New State"),Is.True);
        }

        [Test] public void GreatswordGuardEnteredFromReadyRemainsAtTheLeftChestUntilReleased()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword);
            var animator=_defender.GetComponent<Animator>();
            animator.Play("Weapon_GreatswordReady",1,0); animator.Update(0);
            Assert.That(Call(Defender,"SetWeaponGuard",true),Is.True);
            for(int i=0;i<30;i++) { Call(Defender,"UpdateWeaponCombat"); animator.Update(1f/60); }
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Weapon_SwordGuard"),Is.True,
                "The ready-pose exit must not overwrite the guard CrossFade in Update.");
            Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true).transform;
            var chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            var grip=Fist(animator,false);
            Assert.That(Mathf.Abs(Vector3.Dot(blade.forward,Vector3.up)),Is.LessThan(.16f));
            Assert.That(grip.y,Is.GreaterThan(chest.position.y-.10f));
            Assert.That(grip.x-chest.position.x,Is.LessThan(-.06f),"Hands on the character's left.");
            Call(Defender,"SetWeaponGuard",false);
            for(int i=0;i<30;i++) { Call(Defender,"UpdateWeaponCombat"); animator.Update(1f/60); }
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Weapon_GreatswordReady"),Is.True);
        }

        [Test] public void AxeUsesALargeSingleCuttingHead()
        {
            Equip(_defender,MeleeWeaponKind.Axe);
            var animator=_defender.GetComponent<Animator>();
            animator.Play("New State",1,0); animator.Update(0);
            Call(_defender.GetComponent<WeaponLoadout>(),"SyncVisual");
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true).transform;
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe);
            Assert.That(entry.Mesh.bounds.size.z,Is.GreaterThan(1.7f));
            Assert.That(-entry.Mesh.bounds.min.x,Is.GreaterThan(entry.Mesh.bounds.max.x*3),"Single cutting side.");
            Assert.That(entry.HitCenter.x,Is.LessThan(-.05f),"Damage volume follows the offset axe head.");
        }
        [Test] public void TwoHandedGripStaysDuringAttackRecoveryAndReadyTransition()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); var animator=_defender.GetComponent<Animator>();
            var grip=typeof(PlayerCombat).GetProperty("UsesTwoHandedGrip",Private);
            Set(Defender,"isAttacking",false);
            animator.Play("Weapon_Greatsword1",1,.95f); animator.Update(0);
            Assert.That(grip.GetValue(Defender),Is.True,"Recovery after the damage/combo window still holds the handle.");
            animator.Play("New State",1,0); animator.Update(0);
            Call(Defender,"UpdateWeaponReadyPose"); animator.Update(.02f);
            Assert.That(grip.GetValue(Defender),Is.True,"The blend into ready stance holds both hands.");
            Set(Defender,"_isBowEquipped",true);
            Assert.That(grip.GetValue(Defender),Is.False);
        }

        [TestCase("default")] [TestCase("brute")] [TestCase("security-officer")]
        [TestCase("megumi")] [TestCase("casual-1")] [TestCase("picochan")]
        public void AxeIdleAndLocomotionUseExactlyTheOneHandedSwordPose(string character)
        {
            var animator=_defender.GetComponent<Animator>();
            using var skin=new CharacterSkin(_defender.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
            var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
            var bones=Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(b=>b!=HumanBodyBones.LastBone)
                .Select(visible.GetBoneTransform).Where(b=>b!=null).ToArray();
            var loadout=_defender.GetComponent<WeaponLoadout>();
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true);
            foreach(var move in new[]{Vector2.zero,Vector2.up,Vector2.down,Vector2.left,Vector2.right})
            {
                Equip(_defender,MeleeWeaponKind.Sword);
                animator.SetFloat("MoveX",move.x); animator.SetFloat("MoveY",move.y);
                animator.Play("Movement",0,.3f); animator.Play("New State",1,0); animator.Update(0); skin.SyncPose(); Call(loadout,"SyncVisual");
                var positions=bones.Select(b=>b.position).ToArray(); var rotations=bones.Select(b=>b.rotation).ToArray();
                var weapon=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(blade);
                Quaternion swordRotation=weapon.rotation;
                Equip(_defender,MeleeWeaponKind.Axe);
                Call(Defender,"UpdateWeaponReadyPose");
                animator.Play("Movement",0,.3f); animator.Update(0); skin.SyncPose(); Call(loadout,"SyncVisual");
                Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("New State"),Is.True);
                Assert.That(typeof(PlayerCombat).GetProperty("UsesTwoHandedGrip",Private).GetValue(Defender),Is.False);
                for(int i=0;i<bones.Length;i++)
                {
                    Assert.That(Vector3.Distance(positions[i],bones[i].position),Is.LessThan(.002f),character+" / "+move+" / "+bones[i].name);
                    Assert.That(Quaternion.Angle(rotations[i],bones[i].rotation),Is.LessThan(.1f),character+" / "+bones[i].name);
                }
                Assert.That(Quaternion.Angle(swordRotation*Quaternion.AngleAxis(180f,Vector3.forward),weapon.rotation),Is.LessThan(.1f),"Roll only the axe prop, leaving the one-handed skeleton unchanged.");
                if(move==Vector2.zero) Assert.That(Vector3.Dot(weapon.rotation*Vector3.left,Vector3.down),Is.GreaterThan(.5f),"Idle cutting edge must face down.");
                Assert.That(Vector3.Distance(Palm(visible,false),weapon.TransformPoint(WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe).RightGrip)),Is.LessThan(.01f));
            }
        }

        [TestCase(0f,0f)] [TestCase(0f,1f)] [TestCase(0f,-1f)] [TestCase(-1f,0f)] [TestCase(1f,0f)]
        public void AxeAttackLeavesTheHipsAndLegsOnLocomotion(float x,float y)
        {
            var attack=_defender.GetComponent<Animator>(); var move=_attacker.GetComponent<Animator>();
            Equip(_defender,MeleeWeaponKind.Axe); Equip(_attacker,MeleeWeaponKind.Sword);
            foreach(var animator in new[]{attack,move}) { animator.SetFloat("MoveX",x); animator.SetFloat("MoveY",y); }
            Set(Defender,"isAttacking",true); Call(Defender,"PlayAttackAnimation",0); Call(Defender,"UpdateWeaponCombat");
            var bones=new[]{HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
            for(int i=0;i<=20;i++)
            {
                float phase=i/20f;
                attack.Play("Movement",0,phase); move.Play("Movement",0,phase);
                attack.Play("Weapon_AxeChop",1,phase); move.Play("New State",1,0);
                attack.Update(0); move.Update(0);
                Assert.That(attack.GetLayerWeight(attack.GetLayerIndex("Weapon Footwork")),Is.Zero);
                foreach(var bone in bones)
                {
                    var a=attack.GetBoneTransform(bone); var b=move.GetBoneTransform(bone);
                    Assert.That(Vector3.Distance(attack.transform.InverseTransformPoint(a.position),move.transform.InverseTransformPoint(b.position)),Is.LessThan(.002f),bone+" / "+phase);
                    Assert.That(Quaternion.Angle(a.rotation,b.rotation),Is.LessThan(.1f),bone+" / "+phase);
                }
            }
        }

        [TestCase(-45f)] [TestCase(0f)] [TestCase(45f)]
        public void AxeDamageWindowUsesTheCuttingEdgeDuringTheDownstroke(float pitch)
        {
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe); var data=entry.Attacks[0];
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Remodel/Weapons/AxeChop.anim");
            var events=AnimationUtility.GetAnimationEvents(clip);
            float start=events.First(e=>e.functionName=="EnableHitBox").time/clip.length;
            float end=events.Last(e=>e.functionName=="DisableHitBox").time/clip.length;
            Vector3 direction=Quaternion.AngleAxis(pitch,Vector3.right)*Vector3.forward;
            for(float phase=start;phase<end;phase+=.005f)
            {
                MeleeMotionSample.TryEvaluate(data,_defender.transform,phase,direction*1.4f,direction,1,out var a);
                MeleeMotionSample.TryEvaluate(data,_defender.transform,phase+.002f,direction*1.4f,direction,1,out var b);
                Vector3 travel=(b.position+b.rotation*entry.BladeTip)-(a.position+a.rotation*entry.BladeTip);
                Assert.That(Vector3.Dot(a.rotation*Vector3.left,travel.normalized),Is.GreaterThan(.45f),"Cutting edge leads the strike at "+phase);
            }
        }
        [Test] public void ServerRejectsInvalidWeaponsAndMidBattleChangesAfterInitialSelection()
        {
            var scene=_defender.scene; string previousName=scene.name; scene.name="Battle";
            try
            {
                _defender.GetComponent<BattlePvp.Stats.StatManager>().ApplyLocalSceneStats(new BattlePvp.Stats.StatContainer
                    { STR = new BattlePvp.Stats.StatSlot { Invested = 30 } });
                var loadout=_defender.GetComponent<WeaponLoadout>(); loadout.OnStartServer();
                Assert.That(Call(loadout,"TrySelect",(MeleeWeaponKind)99,true),Is.False);
                Assert.That(Call(loadout,"TrySelect",MeleeWeaponKind.Axe,true),Is.True);
                Set(loadout,"_nextRequest",0d);
                Assert.That(Call(loadout,"TrySelect",MeleeWeaponKind.SwordShield,true),Is.False);
                Assert.That(Call(loadout,"TrySelect",MeleeWeaponKind.SwordShield,false),Is.False);
            }
            finally { scene.name=previousName; }
        }
    }
}
