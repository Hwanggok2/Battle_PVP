using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace BattlePvp.Remodel.Editor
{
    public static class SkillMotionInspection
    {
        public static void Import()
        {
            const string path="Assets/Remodel/Skills/Source/Quaternius/UAL2_Standard.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation=true;
            importer.SaveAndReimport();
            Directory.CreateDirectory("Reports/SkillMotions");
            File.WriteAllLines("Reports/SkillMotions/source-clips.txt",importer.defaultClipAnimations.Select(c=>$"{c.name}: {c.firstFrame} - {c.lastFrame}"));
        }
        public static void Sample()
        {
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            player.SetActive(true);
            var animator=player.GetComponent<Animator>(); animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion=false; animator.Rebind();
            var lines=new System.Collections.Generic.List<string>();
            foreach(var source in new[]{("UAL2_Standard.fbx","OverhandThrow"),("UAL1_Standard.fbx","Trap_Assembly"),("UAL1_Standard.fbx","Fixing_Kneeling")})
            {
                var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/Remodel/Skills/Source/Quaternius/"+source.Item1).OfType<AnimationClip>().FirstOrDefault(c=>c.name.EndsWith(source.Item2));
                if(clip==null) continue;
                var graph=PlayableGraph.Create(); var playable=AnimationClipPlayable.Create(graph,clip);
                var output=AnimationPlayableOutput.Create(graph,"Sample",animator); output.SetSourcePlayable(playable); graph.Play();
                for(int i=0;i<=20;i++)
                {
                    float t=clip.length*i/20f; playable.SetTime(t); graph.Evaluate(0);
                    Vector3 hand=player.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
                    Vector3 hip=player.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
                    lines.Add($"{source.Item2} t={t:F3} hand={hand.ToString("F3")} hip={hip.ToString("F3")}");
                }
                graph.Destroy();
            }
            File.WriteAllLines("Reports/SkillMotions/sampled-motion.txt",lines);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }
}
