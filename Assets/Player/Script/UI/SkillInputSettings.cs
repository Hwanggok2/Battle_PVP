using UnityEngine;
using UnityEngine.InputSystem;

namespace BattlePvp.UI
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class SkillInputSettings : MonoBehaviour
    {
        private PlayerInput _input;
        private void OnEnable() { _input = GetComponent<PlayerInput>(); LocalGameSettings.Changed += Apply; Apply(); }
        private void OnDisable() => LocalGameSettings.Changed -= Apply;
        private void Start() => Apply();
        private void Apply()
        {
            if (_input == null || _input.actions == null) return;
            ApplyKey("Skill1", LocalGameSettings.Current.skill1);
            ApplyKey("Skill2", LocalGameSettings.Current.skill2);
        }
        private void ApplyKey(string action, string key)
        {
            var inputAction = _input.actions.FindAction(action, false);
            if (inputAction != null && inputAction.bindings.Count > 0) inputAction.ApplyBindingOverride(0, "<Keyboard>/" + key);
        }
    }
}
