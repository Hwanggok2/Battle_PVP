using System.Collections.Generic;
using System.Collections;
using System.Runtime.InteropServices;
using BattlePvp.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class BattleChatUI : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BattlePvpWebGlIme_Open(string receiverName, string initialText, int maxLength);

        [DllImport("__Internal")]
        private static extern void BattlePvpWebGlIme_Close();
#endif

        private readonly Queue<string> _lines = new Queue<string>();

        [Header("UI References")]
        [SerializeField] private RectTransform _panelRect;
        [SerializeField] private RectTransform _resizeHandle;
        [SerializeField] private RectTransform _viewportRect;
        [SerializeField] private RectTransform _contentRect;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private TextMeshProUGUI _logText;
        [SerializeField] private TMP_InputField _input;

        [Header("Behavior")]
        [SerializeField] private float _minHeight = 150f;
        [SerializeField] private float _maxHeight = 600f;
        [SerializeField] private int _maxLines = 80;

        private float _dragStartHeight;
        private float _dragStartPointerY;
        private bool _isTyping;
        private int _lastSubmitFrame = -1;
        private Coroutine _submitRoutine;
        private bool _layoutDirty;
        private bool _logDirty;
        private bool _inputSubmitHooked;
        private bool _isSubmitting;
        private bool _webGlImeActive;

        private void Awake()
        {
            BattleChatNetwork.EnsureRegistered();
            ResolveReferences();
            ConfigureRuntimeComponents();
            EnsureEventSystem();
            SetTyping(false);
        }

        private void OnEnable()
        {
            BattleChatNetwork.MessageReceived += AddMessage;
            GameInputController.TextInputCancelled += CancelTyping;
            QueueScrollToBottom();
        }

        private void OnDisable()
        {
            BattleChatNetwork.MessageReceived -= AddMessage;
            GameInputController.TextInputCancelled -= CancelTyping;
            StopAllCoroutines();
            _submitRoutine = null;
            _isSubmitting = false;
            CloseWebGlImeInput();
            UnhookInputSubmit();
            _isTyping = false;
            Input.imeCompositionMode = IMECompositionMode.Off;
            GameInputController.SetTextInputActive(false);
            ClearInputField();
            ClearUiSelection();
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return;

            if (_isTyping)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    GameInputController.HandleEscape();
                    CancelTyping();
                    return;
                }
            }

            if (!IsSubmitKeyPressedThisFrame())
                return;

            if (!_isTyping)
            {
                if (BattlePvp.Networking.BattleStateMachine.Instance != null &&
                    BattlePvp.Networking.BattleStateMachine.Instance.IsResultPanelVisible) return;
                ClearUiSelection();
                SetTyping(true);
                return;
            }

            QueueSubmitCurrentText();
        }

        private void Reset()
        {
            _panelRect = GetComponent<RectTransform>();
            ResolveReferences();
        }

        private void SubmitCurrentText(string submittedText = null)
        {
            if (Time.frameCount == _lastSubmitFrame)
                return;

            _lastSubmitFrame = Time.frameCount;

            if (_input == null)
                return;

            string text = string.IsNullOrEmpty(submittedText) ? BuildCurrentInputText() : submittedText;

            if (!string.IsNullOrWhiteSpace(text))
                BattleChatNetwork.Send(text);

            SetTyping(false);
            ClearInputField();
            StartCoroutine(CoClearInputAfterImeSettles());
        }

        private string BuildCurrentInputText()
        {
            if (_input == null)
                return string.Empty;

            string text = _input.text ?? string.Empty;
            string composition = Input.compositionString;
            if (!string.IsNullOrEmpty(composition))
                text += composition;

            return text;
        }

        private void QueueSubmitCurrentText()
        {
            GameInputController.ConsumeSubmit();
            if (_submitRoutine != null || _isSubmitting)
                return;

            _submitRoutine = StartCoroutine(CoSubmitAfterInputSettles(BuildCurrentInputText()));
        }

        private void QueueSubmittedText(string submittedText)
        {
            GameInputController.ConsumeSubmit();
            if (!_isTyping)
                return;

            if (_isSubmitting)
                return;

            if (_submitRoutine != null)
                StopCoroutine(_submitRoutine);

            string currentText = BuildCurrentInputText();
            string preSubmitText = ChooseSubmittedText(submittedText, currentText);
            _submitRoutine = StartCoroutine(CoSubmitAfterInputSettles(preSubmitText));
        }

        private IEnumerator CoSubmitAfterInputSettles(string preSubmitText)
        {
            _isSubmitting = true;

            if (_input != null)
            {
                _input.DeactivateInputField();
                Canvas.ForceUpdateCanvases();
            }

            yield return null;
            yield return new WaitForEndOfFrame();

            _submitRoutine = null;
            string settledText = BuildCurrentInputText();
            string text = ChooseSubmittedText(preSubmitText, settledText);
            SubmitCurrentText(text);
            _isSubmitting = false;

            yield return null;
            if (!_isTyping)
                ClearInputField();
        }

        private static string ChooseSubmittedText(string submittedText, string settledText)
        {
            if (string.IsNullOrEmpty(submittedText))
                return settledText;

            if (string.IsNullOrEmpty(settledText))
                return submittedText;

            return submittedText.Length >= settledText.Length ? submittedText : settledText;
        }

        private void SetTyping(bool isTyping)
        {
            _isTyping = isTyping;
            GameInputController.SetTextInputActive(isTyping);

            if (_input == null)
                return;

            if (isTyping)
            {
                Input.imeCompositionMode = IMECompositionMode.On;
#if UNITY_WEBGL && !UNITY_EDITOR
                OpenWebGlImeInput();
#else
                HookInputSubmit();
                _input.interactable = true;
                _input.ActivateInputField();
                _input.Select();
#endif
            }
            else
            {
                CloseWebGlImeInput();
                UnhookInputSubmit();
                _input.DeactivateInputField();
                _input.interactable = false;
                Input.imeCompositionMode = IMECompositionMode.Off;
                ClearInputField();
                ClearUiSelection();
            }
        }

        private void CancelTyping()
        {
            if (!_isTyping) return;
            if (_submitRoutine != null) StopCoroutine(_submitRoutine);
            _submitRoutine = null;
            _isSubmitting = false;
            SetTyping(false);
        }

        private void LateUpdate()
        {
            if (!_layoutDirty) return;
            _layoutDirty = false;
            if (_logDirty && _logText != null)
            {
                _logDirty = false;
                UserTextPresentation.SetPlain(_logText, string.Join("\n", _lines));
            }
            RefreshLogLayoutAndStickToBottom();
        }

        private void OpenWebGlImeInput()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webGlImeActive)
                return;

            _webGlImeActive = true;
            _input.interactable = true;
            _input.DeactivateInputField();
            WebGLInput.captureAllKeyboardInput = false;
            BattlePvpWebGlIme_Open(gameObject.name, _input.text ?? string.Empty, _input.characterLimit);
