using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Mirror;
using TMPro;
using BattlePvp.Logic;
using UnityEngine.InputSystem;

namespace BattlePvp.UI
{
    [RequireComponent(typeof(Button))]
    public class BattleStartController : MonoBehaviour
    {
        public static bool IsStarting { get; private set; }
        [Header("UI References")]
        [Tooltip("카운트다운을 표시할 텍스트 컴포넌트입니다. 할당하지 않으면 자식에서 자동으로 찾습니다.")]
        [SerializeField] private TextMeshProUGUI _countdownText;

        [Header("Settings")]
        [Tooltip("카운트다운 시간 (초)")]
        [SerializeField] private float _countdownDuration = 5f;
        [Tooltip("이동할 씬의 이름")]
        [SerializeField] private string _battleSceneName = "Battle";
        [SerializeField] private bool _allowKeyboardShortcut = true;

        private Button _button;
        private bool _isCountingDown = false;
        private bool _isSceneTransitioning = false;
        private string _originalText = "Start";
        private bool IsWaitingScene => SceneManager.GetActiveScene().name == "Battle_waiting" ||
            SceneManager.GetActiveScene().name == "Battle_wait";
        private bool CanStart => IsWaitingScene && !_isCountingDown && !_isSceneTransitioning &&
            (BattlePvp.Networking.PlayFabBattleManager.Instance == null || !BattlePvp.Networking.PlayFabBattleManager.Instance.RoomSettingsBusy) &&
            (!NetworkClient.active || NetworkServer.active);

        private void Awake()
        {
            IsStarting = false;
            _button = GetComponent<Button>();
            if (_button != null)
            {
                _button.onClick.AddListener(OnStartButtonClick);
                _button.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            
            // Text가 할당되지 않았다면 자식 오브젝트에서 찾기 시도
            _countdownText = TmpTextMigration.ResolveOrUpgrade(transform, _countdownText);

            if (_countdownText != null)
            {
                _originalText = IsWaitingScene ? (_allowKeyboardShortcut ? "G · 경기 시작" : "경기 시작") : _countdownText.text;
                if (IsWaitingScene) _countdownText.text = _originalText;
            }
        }

        private void OnDestroy()
        {
            IsStarting = false;
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnStartButtonClick);
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _isCountingDown = false;
            _isSceneTransitioning = false;
            IsStarting = false;
            if (_countdownText != null) _countdownText.text = _originalText;
        }

        private void Update()
        {
            if (_button != null)
                _button.interactable = CanStart;
            if (!_isCountingDown && !_isSceneTransitioning && _countdownText != null && IsWaitingScene)
                _countdownText.text = NetworkClient.active && !NetworkServer.active ? "방장이 시작을 준비 중" : _originalText;
            if (_allowKeyboardShortcut && CanStart && Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame &&
                !GameInputController.IsPaused && !GameInputController.IsTextInputActive &&
                GameInputController.CurrentMode == GameInputMode.Gameplay)
                BeginCountdown();
        }

        private void OnStartButtonClick()
        {
            // Captured attack clicks and UI Submit must not start the match.
            if (InputModeRules.CanLockCursor(SceneManager.GetActiveScene().name, GameInputController.CurrentMode)) return;
            BeginCountdown();
        }

        private void BeginCountdown()
        {
            if (CanStart) StartCoroutine(CoStartCountdown());
        }

        private IEnumerator CoStartCountdown()
        {
            _isCountingDown = true;
            IsStarting = true;
            if (_button != null) _button.interactable = false;

            float remainingTime = _countdownDuration;

            while (remainingTime > 0)
            {
                if (_countdownText != null)
                {
                    _countdownText.text = Mathf.CeilToInt(remainingTime).ToString();
                }

                yield return new WaitForSeconds(1f);
                remainingTime -= 1f;
            }

            if (_countdownText != null)
            {
                _countdownText.text = "출격 중";
            }

            // 이미 목표 씬이거나 전환 중이라면 중복 실행 방지
            if (_isSceneTransitioning || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == _battleSceneName)
            {
                _isCountingDown = false;
                IsStarting = false;
                yield break;
            }

            _isSceneTransitioning = true;

            // 씬 전환 처리
            if (NetworkManager.singleton != null && NetworkServer.active)
            {
                NetworkManager.singleton.ServerChangeScene(_battleSceneName);
            }
            else
            {
                SceneManager.LoadScene(_battleSceneName);
            }

            _isCountingDown = false;
        }

    }

    internal static class TmpTextMigration
    {
        public static TextMeshProUGUI ResolveOrUpgrade(Transform root, TextMeshProUGUI assigned)
        {
            if (assigned != null || root == null)
                return assigned;

            TextMeshProUGUI existing = root.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existing != null)
                return existing;

            Text legacy = root.GetComponentInChildren<Text>(true);
            if (legacy == null)
                return null;

            var labelObject = new GameObject(
                "TMP Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(legacy.transform, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = legacy.text;
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = legacy.fontSize;
            label.color = legacy.color;
            label.alignment = ConvertAlignment(legacy.alignment);
            label.enableAutoSizing = legacy.resizeTextForBestFit;
            label.fontSizeMin = legacy.resizeTextMinSize;
            label.fontSizeMax = legacy.resizeTextMaxSize;
            label.textWrappingMode = legacy.horizontalOverflow == HorizontalWrapMode.Wrap
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
            label.overflowMode = legacy.verticalOverflow == VerticalWrapMode.Truncate
                ? TextOverflowModes.Truncate
                : TextOverflowModes.Overflow;
            label.raycastTarget = legacy.raycastTarget;
            legacy.enabled = false;
            return label;
        }

        private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
                TextAnchor.UpperCenter => TextAlignmentOptions.Top,
                TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
                TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
                TextAnchor.MiddleRight => TextAlignmentOptions.Right,
                TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
                TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
                TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
                _ => TextAlignmentOptions.Center
            };
        }
    }
}
