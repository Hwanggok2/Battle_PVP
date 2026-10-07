using System;
using System.Linq;
using BattlePvp.Characters;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace BattlePvp.Remodel.Editor
{
    public static class OfficerProportionInstaller
    {
        private const string Folder="Assets/Characters/security-officer/Converted/";
        [MenuItem("Battle PvP/Characters/Adjust Officer Proportions")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            const string path="Assets/Resources/CharacterVisuals/security-officer.prefab";
            var rig=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var animator=rig.GetComponent<Animator>();
                // Always author from the imported skeleton and original converted mesh, never compound edits.
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/SecurityOfficer/Source/3D Model/Security Officer.fbx").GetComponent<Animator>();
                var description=source.avatar.humanDescription;
                var bones=rig.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
                foreach(var bone in description.skeleton)
                    if(bones.TryGetValue(bone.name,out var t) && t!=rig.transform) { t.localPosition=bone.position; t.localRotation=bone.rotation; t.localScale=bone.scale; }
                animator.avatar=source.avatar;
                float hips=rig.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position).y;
                Vector3 Warp(Vector3 v)
                {
                    float shoulder=Mathf.SmoothStep(0,1,(v.y-hips*.85f)/(hips*.55f));
                    v.x*=1f+.10f*shoulder;
                    v.y+=Mathf.Min(Mathf.Max(v.y,0),hips)*.08f;
                    return v;
                }
                var body=rig.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"OfficerProportions.asset");
                if(mesh==null) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,Folder+"OfficerProportions.asset"); }
                EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"NativeSkin.asset"),mesh);
                mesh.name="Officer - broader shoulders and longer legs";
                mesh.vertices=mesh.vertices.Select(v=>body.transform.InverseTransformPoint(rig.transform.TransformPoint(Warp(rig.transform.InverseTransformPoint(body.transform.TransformPoint(v)))))).ToArray();
                var transforms=rig.GetComponentsInChildren<Transform>(true);
                var positions=transforms.Select(t=>Warp(rig.transform.InverseTransformPoint(t.position))).ToArray();
                for(int i=0;i<transforms.Length;i++) if(transforms[i]!=rig.transform && transforms[i]!=body.transform) transforms[i].position=rig.transform.TransformPoint(positions[i]);
                mesh.bindposes=body.bones.Select(t=>t.worldToLocalMatrix*body.transform.localToWorldMatrix).ToArray();
                mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents(); body.sharedMesh=mesh; body.localBounds=mesh.bounds;
                var skeleton=description.skeleton;
                for(int i=0;i<skeleton.Length;i++)
                {
                    if(i==0) { skeleton[i].position=Vector3.zero; skeleton[i].rotation=Quaternion.identity; skeleton[i].scale=Vector3.one; }
                    else if(bones.TryGetValue(skeleton[i].name,out var t)) { skeleton[i].position=t.localPosition; skeleton[i].rotation=t.localRotation; skeleton[i].scale=t.localScale; }
                }
                description.skeleton=skeleton;
                string rootName=rig.name; rig.name=skeleton[0].name;
                animator.avatar=null;
                var avatar=AvatarBuilder.BuildHumanAvatar(rig,description);
                rig.name=rootName;
                if(!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Officer avatar validation failed.");
                var saved=AssetDatabase.LoadAssetAtPath<Avatar>(Folder+"OfficerProportionsAvatar.asset");
                if(saved==null) { saved=avatar; AssetDatabase.CreateAsset(saved,Folder+"OfficerProportionsAvatar.asset"); }
                else { EditorUtility.CopySerialized(avatar,saved); Object.DestroyImmediate(avatar); }
                animator.avatar=saved;
                var definition=AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Folder+"Character.asset");
                definition.RelativeHeight=.95f*1.05f;
                EditorUtility.SetDirty(mesh); EditorUtility.SetDirty(saved); EditorUtility.SetDirty(definition);
                PrefabUtility.SaveAsPrefabAsset(rig,path); AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(rig); }
        }
    }
}
