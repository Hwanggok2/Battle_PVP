using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEditor.Animations;
using BattlePvp.Combat;
using Object=UnityEngine.Object;

namespace BattlePvp.Remodel.Editor
{
    public static class FortifyMotionInstaller
    {
        const string SourcePath="Assets/Remodel/Skills/Source/Motifect/army_crawl.fbx";
        static AnimationClip Source()
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath(SourcePath);
            const string name="Fortify_ProneSource";
            if(importer.animationType!=ModelImporterAnimationType.Human)
            { importer.animationType=ModelImporterAnimationType.Human; importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel; importer.SaveAndReimport(); }
            if(!importer.clipAnimations.Any(c=>c.name==name))
            {
                var spec=importer.defaultClipAnimations.First(); spec.name=name;
                spec.lockRootRotation=spec.lockRootHeightY=spec.lockRootPositionXZ=true; spec.loopTime=false;
                importer.clipAnimations=importer.clipAnimations.Concat(new[]{spec}).ToArray(); importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().First(c=>c.name==name);
        }
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing Fortify.");
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var rig=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rig,scene);
            rig.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var animator=rig.GetComponent<Animator>(); animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion=false;
            var graph=PlayableGraph.Create(); var output=AnimationPlayableOutput.Create(graph,"Fortify bake",animator);
            try
            {
                graph.Play();
                using var handler=new HumanPoseHandler(animator.avatar,animator.transform);
                HumanPose Sample(AnimationClip input,float time)
                {
                    var playable=AnimationClipPlayable.Create(graph,input); output.SetSourcePlayable(playable); playable.SetTime(time); graph.Evaluate(0);
                    var pose=new HumanPose(); handler.GetHumanPose(ref pose);
                    pose.bodyPosition.x=pose.bodyPosition.z=0;
                    return pose;
                }
                var standing=Sample(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim"),0);
                var kneeling=Sample(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Remodel/Skills/Animations/SHARED_Trap.anim"),.7f);
                // Fold into a compact guard, with a rounded back and fists shielding the head.
                var prone=CurledGuard(animator,handler,standing);
                const string path="Assets/Remodel/Skills/Animations/DEF_Fortify.anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(clip==null) { clip=new AnimationClip(); AssetDatabase.CreateAsset(clip,path); }
                clip.ClearCurves(); clip.name="DEF_Fortify"; clip.frameRate=60;
                var curves=Enumerable.Range(0,HumanTrait.MuscleCount+7).Select(_=>new AnimationCurve()).ToArray();
                // Smooth zero-tangent pose keys avoid baking thousands of redundant hold samples.
                foreach(float time in new[]{0f,.25f,.6f,2.5f,2.8f,3f})
                {
                    HumanPose a,b; float blend;
                    if(time<.25f) { a=standing; b=kneeling; blend=time/.25f; }
                    else if(time<.6f) { a=kneeling; b=prone; blend=(time-.25f)/.35f; }
                    else if(time<2.5f) { a=b=prone; blend=0; }
                    else if(time<2.8f) { a=prone; b=kneeling; blend=(time-2.5f)/.3f; }
                    else { a=kneeling; b=standing; blend=(time-2.8f)/.2f; }
                    blend=Mathf.SmoothStep(0,1,blend);
                    Vector3 position=Vector3.Lerp(a.bodyPosition,b.bodyPosition,blend);
                    Quaternion rotation=Quaternion.Slerp(a.bodyRotation,b.bodyRotation,blend);
                    for(int i=0;i<HumanTrait.MuscleCount;i++) curves[i].AddKey(new Keyframe(time,Mathf.Lerp(a.muscles[i],b.muscles[i],blend),0,0));
                    int offset=HumanTrait.MuscleCount;
                    for(int axis=0;axis<3;axis++) curves[offset+axis].AddKey(new Keyframe(time,position[axis],0,0));
                    for(int axis=0;axis<4;axis++) curves[offset+3+axis].AddKey(new Keyframe(time,rotation[axis],0,0));
                }
                var rootNames=new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"};
                for(int i=0;i<curves.Length;i++) AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),i<HumanTrait.MuscleCount ? MuscleProperty(i) : rootNames[i-HumanTrait.MuscleCount]),curves[i]);
                AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
                var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=false; settings.startTime=0; settings.stopTime=3;
                settings.keepOriginalOrientation=true; settings.keepOriginalPositionXZ=true; settings.keepOriginalPositionY=true;
                settings.loopBlendOrientation=settings.loopBlendPositionXZ=settings.loopBlendPositionY=true;
                AnimationUtility.SetAnimationClipSettings(clip,settings); clip.EnsureQuaternionContinuity();
                BakeGroundClearance(animator,graph,output,clip); EditorUtility.SetDirty(clip);
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
                // Keep attacks on the existing masked upper-body layer while the legs/root stay prone.
                var oldMachine=controller.layers.First(l=>l.name=="ExpandedSkills").stateMachine;
                foreach(var old in oldMachine.states.Where(s=>s.state.name=="Skill_DEF_Fortify").ToArray()) oldMachine.RemoveState(old.state);
                var machine=controller.layers[0].stateMachine;
                var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Skill_DEF_Fortify") ?? machine.AddState("Skill_DEF_Fortify");
                state.motion=clip; state.speed=3f/ExpandedSkillController.Value(JobSkillKind.Fortify,"DurationSeconds",3); state.speedParameterActive=false; state.writeDefaultValues=false;
                foreach(var transition in state.transitions) state.RemoveTransition(transition);
                var exit=state.AddTransition(machine.states.First(s=>s.state.name=="Movement").state); exit.hasExitTime=true; exit.exitTime=1; exit.hasFixedDuration=true; exit.duration=.08f;
                var skill=new SerializedObject(SkillPresentationCatalog.Data((int)JobSkillKind.Fortify)); skill.FindProperty("_castAnimationLayer").intValue=0; skill.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            }
            finally { graph.Destroy(); Object.DestroyImmediate(rig); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }
        static HumanPose CurledGuard(Animator animator,HumanPoseHandler handler,HumanPose standing)
        {
            standing.muscles=(float[])standing.muscles.Clone();
            for(int i=0;i<HumanTrait.MuscleCount;i++)
                if(MuscleProperty(i).Contains("Hand.")) standing.muscles[i]=HumanTrait.MuscleName[i].Contains("Stretched") ? -.85f : 0;
            handler.SetHumanPose(ref standing);
            Transform Bone(HumanBodyBones id) => animator.GetBoneTransform(id);
            var hips=Bone(HumanBodyBones.Hips);
            var head=Bone(HumanBodyBones.Head); var headRotation=head.rotation;
            var feet=new[]{Bone(HumanBodyBones.LeftFoot).rotation,Bone(HumanBodyBones.RightFoot).rotation};
            var hands=new[]{Bone(HumanBodyBones.LeftHand).rotation,Bone(HumanBodyBones.RightHand).rotation};
            var forearms=new[]{Bone(HumanBodyBones.LeftHand).position-Bone(HumanBodyBones.LeftLowerArm).position,
                Bone(HumanBodyBones.RightHand).position-Bone(HumanBodyBones.RightLowerArm).position};
            float scale=animator.humanScale/1.070545f;
            hips.rotation=Quaternion.AngleAxis(35,Vector3.right)*hips.rotation;
            hips.position=new Vector3(0,.28f,-.22f)*scale;
            Bend(Bone(HumanBodyBones.Spine),Bone(HumanBodyBones.Chest),60);
            var upperChest=Bone(HumanBodyBones.UpperChest);
            Bend(Bone(HumanBodyBones.Chest),upperChest!=null ? upperChest : Bone(HumanBodyBones.Neck),95);
            if(upperChest!=null) Bend(upperChest,Bone(HumanBodyBones.Neck),120);
            Bend(Bone(HumanBodyBones.Neck),head,135);
            head.rotation=Quaternion.Euler(90,0,0)*headRotation;
            for(int side=0;side<2;side++)
            {
                var thigh=Bone(side==0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var knee=Bone(side==0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                var foot=Bone(side==0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                AimJointAtHeight(thigh,knee,.085f*scale,Vector3.forward);
                AimJointAtHeight(knee,foot,.13f*scale,Vector3.back);
                foot.rotation=feet[side];
                var arm=Bone(side==0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var elbow=Bone(side==0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hand=Bone(side==0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                float sign=side==0 ? -1 : 1;
                Vector3 wrist=head.position+new Vector3(sign*.16f,.065f,.075f)*scale;
                SolveArm(arm,elbow,hand,wrist,head.position+new Vector3(sign*.27f,-.18f,.03f)*scale);
                hand.rotation=Quaternion.FromToRotation(forearms[side],new Vector3(-sign,.1f,.4f))*hands[side];
            }
            var pose=new HumanPose(); handler.GetHumanPose(ref pose);
            return pose;
        }
        static void Bend(Transform bone,Transform child,float degrees)
        {
            float angle=degrees*Mathf.Deg2Rad;
            bone.rotation=Quaternion.FromToRotation(child.position-bone.position,new Vector3(0,Mathf.Cos(angle),Mathf.Sin(angle)))*bone.rotation;
        }
        static void SolveArm(Transform arm,Transform elbow,Transform hand,Vector3 wrist,Vector3 hint)
        {
            float upper=Vector3.Distance(arm.position,elbow.position),lower=Vector3.Distance(elbow.position,hand.position);
            Vector3 direction=(wrist-arm.position).normalized;
            float distance=Mathf.Clamp(Vector3.Distance(arm.position,wrist),Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
            float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            Vector3 bend=Vector3.ProjectOnPlane(hint-arm.position,direction).normalized;
            Vector3 targetElbow=arm.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            arm.rotation=Quaternion.FromToRotation(elbow.position-arm.position,targetElbow-arm.position)*arm.rotation;
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,wrist-elbow.position)*elbow.rotation;
        }
        static void AimJointAtHeight(Transform parent,Transform joint,float height,Vector3 horizontal)
        {
            Vector3 limb=joint.position-parent.position;
            float vertical=Mathf.Clamp(height-parent.position.y,-limb.magnitude,limb.magnitude);
            Vector3 direction=Vector3.up*vertical+horizontal*Mathf.Sqrt(Mathf.Max(0,limb.sqrMagnitude-vertical*vertical));
            parent.rotation=Quaternion.FromToRotation(limb,direction)*parent.rotation;
        }
        static string MuscleProperty(int index)
        {
            string name=HumanTrait.MuscleName[index];
            foreach(string side in new[]{"Left","Right"})
                foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                    if(name.StartsWith(side+" "+finger+" "))
                        return name.Replace(side+" "+finger+" ",side+"Hand."+finger+".");
            return name;
        }
        static void BakeGroundClearance(Animator animator,PlayableGraph graph,AnimationPlayableOutput output,AnimationClip clip)
        {
            // Muscle interpolation can lower a foot/knee between the authored keys. Bake
            // clearance from the skinned body, including both entry and recovery, at edit time.
            var binding=EditorCurveBinding.FloatCurve("",typeof(Animator),"RootT.y");
            var original=AnimationUtility.GetEditorCurve(clip,binding); var grounded=new AnimationCurve();
            var playable=AnimationClipPlayable.Create(graph,clip); playable.SetApplyFootIK(false); output.SetSourcePlayable(playable);
            var renderers=animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.gameObject.activeInHierarchy).ToArray();
            var mesh=new Mesh(); var vertices=new System.Collections.Generic.List<Vector3>();
            try
            {
                for(int frame=0;frame<=180;frame++)
                {
                    float time=frame/60f; playable.SetTime(time); graph.Evaluate(0); float bottom=float.PositiveInfinity;
                    foreach(var renderer in renderers)
                    {
                        renderer.BakeMesh(mesh); mesh.GetVertices(vertices);
                        foreach(var vertex in vertices) bottom=Mathf.Min(bottom,renderer.transform.TransformPoint(vertex).y);
                    }
                    float lift=Mathf.Max(0,.005f-bottom)/animator.humanScale;
                    grounded.AddKey(new Keyframe(time,original.Evaluate(time)+lift,0,0));
                }
                AnimationUtility.SetEditorCurve(clip,binding,grounded);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        public static void InspectSource()
        {
            var clip=Source();
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            root.transform.position=Vector3.right*10000;
            var animator=root.GetComponent<Animator>(); animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion=false;
            var graph=PlayableGraph.Create(); var playable=AnimationClipPlayable.Create(graph,clip);
            AnimationPlayableOutput.Create(graph,"Fortify source",animator).SetSourcePlayable(playable); graph.Play();
            var camera=new GameObject("Fortify preview camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=root.transform.position+new Vector3(2,1.5f,2); camera.transform.LookAt(root.transform.position+Vector3.up*.55f);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.025f,.035f,.05f);
            var light=new GameObject("Fortify preview light",typeof(Light)).GetComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(35,-40,0);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=root.transform.position+Vector3.down*.05f; floor.transform.localScale=new Vector3(6,.1f,6);
            Directory.CreateDirectory("Reports/BowSkillPolish");
            try
            {
                for(int i=0;i<3;i++)
                {
                    playable.SetTime(i*clip.length/2); graph.Evaluate(0);
                    var render=new RenderTexture(1200,800,24); var image=new Texture2D(1200,800,TextureFormat.RGB24,false); var old=RenderTexture.active;
                    try { camera.targetTexture=render; camera.Render(); RenderTexture.active=render; image.ReadPixels(new Rect(0,0,1200,800),0,0); image.Apply(); File.WriteAllBytes("Reports/BowSkillPolish/fortify-source-"+i+".png",image.EncodeToPNG()); }
                    finally { camera.targetTexture=null; RenderTexture.active=old; render.Release(); Object.DestroyImmediate(render); Object.DestroyImmediate(image); }
                }
            }
            finally { graph.Destroy(); Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(floor); }
        }
    }
}
