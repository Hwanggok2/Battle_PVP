using BattlePvp.Stats;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BattlePvp.UI
{
    public sealed class CombatHudAttackInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private PlayerCombat _held;
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _held = StatManager.Local != null ? StatManager.Local.GetComponent<PlayerCombat>() : null;
            if (_held != null) _held.AttackFromHud(true);
        }
        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) => Release();
        private void OnDisable() => Release();
        private void Release() { if (_held != null) _held.AttackFromHud(false); _held = null; }
    }
}
