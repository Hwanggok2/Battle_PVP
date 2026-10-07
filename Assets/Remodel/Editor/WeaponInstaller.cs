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
        private static readonly string[] Names = { "ShieldGuard", "Recoil" };
        private static readonly string[] Takes = { "Idle_Shield_Loop", "Hit_Knockback" };
        private static readonly float[] From = { 0, 0 }, To = { 75, 25 };
        private static readonly float[] Seconds = { 1f, .65f };

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
                spec.loopTime = i == 0; specs.Add(spec);
            }
            importer.clipAnimations = specs.ToArray(); importer.SaveAndReimport();
            var clips = new AnimationClip[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                var original = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().First(c => c.name == "Weapon_" + Names[i]);
                var clip = Asset<AnimationClip>(Names[i]+".anim"); EditorUtility.CopySerialized(original, clip); clip.name = "Weapon_" + Names[i];
                Retime(clip, Seconds[i]); ConfigureEvents(clip, false);
                CloseFingers(clip, true);
                EditorUtility.SetDirty(clip); clips[i] = clip;
            }
            var motions = WeaponMotionRetargeter.Bake();
            var thrust = motions.First(c=>c.name=="Weapon_Thrust"); var chop = motions.First(c=>c.name=="Weapon_AxeChop");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            var machine = controller.layers[1].stateMachine;
            var empty = machine.states.First(s => s.state.name == "New State").state;
            foreach (var clip in clips.Concat(motions))
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name) ?? machine.AddState(clip.name);
                state.motion = clip; state.speed = 1; state.writeDefaultValues = true;
                state.tag = clip != thrust && motions.Contains(clip) ? "WeaponTwoHanded" : "";
                foreach (var t in state.transitions) state.RemoveTransition(t);
                if (!clip.isLooping) { var t = state.AddTransition(empty); t.hasExitTime = true; t.exitTime = 1; t.hasFixedDuration = true; t.duration = .08f; }
            }
            var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                bool addedLoadout = player.GetComponent<WeaponLoadout>() == null;
                if (addedLoadout) player.AddComponent<WeaponLoadout>();
                var blade = player.GetComponentInChildren<MeleeHitBox>(true); var box = blade.GetComponent<BoxCollider>();
                var old = Enumerable.Range(1,3).Select(i => AssetDatabase.LoadAssetAtPath<AttackData>("Assets/Player/AttackData/Atk_"+i+".asset")).ToArray();
                var sword = new WeaponCatalog.Entry { Kind=MeleeWeaponKind.Sword, Name="한손검", Description="좌클릭 · 기본 3연격\n우클릭 · 전방 찌르기", Mesh=blade.GetComponent<MeshFilter>().sharedMesh, Materials=blade.GetComponent<MeshRenderer>().sharedMaterials,
                    HitCenter=box.center, HitSize=box.size, BladeBase=new Vector3(0,0,.13612f), BladeTip=new Vector3(0,0,1.0816832f), Attacks=old.Concat(new[]{Attack(thrust,1f)}).ToArray() };
                var shield = new WeaponCatalog.Entry { Kind=MeleeWeaponKind.SwordShield, Name="한손검 + 방패", Description="좌클릭 · 기본 3연격\n우클릭 유지 · 정면 막기 / 성공 시 상대 0.65초 경직", Mesh=sword.Mesh, Materials=sword.Materials, HitCenter=sword.HitCenter, HitSize=sword.HitSize, BladeBase=sword.BladeBase, BladeTip=sword.BladeTip, Attacks=old };
                var great = Weapon("Assets/Medieval Melee Weapon Pack/prefabs/Sword(1)_DH.prefab", MeleeWeaponKind.Greatsword, "양손검", "좌클릭 · 3콤보 / 패링 후 빠른 2연격\n우클릭 · 검 막기 / 시작 0.3초 내 피격 시 패링", 0);
                great.Attacks = new[]{Attack(motions.First(c=>c.name=="Weapon_Greatsword1"),1),Attack(motions.First(c=>c.name=="Weapon_Greatsword2"),1),Attack(motions.First(c=>c.name=="Weapon_Greatsword3"),1),null,Attack(motions.First(c=>c.name=="Weapon_Riposte1"),1),Attack(motions.First(c=>c.name=="Weapon_Riposte2"),1)};
                great.ReadyState = "Weapon_GreatswordReady"; great.RightGrip = new Vector3(0,0,.02f); great.LeftGrip = new Vector3(0,0,-.14f);
                var axe = Weapon("Assets/Medieval Melee Weapon Pack/prefabs/Axe(1)_DS.prefab", MeleeWeaponKind.Axe,"도끼","좌클릭 · 위에서 아래로 내려찍기\n우클릭 · 없음", .52f);
                axe.Attacks = new[]{Attack(chop,1)};
                axe.ReadyState = "Weapon_AxeReady"; axe.RightGrip = new Vector3(0,0,.30f); axe.LeftGrip = Vector3.zero;
                var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Resources/WeaponCatalog.asset");
                if (catalog == null) { catalog=ScriptableObject.CreateInstance<WeaponCatalog>(); AssetDatabase.CreateAsset(catalog,"Assets/Resources/WeaponCatalog.asset"); }
                catalog.Weapons=new[]{sword,shield,great,axe};
                var shieldSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Crusader_Castle/Prefabs/Round_Shield_01.prefab").GetComponentInChildren<MeshFilter>();
                catalog.ShieldMesh=shieldSource.sharedMesh; catalog.ShieldMaterials=shieldSource.GetComponent<MeshRenderer>().sharedMaterials;
                EditorUtility.SetDirty(catalog);
                if (addedLoadout) PrefabUtility.SaveAsPrefabAsset(player,"Assets/Prefabs/Player.prefab");
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
        internal static void Retime(AnimationClip clip,float seconds)
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
        internal static void CloseFingers(AnimationClip clip,bool left)
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
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+data.animationName.Substring("Weapon_".Length)+".anim");
                    var events=AnimationUtility.GetAnimationEvents(clip);
                    float open=events.First(e=>e.functionName=="EnableHitBox").time;
                    float close=events.Last(e=>e.functionName=="DisableHitBox").time;
                    float contact=(open+close)*.5f/clip.length;
                    animator.Play("Movement",0,0); animator.Play(data.animationName,1,contact); animator.Update(0);
                    if(entry.TwoHanded) WeaponLoadout.FitTwoHandedGrip(animator,blade,entry);
                    data.aimCrossingPhase=contact;
                    data.aimBladeBase=spine.parent.InverseTransformVector(blade.TransformPoint(entry.BladeBase)-spine.position);
                    data.aimBladeTip=spine.parent.InverseTransformVector(blade.TransformPoint(entry.BladeTip)-spine.position);
                    data.aimBladePoint=Vector3.Lerp(data.aimBladeBase,data.aimBladeTip,.7f);
                    data.motionSamples=new MeleeMotionSample[241];
                    for(int i=0;i<data.motionSamples.Length;i++)
                    {
                        animator.Play("Movement",0,0); animator.Play(data.animationName,1,i/240f); animator.Update(0);
                        if(entry.TwoHanded) WeaponLoadout.FitTwoHandedGrip(animator,blade,entry);
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
