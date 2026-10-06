using System.Runtime.InteropServices;
using BattlePvp.Logic;
using TMPro;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>Uses a browser text field for Hangul composition in WebGL forms.</summary>
    public sealed class WebGlTextInput : MonoBehaviour
    {
        private TMP_InputField _field;
        private bool _editing;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool _previousCapture;
        [DllImport("__Internal")] private static extern void BattlePvpWebGlIme_Open(string receiver, string text, int limit);
        [DllImport("__Internal")] private static extern void BattlePvpWebGlIme_SetRect(float x, float y, float width, float height);
        [DllImport("__Internal")] private static extern void BattlePvpWebGlIme_Close();
#endif

        public static void Attach(TMP_InputField field)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (field == null || field.GetComponentInChildren<WebGlTextInput>(true) != null) return;
            var receiver = new GameObject("WebGL room name " + field.GetInstanceID());
            receiver.transform.SetParent(field.transform, false);
            var bridge = receiver.AddComponent<WebGlTextInput>();
            bridge._field = field;
            field.onSelect.AddListener(bridge.BeginEdit);
#endif
        }

        private void BeginEdit(string _)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_editing || _field == null || !_field.interactable) return;
            _editing = true;
            _previousCapture = WebGLInput.captureAllKeyboardInput;
            _field.DeactivateInputField();
            GameInputController.SetTextInputActive(true);
            WebGLInput.captureAllKeyboardInput = false;
            BattlePvpWebGlIme_Open(gameObject.name, _field.text, _field.characterLimit);
            UpdateRect();
#endif
        }

        private void LateUpdate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_editing) UpdateRect();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private readonly Vector3[] _corners = new Vector3[4];
        private void UpdateRect()
        {
            _field.GetComponent<RectTransform>().GetWorldCorners(_corners);
            Canvas canvas = _field.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 lower = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
            Vector2 upper = RectTransformUtility.WorldToScreenPoint(camera, _corners[2]);
            BattlePvpWebGlIme_SetRect(lower.x / Screen.width, 1f - upper.y / Screen.height,
                (upper.x - lower.x) / Screen.width, (upper.y - lower.y) / Screen.height);
        }
#endif

        // Invoked by the browser IME bridge through SendMessage.
        public void OnWebGlInputChanged(string text)
        {
            if (_editing && _field != null) _field.text = text ?? string.Empty;
        }
        public void OnWebGlInputSubmitted(string text) { OnWebGlInputChanged(text); EndEdit(); }
        public void OnWebGlInputBlurred(string text) { OnWebGlInputChanged(text); EndEdit(); }
        public void OnWebGlInputCancelled(string _) => EndEdit();

        private void EndEdit()
        {
            if (!_editing) return;
            _editing = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            BattlePvpWebGlIme_Close();
            WebGLInput.captureAllKeyboardInput = _previousCapture;
            GameInputController.SetTextInputActive(false);
#endif
        }
        private void OnDisable() => EndEdit();
        private void OnDestroy()
        {
            EndEdit();
            if (_field != null) _field.onSelect.RemoveListener(BeginEdit);
        }
    }
}
