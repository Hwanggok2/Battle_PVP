using System;
using System.Linq;
using BattlePvp.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace BattlePvp.Remodel.Editor
{
    // Copy attack curves unchanged; author only static holds and one-handed adaptations.
    public static class WeaponMotionRetargeter
    {
        private const string Folder = "Assets/Remodel/Weapons/";
        private sealed class Motion
        {
            public string Name, Take;
            public float Seconds, Open, Close, From, To = 1;
            public bool TwoHands, Axe, Loop, Guard, Shield;
            public Motion(string name, string take, float seconds, float open, float close, bool twoHands = true)
            { Name=name; Take=take; Seconds=seconds; Open=open; Close=close; TwoHands=twoHands; }
        }
        private static readonly Motion[] Motions = {
            new("Thrust", "Thrust Slash", .72f, .50f, .83f, false) { From=.12f, To=.36f },
            new("Greatsword1", "Great Sword Slash combo", .85f, .62f, .9f) { To=.34f },
            new("Greatsword2", "Great Sword Slash combo", .80f, .34f, .72f) { From=.34f, To=.66f },
            // The final slice crosses the target early; its late forward pose is recovery.
            new("Greatsword3", "Great Sword Slash combo", 1.05f, .20f, .42f) { From=.66f },
            new("Riposte1", "Great Sword Slash combo", .36f, .28f, .76f) { From=.18f, To=.34f },
            new("Riposte2", "Great Sword Slash combo", .36f, .35f, .78f) { From=.40f, To=.60f },
            new("AxeChop", "KevinIglesias/HumanM@Attack2H01", 1.6f, .325f, .40f) { Axe=true },
            new("GreatswordReady", "Great Sword Idle", 7.55f, 0, 0) { Loop=true },
            new("SwordGuard", "Great Sword Idle", 7.55f, 0, 0) { Loop=true, Guard=true },
            new("ShieldGuard", "Sword And Shield Block Idle", 1.383333f, 0, 0, false) { Loop=true, Shield=true },
            new("ShieldEnter", "Sword And Shield Block (1)", .18f, 0, 0, false) { Shield=true },
            new("ShieldExit", "Sword And Shield Block", .22f, 0, 0, false) { Shield=true }
        };

        public static AnimationClip[] Bake(bool guardOnly = false, string motionName = null)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            var rig=Object.Instantiate(prefab);
            var raw=Object.Instantiate(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            raw.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var animator=rig.GetComponent<Animator>(); var sourceAnimator=raw.GetComponent<Animator>();
            foreach(var a in new[]{animator,sourceAnimator})
            { a.fireEvents=false; a.applyRootMotion=false; a.cullingMode=AnimatorCullingMode.AlwaysAnimate; a.Rebind(); a.Update(0); }
            var blade=rig.GetComponentInChildren<MeleeHitBox>(true).transform;
            Quaternion handToBlade=Quaternion.Inverse(animator.GetBoneTransform(HumanBodyBones.RightHand).rotation)*blade.rotation;
            try
            {
                using var handler=new HumanPoseHandler(animator.avatar,animator.transform);
                return Motions.Where(m=>(!guardOnly || m.Guard || m.Shield) && (motionName==null || m.Name==motionName))
                    .Select(m=>BakeMotion(m,animator,sourceAnimator,handToBlade,handler)).ToArray();
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(raw); }
        }

        private static AnimationClip BakeMotion(Motion spec,Animator animator,Animator raw,Quaternion handToBlade,HumanPoseHandler handler)
        {
            string path=Folder+"Source/"+(spec.Take.Contains('/') ? spec.Take : "Mixamo/"+spec.Take)+".fbx";
            var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
            if(spec.TwoHands && !spec.Guard && !(spec.Axe && spec.Loop)) return CopySourceMotion(spec,source);
            var result=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+spec.Name+".anim");
            if(result==null) { result=new AnimationClip(); AssetDatabase.CreateAsset(result,Folder+spec.Name+".anim"); }
            // Keep the Humanoid type, but remove root curves: locomotion owns translation.
            EditorUtility.CopySerialized(source,result); result.name="Weapon_"+spec.Name;
            foreach(var binding in AnimationUtility.GetCurveBindings(result)) AnimationUtility.SetEditorCurve(result,binding,null);
            var graph=PlayableGraph.Create("Retarget Mixamo weapon motion"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable=AnimationClipPlayable.Create(graph,source);
            var full=AnimationClipPlayable.Create(graph,source);
            var idle=AnimationClipPlayable.Create(graph,AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim"));
            var readySource=AssetDatabase.LoadAllAssetsAtPath(Folder+"Source/Mixamo/Great Sword Idle.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
            var ready=AnimationClipPlayable.Create(graph,readySource);
            var mixer=AnimationLayerMixerPlayable.Create(graph,3);
            graph.Connect(idle,0,mixer,0); graph.Connect(playable,0,mixer,1);
            graph.Connect(ready,0,mixer,2);
            mixer.SetInputWeight(0,1); mixer.SetInputWeight(1,1);
            mixer.SetLayerMaskFromAvatarMask(1,AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/Player/Anim/idle_Attack.mask"));
            mixer.SetLayerMaskFromAvatarMask(2,AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/Player/Anim/idle_Attack.mask"));
            AnimationPlayableOutput.Create(graph,"Body",animator).SetSourcePlayable(mixer);
            AnimationPlayableOutput.Create(graph,"Source torso",raw).SetSourcePlayable(full); graph.Play();
            var curves=Enumerable.Range(0,HumanTrait.MuscleCount).Select(_=>new AnimationCurve()).ToArray();
            try
            {
                for(int frame=0;frame<=120;frame++)
                {
                    float phase=frame/120f;
                    // The long idle includes looking around and releasing the grip.
                    // Loop its opening settled stance instead of bringing those gestures into combat.
                    float sourcePhase=spec.Loop && !spec.Shield ? .02f*Mathf.Pow(Mathf.Sin(phase*Mathf.PI),2) : Mathf.Lerp(spec.From,spec.To,phase);
                    double time=source.length*sourcePhase;
                    // Blend the complete upper body back to the shared ready pose,
                    // including head/torso. Blending just the hands leaves the source's
                    // next combo wind-up on the neck when gameplay returns to idle.
                    float action=spec.Loop ? 0 : Mathf.SmoothStep(0,1,phase/.12f)*Mathf.SmoothStep(0,1,(1-phase)/.18f);
                    mixer.SetInputWeight(2,spec.TwoHands ? 1-action : 0);
                    ready.SetTime(0);
                    playable.SetTime(time); full.SetTime(time); graph.Evaluate(0);
                    var pose=new HumanPose(); handler.GetHumanPose(ref pose);
                    CloseHands(ref pose,spec.TwoHands || spec.Shield || spec.Name=="Thrust"); handler.SetHumanPose(ref pose);
                    // Upper-body masking removes hip rotation. Transfer that turn to the
                    // spine without importing the source's forward root displacement.
                    if (!spec.TwoHands)
                        animator.GetBoneTransform(HumanBodyBones.Spine).rotation=raw.GetBoneTransform(HumanBodyBones.Spine).rotation;
                    // A full-body source may pivot its hips through a complete turn.
                    // Our legs remain in the locomotion layer; do not bake that entire
                    // turn or deep bend into a single spine joint.
                    handler.GetHumanPose(ref pose);
                    for(int j=0;j<HumanTrait.MuscleCount;j++)
                    {
                        string muscle=HumanTrait.MuscleName[j];
                        if(!(muscle.StartsWith("Spine ") || muscle.StartsWith("Chest ") || muscle.StartsWith("UpperChest "))) continue;
                        float limit=muscle.Contains("Twist") ? .6f : muscle.Contains("Front-Back") ? .4f : .3f;
                        pose.muscles[j]=Mathf.Clamp(pose.muscles[j],-limit,limit);
                    }
                    handler.SetHumanPose(ref pose);
                    var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    Vector3 shoulders=(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position+
                        animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position)*.5f;
                    if(spec.Shield)
                    {
                        float raised=spec.Loop ? 1 : spec.Name=="ShieldEnter" ? Mathf.SmoothStep(0,1,phase) : 1-Mathf.SmoothStep(0,1,phase);
                        Vector3 target=shoulders+Vector3.Lerp(new Vector3(-.28f,-.40f,.18f),new Vector3(-.10f,-.14f,.42f),raised);
                        Quaternion rotation=Quaternion.LookRotation(Vector3.right,Vector3.up)*Quaternion.Inverse(Frame(animator,true))*left.rotation;
                        FitHand(animator,true,target,rotation,shoulders+new Vector3(-.38f,-.27f,.12f));
                    }
                    else if (spec.TwoHands)
                    {
                        Vector3[] grip=PoseTwoHands(spec,animator,shoulders,phase);
                        FitStaticGrip(animator,grip,spec.Axe ? .46f : .16f,spec.Axe);
                    }
                    else
                    {
                        // Trim the thrust before the following slash; keep its tip on aim.
                        Vector3 center=WeaponLoadout.GripCenter(animator,false);
                        center.x*=.65f;
                        FitHand(animator,false,center,Quaternion.LookRotation(Vector3.forward,Vector3.left)*Quaternion.Inverse(handToBlade));
                        WeaponLoadout.FitThrustSupportGrip(animator,animator.GetComponentInChildren<MeleeHitBox>(true).transform,1f);
                    }
                    handler.GetHumanPose(ref pose);
                    for(int j=0;j<curves.Length;j++)
                    {
                        float value=pose.muscles[j];
                        // HumanPose represents arm twist in [-180,180] degrees, but
                        // Animator interpolates these muscle curves as ordinary floats.
                        // Unwrap at the boundary instead of sweeping through a full turn.
                        string muscle=HumanTrait.MuscleName[j];
                        if(spec.Name=="Thrust" && (muscle.StartsWith("Left Shoulder ") || muscle.StartsWith("Left Arm ") ||
                            muscle.StartsWith("Left Forearm ") || muscle.StartsWith("Left Hand ")))
                            value=Mathf.Clamp(value,-.95f,.95f);
                        if(spec.TwoHands && (muscle.Contains(" Arm Twist") || muscle.Contains(" Forearm Twist")))
                        {
                            float degrees=HumanTrait.GetMuscleDefaultMax(j);
                            if(frame>0 && degrees>0)
                            {
                                float previous=curves[j][curves[j].length-1].value;
                                value=previous+Mathf.DeltaAngle(previous*degrees,value*degrees)/degrees;
                            }
                        }
                        curves[j].AddKey(spec.Seconds*phase,value);
                    }
                }
                for(int j=0;j<curves.Length;j++) AnimationUtility.SetEditorCurve(result,
                    EditorCurveBinding.FloatCurve("",typeof(Animator),MuscleProperty(HumanTrait.MuscleName[j])),Reduce(curves[j]));
                // Static holds use the standing body's root pose. A full-body layer
                // with muscle curves alone otherwise places the hips at ground level.
                var standing=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim");
                foreach(var binding in AnimationUtility.GetCurveBindings(standing))
                    if(binding.propertyName.StartsWith("RootT.") || binding.propertyName.StartsWith("RootQ."))
                    {
                        float value=AnimationUtility.GetEditorCurve(standing,binding).Evaluate(0);
                        AnimationUtility.SetEditorCurve(result,binding,AnimationCurve.Constant(0,spec.Seconds,value));
                    }
                var settings=AnimationUtility.GetAnimationClipSettings(result); settings.startTime=0; settings.stopTime=spec.Seconds;
                settings.loopTime=spec.Loop; settings.loopBlend=spec.Loop;
                settings.loopBlendOrientation=true; settings.loopBlendPositionXZ=true;
                AnimationUtility.SetAnimationClipSettings(result,settings);
                AnimationUtility.SetAnimationEvents(result,spec.Loop || spec.Shield ? Array.Empty<AnimationEvent>() : new[] {
                    new AnimationEvent {time=0,functionName="DisableHitBox"},
                    new AnimationEvent {time=spec.Seconds*spec.Open,functionName="EnableHitBox"},
                    new AnimationEvent {time=spec.Seconds*spec.Close,functionName="DisableHitBox"} });
                result.EnsureQuaternionContinuity(); EditorUtility.SetDirty(result); return result;
            }
            finally { graph.Destroy(); }
        }

        private static AnimationClip CopySourceMotion(Motion spec,AnimationClip source)
        {
            var result=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+spec.Name+".anim");
            if(result==null) { result=new AnimationClip(); AssetDatabase.CreateAsset(result,Folder+spec.Name+".anim"); }
            EditorUtility.CopySerialized(source,result); result.name="Weapon_"+spec.Name;
            // Retain the imported Humanoid curves directly. Do not round-trip the
            // arms through HumanPose or replace the attack with a generated IK path.
            foreach(var binding in AnimationUtility.GetCurveBindings(result))
            {
                var original=AnimationUtility.GetEditorCurve(source,binding);
                var curve=new AnimationCurve();
                for(int i=0;i<=120;i++)
                {
                    float phase=i/120f;
                    float time=source.length*(spec.Loop ? .02f*Mathf.Pow(Mathf.Sin(phase*Mathf.PI),2) : Mathf.Lerp(spec.From,spec.To,phase));
                    // Keep the root's authored turn and vertical weight shift,
                    // but never bake forward travel into a network-controlled body.
                    bool horizontalTravel=binding.type==typeof(Animator) && (binding.propertyName=="RootT.x" || binding.propertyName=="RootT.z");
                    curve.AddKey(spec.Seconds*phase,original.Evaluate(horizontalTravel ? 0 : time));
                }
                AnimationUtility.SetEditorCurve(result,binding,Reduce(curve));
            }
            var settings=AnimationUtility.GetAnimationClipSettings(result);
            settings.startTime=0; settings.stopTime=spec.Seconds; settings.loopTime=spec.Loop; settings.loopBlend=spec.Loop;
            settings.loopBlendOrientation=true; settings.loopBlendPositionXZ=true;
            AnimationUtility.SetAnimationClipSettings(result,settings);
            AnimationUtility.SetAnimationEvents(result,spec.Loop ? Array.Empty<AnimationEvent>() : new[]{
                new AnimationEvent{time=0,functionName="DisableHitBox"},
                new AnimationEvent{time=spec.Seconds*spec.Open,functionName="EnableHitBox"},
                new AnimationEvent{time=spec.Seconds*spec.Close,functionName="DisableHitBox"}});
            result.EnsureQuaternionContinuity(); EditorUtility.SetDirty(result); return result;
        }

        private static void FitStaticGrip(Animator rig,Vector3[] pose,float spacing,bool axe)
        {
            foreach(bool left in new[]{false,true})
            {
                var hand=rig.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                Vector3 target=pose[0]+pose[1]*(left ? -.5f : .5f)*spacing;
                Vector3 hint=pose[left?3:2];
                Vector3 fingers=Vector3.ProjectOnPlane(axe ? (left ? Vector3.right : Vector3.left) : target-hint,pose[1]).normalized;
                Quaternion rotation=Quaternion.LookRotation(fingers,pose[1])*Quaternion.Inverse(Frame(rig,left))*hand.rotation;
                FitHand(rig,left,target,rotation,hint);
            }
        }

        // Dedicated static hold poses only. Attacks always retain the imported curves.
        private static Vector3[] PoseTwoHands(Motion spec, Animator rig, Vector3 shoulders, float phase)
        {
            if(spec.Guard) return new[]{shoulders+new Vector3(-.28f,.04f,.36f), new Vector3(1,.015f,0).normalized,
                shoulders+new Vector3(.13f,-.22f,.26f), shoulders+new Vector3(-.42f,-.19f,.14f)};
            return new[]{shoulders+new Vector3(.025f,-.32f,.42f), new Vector3(.22f,-.28f,.93f).normalized,
                shoulders+new Vector3(.36f,-.34f,.18f), shoulders+new Vector3(-.32f,-.32f,.15f)};
        }
        private static void CloseHands(ref HumanPose pose,bool left)
        {
            for(int j=0;j<HumanTrait.MuscleCount;j++)
            {
                string name=HumanTrait.MuscleName[j];
                if(!(name.StartsWith("Right ") || left && name.StartsWith("Left "))) continue;
                if(name.EndsWith("Stretched")) pose.muscles[j]=name.Contains("Thumb") ? -.3f : -.9f;
                else if(name.EndsWith("Spread") && name.Contains("Thumb")) pose.muscles[j]=-.2f;
            }
        }
        private static string MuscleProperty(string name)
        {
            foreach(string side in new[]{"Left","Right"}) foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                name=name.Replace(side+" "+finger+" ",side+"Hand."+finger+".");
            return name;
        }
        private static AnimationCurve Reduce(AnimationCurve curve)
        {
            var keys=curve.keys; var keep=new bool[keys.Length]; keep[0]=keep[^1]=true;
            void Segment(int first,int last)
            {
                float worst=.0005f; int split=-1;
                for(int k=first+1;k<last;k++)
                {
                    float value=Mathf.Lerp(keys[first].value,keys[last].value,Mathf.InverseLerp(keys[first].time,keys[last].time,keys[k].time));
                    float error=Mathf.Abs(keys[k].value-value);
                    if(error>worst) { worst=error; split=k; }
                }
                if(split<0) return;
                keep[split]=true; Segment(first,split); Segment(split,last);
            }
            Segment(0,keys.Length-1);
            var reduced=new AnimationCurve(keys.Where((_,i)=>keep[i]).ToArray());
            for(int k=0;k<reduced.length;k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(reduced,k,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(reduced,k,AnimationUtility.TangentMode.Linear);
            }
            return reduced;
        }
        private static Quaternion Frame(Animator animator,bool left) => Quaternion.LookRotation(
            animator.GetBoneTransform(left?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal).position-animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand).position,
            animator.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal).position-animator.GetBoneTransform(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal).position);
        private static void FitHand(Animator animator,bool left,Vector3 palm,Quaternion rotation,Vector3? elbowHint = null)
        {
            var arm=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            var elbow=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            Vector3 hint=elbowHint ?? elbow.position+Vector3.right*(left?-.12f:.12f);
            hand.rotation=rotation;
            WeaponLoadout.SolveArm(arm,elbow,hand,palm-(WeaponLoadout.GripCenter(animator,left)-hand.position),hint);
            hand.rotation=rotation;
        }
    }
}
