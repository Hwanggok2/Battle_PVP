using System;
using UnityEngine;
namespace BattlePvp.Combat
{
    public static class SkillTargeting
    {
        // At most eight players; grow on saturation so decorative colliders cannot hide a nearer wall.
        private static RaycastHit[] _hits = new RaycastHit[64];
        private static readonly Vector3[] TrapCorners = { new(-.32f,0,-.32f), new(-.32f,0,.32f), new(.32f,0,-.32f), new(.32f,0,.32f) };
        private static Collider[] _overlaps = new Collider[32];
        public static bool TrapPlacement(ExpandedSkillController owner, out Vector3 point)
        {
            Vector3 forward=owner.transform.forward; forward.y=0; forward.Normalize();
            float distance=ExpandedSkillController.Value(JobSkillKind.Trap,"PlaceDistance",1.2f);
            point=owner.transform.position+forward*distance;
            bool floorFound=Cast(owner,point+Vector3.up,Vector3.down,2,0,out var floor);
            if(floorFound) point=floor.point+Vector3.up*.03f;
            if(!floorFound || floor.normal.y<.7f || floor.collider.GetComponentInParent<IDamageReceiver>()!=null) return false;
            Vector3 origin=owner.transform.position+Vector3.up*.4f;
            Vector3 path=point+Vector3.up*.35f-origin;
            if(Cast(owner,origin,path.normalized,path.magnitude,.1f,out _)) return false;
            return TrapFootprint(owner,point);
        }
        public static bool TrapFootprint(ExpandedSkillController owner, Vector3 point)
        {
            if(!CombatValidation.IsFinite(point)) return false;
            foreach(var offset in TrapCorners)
            {
                if(!Cast(owner,point+offset+Vector3.up*.25f,Vector3.down,.6f,0,out var floor) || floor.normal.y<.7f ||
                    Mathf.Abs(floor.point.y-(point.y-.03f))>.15f || floor.collider.GetComponentInParent<IDamageReceiver>()!=null) return false;
            }
            int count;
            do
            {
                count=Physics.OverlapBoxNonAlloc(point+Vector3.up*.25f,new Vector3(.36f,.20f,.36f),_overlaps,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
                if(count<_overlaps.Length) break;
                Array.Resize(ref _overlaps,_overlaps.Length*2);
            } while(true);
            for(int i=0;i<count;i++) if(!_overlaps[i].transform.IsChildOf(owner.transform)) return false;
            return true;
        }
        public static bool Cast(ExpandedSkillController owner, Vector3 start, Vector3 direction, float distance, float radius, out RaycastHit nearest)
        {
            nearest=default; int count;
            do
            {
                count=radius>0 ? Physics.SphereCastNonAlloc(start,radius,direction,_hits,distance,~0,QueryTriggerInteraction.Collide)
                    : Physics.RaycastNonAlloc(start,direction,_hits,distance,~0,QueryTriggerInteraction.Collide);
                if(count<_hits.Length) break;
                Array.Resize(ref _hits,_hits.Length*2);
            } while(true);
            bool found=false; float best=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var hit=_hits[i]; if(hit.collider==null || hit.collider.transform.IsChildOf(owner.transform) || hit.distance>=best) continue;
                if(hit.collider.isTrigger && hit.collider.GetComponentInParent<IDamageReceiver>()==null) continue;
                best=hit.distance; nearest=hit; found=true;
            }
            return found;
        }
    }
    public sealed class SkillTrap : MonoBehaviour
    {
        private ExpandedSkillController _owner;
        public int Id { get; private set; }
        public double ExpiresAt { get; private set; }
        private static int _nextId;
        public static SkillTrap Create(ExpandedSkillController owner,Vector3 position,double expiry)
        {
            var go=new GameObject("Skill trap authority"); go.transform.position=position;
            var trap=go.AddComponent<SkillTrap>(); trap._owner=owner; trap.ExpiresAt=expiry; trap.Id=++_nextId;
            return trap;
        }
        private float _nextCheck;
        private void Update()
        {
            if(_owner==null || !_owner.Authority) { Expire(); return; }
            if(_owner.Now>=ExpiresAt) { _owner.CloseTrap(this); return; }
            if(Time.time<_nextCheck) return; _nextCheck=Time.time+.05f;
            foreach(var collider in Physics.OverlapSphere(transform.position,.45f,~0,QueryTriggerInteraction.Collide))
                if(!collider.transform.IsChildOf(_owner.transform) && collider.GetComponentInParent<IDamageReceiver>()!=null)
                { _owner.TrapTriggered(this,collider); return; }
        }
        public void Expire() { if(gameObject==null) return; enabled=false; if(Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject); }
    }
}
