using System.Collections.Generic;
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
            if (_isTyping)
                GameInputController.SetTextInputActive(false);
        }

        private void Update()
        {
            if (_isTyping && !GameInputController.IsTextInputActive)
            {
                SetTyping(false);
                return;
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !keyboard.enterKey.wasPressedThisFrame)
                return;

            if (!_isTyping)
            {
                SetTyping(true);
            }
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

            string text = string.IsNullOrEmpty(submittedText) ? _input.text : submittedText;
            string composition = Input.compositionString;
            if (!string.IsNullOrEmpty(composition) && !text.EndsWith(composition))
                text += composition;

            _input.text = string.Empty;

            if (!string.IsNullOrWhiteSpace(text))
                BattleChatNetwork.Send(text);

            SetTyping(false);
        }

        private void SetTyping(bool isTyping)
        {
            _isTyping = isTyping;
            GameInputController.SetTextInputActive(isTyping);

            if (_input == null)
                return;

            if (isTyping)
            {
                _input.interactable = true;
                _input.ActivateInputField();
                _input.Select();
            }
            else
            {
                _input.DeactivateInputField();
            }
        }

        private void AddMessage(string sender, string text, double serverTime)
        {
            if (_logText == null || _scrollRect == null)
                return;

            _lines.Add($"[{sender}] {text}");
            while (_lines.Count > _maxLines)
                _lines.RemoveAt(0);

            _logText.text = string.Join("\n", _lines);
            Canvas.ForceUpdateCanvases();
            UpdateContentHeight();
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void UpdateContentHeight()
        {
            if (_contentRect == null || _viewportRect == null || _logText == null)
                return;

            float viewportHeight = Mathf.Max(1f, _viewportRect.rect.height);
            float preferredHeight = Mathf.Ceil(_logText.preferredHeight);
            _contentRect.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, preferredHeight));
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
                _input.onSubmit.RemoveListener(HandleInputSubmit);
                _input.onSubmit.AddListener(HandleInputSubmit);
            }

            if (_resizeHandle != null)
            {
                var trigger = _resizeHandle.GetComponent<EventTrigger>();
                if (trigger == null)
                    trigger = _resizeHandle.gameObject.AddComponent<EventTrigger>();

                trigger.triggers.Clear();
                AddDragTrigger(trigger, EventTriggerType.BeginDrag, data =>
                {
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
                });
            }
        }

        private static void AddDragTrigger(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback(data));
            trigger.triggers.Add(entry);
        }

        private void HandleInputSubmit(string _)
        {
            if (_isTyping)
                SubmitCurrentText(_);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
