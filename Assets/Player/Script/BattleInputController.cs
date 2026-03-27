using UnityEngine;
using BattlePvp.CameraLogic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattlePvp.Logic
{
    /// <summary>
    /// Core Task 5: Battle 씬 전용 ESC 토글 기능
    /// - ESC 1회: 커서 활성, 카메라 회전 중지
    /// - ESC 2회: 커서 잠금, 카메라 회전 재개
    /// </summary>
    public sealed class BattleInputController : MonoBehaviour
    {
        public static bool IsPaused { get; private set; } = false;

        private FollowCamera _followCamera;
        private bool _isCursorUnlocked = false;

        private void Awake()
        {
            _followCamera = FindFirstObjectByType<FollowCamera>();
            IsPaused = false;
        }

        private void Start()
        {
            CheckSceneDependencies();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ToggleCursor();
            }

            // ESC로 풀린 상태에서 화면을 클릭해도 다시 잠기지 않도록 강제 유지
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
            // 포커스를 다시 얻었을 때 현재 상태 유지
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
                Debug.Log("[BattleInput] Cursor Unlocked, UI Interaction Enabled");
            else
                Debug.Log("[BattleInput] Cursor Locked, Gameplay Resumed");
        }

        private void ApplyCursorState()
        {
            if (_isCursorUnlocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_followCamera != null) _followCamera.IsLocked = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (_followCamera != null) _followCamera.IsLocked = false;
            }
        }

        /// <summary>
        /// Task 4: UI 상호작용 불가 원인 체크
        /// </summary>
        private void CheckSceneDependencies()
        {
            if (EventSystem.current == null)
            {
                Debug.LogError("[BattleInput] 씬에 EventSystem 이 없습니다! UI 클릭이 작동하지 않습니다.");
                // 필요하다면 동적으로 생성할 수도 있지만 일단 경고.
                GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Debug.LogWarning("[BattleInput] EventSystem을 동적으로 생성했습니다.");
            }

            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    Debug.LogWarning($"[BattleInput] Canvas '{canvas.name}' 에 GraphicRaycaster가 없습니다. UI 클릭이 안 될 수 있습니다.");
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                    Debug.Log($"[BattleInput] '{canvas.name}' 에 GraphicRaycaster 를 추가했습니다.");
                }
            }
        }
    }
}
