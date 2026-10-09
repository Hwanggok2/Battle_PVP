using BattlePvp.Combat;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BattlePvp.UI
{
    public sealed class PassiveDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public JobGuidePanel Owner;
        public int Kind;
        public void OnBeginDrag(PointerEventData e) {if(Kind>0 && PassiveLoadout.CanEdit) Owner.BeginPassiveDrag(Kind,e);}
        public void OnDrag(PointerEventData e) => Owner.MovePassiveDrag(e);
        public void OnEndDrag(PointerEventData e) => Owner.EndPassiveDrag();
        public void OnPointerClick(PointerEventData e) {if(!e.dragging && Kind>0) Owner.ChoosePassive(Kind);}
        private void OnDisable() {if(Owner!=null) Owner.EndPassiveDrag();}
    }

}
