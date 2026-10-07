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
    // The KayKit body motion is retained. Bake both hand contacts for our full-height rig
    // instead of transplanting a one-handed swing and pulling the other hand after it.
    public static class WeaponMotionRetargeter
    {
        private const string Folder = "Assets/Remodel/Weapons/";
        private const string Source = Folder + "Source/KayKit/CombatMelee.fbx";
        private const string Reference = Folder + "Source/KayKit/CombatMeleeReference.fbx";
        private sealed class Motion
        {
            public string Name, Take;
            public float Seconds, Open, Close;
            public bool TwoHands, Axe, Loop, Guard;
            public Motion(string name, string take, float seconds, float open, float close, bool twoHands = true)
            { Name=name; Take=take; Seconds=seconds; Open=open; Close=close; TwoHands=twoHands; }
        }
        private static readonly Motion[] Motions = {
            new("Thrust", "Melee_1H_Attack_Stab", .72f, .20f, .44f, false),
            new("Greatsword1", "Melee_2H_Attack_Slice", .85f, .30f, .62f),
            new("Greatsword2", "Melee_2H_Attack_Stab", .80f, .20f, .44f),
            new("Greatsword3", "Melee_2H_Attack_Chop", 1f, .40f, .64f),
            new("Riposte1", "Melee_2H_Attack_Slice", .36f, .30f, .62f),
            new("Riposte2", "Melee_2H_Attack_Stab", .36f, .20f, .44f),
            new("AxeChop", "Melee_2H_Attack_Chop", 1.05f, .40f, .64f) { Axe=true },
            new("GreatswordReady", "Melee_2H_Idle", 1.2f, 0, 0) { Loop=true },
            new("AxeReady", "Melee_2H_Idle", 1.2f, 0, 0) { Loop=true, Axe=true },
            new("SwordGuard", "Melee_2H_Idle", 1.2f, 0, 0) { Loop=true, Guard=true }
        };

        public static AnimationClip[] Bake()
        {
            var rig=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            var raw=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Reference));
            rig.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            raw.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var animator=rig.GetComponent<Animator>(); animator.fireEvents=false; animator.applyRootMotion=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
            var blade=rig.GetComponentInChildren<MeleeHitBox>(true).transform;
            var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
            Quaternion handToBlade=Quaternion.Inverse(right.rotation)*blade.rotation;
            var rawGrip=raw.GetComponentsInChildren<Transform>().First(t=>t.name=="handslot.r");
            var clips=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            var references=AssetDatabase.LoadAllAssetsAtPath(Reference).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            try
            {
                using var poseHandler=new HumanPoseHandler(animator.avatar,animator.transform);
                return Motions.Select(m=>BakeMotion(m,rig,raw,animator,rawGrip,handToBlade,poseHandler,
                    clips.First(c=>c.name==m.Take),references.First(c=>c.name==m.Take))).ToArray();
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(raw); }
        }

        private static AnimationClip BakeMotion(Motion spec,GameObject rig,GameObject raw,Animator animator,Transform rawGrip,
            Quaternion handToBlade,HumanPoseHandler poseHandler,AnimationClip source,AnimationClip reference)
        {
            var result=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+spec.Name+".anim");
            if(result==null) { result=new AnimationClip(); AssetDatabase.CreateAsset(result,Folder+spec.Name+".anim"); }
            EditorUtility.CopySerialized(source,result); result.name="Weapon_"+spec.Name;
            var graph=PlayableGraph.Create("Retarget weapon motion"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable=AnimationClipPlayable.Create(graph,source);
            var idle=AnimationClipPlayable.Create(graph,AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Anim/Move/Idle.anim"));
            var mixer=AnimationLayerMixerPlayable.Create(graph,2);
            graph.Connect(idle,0,mixer,0); graph.Connect(playable,0,mixer,1);
            mixer.SetInputWeight(0,1); mixer.SetInputWeight(1,1);
            mixer.SetLayerMaskFromAvatarMask(1,AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/Player/Anim/idle_Attack.mask"));
            var output=AnimationPlayableOutput.Create(graph,"Body",animator); output.SetSourcePlayable(mixer); graph.Play();
            var curves=Enumerable.Range(0,HumanTrait.MuscleCount).Select(_=>new AnimationCurve()).ToArray();
            var rawHips=raw.GetComponentsInChildren<Transform>().First(t=>t.name=="hips");
            reference.SampleAnimation(raw,0);
            Quaternion neutralHips=rawHips.rotation;
            playable.SetTime(0); graph.Evaluate(0);
            Vector3 shoulders=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position-
                animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            Quaternion frontStance=spec.TwoHands
                ? Quaternion.FromToRotation(Vector3.ProjectOnPlane(shoulders,Vector3.up),Vector3.right)
                : Quaternion.identity;
            try
            {
                for(int frame=0;frame<=120;frame++)
                {
                    float phase=frame/120f;
                    playable.SetTime(source.length*phase); graph.Evaluate(0);
                    reference.SampleAnimation(raw,reference.length*phase);
                    // Locomotion owns the root. Preserve the source's wind-up and lunge
                    // rotation on the spine so the upper-body mask does not erase them.
                    var spine=animator.GetBoneTransform(HumanBodyBones.Spine);
                    spine.rotation=(rawHips.rotation*Quaternion.Inverse(neutralHips))*frontStance*spine.rotation;
                    var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    Vector3 rightPalm=Palm(animator,false), leftPalm=Palm(animator,true);
                    Vector3 direction=rawGrip.up;
                    bool chop=spec.Take.EndsWith("Chop"), stab=spec.Take.EndsWith("Stab");
                    Vector3 center=(rightPalm+leftPalm)*.5f;
                    if(spec.TwoHands)
                    {
                        center+=new Vector3(0,.04f,.15f);
                        if(chop) { center.x*=.2f; center.y+=Mathf.Max(0,center.y-1.22f)*1.3f; direction.x=0; }
                        // Blend into the shared front guard at either end; preserve the source's strike arc.
                        float action=spec.Loop ? 0 : Mathf.SmoothStep(0,1,phase/.12f)*Mathf.SmoothStep(0,1,(1-phase)/.2f);
                        Vector3 readyCenter=(animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position+
                            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position)*.5f+new Vector3(0,-.19f,.36f);
                        center=Vector3.Lerp(readyCenter,center,action);
                        direction=Vector3.Slerp(new Vector3(0,.65f,.76f),stab ? Vector3.forward : direction.normalized,action);
                        if(spec.Guard) { center.y+=.18f; direction=new Vector3(.8f,.35f,.45f); }
                    }
                    else
                    {
                        direction=Vector3.forward;
                        center=rightPalm+Vector3.up*.08f;
                        center.x*=.65f;
                    }
                    Quaternion weaponRotation=Quaternion.LookRotation(direction,chop || !spec.TwoHands ? Vector3.left : Vector3.up);
                    Quaternion rightRotation=weaponRotation*Quaternion.Inverse(handToBlade);
                    float separation=spec.Axe ? .30f : .16f;
                    Vector3 rightTarget=spec.TwoHands ? center+direction.normalized*separation*.5f : center;
                    FitHand(animator,false,rightTarget,rightRotation);
                    if(spec.TwoHands)
                    {
                        var rightFrame=Frame(animator,false);
                        var leftFrame=Frame(animator,true);
                        Quaternion leftRotation=Quaternion.LookRotation(-(rightFrame*Vector3.forward),rightFrame*Vector3.up)*Quaternion.Inverse(leftFrame)*left.rotation;
                        FitHand(animator,true,center-direction.normalized*separation*.5f,leftRotation);
                    }
                    var pose=new HumanPose(); poseHandler.GetHumanPose(ref pose);
                    for(int j=0;j<curves.Length;j++) curves[j].AddKey(source.length*phase,pose.muscles[j]);
                }
                for(int j=0;j<curves.Length;j++) AnimationUtility.SetEditorCurve(result,
                    EditorCurveBinding.FloatCurve("",typeof(Animator),MuscleProperty(HumanTrait.MuscleName[j])),Reduce(curves[j]));
                WeaponInstaller.Retime(result,spec.Seconds);
                WeaponInstaller.CloseFingers(result,spec.TwoHands);
                var settings=AnimationUtility.GetAnimationClipSettings(result); settings.loopTime=spec.Loop;
                settings.loopBlend=spec.Loop; settings.loopBlendOrientation=true; settings.loopBlendPositionXZ=true;
                AnimationUtility.SetAnimationClipSettings(result,settings);
                AnimationUtility.SetAnimationEvents(result,spec.Loop ? Array.Empty<AnimationEvent>() : new[] {
                    new AnimationEvent {time=0,functionName="DisableHitBox"},
                    new AnimationEvent {time=spec.Seconds*spec.Open,functionName="EnableHitBox"},
                    new AnimationEvent {time=spec.Seconds*spec.Close,functionName="DisableHitBox"} });
                result.EnsureQuaternionContinuity(); EditorUtility.SetDirty(result); return result;
            }
            finally { graph.Destroy(); }
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
        private static Vector3 Palm(Animator animator,bool left) => Vector3.Lerp(
            animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand).position,
            animator.GetBoneTransform(left?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal).position,.65f);
        private static Quaternion Frame(Animator animator,bool left) => Quaternion.LookRotation(
            animator.GetBoneTransform(left?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal).position-animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand).position,
            animator.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal).position-animator.GetBoneTransform(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal).position);
        private static void FitHand(Animator animator,bool left,Vector3 palm,Quaternion rotation)
        {
            var arm=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            var elbow=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            Vector3 hint=elbow.position+Vector3.right*(left?-.12f:.12f);
            hand.rotation=rotation;
            WeaponLoadout.SolveArm(arm,elbow,hand,palm-(Palm(animator,left)-hand.position),hint);
            hand.rotation=rotation;
        }
    }
}
