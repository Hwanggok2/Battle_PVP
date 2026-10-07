using System;
using System.IO;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace BattlePvp.Remodel.Editor
{
    public static class WeaponInstaller
    {
        private const string Folder = "Assets/Remodel/Weapons/";
        private const string Source = "Assets/Remodel/Skills/Source/Quaternius/UAL2_Standard.fbx";
        private static readonly string[] Names = { "Greatsword1", "Greatsword2", "Greatsword3", "Riposte1", "Riposte2", "ShieldGuard", "SwordGuard", "Recoil" };
        private static readonly string[] Takes = { "Sword_Heavy_Combo", "Sword_Heavy_Combo", "Sword_Heavy_Combo", "Sword_Regular_A", "Sword_Regular_B", "Idle_Shield_Loop", "Sword_Block", "Hit_Knockback" };
        private static readonly float[] From = { 0, 42, 84, 0, 0, 0, 10, 0 }, To = { 42, 84, 130, 13, 16, 75, 20, 25 };
        private static readonly float[] Seconds = { .85f, .85f, .95f, .32f, .36f, 1f, 1f, .65f };

        [MenuItem("Battle PvP/Weapons/Install Weapons")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Source);
            var specs = importer.clipAnimations.Where(c => !c.name.StartsWith("Weapon_")).ToList();
            for (int i = 0; i < Names.Length; i++)
            {
                var spec = importer.defaultClipAnimations.First(c => c.name.EndsWith("|" + Takes[i]));
                spec.name = "Weapon_" + Names[i]; spec.firstFrame = From[i]; spec.lastFrame = To[i];
                spec.lockRootRotation = spec.lockRootHeightY = spec.lockRootPositionXZ = true;
                spec.loopTime = i == 5 || i == 6; specs.Add(spec);
            }
            importer.clipAnimations = specs.ToArray(); importer.SaveAndReimport();
            var clips = new AnimationClip[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                var original = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().First(c => c.name == "Weapon_" + Names[i]);
                var clip = Asset<AnimationClip>(Names[i]+".anim"); EditorUtility.CopySerialized(original, clip); clip.name = "Weapon_" + Names[i];
                Retime(clip, Seconds[i]); ConfigureEvents(clip, i < 5);
                CloseFingers(clip, true);
                EditorUtility.SetDirty(clip); clips[i] = clip;
            }
            var thrust = AuthorAttack("Thrust", false); var chop = AuthorAttack("AxeChop", true);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            var machine = controller.layers[1].stateMachine;
            var empty = machine.states.First(s => s.state.name == "New State").state;
            foreach (var clip in clips.Concat(new[] { thrust, chop }))
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name) ?? machine.AddState(clip.name);
                state.motion = clip; state.speed = 1; state.writeDefaultValues = true;
                foreach (var t in state.transitions) state.RemoveTransition(t);
                if (!clip.name.EndsWith("Guard")) { var t = state.AddTransition(empty); t.hasExitTime = true; t.exitTime = 1; t.hasFixedDuration = true; t.duration = .08f; }
            }
            var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                if (player.GetComponent<WeaponLoadout>() == null) player.AddComponent<WeaponLoadout>();
                var blade = player.GetComponentInChildren<MeleeHitBox>(true); var box = blade.GetComponent<BoxCollider>();
                var old = Enumerable.Range(1,3).Select(i => AssetDatabase.LoadAssetAtPath<AttackData>("Assets/Player/AttackData/Atk_"+i+".asset")).ToArray();
                var sword = new WeaponCatalog.Entry { Kind=MeleeWeaponKind.Sword, Name="한손검", Description="좌클릭 · 기본 3연격\n우클릭 · 전방 찌르기", Mesh=blade.GetComponent<MeshFilter>().sharedMesh, Materials=blade.GetComponent<MeshRenderer>().sharedMaterials,
                    HitCenter=box.center, HitSize=box.size, BladeBase=new Vector3(0,0,.13612f), BladeTip=new Vector3(0,0,1.0816832f), Attacks=old.Concat(new[]{Attack(thrust,1f)}).ToArray() };
                var shield = new WeaponCatalog.Entry { Kind=MeleeWeaponKind.SwordShield, Name="한손검 + 방패", Description="좌클릭 · 기본 3연격\n우클릭 유지 · 정면 막기 / 성공 시 상대 0.65초 경직", Mesh=sword.Mesh, Materials=sword.Materials, HitCenter=sword.HitCenter, HitSize=sword.HitSize, BladeBase=sword.BladeBase, BladeTip=sword.BladeTip, Attacks=old };
                var great = Weapon("Assets/Medieval Melee Weapon Pack/prefabs/Sword(1)_DH.prefab", MeleeWeaponKind.Greatsword, "양손검", "좌클릭 · 3콤보 / 패링 후 빠른 2연격\n우클릭 · 검 막기 / 시작 0.3초 내 피격 시 패링", 0);
                great.Attacks = new[]{Attack(clips[0],1),Attack(clips[1],1),Attack(clips[2],1),null,Attack(clips[3],1),Attack(clips[4],1)};
                var axe = Weapon("Assets/Medieval Melee Weapon Pack/prefabs/Axe(1)_DS.prefab", MeleeWeaponKind.Axe,"도끼","좌클릭 · 위에서 아래로 내려찍기\n우클릭 · 없음", .52f);
                axe.Attacks = new[]{Attack(chop,1)};
                var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Resources/WeaponCatalog.asset");
                if (catalog == null) { catalog=ScriptableObject.CreateInstance<WeaponCatalog>(); AssetDatabase.CreateAsset(catalog,"Assets/Resources/WeaponCatalog.asset"); }
                catalog.Weapons=new[]{sword,shield,great,axe};
                var shieldSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Crusader_Castle/Prefabs/Round_Shield_01.prefab").GetComponentInChildren<MeshFilter>();
                catalog.ShieldMesh=shieldSource.sharedMesh; catalog.ShieldMaterials=shieldSource.GetComponent<MeshRenderer>().sharedMaterials;
                EditorUtility.SetDirty(catalog); PrefabUtility.SaveAsPrefabAsset(player,"Assets/Prefabs/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            BakeAttackTracks(); AssetDatabase.SaveAssets();
        }
        private static T Asset<T>(string name) where T : Object, new()
        {
            var asset=AssetDatabase.LoadAssetAtPath<T>(Folder+name);
            if(asset==null) { asset=new T(); AssetDatabase.CreateAsset(asset,Folder+name); } return asset;
        }
        private static AttackData Attack(AnimationClip clip,float damage)
        {
            var data=AssetDatabase.LoadAssetAtPath<AttackData>(Folder+clip.name+".asset");
            if(data==null) { data=ScriptableObject.CreateInstance<AttackData>(); AssetDatabase.CreateAsset(data,Folder+clip.name+".asset"); }
            data.animationName=clip.name; data.damage=damage; data.comboWindowStart=.3f; data.comboWindowEnd=.95f;
            EditorUtility.SetDirty(data); return data;
        }
        private static WeaponCatalog.Entry Weapon(string path,MeleeWeaponKind kind,string name,string description,float shift)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>();
            var mesh=Asset<Mesh>(kind+".asset"); EditorUtility.CopySerialized(source.sharedMesh,mesh); mesh.name=name;
            mesh.vertices=mesh.vertices.Select(v=>v+Vector3.forward*shift).ToArray(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            // Meshes are authored along +Z; place the handle at the same palm pivot as the original sword.
            float min=kind==MeleeWeaponKind.Axe ? mesh.bounds.max.z-.3f : .13f;
            var size=mesh.bounds.size; size.z=mesh.bounds.max.z-min;
            return new WeaponCatalog.Entry {Kind=kind,Name=name,Description=description,Mesh=mesh,Materials=source.GetComponent<MeshRenderer>().sharedMaterials,
                HitCenter=new Vector3(0,0,(mesh.bounds.max.z+min)*.5f),HitSize=size,BladeBase=new Vector3(0,0,min),BladeTip=new Vector3(0,0,mesh.bounds.max.z)};
        }
        private static void Retime(AnimationClip clip,float seconds)
        {
            float factor=seconds/clip.length;
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve=AnimationUtility.GetEditorCurve(clip,binding); var keys=curve.keys;
                for(int i=0;i<keys.Length;i++) { keys[i].time*=factor; keys[i].inTangent/=factor; keys[i].outTangent/=factor; }
                AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
            }
        }
        private static void ConfigureEvents(AnimationClip clip,bool hit)
        {
            AnimationUtility.SetAnimationEvents(clip,hit ? new[]{new AnimationEvent{time=0,functionName="DisableHitBox"},new AnimationEvent{time=clip.length*.3f,functionName="EnableHitBox"},new AnimationEvent{time=clip.length*.78f,functionName="DisableHitBox"}} : Array.Empty<AnimationEvent>());
        }
        private static AnimationClip AuthorAttack(string name,bool axe)
        {
            var clip=Asset<AnimationClip>(name+".anim"); clip.ClearCurves(); clip.name="Weapon_"+name; clip.frameRate=60;
            var rig=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            rig.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var animator=rig.GetComponent<Animator>(); animator.fireEvents=false;
            var graph=PlayableGraph.Create("Weapon authoring"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                animator.Rebind(); animator.Update(0);
                var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim");
                var playable=AnimationClipPlayable.Create(graph,idle); var output=AnimationPlayableOutput.Create(graph,"Pose",animator); output.SetSourcePlayable(playable); graph.Play(); graph.Evaluate(0);
                using var handler=new HumanPoseHandler(animator.avatar,animator.transform);
                var rest=new HumanPose(); handler.GetHumanPose(ref rest);
                var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                var blade=rig.GetComponentInChildren<MeleeHitBox>(true).transform;
                Quaternion handToBlade=Quaternion.Inverse(hand.rotation)*blade.rotation;
                var curves=Enumerable.Range(0,HumanTrait.MuscleCount).Select(_=>new AnimationCurve()).ToArray();
                float duration=axe?1.05f:.65f;
                for(int frame=0;frame<=60;frame++)
                {
                    float t=frame/60f; handler.SetHumanPose(ref rest);
                    float phase=t<.32f?Mathf.SmoothStep(0,1,t/.32f):t<.72f?Mathf.SmoothStep(0,1,(t-.32f)/.4f):Mathf.SmoothStep(0,1,(t-.72f)/.28f);
                    Vector3 start=new Vector3(.3f,1.15f,.28f),wind=axe?new Vector3(.24f,1.72f,.02f):new Vector3(.28f,1.23f,.1f),end=axe?new Vector3(.22f,.9f,.6f):new Vector3(.2f,1.28f,.7f);
                    Vector3 wrist=t<.32f?Vector3.Lerp(start,wind,phase):t<.72f?Vector3.Lerp(wind,end,phase):Vector3.Lerp(end,start,phase);
                    Vector3 forward=axe?(t<.32f?Vector3.Slerp(Vector3.forward,Vector3.up,phase):t<.72f?Vector3.Slerp(Vector3.up,new Vector3(0,-.65f,1),phase):Vector3.Slerp(new Vector3(0,-.65f,1),Vector3.forward,phase)):Vector3.forward;
                    WeaponLoadout.SolveArm(animator.GetBoneTransform(HumanBodyBones.RightUpperArm),animator.GetBoneTransform(HumanBodyBones.RightLowerArm),hand,wrist,new Vector3(.65f,1,.05f));
                    hand.rotation=Quaternion.LookRotation(forward,Vector3.left)*Quaternion.Inverse(handToBlade);
                    var pose=new HumanPose(); handler.GetHumanPose(ref pose);
                    for(int i=0;i<curves.Length;i++) curves[i].AddKey(t*duration,pose.muscles[i]);
                }
                for(int i=0;i<curves.Length;i++)
                {
                    string property=HumanTrait.MuscleName[i];
                    foreach(string side in new[]{"Left","Right"}) foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                        property=property.Replace(side+" "+finger+" ",side+"Hand."+finger+".");
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),property),curves[i]);
                }
                CloseFingers(clip, false); ConfigureEvents(clip,true); EditorUtility.SetDirty(clip); return clip;
            }
            finally { graph.Destroy(); Object.DestroyImmediate(rig); }
        }
        private static void CloseFingers(AnimationClip clip,bool left)
        {
            foreach(string side in left ? new[]{"Left","Right"} : new[]{"Right"})
                foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                    for(int i=1;i<=3;i++) AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),side+"Hand."+finger+"."+i+" Stretched"),AnimationCurve.Constant(0,clip.length,finger=="Thumb"?-.15f:-.65f));
        }
        private static void BakeAttackTracks()
        {
            var rig=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            rig.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            try
            {
                var animator=rig.GetComponent<Animator>(); animator.fireEvents=false; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind(); animator.Update(0);
                var blade=rig.GetComponentInChildren<MeleeHitBox>(true).transform;
                var spine=animator.GetBoneTransform(HumanBodyBones.Spine);
                var catalog=AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Resources/WeaponCatalog.asset");
                foreach(var entry in catalog.Weapons) foreach(var data in entry.Attacks.Where(d=>d!=null && d.animationName.StartsWith("Weapon_")))
                {
                    animator.Play("Movement",0,0); animator.Play(data.animationName,1,.6f); animator.Update(0);
                    data.aimCrossingPhase=.6f;
                    data.aimBladeBase=spine.parent.InverseTransformVector(blade.TransformPoint(entry.BladeBase)-spine.position);
                    data.aimBladeTip=spine.parent.InverseTransformVector(blade.TransformPoint(entry.BladeTip)-spine.position);
                    data.aimBladePoint=Vector3.Lerp(data.aimBladeBase,data.aimBladeTip,.7f);
                    data.motionSamples=new MeleeMotionSample[241];
                    for(int i=0;i<data.motionSamples.Length;i++)
                    {
                        animator.Play("Movement",0,0); animator.Play(data.animationName,1,i/240f); animator.Update(0);
                        data.motionSamples[i]=new MeleeMotionSample {position=rig.transform.InverseTransformPoint(blade.position),rotation=Quaternion.Inverse(rig.transform.rotation)*blade.rotation,
                            pivot=rig.transform.InverseTransformPoint(spine.position),referenceBase=rig.transform.InverseTransformVector(spine.parent.TransformVector(data.aimBladeBase)),referenceTip=rig.transform.InverseTransformVector(spine.parent.TransformVector(data.aimBladeTip))};
                    }
                    EditorUtility.SetDirty(data);
                }
            }
            finally { Object.DestroyImmediate(rig); }
        }
    }
}
