using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BattlePvp.Remodel.Editor
{
    // Opt-in native Play Mode smoke test. No service login or web build is performed.
    [InitializeOnLoad] public static class SkillNativeValidation
    {
        const string Pending="BattlePvp.SkillNativeValidation";
        [Serializable] sealed class SceneSnapshot { public SavedScene[] scenes; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded,active; }
        static SkillNativeValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if(!SessionState.GetBool(Pending,false)) return;
                if(state==PlayModeStateChange.EnteredPlayMode) new GameObject("Skill native probe").AddComponent<SkillNativeProbe>();
                if(state==PlayModeStateChange.EnteredEditMode)
                {
                    SessionState.SetBool(Pending,false);
                    if(Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Pending+".ok",false)?0:1);
                    else
                    {
                        var snapshot=JsonUtility.FromJson<SceneSnapshot>(SessionState.GetString(Pending+".scenes",""));
                        if(snapshot?.scenes!=null) EditorSceneManager.RestoreSceneManagerSetup(snapshot.scenes.Select(s=>new SceneSetup{path=s.path,isLoaded=s.loaded,isActive=s.active}).ToArray());
                    }
                }
            };
        }
        public static void Run()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before validation.");
            if(!Application.isBatchMode)
            {
                for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                {
                    var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if(scene.isDirty || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save open scenes before native validation.");
                }
                SessionState.SetString(Pending+".scenes",JsonUtility.ToJson(new SceneSnapshot{scenes=EditorSceneManager.GetSceneManagerSetup().Select(s=>new SavedScene{path=s.path,loaded=s.isLoaded,active=s.isActive}).ToArray()}));
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(Pending,true); SessionState.SetBool(Pending+".ok",false);
            EditorApplication.EnterPlaymode();
        }
        internal static void Finish(bool ok)
        { SessionState.SetBool(Pending+".ok",ok); EditorApplication.ExitPlaymode(); }
    }
    public sealed class SkillNativeProbe : MonoBehaviour
    {
        const string Folder="Reports/SkillExpansion";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<string> _checks=new();
        readonly List<string> _errors=new();
        GameObject _a,_b;
        Camera _camera;
        float _previousCaptureDelta;
        IEnumerator Start()
        {
            _previousCaptureDelta=Time.captureDeltaTime; Time.captureDeltaTime=1f/60f;
            Application.logMessageReceived+=Log;
            var routine=Exercise();
            while(true)
            {
                object current;
                try { if(!routine.MoveNext()) break; current=routine.Current; }
                catch(Exception e) { _errors.Add(e.ToString()); break; }
                yield return current;
            }
            Application.logMessageReceived-=Log;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/native.json",JsonUtility.ToJson(new Result{passed=_errors.Count==0,checks=_checks.ToArray(),errors=_errors.ToArray()},true));
            Debug.Log("[SkillNative] "+_checks.Count+" checks; "+_errors.Count+" errors.");
            SkillNativeValidation.Finish(_errors.Count==0);
        }
        void OnDestroy() { Time.captureDeltaTime=_previousCaptureDelta; }
        void Log(string message,string trace,LogType type)
        { if(type is LogType.Error or LogType.Exception or LogType.Assert) _errors.Add(message+"\n"+trace); }
        [Serializable] sealed class Result { public bool passed; public string[] checks,errors; }
        void Check(bool condition,string name) { if(!condition) throw new InvalidOperationException(name); _checks.Add(name); }
        static void Set(object target,string name,object value) => target.GetType().GetField(name,Private).SetValue(target,value);
        static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,Private).Invoke(target,args);
        void Equip(GameObject player,int job,JobSkillKind kind)
        {
            var stats=new StatContainer();
            if(job==0) stats.STR.Invested=30; else if(job==1) stats.CON.Invested=30; else if(job==2) stats.AGI.Invested=30; else if(job==3) stats.DEF.Invested=30;
            else if(job==4) { stats.STR.Invested=18; stats.CON.Invested=6; stats.AGI.Invested=stats.DEF.Invested=3; }
            else { stats.STR.Invested=stats.CON.Invested=8; stats.AGI.Invested=stats.DEF.Invested=7; }
            var extra=player.GetComponent<ExpandedSkillController>(); extra.CancelForLoadout(); extra.States.Clear();
            player.GetComponent<StatManager>().ApplyLocalSceneStats(stats);
            var choices=SkillLoadout.Defaults(); if(choices[job*2+1]==(int)kind) choices[job*2+1]=choices[job*2]; choices[job*2]=(int)kind;
            var loadout=player.GetComponent<SkillLoadout>(); loadout.Choices.Clear(); foreach(int choice in choices) loadout.Choices.Add(choice);
            Set(extra,"_nextUse",0d);
            player.GetComponent<HealthSystem>().SetCurrentHp(player.GetComponent<HealthSystem>().MaxHp);
            Call(player.GetComponent<HealthSystem>(),"StopRegenRoutine");
        }
        void Position(GameObject player,Vector3 position)
        { var cc=player.GetComponent<CharacterController>(); cc.enabled=false; player.transform.SetPositionAndRotation(position,Quaternion.identity); cc.enabled=true; Physics.SyncTransforms(); }
        IEnumerator Exercise()
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="Validation floor"; floor.transform.position=new Vector3(0,-.5f,0); floor.transform.localScale=new Vector3(40,1,40);
            _camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>(); _camera.tag="MainCamera";
            _camera.transform.position=new Vector3(4,3,-6); _camera.transform.LookAt(new Vector3(0,1,1)); _camera.backgroundColor=new Color(.02f,.035f,.06f); _camera.clearFlags=CameraClearFlags.SolidColor;
            var light=new GameObject("Light",typeof(Light)).GetComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(40,-35,0);
            _a=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab")); _a.name="Skill owner";
            _b=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab")); _b.name="Skill target";
            foreach(var p in new[]{_a,_b}) { var input=p.GetComponent<PlayerInput>(); if(input!=null) input.enabled=false; }
            yield return null; yield return null;
            Position(_a,Vector3.up*.05f); Position(_b,new Vector3(0,.05f,5.4f));
            var skills=_a.GetComponent<ExpandedSkillController>(); var target=_b.GetComponent<ExpandedSkillController>();
            var health=_b.GetComponent<HealthSystem>();
            var animator=_a.GetComponent<Animator>();
            Vector3 handBefore=animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            Equip(_a,0,JobSkillKind.Hook); Equip(_b,1,JobSkillKind.Recovery);
            float hookHp=health.CurrentHp;
            Check(skills.TryUse(0,Vector3.forward),"hook accepted"); Check(skills.BlocksCombat,"hook locks attacks while travelling");
            Call(skills,"Update");
            Check(animator.GetLayerWeight(animator.GetLayerIndex("ExpandedUpperBody"))>.9f,"hook input cue survives visual Update before Animator evaluation");
            yield return new WaitForSeconds(.16f);
            Capture("hook-windup");
            Directory.CreateDirectory("Reports/SkillMotions");
            int hookLayer=animator.GetLayerIndex("ExpandedUpperBody");
            File.WriteAllText("Reports/SkillMotions/runtime-pose.txt",$"before={handBefore} after={animator.GetBoneTransform(HumanBodyBones.RightHand).position} enabled={animator.enabled} speed={animator.speed} culled={animator.cullingMode} layer={animator.GetLayerWeight(hookLayer)} state={animator.GetCurrentAnimatorStateInfo(hookLayer).shortNameHash} expected={Animator.StringToHash("Skill_STR_Hook")} time={animator.GetCurrentAnimatorStateInfo(hookLayer).normalizedTime} next={animator.GetNextAnimatorStateInfo(hookLayer).shortNameHash}");
            Check(Vector3.Distance(handBefore,animator.GetBoneTransform(HumanBodyBones.RightHand).position)>.15f,"imported hook throw visibly raises the right hand");
            yield return new WaitForSeconds(.18f);
            var chain=GameObject.Find("Hook chain");
            Check(chain!=null && chain.GetComponent<MeshFilter>().sharedMesh.vertexCount>0,"hook renders metal chain links from the hand"); Capture("hook-chain");
            yield return new WaitForSeconds(.46f);
            Check(skills.IsRetrievingHook,"hook enters server-owned retrieval phase on hit");
            Check(Mathf.Abs(hookHp-health.CurrentHp-10)<.01f,"hook deals exactly ten damage independent of STR");
            Check(target.IsBeingHooked && target.BlocksCombat && target.LookLocked,"hook victim cannot move attack or freely turn during retrieval");
            var follow=_camera.gameObject.AddComponent<BattlePvp.CameraLogic.FollowCamera>(); follow.enabled=false; follow.SetTarget(_b.transform);
            var viewPosition=_camera.transform.position; var viewRotation=_camera.transform.rotation;
            Call(follow,"LateUpdate");
            Check(Vector3.Dot(_camera.transform.forward,(_a.transform.position+Vector3.up*1.2f-_camera.transform.position).normalized)>.999f,"hook victim camera looks directly at the caster");
            Object.Destroy(follow); _camera.transform.SetPositionAndRotation(viewPosition,viewRotation);
            Check(animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("ExpandedUpperBody")).IsName("HookRetrieve") || animator.GetNextAnimatorStateInfo(animator.GetLayerIndex("ExpandedUpperBody")).IsName("HookRetrieve"),"hook plays a separate retrieval motion"); Capture("hook-retrieve");
            yield return new WaitForSeconds(.84f);
            Check(_b.transform.position.z<1.6f,"hook physically pulls the target into melee range"); Check(!skills.BlocksCombat,"hook releases movement and attack lock");
            Check(!target.IsBeingHooked && !target.BlocksCombat && !target.LookLocked,"hook victim control is restored after arrival");
            Position(_b,new Vector3(10,.05f,10)); Equip(_a,0,JobSkillKind.Hook);
            Check(skills.TryUse(0,Vector3.forward),"missed hook accepted");
            for(int frame=0;frame<120 && !skills.IsRetrievingHook;frame++) yield return null;
            Check(skills.IsRetrievingHook && skills.BlocksCombat,"missed hook also plays retrieval before unlocking");
            yield return new WaitForSeconds(.7f);
            Check(!skills.IsHookActive && !skills.BlocksCombat,"missed hook returns and releases its lock");
            var dummy=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/training-dummy.prefab"));
            dummy.transform.position=new Vector3(0,.02f,4); yield return null; Physics.SyncTransforms();
            var dummyHealth=dummy.GetComponent<DummyHealth>(); float dummyHp=dummyHealth.CurrentHp;
            Equip(_a,0,JobSkillKind.Hook); Check(skills.TryUse(0,Vector3.forward),"hook accepts a training dummy target");
            yield return new WaitForSeconds(.85f);
            Check(Mathf.Abs(dummyHp-dummyHealth.CurrentHp-10)<.01f && Mathf.Abs(dummyHealth.CurrentDps-10)<.01f,"dummy receives hook damage and shows ten DPS");
            Capture("dummy-dps");
            yield return new WaitForSeconds(.7f);
            Check(dummy.transform.position.z<1.7f,"hook pulls the training dummy into melee range");
            yield return new WaitForSeconds(.5f); Check(dummyHealth.CurrentDps==0,"dummy DPS expires after one second without damage");
            Object.Destroy(dummy); yield return null;
            Position(_a,Vector3.up*.05f); Position(_b,new Vector3(0,.05f,3));
            Equip(_a,2,JobSkillKind.Knife); Equip(_b,1,JobSkillKind.Recovery);
            Check(skills.TryUse(0,Vector3.forward),"knife readiness accepted"); yield return new WaitForSeconds(.2f);
            Check(animator.GetLayerWeight(animator.GetLayerIndex("ExpandedUpperBody"))>.9f,"knife readiness uses the upper-body layer");
            Check(skills.ControlFlags==SkillInputLockFlags.None,"knife readiness permits movement, jumping and crouching");
            Capture("knife-ready"); float hp=health.CurrentHp;
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var heldKnife=hand.GetComponentsInChildren<Transform>().First(t=>t.name=="Knife(Clone)");
            Check(Vector3.Distance(heldKnife.position,hand.position)>.015f && Vector3.Distance(heldKnife.position,hand.position)<.12f,"knife grip sits in the palm rather than the wrist pivot");
            var cameraPositionKnife=_camera.transform.position; var cameraRotationKnife=_camera.transform.rotation;
            _camera.transform.position=hand.position+new Vector3(1,.25f,1); _camera.transform.LookAt(hand.position); Capture("knife-grip-close");
            _camera.transform.SetPositionAndRotation(cameraPositionKnife,cameraRotationKnife);
            Call(skills,"ThrowKnife",Vector3.forward); yield return new WaitForSeconds(.6f);
            Check(health.CurrentHp<hp,"knife flight damages the target"); Check(skills.Read(JobSkillKind.Knife).Charges==2,"throw consumes one charge");
            Check(GameObject.FindObjectsByType<SkillProjectileVisual>(FindObjectsSortMode.None).Length==0,"projectile presentation expires on hit");
            Equip(_a,2,JobSkillKind.Stealth);
            Check(skills.TryUse(0,Vector3.forward),"stealth accepted"); yield return new WaitForSeconds(.1f);
            var body=_a.GetComponentInChildren<SkinnedMeshRenderer>();
            Check(Mathf.Approximately(body.sharedMaterial.GetColor("_BaseColor").a,.5f),"local stealth has alpha 0.5"); Capture("stealth-local");
            using(var observer=new SkillStealthPresentation(_a.transform))
            {
                observer.Apply(true,false); Check(body.forceRenderingOff,"observer cannot render stealthed body"); Capture("stealth-observer");
            }
            skills.NotifyAttackStarted(); yield return null;
            Check(!body.forceRenderingOff && Mathf.Approximately(body.sharedMaterial.GetColor("_BaseColor").a,1),"stealth break restores the original material");
            Equip(_a,4,JobSkillKind.Trap); Position(_b,new Vector3(5,.05f,4));
            Check(skills.TryUse(0,Vector3.forward),"trap preview accepted"); yield return new WaitForSeconds(.2f);
            Check(skills.TrapReady && skills.Traps.Count==0 && skills.Read(JobSkillKind.Trap).CooldownUntil==0,"preview does not place a trap or spend cooldown");
            Check(GameObject.Find("Trap placement preview")!=null,"trap placement ghost is visible locally"); Capture("trap-preview");
            Check(skills.TryGetTrapPlacement(out var placement),"flat floor in front is a valid placement");
            Check(!skills.ConfirmTrap(placement+Vector3.forward*3),"server rejects forged remote placement");
            float standingHip=animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
            Check(skills.ConfirmTrap(placement),"click confirms valid trap placement");
            Check(skills.Read(JobSkillKind.Trap).Charges==2 && skills.Read(JobSkillKind.Trap).NextChargeAt>skills.Now,"confirming a trap spends one of three charges and starts recharge");
            Call(skills,"Update");
            yield return new WaitForSeconds(.5f);
            Check(animator.GetBoneTransform(HumanBodyBones.Hips).position.y<standingHip-.25f,"trap placement visibly lowers the hips into a kneel");
            Check(skills.TrapCameraDrop>.25f,"first-person camera follows the trap kneel instead of staying at standing height");
            Check(animator.GetBoneTransform(HumanBodyBones.RightHand).position.y<_a.transform.position.y+.65f,"trap assembly hand reaches down near the ground");
            Check(skills.Traps.Count==0 && skills.IsPlacingTrap && skills.BlocksCombat,"trap waits for the assembly motion"); Capture("trap-assembly");
            var cameraPosition=_camera.transform.position; var cameraRotation=_camera.transform.rotation;
            _camera.transform.position=_a.transform.position+new Vector3(2,1.4f,2.5f); _camera.transform.LookAt(_a.transform.position+new Vector3(0,.65f,.3f));
            Capture("trap-assembly-close"); _camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            yield return new WaitForSeconds(.7f);
            Check(skills.Traps.Count==1,"trap appears in replicated world state"); Capture("trap");
            _camera.transform.position=placement+new Vector3(.85f,1.1f,.9f); _camera.transform.LookAt(placement+Vector3.up*.08f); Capture("trap-asset-close");
            _camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            var trap=skills.Traps.First(); hp=health.CurrentHp; Position(_b,trap.Value.Position+Vector3.up*.03f);
            yield return new WaitForSeconds(.12f);
            Check(health.CurrentHp<hp,"trap damages the triggering enemy"); Check(target.LookLocked,"trap locks target facing");
            Check(skills.Traps[trap.Key].ClosedAt>0,"triggered trap broadcasts its closing timestamp");
            _camera.transform.position=placement+new Vector3(.85f,1.1f,.9f); _camera.transform.LookAt(placement+Vector3.up*.15f); Capture("trap-closing");
            _camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            float afterTrapHp=health.CurrentHp; yield return new WaitForSeconds(.7f);
            Check(skills.Traps.Count==0,"closed trap disappears after its closing animation"); Check(Mathf.Abs(afterTrapHp-health.CurrentHp)<.01f,"closing trap cannot damage the same target twice");
            target.CancelForLoadout();
            Position(_b,new Vector3(10,.05f,10));
            for(int remaining=1;remaining>=0;remaining--)
            {
                Position(_a,new Vector3(remaining*2,.05f,0));
                Check(skills.TryUse(0,Vector3.forward),"remaining trap charge can preview immediately");
                Check(skills.TryGetTrapPlacement(out var extraPoint) && skills.ConfirmTrap(extraPoint),"remaining trap charge can be placed without waiting for recharge");
                yield return new WaitForSeconds(1.2f);
                Check(skills.Read(JobSkillKind.Trap).Charges==remaining,"trap charge count decreases once per placement");
            }
            Check(!skills.TryUse(0,Vector3.forward),"fourth trap placement is rejected with no charges");
            Check(skills.Hud(0,JobSkillKind.Trap).RemainingSeconds>0 && skills.Hud(0,JobSkillKind.Trap).Phase==SkillHudPhase.Cooldown,"empty trap slot shows recharge time");
            var retainedTraps=skills.Traps.ToArray(); skills.CancelForLoadout();
            Check(retainedTraps.Length==2 && skills.Traps.Count==2,"changing the loadout preserves both placed traps");
            // Finish these fixtures independently, so later combat checks cannot step on them.
            var authorities=Object.FindObjectsByType<SkillTrap>(FindObjectsSortMode.None);
            skills.CloseTrap(authorities.First(t=>t.Id==retainedTraps[0].Key));
            yield return new WaitForSeconds(.7f);
            Check(!skills.Traps.ContainsKey(retainedTraps[0].Key) && skills.Traps[retainedTraps[1].Key].ExpiresAt==retainedTraps[1].Value.ExpiresAt,"closing one trap leaves the other lifetime untouched");
            skills.CloseTrap(authorities.First(t=>t!=null && t.Id==retainedTraps[1].Key)); yield return new WaitForSeconds(.7f);
            Position(_b,new Vector3(0,.05f,3)); Equip(_a,5,JobSkillKind.Steal); Equip(_b,1,JobSkillKind.Berserk);
            Position(_a,Vector3.up*.05f);
            _b.GetComponent<SkillLoadout>().Choices[3]=(int)JobSkillKind.Recovery;
            Check(skills.TryUse(0,Vector3.forward),"steal acquires an equipped skill in range");
            Check(skills.CopiedKind is 105 or 106,"steal only chooses equipped target skills");
            yield return new WaitForSeconds(.15f); Check(skills.TryUse(0,Vector3.forward),"copied skill executes from the steal slot");
            Check(skills.Read(JobSkillKind.Steal).CooldownUntil>skills.Now,"copy use starts steal cooldown");
            Equip(_a,0,JobSkillKind.Charge); Position(_a,Vector3.up*.05f); Position(_b,new Vector3(0,.05f,1.1f)); target.CancelForLoadout(); hp=health.CurrentHp;
            Check(skills.TryUse(0,Vector3.forward),"charge accepted"); yield return new WaitForSeconds(.9f);
            Check(_a.transform.position.z>.3f,"charge advances through character-controller motion");
            Check(health.CurrentHp<hp,"charge collision applies damage");
            Check(skills.IsCharging && skills.Read(JobSkillKind.Charge).CooldownUntil==0,"a successful collision does not end charge or begin cooldown");
            Check(_a.transform.position.z>_b.transform.position.z+.7f,"charge keeps travelling past the hit target: charger="+_a.transform.position+" target="+_b.transform.position);
            Check(target.IsStunned && target.GetComponent<StunIndicator>().Visible,"charge victim displays a stun ring"); Capture("charge-player-stun");
            var chargerCollider=_a.GetComponent<CharacterController>(); var victimCollider=_b.GetComponent<CharacterController>();
            Check(Physics.GetIgnoreCollision(chargerCollider,victimCollider),"the hit player's body cannot block the running charge");
            float firstChargeHp=health.CurrentHp;
            Position(_a,new Vector3(0,.05f,.6f)); yield return null;
            Check(Mathf.Abs(health.CurrentHp-firstChargeHp)<.001f,"the same enemy takes charge damage only once per cast");
            _a.GetComponent<PlayerCombat>().AttackFromHud(true); yield return null;
            Check(!skills.IsCharging && skills.Read(JobSkillKind.Charge).CooldownUntil>skills.Now,"actual attack input ends charge and starts cooldown");
            Check(!Physics.GetIgnoreCollision(chargerCollider,victimCollider),"attack cancellation restores player collisions"); _a.GetComponent<PlayerCombat>().CancelCurrentAttack();
            target.CancelForLoadout(); Position(_b,new Vector3(10,.05f,10));
            var chargeDummy=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/training-dummy.prefab"));
            chargeDummy.transform.position=new Vector3(0,.02f,1.1f); yield return null;
            Equip(_a,1,JobSkillKind.Charge); Position(_a,Vector3.up*.05f);
            var chargeDummyHealth=chargeDummy.GetComponent<DummyHealth>(); float beforeDummy=chargeDummyHealth.CurrentHp;
            Check(skills.TryUse(0,Vector3.forward),"CON charge starts against a training dummy"); yield return new WaitForSeconds(.5f);
            Check(chargeDummyHealth.CurrentHp<beforeDummy && chargeDummyHealth.IsStunned && chargeDummy.GetComponent<StunIndicator>().Visible,"charge damages and displays stun on the training dummy"); Capture("charge-dummy-stun");
            Check(skills.IsCharging,"dummy impact does not cancel charge");
            yield return new WaitForSeconds(.8f);
            Check(!chargeDummyHealth.IsStunned && !chargeDummy.GetComponent<StunIndicator>().Visible,"dummy charge stun expires independently of the continuing charge");
            Check(_a.transform.position.z>chargeDummy.transform.position.z+.7f && skills.IsCharging,"charge passes the training dummy and continues moving");
            yield return new WaitForSeconds(2.85f);
            Check(!skills.IsCharging && skills.Read(JobSkillKind.Charge).CooldownUntil>skills.Now,"charge automatically ends at four seconds and starts cooldown");
            foreach(var contact in chargeDummy.GetComponentsInChildren<Collider>()) Check(!Physics.GetIgnoreCollision(chargerCollider,contact),"expired charge restores dummy collision: "+contact.name);
            Object.Destroy(chargeDummy);
            Equip(_a,0,JobSkillKind.Charge); Position(_a,Vector3.up*.05f); yield return new WaitForSeconds(.15f);
            Check(skills.TryUse(0,Vector3.forward),"charge starts for jump cancellation"); yield return new WaitForSeconds(.25f);
            float jumpStartY=_a.transform.position.y;
            Call(_a.GetComponent<PlayerManager>(),"QueueJumpRequest"); yield return new WaitForSeconds(.12f);
            Check(!skills.IsCharging && skills.Read(JobSkillKind.Charge).CooldownUntil>skills.Now,"jump input ends charge and starts cooldown");
            Check(_a.transform.position.y>jumpStartY+.2f,"the cancelling jump actually leaves the ground");
            yield return new WaitForSeconds(.8f);
            foreach(var cancelKind in new[]{JobSkillKind.WarCry,JobSkillKind.MonostatStrLifesteal})
            {
                Equip(_a,0,JobSkillKind.Charge); Position(_a,Vector3.up*.05f);
                _a.GetComponent<SkillLoadout>().Choices[1]=(int)cancelKind;
                Check(skills.TryUse(0,Vector3.forward),"charge starts before skill-key cancellation: "+cancelKind); yield return new WaitForSeconds(.2f);
                float beforeReleaseZ=_a.transform.position.z;
                _a.GetComponent<PlayerCombat>().UseSkillSlot(1,true);
                Check(!skills.IsCharging && skills.Read(JobSkillKind.Charge).CooldownUntil>skills.Now,"other skill key cancels charge: "+cancelKind);
                Check(!skills.Active(cancelKind) && !_a.GetComponent<PlayerCombat>().IsMonostatStrLifestealActive,"the cancelling key does not cast another skill: "+cancelKind);
                yield return new WaitForSeconds(.12f);
                Check(_a.transform.position.z>beforeReleaseZ+.001f,"charge release keeps its already-started short forward motion: "+cancelKind);
                float releasedZ=_a.transform.position.z; yield return new WaitForSeconds(.15f);
                Check(Mathf.Abs(_a.transform.position.z-releasedZ)<.01f,"no further charge steps start after the short release motion: "+cancelKind);
            }
            Equip(_a,1,JobSkillKind.Berserk); Position(_a,Vector3.up*.05f);
            var berserkHealth=_a.GetComponent<HealthSystem>(); Check(skills.TryUse(0,Vector3.forward),"Berserk starts for nonlethal drain");
            berserkHealth.SetCurrentHp(2); yield return new WaitForSeconds(1.05f);
            Check(berserkHealth.CurrentHp==1 && !berserkHealth.IsDead && !skills.Berserking,"low-health Berserk stops alive at exactly one HP");
            Check(skills.Read(JobSkillKind.Berserk).CooldownUntil>skills.Now && skills.RegenMultiplier==.5f,"automatic Berserk exit keeps cooldown and regeneration penalty");
            var penaltyGlow=_a.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Skill buff body glow" && r.enabled);
            var penaltyProperties=new MaterialPropertyBlock(); if(penaltyGlow!=null) penaltyGlow.GetPropertyBlock(penaltyProperties);
            var penaltyTint=penaltyProperties.GetColor("_BaseColor");
            Check(penaltyGlow!=null && penaltyTint.r>penaltyTint.g && penaltyTint.b>penaltyTint.g,"regeneration decrease emits purple light");
            Equip(_a,4,JobSkillKind.Dice); Position(_b,new Vector3(4,.05f,3));
            Check(skills.TryUse(0,Vector3.forward),"dice cast accepted"); yield return new WaitForSeconds(1.2f);
            Check(skills.DiceFace>=1 && skills.DiceFace<=6,"authoritative dice face stays in range");
            Check(GameObject.Find("Skill Dice Result")!=null,"dice roll appears at the top of the screen");
            var diceHud=_a.GetComponent<PlayerCombat>().GetSkillHudState(0);
            Check(diceHud.Phase==SkillHudPhase.Active && diceHud.RemainingSeconds>12 && diceHud.RemainingSeconds<15 && diceHud.NormalizedFill<1,"dice HUD displays its remaining duration and shrinking radial fill");
            foreach(var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=_camera; canvas.planeDistance=1; }
            Capture("dice");
            Set(skills,"_diceFace",1); yield return null;
            var diceGlow=_a.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Skill buff body glow" && r.enabled);
            if(diceGlow!=null) diceGlow.GetPropertyBlock(penaltyProperties); var diceTint=penaltyProperties.GetColor("_BaseColor");
            Check(diceGlow!=null && diceTint.r>diceTint.g && diceTint.b>diceTint.g,"negative dice uses purple body light");
            var stateCameraPosition=_camera.transform.position; var stateCameraRotation=_camera.transform.rotation;
            _camera.transform.position=_a.transform.position+new Vector3(2,1.5f,2.5f); _camera.transform.LookAt(_a.transform.position+Vector3.up); Capture("debuff-purple");
            Equip(_a,4,JobSkillKind.StrategistPresetChange);
            berserkHealth.GrantDecayingShield(30,1.5f); yield return null;
            var presetShield=_a.transform.Find("Preset yellow shield");
            Check(presetShield!=null && presetShield.gameObject.activeInHierarchy,"preset shield displays an independent shell");
            var presetTint=presetShield.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
            Check(presetTint.r>1 && presetTint.g>1 && presetTint.b<.1f,"preset shield is yellow");
            Check(Mathf.Abs(presetTint.a-.19f)<.001f,"yellow shield alpha is halved to 0.19");
            Check(!_a.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.name=="Skill buff body glow" && r.enabled),"shield without a stat buff has no body radiance");
            Capture("preset-yellow-shield");
            var shieldHitPoint=_a.transform.position+new Vector3(.35f,1.2f,.5f);
            berserkHealth.ApplyDamage(new DamageRequest(5,DamageSource.Fixed,0,null,shieldHitPoint)); yield return new WaitForSeconds(.08f);
            var shieldProperties=new MaterialPropertyBlock(); presetShield.GetComponent<Renderer>().GetPropertyBlock(shieldProperties);
            Check(shieldProperties.GetFloat("_ImpactAge")>=0 && shieldProperties.GetFloat("_ImpactAge")<.35f,"absorbed damage lights up the shield at the impact point"); Capture("shield-impact");
            berserkHealth.ApplyDamage(new DamageRequest(berserkHealth.CurrentShield+1,DamageSource.Fixed,0,null,shieldHitPoint)); yield return null;
            Check(berserkHealth.CurrentShield==0 && presetShield.gameObject.activeInHierarchy,"breaking hit keeps the last shield impact visible");
            yield return new WaitForSecondsRealtime(.4f); yield return null;
            Check(berserkHealth.CurrentShield==0 && !presetShield.gameObject.activeInHierarchy,"the yellow shell disappears when shield expires");
            berserkHealth.GrantDecayingShield(10,.3f); yield return new WaitForSeconds(.4f); yield return null;
            Check(berserkHealth.CurrentShield==0 && !presetShield.gameObject.activeInHierarchy,"untouched shield still expires normally");
            _camera.transform.SetPositionAndRotation(stateCameraPosition,stateCameraRotation);
            skills.CancelForLoadout(); yield return null;
            foreach(int job in new[]{4,5})
            {
                var roll=job==4 ? JobSkillKind.StrategistRoll : JobSkillKind.PolymathRoll;
                Equip(_a,job,roll); Position(_a,Vector3.up*.05f); Position(_b,new Vector3(10,.05f,10)); yield return new WaitForSeconds(.2f);
                for(int remaining=1;remaining>=0;remaining--)
                {
                    float startZ=_a.transform.position.z; _a.GetComponent<PlayerCombat>().UseSkillSlot(0,true); yield return new WaitForSeconds(.65f);
                    Check(skills.Read(roll).Charges==remaining,roll+" spends one charge through actual skill input");
                    Check(Mathf.Abs(_a.transform.position.z-startZ-3.6f)<.1f,roll+" travels 3.6 units: "+(_a.transform.position.z-startZ));
                }
                float finalZ=_a.transform.position.z; _a.GetComponent<PlayerCombat>().UseSkillSlot(0,true); yield return new WaitForSeconds(.5f);
                Check(Mathf.Abs(_a.transform.position.z-finalZ)<.01f,roll+" cannot execute a third time without recharging");
                var dashHud=_a.GetComponent<PlayerCombat>().GetSkillHudState(0);
                Check(dashHud.Phase==SkillHudPhase.Cooldown && dashHud.RemainingSeconds>0,roll+" shows next recharge when empty");
            }
            Position(_a,Vector3.up*.05f); Position(_b,new Vector3(0,.05f,3));
            Equip(_a,3,JobSkillKind.Thorns);
            var thornsData=SkillPresentationCatalog.Data((int)JobSkillKind.Thorns);
            Check(thornsData.CastSeconds==0 && thornsData.ResolveInputLockSeconds()==0 && thornsData.InputLockFlags==SkillInputLockFlags.None,"Thorns has no cast or input lock duration");
            Check(skills.TryUse(0,Vector3.forward) && skills.ReflectMultiplier==2 && skills.ControlFlags==SkillInputLockFlags.None,"Thorns immediately enables reflection without blocking controls");
            yield return null;
            var shield=_a.transform.Find("Thorns spiked shield");
            Check(shield!=null && shield.gameObject.activeInHierarchy,"Thorns displays a spiked translucent shell");
            Check(Mathf.Abs(shield.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor").a-.275f)<.001f,"Thorns opacity is half of its previous value");
            Check(shield.GetComponentsInChildren<Collider>().Length==0,"Thorns visual adds no gameplay collider");
            int fullLayer=animator.GetLayerIndex("ExpandedSkills");
            Check(!animator.GetCurrentAnimatorStateInfo(fullLayer).IsName("Skill_DEF_Thorns") && !animator.GetNextAnimatorStateInfo(fullLayer).IsName("Skill_DEF_Thorns"),"Thorns does not play the former cast animation");
            var defenseCameraPosition=_camera.transform.position; var defenseCameraRotation=_camera.transform.rotation;
            _camera.transform.position=_a.transform.position+new Vector3(2,1.5f,2.5f); _camera.transform.LookAt(_a.transform.position+Vector3.up); Capture("thorns-shield");
            _camera.transform.position=_a.transform.position+Vector3.up*1.5f; _camera.transform.rotation=Quaternion.identity; Capture("thorns-first-person");
            _camera.transform.SetPositionAndRotation(defenseCameraPosition,defenseCameraRotation);
            var thornsState=skills.Read(JobSkillKind.Thorns); thornsState.ActiveUntil=skills.Now-.1; skills.States[(int)JobSkillKind.Thorns]=thornsState; yield return null;
            Check(!shield.gameObject.activeSelf && skills.ReflectMultiplier==1,"Thorns shell ends with the reflection buff");
            Equip(_a,3,JobSkillKind.Bash); Equip(_b,1,JobSkillKind.Recovery);
            var bashData=SkillPresentationCatalog.Data((int)JobSkillKind.Bash);
            Check(bashData.CastSeconds==0 && bashData.ResolveInputLockSeconds()==0 && bashData.InputLockFlags==SkillInputLockFlags.None,"Bash has no cast or input lock duration");
            Check(skills.TryUse(0,Vector3.forward),"Bash can arm for the next hit");
            Check(skills.Active(JobSkillKind.Bash) && skills.ControlFlags==SkillInputLockFlags.None,"Bash is armed immediately without blocking controls"); yield return null;
            var weaponGlow=_a.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.name=="Bash weapon glow" && r.enabled);
            Check(weaponGlow!=null,"Bash illuminates the equipped weapon blue");
            Check(!animator.GetCurrentAnimatorStateInfo(fullLayer).IsName("Skill_DEF_Bash") && !animator.GetNextAnimatorStateInfo(fullLayer).IsName("Skill_DEF_Bash"),"Bash does not play the former cast animation");
            _camera.transform.position=_a.transform.position+new Vector3(2,1.5f,2.5f); _camera.transform.LookAt(_a.transform.position+Vector3.up); Capture("bash-weapon");
            _camera.transform.SetPositionAndRotation(defenseCameraPosition,defenseCameraRotation);
            skills.NotifyPhysicalHit(health); yield return null;
            Check(!weaponGlow.enabled,"Bash weapon light disappears when its charged hit is consumed");
            Check(target.IsStunned && target.GetComponent<StunIndicator>().Visible,"Bash victim shows an overhead stun ring");
            Check((target.ControlFlags & (SkillInputLockFlags.Move|SkillInputLockFlags.Attack))==(SkillInputLockFlags.Move|SkillInputLockFlags.Attack),"Bash prevents moving and attacking");
            Capture("bash-player");
            var bashDummy=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/training-dummy.prefab"));
            bashDummy.transform.position=new Vector3(2,.02f,1.5f); yield return null;
            var bashHealth=bashDummy.GetComponent<DummyHealth>();
            Equip(_a,3,JobSkillKind.Bash); Check(skills.TryUse(0,Vector3.forward),"Bash can be prepared for dummy test");
            skills.NotifyPhysicalHit(bashHealth); yield return null;
            Check(bashHealth.IsStunned && bashDummy.GetComponent<StunIndicator>().Visible,"training dummy shows the same stun ring"); Capture("bash-dummy");
            yield return new WaitForSeconds(3.1f);
            Check(!target.IsStunned && !target.GetComponent<StunIndicator>().Visible && !bashHealth.IsStunned && !bashDummy.GetComponent<StunIndicator>().Visible,"player and dummy rings disappear with the authoritative stun expiry");
            Object.Destroy(bashDummy);
            Equip(_a,3,JobSkillKind.Fortify); float initialHead=animator.GetBoneTransform(HumanBodyBones.Head).position.y;
            Check(skills.TryUse(0,Vector3.forward),"Fortify starts a curled defensive guard"); yield return new WaitForSeconds(.9f);
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Skill_DEF_Fortify"),"Fortify runs below the upper-body attack layer");
            Check(animator.GetBoneTransform(HumanBodyBones.Head).position.y<initialHead-.7f,"Fortify lowers the head into a prone posture");
            foreach(var contact in new[]{HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg})
            {
                float height=animator.GetBoneTransform(contact).position.y-_a.transform.position.y;
                Check(height>.05f && height<.14f,"Fortify plants "+contact+" on the floor without burying the joint");
            }
            var guardHead=animator.GetBoneTransform(HumanBodyBones.Head);
            Check(animator.GetBoneTransform(HumanBodyBones.Hips).position.y-_a.transform.position.y<.34f,"Fortify lowers the hips towards the heels");
            Check(Vector3.Dot(guardHead.forward,Vector3.down)>.85f,"Fortify tucks the face towards the floor");
            foreach(var handBone in new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightHand})
            {
                var fist=animator.GetBoneTransform(handBone);
                Check(Vector3.Distance(fist.position,guardHead.position)<.24f && fist.position.y>guardHead.position.y,"Fortify shields the head with "+handBone);
            }
            Check(skills.SkillCameraDrop>.7f,"Fortify lowers the first-person camera with the posture"); Capture("fortify-prone");
            var bodyGlow=_a.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Skill buff body glow" && r.enabled);
            Check(bodyGlow!=null,"Fortify surrounds the curled body with light");
            var bodyProperties=new MaterialPropertyBlock(); bodyGlow.GetPropertyBlock(bodyProperties);
            Check(bodyProperties.GetColor("_BaseColor").b>2,"Fortify body light is blue");
            _camera.transform.position=_a.transform.position+new Vector3(1.8f,1.3f,2.2f); _camera.transform.LookAt(_a.transform.position+Vector3.up*.4f); Capture("fortify-blue-aura");
            _camera.transform.SetPositionAndRotation(defenseCameraPosition,defenseCameraRotation);
            var ownerCombat=_a.GetComponent<PlayerCombat>(); ownerCombat.AttackFromHud(true);
            Check(ownerCombat.IsAttackActive,"Fortify still accepts a melee attack"); yield return new WaitForSeconds(.15f);
            Check(animator.GetBoneTransform(HumanBodyBones.Hips).position.y<_a.transform.position.y+.65f,"Fortify keeps the lower body prone during an upper-body attack");
            Capture("fortify-attack"); ownerCombat.CancelCurrentAttack();
            yield return new WaitForSeconds(2.15f);
            Check(!skills.Active(JobSkillKind.Fortify) && skills.SkillCameraDrop==0,"Fortify releases camera and movement after three seconds");
            Check(animator.GetBoneTransform(HumanBodyBones.Head).position.y>initialHead-.1f,"Fortify returns to standing");
            Check(!bodyGlow.enabled,"Fortify body light ends with its defense bonus");
            foreach(var kind in new[]{JobSkillKind.WarCry,JobSkillKind.Berserk,JobSkillKind.Recovery})
            {
                Equip(_a,kind==JobSkillKind.WarCry?0:1,kind);
                Check(skills.TryUse(0,Vector3.forward),kind+" starts for body light validation"); yield return new WaitForSeconds(.35f);
                bodyGlow=_a.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Skill buff body glow" && r.enabled);
                if(kind==JobSkillKind.WarCry)
                {
                    Check(bodyGlow==null,"WarCry replaces the red body shell with rising shoe symbols");
                    Check(_a.transform.Find("WarCry buff symbols").gameObject.activeInHierarchy,"WarCry displays rising shoes and upward arrows");
                }
                else
                {
                    Check(bodyGlow!=null,kind+" radiates light while active"); bodyGlow.GetPropertyBlock(bodyProperties);
                    var tint=bodyProperties.GetColor("_BaseColor");
                    Check(kind==JobSkillKind.Berserk ? tint.r>2 && tint.g>1.5f && tint.b<.1f : tint.g>tint.r+tint.b,kind+" has its requested yellow or green light");
                    if(kind==JobSkillKind.Recovery) Check(_a.transform.Find("Recovery buff symbols").gameObject.activeInHierarchy,"Recovery adds green upward arrows");
                }
                _camera.transform.position=_a.transform.position+new Vector3(2,1.5f,2.5f); _camera.transform.LookAt(_a.transform.position+Vector3.up); Capture("buff-"+kind);
                if(kind==JobSkillKind.WarCry)
                { _camera.transform.position=_a.transform.position+Vector3.up*1.5f; _camera.transform.rotation=Quaternion.identity; Capture("buff-first-person"); }
                _camera.transform.SetPositionAndRotation(defenseCameraPosition,defenseCameraRotation);
            }
            Equip(_a,0,JobSkillKind.MonostatStrLifesteal); ownerCombat.UseSkillSlot(0,true); yield return new WaitForSeconds(1.1f);
            Check(ownerCombat.IsMonostatStrLifestealActive,"STR frenzy has finished casting and grants its buff");
            bodyGlow=_a.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Skill buff body glow" && r.enabled);
            Check(bodyGlow==null,"STR frenzy replaces the body shell with fist symbols");
            Check(_a.transform.Find("STR buff symbols").gameObject.activeInHierarchy,"STR frenzy displays rising fists and upward arrows");
            _camera.transform.position=_a.transform.position+new Vector3(2,1.5f,2.5f); _camera.transform.LookAt(_a.transform.position+Vector3.up); Capture("buff-str-frenzy");
            _camera.transform.SetPositionAndRotation(defenseCameraPosition,defenseCameraRotation);
            Call(ownerCombat,"CancelAllCombatActions"); skills.CancelForLoadout(); yield return null;
            Check(!_a.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.name=="Skill buff body glow" && r.enabled),"cancel removes all body buff light");
            Check(!_a.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name.EndsWith("buff symbols")),"cancel removes rising buff symbols");
            Position(_b,new Vector3(10,.05f,10)); Equip(_a,5,JobSkillKind.PolymathWeaponSwap);
            var bow=_a.GetComponent<BowAttackController>(); var bowData=SkillPresentationCatalog.Data((int)JobSkillKind.PolymathWeaponSwap);
            Set(ownerCombat,"_isBowEquipped",true); Call(ownerCombat,"ApplyIdentityVisuals");
            foreach(float charge in new[]{.05f,.55f,1.1f})
            {
                ownerCombat.AttackFromHud(true); Check(bow.IsCharging,"bow press starts charging through the real combat input"); yield return new WaitForSeconds(charge);
                int arrows=FindObjectsByType<BowArrowProjectile>(FindObjectsSortMode.None).Length;
                int releaseFrame=Time.frameCount; ownerCombat.AttackFromHud(false);
                yield return new WaitForEndOfFrame();
                Check(Time.frameCount==releaseFrame,"bow "+charge+" release is verified in the same frame");
                Check(FindObjectsByType<BowArrowProjectile>(FindObjectsSortMode.None).Length==arrows+1,"bow "+charge+" fires without waiting for draw animation");
                bow.HandleAttackInput(true,bowData,Vector3.forward); Check(!bow.IsCharging,"bow recovery prevents another immediate draw");
                bow.OnBowReleaseArrow();
                yield return null;
                Check(FindObjectsByType<BowArrowProjectile>(FindObjectsSortMode.None).Length<=arrows+1,"stale bow release event cannot duplicate the arrow");
                foreach(var arrow in FindObjectsByType<BowArrowProjectile>(FindObjectsSortMode.None)) Object.Destroy(arrow.gameObject);
                yield return new WaitForSeconds(.8f);
            }
            var ui=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(JobGuideBuilder.PrefabPath)); var guide=ui.GetComponent<JobGuidePanel>();
            var uiCanvas=ui.GetComponent<Canvas>(); uiCanvas.renderMode=RenderMode.ScreenSpaceCamera; uiCanvas.worldCamera=_camera; uiCanvas.planeDistance=1;
            guide.Open();
            for(int job=0;job<6;job++)
            {
                guide.Select(job); Canvas.ForceUpdateCanvases(); yield return null;
                Capture("job-"+job);
                foreach(var label in ui.GetComponentsInChildren<TMP_Text>()) { label.ForceMeshUpdate(); Check(!label.isTextOverflowing,"job "+job+" text fits: "+label.transform.parent.name+"/"+label.name+" / "+label.text); }
            }
            var first=ui.transform.Find("Panel/Skill0/Equip1").GetComponent<Button>(); first.onClick.Invoke();
            var draft=(int[])typeof(JobGuidePanel).GetField("_draft",Private).GetValue(guide);
            Check(SkillLoadout.Validate(draft),"UI slot swap preserves a valid distinct pair"); guide.Close();
            Check(_errors.Count==0,"no native Play Mode errors");
        }
        void Capture(string name)
        {
            var render=new RenderTexture(1600,900,24); var image=new Texture2D(1600,900,TextureFormat.RGB24,false); var previous=RenderTexture.active;
            try
            {
                _camera.targetTexture=render; Canvas.ForceUpdateCanvases(); _camera.Render(); RenderTexture.active=render;
                image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply(); Directory.CreateDirectory(Folder); File.WriteAllBytes(Folder+"/native-"+name+".png",image.EncodeToPNG());
            }
            finally { _camera.targetTexture=null; RenderTexture.active=previous; render.Release(); Destroy(render); Destroy(image); }
        }
    }
}