#endif
        }

        private void CloseWebGlImeInput()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_webGlImeActive)
                return;

            _webGlImeActive = false;
            BattlePvpWebGlIme_Close();
            WebGLInput.captureAllKeyboardInput = true;
#endif
        }

        public void OnWebGlInputChanged(string text)
        {
            if (!_isTyping || _input == null)
                return;

            text ??= string.Empty;
            _input.SetTextWithoutNotify(text);
            _input.caretPosition = text.Length;
            _input.stringPosition = text.Length;
            _input.ForceLabelUpdate();
        }

        public void OnWebGlInputSubmitted(string text)
        {
            if (!_isTyping)
                return;

            OnWebGlInputChanged(text);
            QueueSubmittedText(text);
        }

        public void OnWebGlInputCancelled(string _)
        {
            if (_isTyping)
            {
                GameInputController.HandleEscape();
                CancelTyping();
            }
        }

        private void ClearInputField()
        {
            if (_input == null)
                return;

            _input.SetTextWithoutNotify(string.Empty);
            _input.caretPosition = 0;
            _input.stringPosition = 0;
            _input.ForceLabelUpdate();
        }

        private IEnumerator CoClearInputAfterImeSettles()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                if (!_isTyping)
                {
                    Input.imeCompositionMode = IMECompositionMode.Off;
                    ClearInputField();
                }
            }
        }

        private void AddMessage(string sender, string text, double serverTime)
        {
            if (_logText == null || _scrollRect == null)
                return;

            string name = UserDisplayText.SingleLine(sender, UserDisplayText.NameLimit, "Unknown");
            string message = UserDisplayText.SingleLine(text, UserDisplayText.MessageLimit);
            _lines.Enqueue($"[{name}] {message}");
            while (_lines.Count > Mathf.Max(1, _maxLines)) _lines.Dequeue();
            _logDirty = true;
            QueueScrollToBottom();
        }

        private void UpdateContentHeight()
        {
            if (_contentRect == null || _viewportRect == null || _logText == null)
                return;

            float viewportHeight = Mathf.Max(1f, _viewportRect.rect.height);
            float preferredHeight = Mathf.Ceil(_logText.preferredHeight);
            _contentRect.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, preferredHeight));
        }

        private void RefreshLogLayoutAndStickToBottom()
        {
            if (_scrollRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            UpdateContentHeight();
            Canvas.ForceUpdateCanvases();
            _scrollRect.Rebuild(CanvasUpdate.PostLayout);

            _scrollRect.velocity = Vector2.zero;
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void QueueScrollToBottom()
        {
            _layoutDirty = true;
        }

        private void ResolveReferences()
        {
            if (_panelRect == null)
                _panelRect = GetComponent<RectTransform>();

            if (_scrollRect == null)
                _scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (_input == null)
                _input = GetComponentInChildren<TMP_InputField>(true);

            if (_viewportRect == null && _scrollRect != null)
                _viewportRect = _scrollRect.viewport;

            if (_contentRect == null && _scrollRect != null)
                _contentRect = _scrollRect.content;

            if (_logText == null && _contentRect != null)
                _logText = _contentRect.GetComponent<TextMeshProUGUI>();

            if (_resizeHandle == null)
            {
                var handle = transform.Find("ResizeHandle");
                if (handle != null)
                    _resizeHandle = handle as RectTransform;
            }
        }

        private void ConfigureRuntimeComponents()
        {
            AnchorBottom();
            if (_contentRect != null)
            {
                _contentRect.anchorMin = Vector2.zero;
                _contentRect.anchorMax = new Vector2(1f,0f);
                _contentRect.pivot = new Vector2(.5f,0f);
            }
            if (_scrollRect != null)
            {
                _scrollRect.viewport = _viewportRect;
                _scrollRect.content = _contentRect;
                _scrollRect.horizontal = false;
                _scrollRect.vertical = true;
                _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }

            if (_logText != null)
            {
                _logText.textWrappingMode = TextWrappingModes.Normal;
                _logText.alignment = TextAlignmentOptions.BottomLeft;
                _logText.raycastTarget = false;
            }

            if (_input != null)
            {
                _input.lineType = TMP_InputField.LineType.SingleLine;
                HookInputSubmit();
            }

            if (_resizeHandle != null)
            {
                var trigger = _resizeHandle.GetComponent<EventTrigger>();
                if (trigger == null)
                    trigger = _resizeHandle.gameObject.AddComponent<EventTrigger>();

                trigger.triggers.Clear();
                AddDragTrigger(trigger, EventTriggerType.BeginDrag, data =>
                {
                    GameInputController.SetTextInputActive(true);
                    var pointer = (PointerEventData)data;
                    _dragStartHeight = _panelRect.rect.height;
                    _dragStartPointerY = PointerLocalY(pointer);
                });
                AddDragTrigger(trigger, EventTriggerType.Drag, data =>
                {
                    var pointer = (PointerEventData)data;
                    float delta = PointerLocalY(pointer) - _dragStartPointerY;
                    ResizeHeight(_dragStartHeight + delta);
                });
                AddDragTrigger(trigger, EventTriggerType.EndDrag, data =>
                {
                    if (!_isTyping)
                        GameInputController.SetTextInputActive(false);
                    ClearUiSelection();
                    QueueScrollToBottom();
                });
            }
        }

        private void AnchorBottom()
        {
            if (_panelRect == null) return;
            Vector3 bottom = _panelRect.TransformPoint(new Vector3(0f, _panelRect.rect.yMin, 0f));
            _panelRect.pivot = new Vector2(_panelRect.pivot.x,0);
            _panelRect.position += bottom - _panelRect.TransformPoint(new Vector3(0f, _panelRect.rect.yMin, 0f));
        }

        public void ResizeHeight(float height)
        {
            if (_panelRect == null) return;
            AnchorBottom();
            _panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Mathf.Clamp(height,_minHeight,_maxHeight));
            QueueScrollToBottom();
        }

        private float PointerLocalY(PointerEventData pointer)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelRect,
                pointer.position,pointer.pressEventCamera,out Vector2 point);
            return point.y;
        }

        private void HookInputSubmit()
        {
            if (_input == null || _inputSubmitHooked)
                return;

            _input.onSubmit.AddListener(QueueSubmittedText);
            _inputSubmitHooked = true;
        }

        private void UnhookInputSubmit()
        {
            if (_input == null || !_inputSubmitHooked)
                return;

            _input.onSubmit.RemoveListener(QueueSubmittedText);
            _inputSubmitHooked = false;
        }

        private static void AddDragTrigger(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback(data));
            trigger.triggers.Add(entry);
        }

        private static bool IsSubmitKeyPressedThisFrame()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return false;

            return keyboard.enterKey.wasPressedThisFrame ||
                   keyboard.numpadEnterKey.wasPressedThisFrame;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            events.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
        }

        private static void ClearUiSelection()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
