using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using BodyPart = BattlePvp.Combat.BodyPart;

namespace BattlePvp.EditorTests
{
    public sealed class PassiveCombatTests
    {
        private GameObject _a, _b;
        private Scene _scene;
        private string _sceneName;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Set(object obj,string name,object value) => obj.GetType().GetField(name,Private).SetValue(obj,value);
        private static object Call(object obj,string name,params object[] args) => obj.GetType().GetMethod(name,Private|BindingFlags.Public).Invoke(obj,args);
        private static GameObject Player()
        {
            var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            EditorTestLifecycle.BindNetwork(p);
            foreach(var type in new[]{typeof(HealthSystem),typeof(PassiveLoadout),typeof(PlayerCombat),typeof(WeaponLoadout),typeof(ExpandedSkillController),typeof(AttackProcessor)})
                EditorTestLifecycle.Invoke(p.GetComponent(type),"Awake");
            p.GetComponent<StatManager>().ApplyLocalSceneStats(new StatContainer { STR=new StatSlot{Invested=8},CON=new StatSlot{Invested=7},AGI=new StatSlot{Invested=8},DEF=new StatSlot{Invested=7} });
            Set(p.GetComponent<HealthSystem>(),"_maxHp",1000f); Set(p.GetComponent<HealthSystem>(),"_currentHp",1000f);
            return p;
        }
        [SetUp] public void Setup()
        {
            _scene=SceneManager.GetActiveScene(); _sceneName=_scene.name; _scene.name="Lobby";
            _a=Player(); _b=Player(); _a.transform.position=Vector3.back; _b.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(_a); Object.DestroyImmediate(_b);
            _scene.name=_sceneName;
        }
        private static PassiveLoadout Equip(GameObject p,PassiveKind a,PassiveKind b=PassiveKind.None)
        { var passive=p.GetComponent<PassiveLoadout>(); Call(passive,"Apply",new object[]{new[]{(int)a,(int)b}}); return passive; }
        private static void Hp(GameObject p,float current,float max=1000) { Set(p.GetComponent<HealthSystem>(),"_maxHp",max); Set(p.GetComponent<HealthSystem>(),"_currentHp",current); }

        [TestCase(-1,1,false)] [TestCase(14,1,false)] [TestCase(1,1,false)]
        [TestCase(0,0,true)] [TestCase(0,13,true)] [TestCase(1,13,true)]
        public void SlotsRejectUnknownIdsAndDuplicates(int first,int second,bool expected)
        { Assert.That(PassiveLoadout.Validate(new[]{first,second}),Is.EqualTo(expected)); Assert.That(PassiveLoadout.Validate(new[]{first}),Is.False); }

