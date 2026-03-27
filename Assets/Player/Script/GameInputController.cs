using UnityEngine;
using BattlePvp.CameraLogic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.Logic
{
    /// <summary>
    /// [Core Fix] 전역 입력 및 마우스 커서/카메라 토글 관리 매니저.
    /// Lobby와 Battle 씬 모두에서 사용 가능하며, ESC를 통해 상태를 전환합니다.
    /// </summary>
    public sealed class GameInputController : MonoBehaviour
    {
        // 전역에서 접근 가능한 일시정지(메뉴) 상태
        public static bool IsPaused { get; private set; } = false;

        private FollowCamera _followCamera;
        private bool _isCursorUnlocked = false;

        private void Awake()
        {
            _followCamera = FindFirstObjectByType<FollowCamera>();
            // 씬 진입 시마다 초기화 (Lobby에서 공격이 안 되는 현상 방지)
            IsPaused = false;
            _isCursorUnlocked = false;
        }

        private void OnDisable()
        {
            // 오브젝트가 사라지거나 씬이 바뀔 때 상태 초기화
            IsPaused = false;
        }

        private void Start()
        {
            CheckSceneDependencies();
            ApplyCursorState(); // 시작 시 커서 상태 적용
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ToggleCursor();
            }

            // ESC로 풀린 상태(메뉴 모드)에서는 커서 잠금이 되지 않도록 강제 유지 (Task 1)
            if (_isCursorUnlocked)
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 포커스를 다시 얻었을 때 현재 설정된 상태 다시 적용 (Task 1)
            if (hasFocus)
            {
                ApplyCursorState();
            }
        }

        private void ToggleCursor()
        {
            _isCursorUnlocked = !_isCursorUnlocked;
            IsPaused = _isCursorUnlocked;

            ApplyCursorState();

            if (_isCursorUnlocked)
                Debug.Log("[GameInput] Pause Mode: Cursor Free, Camera Fixed, Attack Disabled");
            else
                Debug.Log("[GameInput] Play Mode: Cursor Locked, Camera Active, Attack Enabled");
        }

        private void ApplyCursorState()
        {
            if (_isCursorUnlocked)
            {
                // ESC 1회: 커서 활성화, 카메라 고정 (Task 1)
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_followCamera != null) _followCamera.IsLocked = true;
            }
            else
            {
                // ESC 2회: 커서 잠금, 카메라 회전 (기본 상태)
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (_followCamera != null) _followCamera.IsLocked = false;
            }
        }

        /// <summary>
        /// Task 4: UI 상호작용 및 씬 필수 요소 체크
        /// </summary>
        private void CheckSceneDependencies()
        {
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                Debug.LogWarning("[GameInput] EventSystem을 동적으로 생성했습니다.");
            }

            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                    Debug.Log($"[GameInput] '{canvas.name}' 에 GraphicRaycaster 를 추가했습니다.");
                }
            }
        }
    }
}
