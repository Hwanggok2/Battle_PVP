using System.Collections.Generic;
using System.Collections;
using BattlePvp.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed class BattleChatUI : MonoBehaviour
    {
        private readonly List<string> _lines = new List<string>();

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
        [SerializeField] private float _maxHeight = 420f;
        [SerializeField] private int _maxLines = 80;

        private float _dragStartHeight;
        private float _dragStartPointerY;
        private bool _isTyping;
        private int _lastSubmitFrame = -1;
        private Coroutine _submitRoutine;
        private Coroutine _scrollRoutine;
        private bool _inputSubmitHooked;

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
        }

        private void OnDisable()
        {
            BattleChatNetwork.MessageReceived -= AddMessage;
            UnhookInputSubmit();
            _isTyping = false;
            GameInputController.SetTextInputActive(false);
            ClearInputField();
            ClearUiSelection();
        }

        private void Update()
        {
            BattleChatNetwork.EnsureRegistered();

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return;

            if (_isTyping)
            {
                GameInputController.SetTextInputActive(true);

                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    SetTyping(false);
                    return;
                }
            }

            if (!IsSubmitKeyPressedThisFrame())
                return;

            if (!_isTyping)
            {
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
        }

        private string BuildCurrentInputText()
        {
            if (_input == null)
                return string.Empty;

            string text = _input.text ?? string.Empty;
            string composition = Input.compositionString;
            if (!string.IsNullOrEmpty(composition) && !text.EndsWith(composition))
                text += composition;

            return text;
        }

        private void QueueSubmitCurrentText()
        {
            if (_submitRoutine != null)
                return;

            _submitRoutine = StartCoroutine(CoSubmitAfterInputSettles(null));
        }

        private void QueueSubmittedText(string submittedText)
        {
            if (!_isTyping)
                return;

            if (_submitRoutine != null)
                StopCoroutine(_submitRoutine);

            _submitRoutine = StartCoroutine(CoSubmitAfterInputSettles(submittedText));
        }

        private IEnumerator CoSubmitAfterInputSettles(string submittedText)
        {
            yield return new WaitForEndOfFrame();
            _submitRoutine = null;
            string settledText = BuildCurrentInputText();
            string text = ChooseSubmittedText(submittedText, settledText);
            SubmitCurrentText(text);

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
                HookInputSubmit();
                _input.interactable = true;
                _input.ActivateInputField();
                _input.Select();
            }
            else
            {
                UnhookInputSubmit();
                _input.DeactivateInputField();
                ClearInputField();
                ClearUiSelection();
            }
        }

        private void ClearInputField()
        {
            if (_input == null)
                return;

            _input.SetTextWithoutNotify(string.Empty);
            _input.text = string.Empty;
            _input.caretPosition = 0;
            _input.stringPosition = 0;
        }

        private void AddMessage(string sender, string text, double serverTime)
        {
            if (_logText == null || _scrollRect == null)
                return;

            _lines.Add($"[{sender}] {text}");
            while (_lines.Count > _maxLines)
                _lines.RemoveAt(0);

            _logText.text = string.Join("\n", _lines);
            RefreshLogLayoutAndStickToBottom();
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

            _scrollRect.velocity = Vector2.zero;
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void QueueScrollToBottom()
        {
            if (_scrollRoutine != null)
                StopCoroutine(_scrollRoutine);

            _scrollRoutine = StartCoroutine(CoScrollToBottomAfterLayout());
        }

        private IEnumerator CoScrollToBottomAfterLayout()
        {
            yield return null;
            RefreshLogLayoutAndStickToBottom();
            _scrollRoutine = null;
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
                    _dragStartHeight = _panelRect.sizeDelta.y;
                    _dragStartPointerY = pointer.position.y;
                });
                AddDragTrigger(trigger, EventTriggerType.Drag, data =>
                {
                    var pointer = (PointerEventData)data;
                    float delta = pointer.position.y - _dragStartPointerY;
                    float height = Mathf.Clamp(_dragStartHeight + delta, _minHeight, _maxHeight);
                    _panelRect.sizeDelta = new Vector2(_panelRect.sizeDelta.x, height);
                    RefreshLogLayoutAndStickToBottom();
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

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static void ClearUiSelection()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