        [Test] public void StatPassivesStackAndRemovalRestoresTheOriginalStats()
        {
            var stats=_a.GetComponent<StatManager>(); var basis=stats.GetDerivedStats();
            Equip(_a,PassiveKind.Vitality,PassiveKind.Ironclad); var modified=stats.GetDerivedStats();
            Assert.That(modified.MaxHp,Is.EqualTo(basis.MaxHp*1.1f).Within(.001f));
            Assert.That(modified.DefenseEfficiencyPercent,Is.EqualTo(basis.DefenseEfficiencyPercent*1.1f).Within(.001f));
            Equip(_a,PassiveKind.Haste,PassiveKind.None); modified=stats.GetDerivedStats();
            Assert.That(modified.MaxHp,Is.EqualTo(basis.MaxHp));
            Assert.That(modified.MoveSpeed,Is.EqualTo(basis.MoveSpeed*1.1f).Within(.001f));
            Assert.That(modified.AttackSpeed,Is.EqualTo(basis.AttackSpeed*1.1f).Within(.001f));
            Equip(_a,PassiveKind.None); Assert.That(stats.GetDerivedStats().MoveSpeed,Is.EqualTo(basis.MoveSpeed));
        }
        [TestCase(1000,0)] [TestCase(500,.075f)] [TestCase(100,.135f)]
        public void BerserkerReactsToLiveHealthAndStacksWithHaste(float hp,float bonus)
        {
            var basis=_a.GetComponent<StatManager>().GetDerivedStats(); Equip(_a,PassiveKind.Berserker,PassiveKind.Haste); Hp(_a,hp);
            var actual=_a.GetComponent<StatManager>().GetDerivedStats();
            Assert.That(actual.AttackPower,Is.EqualTo(basis.AttackPower*(1+bonus)).Within(.001f));
            Assert.That(actual.AttackSpeed,Is.EqualTo(basis.AttackSpeed*(1.1f+bonus)).Within(.001f));
            Hp(_a,1000); Assert.That(_a.GetComponent<StatManager>().GetDerivedStats().AttackPower,Is.EqualTo(basis.AttackPower));
        }
        [Test] public void BlockPassivesHealOnlyForShieldsAndConsumeCounterOnOneHit()
        {
            var p=Equip(_a,PassiveKind.Counterattack,PassiveKind.HealingShield); Hp(_a,500);
            p.BlockSucceeded(false); Assert.That(_a.GetComponent<HealthSystem>().CurrentHp,Is.EqualTo(500));
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
            p.BlockSucceeded(true); p.BlockSucceeded(true); Assert.That(_a.GetComponent<HealthSystem>().CurrentHp,Is.EqualTo(520));
            p.HitAccepted(_b.transform,BodyPart.Body,DamageDelivery.Melee,new DamageResult(true,10,0));
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1f));
            p.BlockSucceeded(true); Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1f));
        }
        [Test] public void BackstabChecksRearArcAndAddsRatherThanMultipliesCounter()
        {
            var p=Equip(_a,PassiveKind.Backstab,PassiveKind.Counterattack); p.BlockSucceeded(false);
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1.2f).Within(.001f));
            _a.transform.position=Vector3.right; Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
            _a.transform.position=Vector3.forward; Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
            _a.transform.position=Vector3.back;
            p.HitAccepted(_b.transform,BodyPart.Head,DamageDelivery.Melee,new DamageResult(true,10,0));
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1f));
        }
        [Test] public void BackstabStunsOnRealMeleeHitAndSharesEightSecondCooldownAcrossVictimComponents()
        {
            var processor=_a.GetComponent<AttackProcessor>(); var health=_b.GetComponent<HealthSystem>();
            processor.ProcessSkillHit(1,_b.GetComponent<StatManager>(),health,Vector3.zero);
            float baseline=1000-health.CurrentHp; Hp(_b,1000);
            var p=Equip(_a,PassiveKind.Backstab); var control=_b.GetComponent<ExpandedSkillController>();
            processor.ProcessSkillHit(1,_b.GetComponent<StatManager>(),health,Vector3.zero);
            Assert.That(1000-health.CurrentHp,Is.EqualTo(baseline*1.1f).Within(.002f));
            Assert.That((double)typeof(ExpandedSkillController).GetField("_stunnedUntil",Private).GetValue(control)-control.Now,
                Is.EqualTo(1).Within(.03));
            Assert.That(control.BlocksCombat,Is.True);
            var cooldowns=(Dictionary<Transform,double>)typeof(PassiveLoadout).GetField("_backstabReadyAt",Private).GetValue(p);
            Assert.That(cooldowns[_b.transform]-Time.timeAsDouble,Is.EqualTo(8).Within(.03));
            var hitbox=new GameObject("Second body part"); hitbox.transform.SetParent(_b.transform,false);
            Assert.That(p.DamageMultiplier(hitbox.transform,DamageDelivery.Melee),Is.EqualTo(1));
            Set(control,"_stunnedUntil",0d); Hp(_b,1000);
            processor.ProcessSkillHit(1,_b.GetComponent<StatManager>(),health,Vector3.zero);
            Assert.That(1000-health.CurrentHp,Is.EqualTo(baseline).Within(.002f));
            Assert.That(control.IsStunned,Is.False);
            cooldowns[_b.transform]=Time.timeAsDouble-.01;
            processor.ProcessSkillHit(1,_b.GetComponent<StatManager>(),health,Vector3.zero);
            Assert.That(control.IsStunned,Is.True);
        }
        [Test] public void BackstabCooldownIsIndependentForEachVictimAndResetsWithAttackerLife()
        {
            var third=Player();
            try
            {
                third.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var p=Equip(_a,PassiveKind.Backstab); var hit=new DamageResult(true,10,0);
                p.HitAccepted(_b.transform,BodyPart.Body,DamageDelivery.Melee,hit);
                Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1));
                Assert.That(p.DamageMultiplier(third.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
                p.HitAccepted(third.transform,BodyPart.Body,DamageDelivery.Melee,hit);
                Assert.That(_b.GetComponent<ExpandedSkillController>().IsStunned,Is.True);
                Assert.That(third.GetComponent<ExpandedSkillController>().IsStunned,Is.True);
                Assert.That(p.DamageMultiplier(third.transform,DamageDelivery.Melee),Is.EqualTo(1));
                Call(p,"ResetLife");
                Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
                Assert.That(p.DamageMultiplier(third.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
            }
            finally { Object.DestroyImmediate(third); }
        }
        [TestCase(DamageDelivery.Ranged,true,10,true)]
        [TestCase(DamageDelivery.Other,true,10,true)]
        [TestCase(DamageDelivery.Melee,false,10,true)]
        [TestCase(DamageDelivery.Melee,true,0,true)]
        [TestCase(DamageDelivery.Melee,true,10,false)]
        public void BackstabDoesNotStunOrSpendCooldownOnIneligibleHits(DamageDelivery delivery,bool accepted,float damage,bool behind)
        {
            var p=Equip(_a,PassiveKind.Backstab);
            if(!behind) _a.transform.position=Vector3.forward;
            p.HitAccepted(_b.transform,BodyPart.Body,delivery,new DamageResult(accepted,damage,0));
            Assert.That(_b.GetComponent<ExpandedSkillController>().IsStunned,Is.False);
            _a.transform.position=Vector3.back;
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1.1f));
        }
        [Test] public void BackstabWorksAgainstAbsorptionShieldsAndRespectsPurification()
        {
            var p=Equip(_a,PassiveKind.Backstab); Equip(_b,PassiveKind.Purification);
            p.HitAccepted(_b.transform,BodyPart.Body,DamageDelivery.Melee,new DamageResult(true,0,10));
            var control=_b.GetComponent<ExpandedSkillController>();
            Assert.That((double)typeof(ExpandedSkillController).GetField("_stunnedUntil",Private).GetValue(control)-control.Now,
                Is.EqualTo(.85).Within(.03));
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1));
        }
        [Test] public void BackstabStunsTrainingDummyAndDoesNotApplyStunToKilledTarget()
        {
            var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Dummy.prefab"));
            try
            {
                EditorTestLifecycle.BindNetwork(target); target.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var p=Equip(_a,PassiveKind.Backstab); var dummy=target.GetComponent<DummyHealth>();
                p.HitAccepted(dummy,BodyPart.Body,DamageDelivery.Melee,new DamageResult(true,10,0));
                Assert.That(dummy.IsStunned,Is.True);
                Assert.That(p.DamageMultiplier(target.transform,DamageDelivery.Melee),Is.EqualTo(1));
                p.HitAccepted(_b.transform,BodyPart.Body,DamageDelivery.Melee,new DamageResult(true,10,0,true));
                Assert.That(_b.GetComponent<ExpandedSkillController>().IsStunned,Is.False);
            }
            finally { Object.DestroyImmediate(target); }
        }
        [TestCase(DamageDelivery.Melee,1f)] [TestCase(DamageDelivery.Ranged,1.12f)]
        public void SniperChangesActualDamageOnlyForRangedHits(DamageDelivery delivery,float multiplier)
        {
            var processor=_a.GetComponent<AttackProcessor>(); var target=_b.GetComponent<HealthSystem>();
            Hp(_b,1000); processor.ProcessSkillHit(1,_b.GetComponent<StatManager>(),target,Vector3.zero,delivery:delivery); float baseline=1000-target.CurrentHp;
            Equip(_a,PassiveKind.Sniper); Hp(_b,1000);
            processor.ProcessSkillHit(1,_b.GetComponent<StatManager>(),target,Vector3.zero,delivery:delivery);
            Assert.That(1000-target.CurrentHp,Is.EqualTo(baseline*multiplier).Within(.002f));
        }
        [Test] public void HeadWindowRequiresFourHitsOnTheSameLifeInsideRollingFourSeconds()
        {
            var window=System.Activator.CreateInstance(typeof(PassiveLoadout).GetNestedType("HeadWindow",BindingFlags.NonPublic),true);
            bool Hit(double now,uint life) => (bool)Call(window,"Hit",now,life);
            Assert.That(Hit(0,0),Is.False); Assert.That(Hit(1,0),Is.False); Assert.That(Hit(2,0),Is.False);
            Assert.That(Hit(4.01,0),Is.False); Assert.That(Hit(4.5,0),Is.True);
            Assert.That(Hit(4.6,0),Is.False); Assert.That(Hit(4.7,0),Is.False); Assert.That(Hit(4.8,1),Is.False);
            Assert.That(Hit(4.9,1),Is.False); Assert.That(Hit(5,1),Is.False); Assert.That(Hit(5.1,1),Is.True);
        }
        [Test] public void ConcussionIgnoresBodyBlockedAndInvulnerableHitsAndStunsOnFourthHead()
        {
            var p=Equip(_a,PassiveKind.Concussion); var target=_b.GetComponent<ExpandedSkillController>(); var hit=new DamageResult(true,10,0);
            for(int i=0;i<5;i++) { p.HitAccepted(_b.transform,BodyPart.Body,DamageDelivery.Melee,hit); p.HitAccepted(_b.transform,BodyPart.Head,DamageDelivery.Melee,default); }
            for(int i=0;i<3;i++) p.HitAccepted(_b.transform,BodyPart.Head,DamageDelivery.Ranged,hit);
            Assert.That(target.IsStunned,Is.False);
            p.HitAccepted(_b.transform,BodyPart.Head,DamageDelivery.Ranged,hit);
            Assert.That(target.IsStunned,Is.True);
            double until=(double)typeof(ExpandedSkillController).GetField("_stunnedUntil",Private).GetValue(target);
            Assert.That(until-target.Now,Is.EqualTo(1d).Within(.03d));
        }
        [Test] public void VictoryHealsOnceOnRealDeathAndNeverOverheals()
        {
            Equip(_a,PassiveKind.Victory); Hp(_a,500); Hp(_b,10);
            var victim=_b.GetComponent<HealthSystem>();
            var request=new DamageRequest(20,DamageSource.Poison,0,_a.GetComponent<HealthSystem>(),Vector3.zero);
            victim.ApplyDamage(request); victim.ApplyDamage(request);
            Assert.That(victim.IsDead,Is.True); Assert.That(_a.GetComponent<HealthSystem>().CurrentHp,Is.EqualTo(700));
            Hp(_a,950); victim.Revive(); Hp(_b,10); victim.ApplyDamage(request);
            Assert.That(_a.GetComponent<HealthSystem>().CurrentHp,Is.EqualTo(1000));
        }
        [Test] public void AbsorptionShieldStillConsumesCounterAndCountsHeadHits()
        {
            var p=Equip(_a,PassiveKind.Counterattack,PassiveKind.Concussion); p.BlockSucceeded(false);
            var absorbed=new DamageResult(true,0,10);
            p.HitAccepted(_b.transform,BodyPart.Head,DamageDelivery.Melee,absorbed);
            Assert.That(p.DamageMultiplier(_b.transform,DamageDelivery.Melee),Is.EqualTo(1));
            for(int i=0;i<3;i++) p.HitAccepted(_b.transform,BodyPart.Head,DamageDelivery.Melee,absorbed);
            Assert.That(_b.GetComponent<ExpandedSkillController>().IsStunned,Is.True);
        }
        [Test] public void PurificationShortensControlAndPoisonDuration()
        {
            Equip(_b,PassiveKind.Purification); var control=_b.GetComponent<ExpandedSkillController>();
            control.ApplyControl(4,true,false);
            double until=(double)typeof(ExpandedSkillController).GetField("_rootUntil",Private).GetValue(control);
            Assert.That(until-control.Now,Is.EqualTo(3.4).Within(.03));
            Assert.That(PassiveLoadout.DebuffDuration(_b.transform,10),Is.EqualTo(8.5f));
        }
        [Test] public void ScholarReducesChargeRechargeAndStandardSkillCooldowns()
        {
            Equip(_a,PassiveKind.Scholar); var skills=_a.GetComponent<ExpandedSkillController>();
            Assert.That(skills.SpendCharge(JobSkillKind.Knife),Is.True);
            Assert.That(skills.Read(JobSkillKind.Knife).NextChargeAt-skills.Now,
                Is.EqualTo(ExpandedSkillController.RechargeSeconds(JobSkillKind.Knife)*.88f).Within(.03));
            var combat=_a.GetComponent<PlayerCombat>();
            float cooldown=(float)typeof(PlayerCombat).GetProperty("MonostatStrCooldownSeconds",Private).GetValue(combat);
            Equip(_a,PassiveKind.None);
            float basis=(float)typeof(PlayerCombat).GetProperty("MonostatStrCooldownSeconds",Private).GetValue(combat);
            Assert.That(cooldown,Is.EqualTo(basis*.88f).Within(.001f));
        }
        [TestCase(DamageDelivery.Melee,true)] [TestCase(DamageDelivery.Ranged,false)] [TestCase(DamageDelivery.Other,false)]
        public void DefenseMonostatReflectsOnlyMelee(DamageDelivery delivery,bool reflected)
        {
            _b.GetComponent<StatManager>().ApplyLocalSceneStats(new StatContainer { DEF=new StatSlot{Invested=30} });
            Hp(_a,1000); Hp(_b,1000);
            _b.GetComponent<HealthSystem>().ApplyDamage(new DamageRequest(10,DamageSource.Physical,100,_a.GetComponent<HealthSystem>(),Vector3.zero,delivery:delivery));
            Assert.That(_a.GetComponent<HealthSystem>().CurrentHp<1000,Is.EqualTo(reflected));
        }
        [Test] public void AirJumpAuthorityAllowsOnlyOneExtraJumpAndLandingResetsIt()
        {
            var validator=new ServerMovementValidator(); validator.Reset(Vector3.zero,0);
            Assert.That(validator.TryBeginAirJump(.8f,0),Is.False);
            Assert.That(validator.TryAccept(Vector3.up*.5f,Quaternion.identity,.2,.2,5,.8f,9.81f,false,45),Is.True);
            Assert.That(validator.TryBeginAirJump(.8f,.2),Is.True);
            Assert.That(validator.TryBeginAirJump(.8f,.21),Is.False);
            Assert.That(validator.TryAccept(Vector3.up*1.2f,Quaternion.identity,.5,.5,5,.8f,9.81f,false,45),Is.True);
            Assert.That(validator.TryAccept(Vector3.zero,Quaternion.identity,.9,.9,5,.8f,9.81f,true,45),Is.True);
            Assert.That(validator.TryAccept(Vector3.up*.4f,Quaternion.identity,1.1,1.1,5,.8f,9.81f,false,45),Is.True);
            Assert.That(validator.TryBeginAirJump(.8f,1.1),Is.True);
        }

        [Test] public void BowChargeSpeedIncludesBothHasteAndMissingHealthBonus()
        {
            var bow=_a.GetComponent<BowAttackController>();
            float baseline=(float)Call(bow,"ResolveCharacterAttackSpeed");
            Equip(_a,PassiveKind.Haste,PassiveKind.Berserker); Hp(_a,500);
            Assert.That((float)Call(bow,"ResolveCharacterAttackSpeed"),Is.EqualTo(baseline*1.175f).Within(.001f));
        }

        [Test] public void LocalAirJumpNeedsPassiveAndCannotBeRepeatedOrUsedWhileLocked()
        {
            var movement=_a.GetComponent<PlayerManager>();
            Set(movement,"controller",_a.GetComponent<CharacterController>());
            _a.transform.position=Vector3.up*20;
            Set(movement,"_lastGroundedAt",double.NegativeInfinity);
            Set(movement,"_jumpRequestedUntil",Time.timeAsDouble+1);
            Assert.That((bool)Call(movement,"TryConsumeJumpRequest"),Is.False);
            Equip(_a,PassiveKind.DoubleJump); movement.ApplySkillInputLock(SkillInputLockFlags.Jump,10);
            Assert.That((bool)Call(movement,"TryConsumeJumpRequest"),Is.False);
            movement.ClearSkillInputLock();
            Assert.That((bool)Call(movement,"TryConsumeJumpRequest"),Is.True);
            Set(movement,"_lastJumpAt",double.NegativeInfinity); Set(movement,"_jumpRequestedUntil",Time.timeAsDouble+1);
            Assert.That((bool)Call(movement,"TryConsumeJumpRequest"),Is.False);
        }

        [Test] public void ServerSelectionRejectsAliveBattleChangesAndAllowsDeadOrLobbyChanges()
        {
            var p=_a.GetComponent<PassiveLoadout>(); Set(p,"_initialized",true);
            Assert.That((bool)Call(p,"TrySetChoices",new object[]{new[]{1,2}}),Is.True);
            _scene.name="Battle";
            Assert.That((bool)Call(p,"TrySetChoices",new object[]{new[]{3,4}}),Is.False);
            Assert.That(p.Snapshot(),Is.EqualTo(new[]{1,2}));
            Set(_a.GetComponent<HealthSystem>(),"_isDead",true);
            Assert.That((bool)Call(p,"TrySetChoices",new object[]{new[]{3,4}}),Is.True);
            Assert.That((bool)Call(p,"TrySetChoices",new object[]{new[]{4,4}}),Is.False);
            Assert.That(p.Snapshot(),Is.EqualTo(new[]{3,4}));
        }

        [Test] public void StoreAndSelectionUiKeepTwoIndependentSlotsAndReopenSavedPair()
        {
            var local=typeof(StatManager).GetField("<Local>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=local.GetValue(null); local.SetValue(null,_a.GetComponent<StatManager>());
            string key=(string)typeof(PassiveStore).GetProperty("Key",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            bool existed=PlayerPrefs.HasKey(key); string saved=PlayerPrefs.GetString(key,"");
            string presetKey=PassivePresetStore.Key;
            bool presetsExisted=PlayerPrefs.HasKey(presetKey); string presetsSaved=PlayerPrefs.GetString(presetKey,"");
            GameObject root=null;
            try
            {
                PassiveStore.Save(new[]{0,0});
                PlayerPrefs.DeleteKey(presetKey);
                root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/JobGuide.prefab"));
                var panel=root.GetComponent<BattlePvp.UI.JobGuidePanel>(); Call(panel,"Awake"); Call(panel,"Bind",_a.GetComponent<StatManager>());
                panel.Open(); Call(panel,"OpenPassives");
                Assert.That(root.transform.Find("Panel/PassiveSelection/List").GetComponent<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition,Is.EqualTo(1).Within(.001f));
                Assert.That(root.transform.Find("Panel/PassiveSelection/List/Content").childCount,Is.EqualTo(13));
                Call(panel,"EquipPassive",(int)PassiveKind.Haste,0); Call(panel,"EquipPassive",(int)PassiveKind.Vitality,1);
                Call(panel,"SavePassives"); panel.Close(); panel.Open(); Call(panel,"OpenPassives");
                Assert.That(_a.GetComponent<PassiveLoadout>().Snapshot(),Is.EqualTo(new[]{11,12}));
                Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{11,12}));
                var draft=(int[])typeof(BattlePvp.UI.JobGuidePanel).GetField("_passiveDraft",Private).GetValue(panel);
                Assert.That(draft,Is.EqualTo(new[]{11,12}));
                Call(panel,"EquipPassive",(int)PassiveKind.Haste,1); Call(panel,"SavePassives");
                Assert.That(PassiveStore.Read(),Is.EqualTo(new[]{12,11}),"Moving an equipped passive swaps slots instead of duplicating it.");
            }
            finally
            {
                if(root!=null) Object.DestroyImmediate(root); local.SetValue(null,previous);
                if(existed) PlayerPrefs.SetString(key,saved); else PlayerPrefs.DeleteKey(key);
                if(presetsExisted) PlayerPrefs.SetString(presetKey,presetsSaved); else PlayerPrefs.DeleteKey(presetKey);
                PlayerPrefs.Save();
            }
        }
    }
}
