using BattlePvp.Combat;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BattlePvp.UI
{
    // A UI mesh, so the octagon and its stencil mask stay crisp at any resolution.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OctagonGraphic : MaskableGraphic
    {
        public float Border;
        private static readonly Vector2[] Corners={new(.28f,0),new(.72f,0),new(1,.28f),new(1,.72f),new(.72f,1),new(.28f,1),new(0,.72f),new(0,.28f)};
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect=GetPixelAdjustedRect();
            if(Border<=0)
            {
                mesh.AddVert(rect.center,color,new Vector2(.5f,.5f));
                foreach(var p in Corners) mesh.AddVert(rect.min+Vector2.Scale(p,rect.size),color,p);
                for(int i=0;i<8;i++) mesh.AddTriangle(0,i+1,(i+1)%8+1);
                return;
            }
            float border=Mathf.Min(Border,Mathf.Min(rect.width,rect.height)*.45f);
            foreach(var p in Corners)
            {
                mesh.AddVert(rect.min+Vector2.Scale(p,rect.size),color,p);
                mesh.AddVert(rect.min+Vector2.one*border+Vector2.Scale(p,rect.size-Vector2.one*border*2),color,p);
            }
            for(int i=0;i<8;i++) {int j=(i+1)%8; mesh.AddTriangle(i*2,j*2,i*2+1);mesh.AddTriangle(j*2,j*2+1,i*2+1);}
        }
    }

}
