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
        [Test] public void ParryGrantsOneTimedRiposteAndHeldGuardDoesNotRefreshItsWindow()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            Set(Defender,"_guardStartedAt",Time.timeAsDouble-1);
            Call(Defender,"SetWeaponGuard",true);
            Assert.That(Call(Defender,"TryBlockMelee",Attacker),Is.False);
            Call(Defender,"SetWeaponGuard",false); Call(Defender,"SetWeaponGuard",true);
            Assert.That(Call(Defender,"TryBlockMelee",Attacker),Is.True);
            Assert.That(Defender.IsRiposteReady,Is.True); Assert.That(Defender.IsWeaponGuarding,Is.False);
            Assert.That(Call(Defender,"WeaponAttackAllowed",4),Is.True);
            Set(Defender,"_riposteUntil",Time.timeAsDouble-1);
            Assert.That(Call(Defender,"WeaponAttackAllowed",4),Is.False);
        }
        [Test] public void ChangingWeaponsClearsGuardRiposteAndAttackState()
        {
            Equip(_defender,MeleeWeaponKind.Greatsword); Call(Defender,"SetWeaponGuard",true);
            Call(Defender,"TryBlockMelee",Attacker); Equip(_defender,MeleeWeaponKind.Axe);
            Assert.That(Defender.IsRiposteReady||Defender.IsWeaponGuarding||Defender.IsAttackActive,Is.False);
            Assert.That(Call(Defender,"WeaponAttackAllowed",3),Is.False);
            Assert.That(Call(Defender,"CanContinueWeaponCombo",0),Is.False);
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
            finally { Object.DestroyImmediate(ui); Object.DestroyImmediate(input.gameObject); }
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
                aim.Restore(); animator.Play("Movement",0,0); animator.Play(attack.animationName,1,.6f); animator.Update(0);
                Vector3 direction=Quaternion.AngleAxis(pitch,_attacker.transform.right)*_attacker.transform.forward;
                aim.ApplyCalibrated(direction,aim.SelectReference(attack,1.4f),.6f,1,direction);
                if(entry.TwoHanded) WeaponLoadout.FitTwoHandedGrip(animator,blade,entry);
                Assert.That(MeleeMotionSample.TryEvaluate(attack,_attacker.transform,.6f,direction*1.4f,direction,1,out var sample),Is.True);
                Assert.That(Vector3.Distance(sample.position,blade.position),Is.LessThan(.003f),attack.name);
                Assert.That(Quaternion.Angle(sample.rotation,blade.rotation),Is.LessThan(.2f),attack.name);
            }
        }

        private static Vector3 Palm(Animator animator,bool left) => (Vector3)typeof(CharacterSkin).Assembly
            .GetType("BattlePvp.Characters.CharacterEquipmentVisual").GetMethod("PalmCenter",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{animator,left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand});

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
                var states=entry.Attacks.Where(a=>a!=null).Select(a=>a.animationName).Concat(new[]{entry.ReadyState});
                if(entry.Kind==MeleeWeaponKind.Greatsword) states=states.Concat(new[]{"Weapon_SwordGuard"});
                foreach(string state in states)
                foreach(float phase in new[]{0f,.15f,.3f,.5f,.7f,.9f})
                {
                    bool ready=state==entry.ReadyState;
                    Set(Defender,"isAttacking",!ready);
                    animator.Play("Movement",0,0); animator.Play(state,1,phase); animator.Update(0);
                    skin.SyncPose(); Call(loadout,"SyncVisual");
                    var weapon=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(blade);
                    string context=character+" / "+state+" / "+phase;
                    Assert.That(Vector3.Distance(Palm(visible,false),weapon.TransformPoint(entry.RightGrip)),Is.LessThan(.005f),context+" right");
                    Assert.That(Vector3.Distance(Palm(visible,true),weapon.TransformPoint(entry.LeftGrip)),Is.LessThan(.035f),context+" left");
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

        [Test] public void TwoHandedSourceClipsAlreadyHaveBothHandsTogetherBeforeRuntimeCorrection()
        {
            var animator=_defender.GetComponent<Animator>();
            foreach(var entry in WeaponCatalog.Instance.Weapons.Where(w=>w.TwoHanded))
            foreach(string state in entry.Attacks.Where(a=>a!=null).Select(a=>a.animationName).Concat(new[]{entry.ReadyState}))
            foreach(float phase in new[]{0f,.15f,.3f,.5f,.7f,.9f})
            {
                animator.Play("Movement",0,0); animator.Play(state,1,phase); animator.Update(0);
                Assert.That(Vector3.Distance(Palm(animator,false),Palm(animator,true)),
                    Is.LessThan(entry.Kind==MeleeWeaponKind.Axe?.39f:.25f),state+" / "+phase);
            }
        }

        [Test] public void ThrustTurnsTheTorsoPullsTheElbowBackAndExtendsForward()
        {
            var animator=_defender.GetComponent<Animator>();
            animator.Play("Weapon_Thrust",1,.15f); animator.Update(0);
            Vector3 wind=animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
            Vector3 windPalm=Palm(animator,false);
            Quaternion chest=animator.GetBoneTransform(HumanBodyBones.Chest).rotation;
            animator.Play("Weapon_Thrust",1,.3f); animator.Update(0);
            Assert.That(wind.z,Is.LessThan(-.1f));
            Assert.That(Palm(animator,false).z-windPalm.z,Is.GreaterThan(.6f));
            Assert.That(Quaternion.Angle(chest,animator.GetBoneTransform(HumanBodyBones.Chest).rotation),Is.GreaterThan(15f));
        }

        [Test] public void AxeRaisesAboveTheHeadAndChopsDownWithBothHands()
        {
            Equip(_defender,MeleeWeaponKind.Axe); Set(Defender,"isAttacking",true);
            var animator=_defender.GetComponent<Animator>(); var loadout=_defender.GetComponent<WeaponLoadout>();
            var blade=_defender.GetComponentInChildren<MeleeHitBox>(true).transform;
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Axe);
            animator.Play("Weapon_AxeChop",1,.3f); animator.Update(0); Call(loadout,"SyncVisual");
            float high=blade.TransformPoint(entry.BladeTip).y;
            Assert.That(high,Is.GreaterThan(animator.GetBoneTransform(HumanBodyBones.Head).position.y+.3f));
            animator.Play("Weapon_AxeChop",1,.6f); animator.Update(0); Call(loadout,"SyncVisual");
            Assert.That(high-blade.TransformPoint(entry.BladeTip).y,Is.GreaterThan(.7f));
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
        [Test] public void ServerRejectsInvalidWeaponsAndMidBattleChangesAfterInitialSelection()
        {
            var scene=_defender.scene; string previousName=scene.name; scene.name="Battle";
            try
            {
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
