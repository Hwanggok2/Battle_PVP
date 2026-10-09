using BattlePvp.Combat;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BattlePvp.UI
{
    public sealed class PassiveDropSlot : MonoBehaviour, IDropHandler, IPointerClickHandler
    {
        public JobGuidePanel Owner;
        public string PresetId;
        public int Slot;
        public void OnDrop(PointerEventData e)
        {
            var item=e.pointerDrag!=null ? e.pointerDrag.GetComponent<PassiveDragItem>() : null;
            if(item!=null && item.Owner==Owner && item.Kind>0) Owner.DropPassive(PresetId,Slot,item.Kind);
        }
        public void OnPointerClick(PointerEventData e) => Owner.ClickPassiveSlot(PresetId,Slot,e.button==PointerEventData.InputButton.Right);
    }
}
