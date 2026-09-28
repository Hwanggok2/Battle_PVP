using UnityEngine;
using UnityEngine.EventSystems;

namespace BattlePvp.UI
{
    public sealed class TerminalTouchInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public void OnPointerDown(PointerEventData data) => WaitingRoomTerminal.Instance?.SetTouchHeld(true);
        public void OnPointerUp(PointerEventData data) => WaitingRoomTerminal.Instance?.SetTouchHeld(false);
        public void OnPointerExit(PointerEventData data) => WaitingRoomTerminal.Instance?.SetTouchHeld(false);
        private void OnDisable() => WaitingRoomTerminal.Instance?.SetTouchHeld(false);
    }
}
