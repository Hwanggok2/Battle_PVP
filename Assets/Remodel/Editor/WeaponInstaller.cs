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
        private static readonly string[] Names = { "Recoil" };
        private static readonly string[] Takes = { "Hit_Knockback" };
        private static readonly float[] From = { 0 }, To = { 25 };
        private static readonly float[] Seconds = { .65f };

        [MenuItem("Battle PvP/Weapons/Install Weapons")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            ConfigureMotionAvatars();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Source);
            var specs = importer.clipAnimations.Where(c => !c.name.StartsWith("Weapon_")).ToList();
            for (int i = 0; i < Names.Length; i++)
            {
                var spec = importer.defaultClipAnimations.First(c => c.name.EndsWith("|" + Takes[i]));
                spec.name = "Weapon_" + Names[i]; spec.firstFrame = From[i]; spec.lastFrame = To[i];
                spec.lockRootRotation = spec.lockRootHeightY = spec.lockRootPositionXZ = true;
                spec.loopTime = false; specs.Add(spec);
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
            foreach (var obsolete in machine.states.Where(s => s.state.name == "Weapon_AxeReady").ToArray())
                machine.RemoveState(obsolete.state);
            foreach (var clip in clips.Concat(motions))
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name) ?? machine.AddState(clip.name);
                state.motion = clip; state.speed = 1; state.writeDefaultValues = true;
                state.tag = clip != thrust && !clip.name.StartsWith("Weapon_Shield") && motions.Contains(clip) ? "WeaponTwoHanded" : "";
                if (clip.isLooping) { foreach (var t in state.transitions) state.RemoveTransition(t); }
                else
                {
                    var destination = clip.name == "Weapon_ShieldEnter"
                        ? machine.states.First(s => s.state.name == "Weapon_ShieldGuard").state : empty;
                    var transition = state.transitions.FirstOrDefault(t => t.destinationState == destination) ?? state.AddTransition(destination);
                    foreach (var t in state.transitions.Where(t => t != transition)) state.RemoveTransition(t);
                    transition.hasExitTime = true; transition.exitTime = 1; transition.hasFixedDuration = true; transition.duration = .08f;
                }
            }
            var footwork=Asset<AvatarMask>("WeaponFootwork.mask");
            for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)
                footwork.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,true);
            EditorUtility.SetDirty(footwork);
            var layers=controller.layers.ToList();
            int footworkIndex=layers.FindIndex(l=>l.name=="Weapon Footwork");
            var footworkLayer=new AnimatorControllerLayer {name="Weapon Footwork",avatarMask=footwork,
                defaultWeight=0,syncedLayerIndex=1,syncedLayerAffectsTiming=false};
            foreach(var item in machine.states.Where(s=>s.state.tag=="WeaponTwoHanded" && s.state.name!="Weapon_AxeChop"))
            {
                var visualClip=Asset<AnimationClip>("Footwork_"+item.state.name+".anim");
                EditorUtility.CopySerialized(item.state.motion,visualClip);
                visualClip.name="Footwork_"+item.state.name;
                AnimationUtility.SetAnimationEvents(visualClip,Array.Empty<AnimationEvent>());
                EditorUtility.SetDirty(visualClip);
                footworkLayer.SetOverrideMotion(item.state,visualClip);
            }
            if(footworkIndex<0) layers.Add(footworkLayer); else layers[footworkIndex]=footworkLayer;
            controller.layers=layers.ToArray();
            var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                bool addedLoadout = player.GetComponent<WeaponLoadout>() == null;
                if (addedLoadout) player.AddComponent<WeaponLoadout>();
                var blade = player.GetComponentInChildren<MeleeHitBox>(true); var box = blade.GetComponent<BoxCollider>();
                var old = Enumerable.Range(1,3).Select(i => AssetDatabase.LoadAssetAtPath<AttackData>("Assets/Player/AttackData/Atk_"+i+".asset")).ToArray();
                var sword = new WeaponCatalog.Entry { Kind=MeleeWeaponKind.Sword, MeleeDamageMultiplier=1.1f, Name="한손검", Description="좌클릭 · 기본 3연격 / 방패 장착 시보다 근접 피해 +10%\n우클릭 · 양손으로 받쳐 찌르기", Mesh=blade.GetComponent<MeshFilter>().sharedMesh, Materials=blade.GetComponent<MeshRenderer>().sharedMaterials,
                    HitCenter=box.center, HitSize=box.size, BladeBase=new Vector3(0,0,.13612f), BladeTip=new Vector3(0,0,1.0816832f), Attacks=old.Concat(new[]{Attack(thrust,1f)}).ToArray() };
                var shield = new WeaponCatalog.Entry { Kind=MeleeWeaponKind.SwordShield, Name="한손검 + 방패", Description="좌클릭 · 기본 3연격\n우클릭 유지 · 정면 막기 / 성공 시 상대 0.65초 경직", Mesh=sword.Mesh, Materials=sword.Materials, HitCenter=sword.HitCenter, HitSize=sword.HitSize, BladeBase=sword.BladeBase, BladeTip=sword.BladeTip, Attacks=old };
                var great = Weapon("Assets/Medieval Melee Weapon Pack/prefabs/Sword(1)_DH.prefab", MeleeWeaponKind.Greatsword, "양손검", "좌클릭 · 3콤보 / 패링 후 빠른 2연격\n우클릭 · 검 막기 / 몸보다 검에 먼저 닿으면 패링", 0);
                great.Attacks = new[]{Attack(motions.First(c=>c.name=="Weapon_Greatsword1"),1),Attack(motions.First(c=>c.name=="Weapon_Greatsword2"),1),Attack(motions.First(c=>c.name=="Weapon_Greatsword3"),1),null,Attack(motions.First(c=>c.name=="Weapon_Riposte1"),1),Attack(motions.First(c=>c.name=="Weapon_Riposte2"),1)};
                great.ReadyState = "Weapon_GreatswordReady"; great.RightGrip = new Vector3(0,0,.02f); great.LeftGrip = new Vector3(0,0,-.14f);
                var axe = Weapon("Assets/Medieval Melee Weapon Pack/prefabs/Axe(2)_OS.prefab", MeleeWeaponKind.Axe,"도끼","좌클릭 · 내려찍기 (한손검 기본 공격의 1.2배) / 우클릭 · 없음\n적중 즉시 회수~다음 공격 동작 2배속 (중첩 없음)", .48f);
                axe.Attacks = new[]{Attack(chop,sword.Attacks[0].damage*1.2f)};
                axe.MeleeDamageMultiplier = sword.MeleeDamageMultiplier;
                axe.ReadyState = ""; axe.RightGrip = new Vector3(0,0,-.10f); axe.LeftGrip = new Vector3(0,0,-.34f);
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
        private static void ConfigureMotionAvatars()
        {
            // These downloads contain the Brute skeleton (including hair/face bones).
            // Animation-only FBXs have no mesh bind pose. Reuse their actual source
            // Avatar rather than deriving a different reference pose from every take.
            var avatar=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Brute/Source/Brute.fbx").GetComponent<Animator>().avatar;
            foreach(string guid in AssetDatabase.FindAssets("t:Model",new[]{Folder+"Source/Mixamo"}))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if(importer.avatarSetup==ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar==avatar) continue;
                importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar=avatar; importer.SaveAndReimport();
            }
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
            // Broaden the single cutting head and lengthen the haft; leave the grip
            // anchors in gameplay metres so fingers still wrap the same handle.
            Vector3 scale=kind==MeleeWeaponKind.Axe ? new Vector3(1.55f,1.2f,1.15f) : Vector3.one;
            mesh.vertices=mesh.vertices.Select(v=>Vector3.Scale(v,scale)+Vector3.forward*shift).ToArray(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            // Meshes are authored along +Z; place the handle at the same palm pivot as the original sword.
            float min=kind==MeleeWeaponKind.Axe ? mesh.bounds.max.z-.42f : .13f;
            var size=mesh.bounds.size; size.z=mesh.bounds.max.z-min;
            return new WeaponCatalog.Entry {Kind=kind,Name=name,Description=description,Mesh=mesh,Materials=source.GetComponent<MeshRenderer>().sharedMaterials,
                HitCenter=new Vector3(mesh.bounds.center.x,mesh.bounds.center.y,(mesh.bounds.max.z+min)*.5f),HitSize=size,
                BladeBase=new Vector3(kind==MeleeWeaponKind.Axe ? mesh.bounds.min.x*.7f : 0,0,min),BladeTip=new Vector3(kind==MeleeWeaponKind.Axe ? mesh.bounds.min.x*.7f : 0,0,mesh.bounds.max.z)};
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
        internal static void BakeAttackTracks(string animationName = null)
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
                    if(animationName != null && data.animationName != animationName) continue;
                    data.aimInRootSpace=entry.Kind==MeleeWeaponKind.Greatsword;
                    data.maxAimCalibration=data.aimInRootSpace ? 80f : entry.TwoHanded ? 0f : 180f;
                    animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),entry.Kind==MeleeWeaponKind.Greatsword ? 1 : 0);
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+data.animationName.Substring("Weapon_".Length)+".anim");
                    var events=AnimationUtility.GetAnimationEvents(clip);
                    float open=events.First(e=>e.functionName=="EnableHitBox").time;
                    float close=events.Last(e=>e.functionName=="DisableHitBox").time;
                    float contact=(open+close)*.5f/clip.length;
                    animator.Play("Movement",0,0); animator.Play(data.animationName,1,contact); animator.Update(0);
                    if(entry.TwoHanded) WeaponLoadout.FitTwoHandedGrip(animator,blade,entry);
                    data.aimCrossingPhase=contact;
                    var referenceFrame=data.aimInRootSpace ? rig.transform : spine.parent;
                    data.aimBladeBase=referenceFrame.InverseTransformVector(blade.TransformPoint(entry.BladeBase)-spine.position);
                    data.aimBladeTip=referenceFrame.InverseTransformVector(blade.TransformPoint(entry.BladeTip)-spine.position);
                    data.aimBladePoint=Vector3.Lerp(data.aimBladeBase,data.aimBladeTip,.7f);
                    data.motionSamples=new MeleeMotionSample[241];
                    for(int i=0;i<data.motionSamples.Length;i++)
                    {
                        animator.Play("Movement",0,0); animator.Play(data.animationName,1,i/240f); animator.Update(0);
                        if(entry.TwoHanded) WeaponLoadout.FitTwoHandedGrip(animator,blade,entry);
                        data.motionSamples[i]=new MeleeMotionSample {position=rig.transform.InverseTransformPoint(blade.position),rotation=Quaternion.Inverse(rig.transform.rotation)*blade.rotation,
                            pivot=rig.transform.InverseTransformPoint(spine.position),referenceBase=rig.transform.InverseTransformVector(referenceFrame.TransformVector(data.aimBladeBase)),referenceTip=rig.transform.InverseTransformVector(referenceFrame.TransformVector(data.aimBladeTip))};
                    }
                    data.visualAim=data.aimInRootSpace ? BakeVisualAim(rig,animator,entry,data) : Array.Empty<AttackData.VisualAim>();
                    EditorUtility.SetDirty(data);
                }
            }
            finally { Object.DestroyImmediate(rig); }
        }

        private static AttackData.VisualAim[] BakeVisualAim(GameObject rig,Animator animator,WeaponCatalog.Entry entry,AttackData data)
        {
            var results=new System.Collections.Generic.List<AttackData.VisualAim>();
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+data.animationName.Substring(7)+".anim");
            var events=AnimationUtility.GetAnimationEvents(clip);
            float open=events.First(e=>e.functionName=="EnableHitBox").time/clip.length;
            float close=events.Last(e=>e.functionName=="DisableHitBox").time/clip.length;
            using var skin=new CharacterSkin(rig.GetComponentInChildren<SkinnedMeshRenderer>());
            foreach(var definition in CharacterCatalog.Instance.Characters.Where(c=>c.Id!=CharacterCatalog.DefaultId))
            {
                if(!skin.Apply(definition,out var error)) throw new InvalidOperationException(error);
                var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{animator});
                var spine=visible.GetBoneTransform(HumanBodyBones.Spine);
                var blade=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(rig.GetComponentInChildren<MeleeHitBox>(true));
                void Sample(float phase)
                {
                    animator.Play("Movement",0,0); animator.Play(data.animationName,1,phase); animator.Update(0);
                    skin.SyncPose(); WeaponLoadout.FitTwoHandedGrip(visible,blade,entry,animator);
                }
                Sample(data.aimCrossingPhase);
                var reference=new AttackData.VisualAim {avatar=visible.avatar,crossingPhase=data.aimCrossingPhase,
                    bladeBase=rig.transform.InverseTransformVector(blade.TransformPoint(entry.BladeBase)-spine.position),
                    bladeTip=rig.transform.InverseTransformVector(blade.TransformPoint(entry.BladeTip)-spine.position),
                    samples=new MeleeMotionSample[241]};
                float bestAngle=180f;
                for(int i=0;i<reference.samples.Length;i++)
                {
                    Sample(i/240f);
                    float phase=i/240f;
                    float angle=Vector3.Angle(rig.transform.forward,blade.TransformPoint(Vector3.Lerp(entry.BladeBase,entry.BladeTip,.7f))-spine.position);
                    if(phase>=open+.01f && phase<=close-.01f && angle<bestAngle)
                    { bestAngle=angle; reference.crossingPhase=phase; }
                    reference.samples[i]=new MeleeMotionSample {position=rig.transform.InverseTransformPoint(blade.position),
                        rotation=Quaternion.Inverse(rig.transform.rotation)*blade.rotation,pivot=rig.transform.InverseTransformPoint(spine.position),
                        referenceBase=reference.bladeBase,referenceTip=reference.bladeTip};
                }
                // Pick the forward part of this avatar's actual swing, rather than
                // dragging a sideways/behind-the-body pose through the crosshair.
                Sample(reference.crossingPhase);
                reference.bladeBase=rig.transform.InverseTransformVector(blade.TransformPoint(entry.BladeBase)-spine.position);
                reference.bladeTip=rig.transform.InverseTransformVector(blade.TransformPoint(entry.BladeTip)-spine.position);
                for(int i=0;i<reference.samples.Length;i++)
                { reference.samples[i].referenceBase=reference.bladeBase; reference.samples[i].referenceTip=reference.bladeTip; }
                results.Add(reference);
            }
            skin.Restore();
            return results.ToArray();
        }
    }
}
