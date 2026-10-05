using System;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;
using System.Collections.Generic;
using BattlePvp.Combat;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    public static class SkillPropInstaller
    {
        const string Folder="Assets/Remodel/Skills/";
        public static void Apply()
        {
            var metal=Material("ImportedSkillSteel",new Color(.42f,.50f,.59f),.8f);
            Replace("Knife","Source/Quaternius/Knife_1.fbx",.38f,metal,true);
            Replace("Hook","Source/Azureguy/GrapplingHook.obj",.5f,metal,false);
            ApplyTrapUpgrade();
        }
        public static void ApplyTrapUpgrade()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before importing the trap.");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Source/Quaternius/BearTrap_Open.fbx");
            if(source==null) throw new InvalidOperationException("Missing CC0 BearTrap_Open.fbx");
            var steel=Material("TrapSteel",new Color(.17f,.23f,.29f),.75f);
            var trim=Material("TrapCyan",new Color(.08f,.56f,.64f),.65f);
            trim.EnableKeyword("_EMISSION"); trim.SetColor("_EmissionColor",new Color(.02f,.24f,.3f));
            var root=new GameObject("Trap");
            try
            {
                var model=UnityEngine.Object.Instantiate(source,root.transform); model.name="Quaternius Bear Trap";
                var bounds=BoundsOf(model); model.transform.localScale*=.85f/Mathf.Max(bounds.size.x,bounds.size.z);
                bounds=BoundsOf(model); model.transform.position-=new Vector3(bounds.center.x,bounds.min.y-.015f,bounds.center.z);
                foreach(var r in model.GetComponentsInChildren<Renderer>())
                {
                    var materials=r.sharedMaterials;
                    for(int i=0;i<materials.Length;i++) materials[i]=i==0 ? steel : trim;
                    r.sharedMaterials=materials;
                }
                foreach(var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                BuildTrapJaws(root,model,steel);
                PrefabUtility.SaveAsPrefabAsset(root,Folder+"Trap.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Anim/Player.controller");
            var state=controller.layers.First(l=>l.name=="ExpandedSkills").stateMachine.states.First(s=>s.state.name=="Skill_SHARED_Trap").state;
            state.speed=state.motion.averageDuration/ExpandedSkillController.Value(JobSkillKind.Trap,"CastSeconds",1.1f);
            EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(trim);
            AssetDatabase.SaveAssets();
        }
        static void BuildTrapJaws(GameObject root,GameObject model,Material steel)
        {
            var filter=model.GetComponentInChildren<MeshFilter>();
            var source=filter.sharedMesh;
            var vertices=source.vertices.Select(v=>root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            var triangles=source.triangles;
            // The licensed open model is one mesh with disconnected teeth, jaws and base.
            // Weld positions only for identifying components; retain its original UV/edge vertices.
            var welded=new Dictionary<Vector3Int,int>(); var parent=new int[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                var v=vertices[i]*100000; var key=new Vector3Int(Mathf.RoundToInt(v.x),Mathf.RoundToInt(v.y),Mathf.RoundToInt(v.z));
                parent[i]=welded.TryGetValue(key,out int existing) ? existing : i; welded[key]=parent[i];
            }
            int Find(int index) { while(parent[index]!=index) index=parent[index]; return index; }
            for(int i=0;i<triangles.Length;i+=3)
            { int a=Find(triangles[i]); for(int j=1;j<3;j++) { int b=Find(triangles[i+j]); if(a!=b) parent[b]=a; } }
            var bounds=new Dictionary<int,Bounds>();
            for(int i=0;i<vertices.Length;i++)
            {
                int part=Find(i); if(!bounds.TryGetValue(part,out var b)) b=new Bounds(vertices[i],Vector3.zero);
                b.Encapsulate(vertices[i]); bounds[part]=b;
            }
            var indices=new[]{new List<int>(),new List<int>(),new List<int>()};
            for(int i=0;i<triangles.Length;i+=3)
            {
                var b=bounds[Find(triangles[i])];
                int part=b.max.x<-.001f ? 1 : b.min.x>.001f ? 2 : 0;
                for(int j=0;j<3;j++) indices[part].Add(triangles[i+j]);
            }
            var parts=new Transform[3];
            for(int part=0;part<3;part++)
            {
                string name=new[]{"TrapBase","TrapLeftJaw","TrapRightJaw"}[part];
                Vector3 pivot=part==0 ? Vector3.zero : new Vector3(part==1 ? -.012f : .012f,.075f,0);
                var mesh=new Mesh { name=name }; var remap=new Dictionary<int,int>(); var points=new List<Vector3>(); var uv=new List<Vector2>(); var output=new List<int>(); var sourceUv=source.uv;
                foreach(int index in indices[part])
                {
                    if(!remap.TryGetValue(index,out int mapped))
                    { mapped=points.Count; remap[index]=mapped; points.Add(vertices[index]-pivot); uv.Add(sourceUv.Length>index ? sourceUv[index] : Vector2.zero); }
                    output.Add(mapped);
                }
                mesh.SetVertices(points); mesh.SetUVs(0,uv); mesh.SetTriangles(output,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string path=Folder+name+".asset"; var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null) { AssetDatabase.CreateAsset(mesh,path); saved=mesh; }
                else { EditorUtility.CopySerialized(mesh,saved); UnityEngine.Object.DestroyImmediate(mesh); }
                var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); parts[part]=go.transform;
                go.transform.SetParent(root.transform,false); go.transform.localPosition=pivot;
                go.GetComponent<MeshFilter>().sharedMesh=saved; go.GetComponent<MeshRenderer>().sharedMaterial=steel;
            }
            UnityEngine.Object.DestroyImmediate(model);
            var visual=root.AddComponent<SkillTrapVisual>(); var serialized=new SerializedObject(visual);
            serialized.FindProperty("_leftJaw").objectReferenceValue=parts[1]; serialized.FindProperty("_rightJaw").objectReferenceValue=parts[2]; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static Material Material(string name,Color color,float metallic)
        {
            var path=Folder+name+".mat"; var result=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(result==null) { result=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result,path); }
            result.SetColor("_BaseColor",color); result.SetFloat("_Metallic",metallic); result.SetFloat("_Smoothness",.55f);
            // Thin prongs remain visible from either side after source-coordinate conversion.
            result.SetFloat("_Cull",0); EditorUtility.SetDirty(result); return result;
        }
        static void Replace(string name,string source,float length,Material material,bool normalize)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+source);
            if(asset==null) throw new InvalidOperationException("Missing licensed skill prop: "+source);
            var root=new GameObject(name);
            try
            {
                var model=UnityEngine.Object.Instantiate(asset,root.transform); model.name="Imported "+name;
                foreach(var r in model.GetComponentsInChildren<Renderer>())
                { var materials=r.sharedMaterials; for(int i=0;i<materials.Length;i++) materials[i]=material; r.sharedMaterials=materials; }
                var bounds=BoundsOf(model);
                if(normalize)
                {
                    var size=bounds.size;
                    var filter=model.GetComponentInChildren<MeshFilter>();
                    // Knife_1's mesh blade points along +Z. Compose with its FBX import
                    // rotation; replacing that rotation turns the blade sideways.
                    Vector3 axis=filter.transform.TransformDirection(Vector3.forward);
                    model.transform.rotation=Quaternion.FromToRotation(axis,Vector3.forward)*model.transform.rotation;
                    model.transform.localScale*=length/Mathf.Max(size.x,Mathf.Max(size.y,size.z));
                    var mesh=filter.sharedMesh;
                    // Original DarkWood submesh 0 is the handle, independent of its material replacement.
                    var indices=mesh.GetIndices(0); var vertices=mesh.vertices;
                    Bounds grip=new Bounds(vertices[indices[0]],Vector3.zero);
                    foreach(int index in indices) grip.Encapsulate(vertices[index]);
                    model.transform.position-=filter.transform.TransformPoint(grip.center);
                }
                else
                {
                    bounds=BoundsOf(model); model.transform.position-=bounds.center;
                    model.transform.localPosition+=Vector3.forward*.12f;
                }
                foreach(var collider in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                PrefabUtility.SaveAsPrefabAsset(root,Folder+name+".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static Bounds BoundsOf(GameObject value)
        {
            var renderers=value.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds; for(int i=1;i<renderers.Length;i++) bounds.Encapsulate(renderers[i].bounds); return bounds;
        }
    }
}
