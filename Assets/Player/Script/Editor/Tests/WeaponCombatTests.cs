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
                aim.Restore(); animator.Play("Movement",0,0); animator.Play(attack.animationName,1,.6f); animator.Update(0);
                Vector3 direction=Quaternion.AngleAxis(pitch,_attacker.transform.right)*_attacker.transform.forward;
                aim.ApplyCalibrated(direction,aim.SelectReference(attack,1.4f),.6f,1,direction);
                Assert.That(MeleeMotionSample.TryEvaluate(attack,_attacker.transform,.6f,direction*1.4f,direction,1,out var sample),Is.True);
                Assert.That(Vector3.Distance(sample.position,blade.position),Is.LessThan(.003f),attack.name);
                Assert.That(Quaternion.Angle(sample.rotation,blade.rotation),Is.LessThan(.2f),attack.name);
            }
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
