using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class NeonButtonSound : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        public void OnPointerClick(PointerEventData e) { if (GetComponent<Button>().IsInteractable()) LocalGameSettings.Click(); }
        public void OnSubmit(BaseEventData e) { if (GetComponent<Button>().IsInteractable()) LocalGameSettings.Click(); }
    }
}
