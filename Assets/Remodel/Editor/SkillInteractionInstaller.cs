using System;
using System.Linq;
using BattlePvp.Combat;
using BattlePvp.EditorData;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BattlePvp.Remodel.Editor
{
    public static class SkillInteractionInstaller
    {
        private const string Folder="Assets/Remodel/Skills/Animations/";
        [MenuItem("Battle PvP/Skills/Apply Skill Interaction Motions")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before changing skill motions.");
            SkillWorkbookImporter.Import();
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            int upper=Array.FindIndex(controller.layers,l=>l.name=="ExpandedUpperBody");
            if(upper<0) { controller.AddLayer("ExpandedUpperBody"); upper=controller.layers.Length-1; }
            var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder+"ThrowUpperBody.mask");
            if(mask==null) { mask=new AvatarMask(); AssetDatabase.CreateAsset(mask,Folder+"ThrowUpperBody.mask"); }
            for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
            foreach(var part in new[]{AvatarMaskBodyPart.Body,AvatarMaskBodyPart.LeftArm,AvatarMaskBodyPart.LeftFingers,AvatarMaskBodyPart.RightArm,AvatarMaskBodyPart.RightFingers}) mask.SetHumanoidBodyPartActive(part,true);
            var layers=controller.layers; layers[upper].defaultWeight=0; layers[upper].avatarMask=mask; controller.layers=layers;
            EditorUtility.SetDirty(mask);
            var machine=controller.layers[upper].stateMachine;
            var empty=State(machine,"Empty"); empty.writeDefaultValues=false; machine.defaultState=empty;
            var throwing=ImportMotion("Hook_Throw","OverhandThrow",0,40,"SHARED_RightArmThrow");
            var knife=ImportMotion("Knife_Throw","OverhandThrow",8,40,"AGI_Knife");
            var retrieve=ImportMotion("Hook_Retrieve","Melee_Hook_Rec",0,18,"STR_HookRetrieve");
            var ready=ReadyClip(throwing);
            Configure(State(machine,"KnifeReady"),ready,empty,false,1);
            foreach(var pair in new[]{(JobSkillKind.Hook,"Skill_STR_Hook"),(JobSkillKind.Knife,"Skill_AGI_Knife")})
            {
                var clip=pair.Item1==JobSkillKind.Hook ? throwing : knife;
                Configure(State(machine,pair.Item2),clip,empty,true,clip.length/ExpandedSkillController.Value(pair.Item1,"CastSeconds",.55f));
                var data=SkillPresentationCatalog.Data((int)pair.Item1);
                var so=new SerializedObject(data); so.FindProperty("_castAnimationLayer").intValue=upper; so.ApplyModifiedPropertiesWithoutUndo();
            }
            if(!controller.parameters.Any(p=>p.name=="HookRetrieveRate")) controller.AddParameter("HookRetrieveRate",AnimatorControllerParameterType.Float);
            var retract=State(machine,"HookRetrieve"); Configure(retract,retrieve,empty,true,1);
            retract.speedParameter="HookRetrieveRate"; retract.speedParameterActive=true;
            var full=controller.layers.First(l=>l.name=="ExpandedSkills").stateMachine;
            foreach(var old in full.states.Where(s=>s.state.name=="KnifeReady" || s.state.name=="Skill_STR_Hook" || s.state.name=="Skill_AGI_Knife").ToArray()) full.RemoveState(old.state);
            var trap=ImportTrapClip();
            Configure(State(full,"Skill_SHARED_Trap"),trap,State(full,"Empty"),true,trap.length/ExpandedSkillController.Value(JobSkillKind.Trap,"CastSeconds",1.2f));
            SkillPropInstaller.Apply();
            FortifyMotionInstaller.Apply();
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        }
        private static AnimatorState State(AnimatorStateMachine machine,string name) => machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name) ?? machine.AddState(name);
        private static void Configure(AnimatorState state,AnimationClip clip,AnimatorState empty,bool exit,float speed)
        {
            state.motion=clip; state.speed=speed; state.speedParameterActive=false; state.writeDefaultValues=false;
            foreach(var transition in state.transitions) state.RemoveTransition(transition);
            if(exit) { var transition=state.AddTransition(empty); transition.hasExitTime=true; transition.exitTime=1; transition.hasFixedDuration=true; transition.duration=.1f; }
            EditorUtility.SetDirty(state);
        }
        private static AnimationClip Editable(string name)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+name+".anim");
            if(clip==null) { clip=new AnimationClip(); AssetDatabase.CreateAsset(clip,Folder+name+".anim"); }
            clip.ClearCurves(); clip.name=name; clip.frameRate=60; AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>()); return clip;
        }
        private static void Curve(AnimationClip clip,string muscle,float[] times,params float[] values)
        {
            if(!HumanTrait.MuscleName.Contains(muscle)) throw new InvalidOperationException("Unknown muscle: "+muscle);
            var keys=new Keyframe[times.Length]; for(int i=0;i<keys.Length;i++) keys[i]=new Keyframe(times[i],values[i]);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),muscle),new AnimationCurve(keys));
        }
        private static AnimationClip ReadyClip(AnimationClip source)
        {
            // Hold the source throw's wind-up pose so ready -> release shares exactly the same arm geometry.
            var clip=Editable("AGI_KnifeReady");
            var rig=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            rig.SetActive(true); var animator=rig.GetComponent<Animator>(); animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var graph=PlayableGraph.Create();
            try
            {
                var playable=AnimationClipPlayable.Create(graph,source);
                AnimationPlayableOutput.Create(graph,"Ready pose",animator).SetSourcePlayable(playable);
                graph.Play(); playable.SetTime(8f/30f); graph.Evaluate(0);
                using var handler=new HumanPoseHandler(animator.avatar,animator.transform);
                var pose=new HumanPose(); handler.GetHumanPose(ref pose);
                for(int i=0;i<HumanTrait.MuscleCount;i++)
                {
                    float value=pose.muscles[i]; string muscle=HumanTrait.MuscleName[i];
                    float sway=muscle=="Chest Front-Back" || muscle=="Right Arm Down-Up" ? .008f : 0;
                    Curve(clip,muscle,new[]{0f,.5f,1f,1.5f,2f},value,value+sway,value,value-sway,value);
                }
            }
            finally { graph.Destroy(); UnityEngine.Object.DestroyImmediate(rig); }
            var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=true; settings.loopBlend=true; AnimationUtility.SetAnimationClipSettings(clip,settings);
            EditorUtility.SetDirty(clip); return clip;
        }
        private static AnimationClip ImportMotion(string name,string take,float first,float last,string output)
        {
            const string path="Assets/Remodel/Skills/Source/Quaternius/UAL2_Standard.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            var spec=importer.defaultClipAnimations.First(c=>c.name.EndsWith(take));
            spec.name=name; spec.firstFrame=first; spec.lastFrame=last; spec.loopTime=false;
            spec.lockRootRotation=true; spec.lockRootHeightY=true; spec.lockRootPositionXZ=true;
            var previous=importer.clipAnimations.FirstOrDefault(c=>c.name==name);
            if(previous==null || previous.firstFrame!=first || previous.lastFrame!=last)
            { importer.clipAnimations=importer.clipAnimations.Where(c=>c.name!=name).Concat(new[]{spec}).ToArray(); importer.SaveAndReimport(); }
            var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>c.name==name);
            if(!source.humanMotion) throw new InvalidOperationException("Expected a retargetable humanoid clip: "+name);
            var clip=Editable(output); EditorUtility.CopySerialized(source,clip); clip.name=output;
            AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
            EditorUtility.SetDirty(clip); return clip;
        }
        private static AnimationClip ImportTrapClip()
        {
            const string path="Assets/Remodel/Skills/Source/Quaternius/UAL1_Standard.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            var sourceTake=importer.defaultClipAnimations.First(c=>c.name.EndsWith("Fixing_Kneeling"));
            var installed=importer.clipAnimations.FirstOrDefault(c=>c.name=="Trap_Assembly");
            if(installed==null || installed.firstFrame!=sourceTake.firstFrame || installed.lastFrame!=sourceTake.lastFrame)
            {
                // Keep the complete kneel, hand assembly and stand-up action; the state retimes it to CastSeconds.
                var trap=sourceTake;
                trap.name="Trap_Assembly";
                trap.loopTime=false; trap.lockRootRotation=true; trap.lockRootHeightY=true; trap.lockRootPositionXZ=true;
                importer.clipAnimations=importer.clipAnimations.Where(c=>c.name!="Trap_Assembly").Concat(new[]{trap}).ToArray(); importer.SaveAndReimport();
            }
            var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>c.name=="Trap_Assembly");
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"SHARED_Trap.anim");
            if(clip==null) { clip=new AnimationClip(); AssetDatabase.CreateAsset(clip,Folder+"SHARED_Trap.anim"); }
            EditorUtility.CopySerialized(source,clip); clip.name="SHARED_Trap"; AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
            EditorUtility.SetDirty(clip); return clip;
        }
    }
}
