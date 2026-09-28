using UnityEngine;

namespace BattlePvp.Combat
{
    // Isolate pose ordering from PlayerCombat's Awake and the existing stat/movement initialization order.
    [DefaultExecutionOrder(-20), DisallowMultipleComponent]
    public sealed class MeleeAimDriver : MonoBehaviour
    {
        private PlayerCombat _combat;
        private void Awake() => _combat = GetComponent<PlayerCombat>();
        private void Update() { if (_combat != null) _combat.RestoreMeleeAimPose(); }
        private void LateUpdate() { if (_combat != null && _combat.isActiveAndEnabled) _combat.UpdateMeleeAimPose(); }
        private void OnDisable() { if (_combat != null) _combat.RestoreMeleeAimPose(); }
    }
}
