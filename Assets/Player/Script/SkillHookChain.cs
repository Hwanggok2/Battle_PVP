using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace BattlePvp.Combat
{
    /// <summary>Alternating metal links in one dynamic mesh, with no per-link objects or colliders.</summary>
    public sealed class SkillHookChain : IDisposable
    {
        private const int MaxLinks=80, Segments=10, Sides=4, PerLink=Segments*Sides;
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly Vector3[] _ring=new Vector3[PerLink], _vertices=new Vector3[MaxLinks*PerLink];
        private readonly Vector3[] _ringNormals=new Vector3[PerLink], _normals=new Vector3[MaxLinks*PerLink];
        private readonly Vector3[] _orientedRing=new Vector3[PerLink*2], _orientedNormals=new Vector3[PerLink*2];
        private readonly int[] _indices=new int[MaxLinks*PerLink*6];
        private int _linkCount;
        private Vector3 _lastStart, _lastEnd;
        private bool _hasPose;
        private static readonly ProfilerMarker UpdateMarker = new("BattlePvp.HookChain");
        public SkillHookChain(Material source, UnityEngine.SceneManagement.Scene scene = default)
        {
            _root=new GameObject("Hook chain",typeof(MeshFilter),typeof(MeshRenderer));
            if(scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root,scene);
            _material=source!=null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _material.SetColor("_BaseColor",new Color(.48f,.56f,.66f));
            var renderer=_root.GetComponent<MeshRenderer>(); renderer.sharedMaterial=_material; renderer.shadowCastingMode=ShadowCastingMode.Off;
            _mesh=new Mesh { name="Hook chain links" }; _mesh.MarkDynamic(); _root.GetComponent<MeshFilter>().sharedMesh=_mesh;
            for(int a=0;a<Segments;a++) for(int b=0;b<Sides;b++)
            {
                float theta=a*Mathf.PI*2/Segments, phi=b*Mathf.PI*2/Sides;
                _ring[a*Sides+b]=new Vector3(Mathf.Cos(theta)*(.064f+.006f*Mathf.Cos(phi)),Mathf.Sin(theta)*(.032f+.006f*Mathf.Cos(phi)),.006f*Mathf.Sin(phi));
                for(int link=0;link<MaxLinks;link++)
                {
                    int start=link*PerLink, index=(start+a*Sides+b)*6;
                    int v0=start+a*Sides+b, v1=start+((a+1)%Segments)*Sides+b, v2=start+((a+1)%Segments)*Sides+(b+1)%Sides, v3=start+a*Sides+(b+1)%Sides;
                    _indices[index]=v0; _indices[index+1]=v1; _indices[index+2]=v2; _indices[index+3]=v0; _indices[index+4]=v2; _indices[index+5]=v3;
                }
            }
            // The link topology never changes. Preserve its original smooth normals once.
            for(int i=0;i<PerLink*6;i+=3)
            {
                int a=_indices[i], b=_indices[i+1], c=_indices[i+2];
                Vector3 normal=Vector3.Cross(_ring[b]-_ring[a],_ring[c]-_ring[a]);
                _ringNormals[a]+=normal; _ringNormals[b]+=normal; _ringNormals[c]+=normal;
            }
            for(int i=0;i<PerLink;i++) _ringNormals[i]=_ringNormals[i].normalized;
        }
        public void Update(Vector3 start,Vector3 end)
        {
            using var sample=UpdateMarker.Auto();
            if(_hasPose && start==_lastStart && end==_lastEnd) return;
            _hasPose=true; _lastStart=start; _lastEnd=end;
            Vector3 delta=end-start; float length=delta.magnitude;
            _root.SetActive(length>.04f); if(length<=.04f) return;
            Vector3 axis=delta/length, side=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 up=Vector3.Cross(side,axis);
            int count=Mathf.Clamp(Mathf.CeilToInt(length/.09f),1,MaxLinks);
            // All links share one of two orientations. Transform each ring once, then translate it.
            for(int parity=0;parity<2;parity++)
            {
                Vector3 ringUp=parity==0 ? up : side, normal=Vector3.Cross(axis,ringUp);
                for(int i=0;i<PerLink;i++)
                {
                    Vector3 v=_ring[i], n=_ringNormals[i];
                    _orientedRing[parity*PerLink+i]=axis*v.x+ringUp*v.y+normal*v.z;
                    _orientedNormals[parity*PerLink+i]=axis*n.x+ringUp*n.y+normal*n.z;
                }
            }
            for(int link=0;link<count;link++)
            {
                float t=(link+.5f)/count;
                Vector3 center=Vector3.Lerp(start,end,t)+Vector3.down*(Mathf.Sin(t*Mathf.PI)*Mathf.Min(.08f,length*.02f));
                int ring=(link%2)*PerLink;
                for(int i=0;i<PerLink;i++) _vertices[link*PerLink+i]=center+_orientedRing[ring+i];
                Array.Copy(_orientedNormals,ring,_normals,link*PerLink,PerLink);
            }
            if(count!=_linkCount) _mesh.Clear();
            _mesh.SetVertices(_vertices,0,count*PerLink,MeshUpdateFlags.DontRecalculateBounds);
            _mesh.SetNormals(_normals,0,count*PerLink,MeshUpdateFlags.DontRecalculateBounds);
            if(count!=_linkCount) _mesh.SetTriangles(_indices,0,count*PerLink*6,0,false);
            _linkCount=count;
            var bounds=new Bounds((start+end)*.5f,Vector3.zero); bounds.Encapsulate(start); bounds.Encapsulate(end);
            bounds.Expand(.14f+2*Mathf.Min(.08f,length*.02f)); _mesh.bounds=bounds;
        }
        public void Hide() { _root.SetActive(false); _hasPose=false; }
        public void Dispose() { Destroy(_root); Destroy(_mesh); Destroy(_material); }
        private static void Destroy(UnityEngine.Object value)
        { if(value==null) return; if(Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
