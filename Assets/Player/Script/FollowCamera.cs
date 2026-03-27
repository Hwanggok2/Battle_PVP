using UnityEngine;
 
namespace BattlePvp.CameraLogic
{
    /// <summary>
    /// 플레이어의 뒤쪽 상단에서 부드럽게 따라다니는 3인칭 팔로우 카메라 스크립트입니다.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [Header("Target Settings")]
        [SerializeField] private Transform _target;           // 추적할 대상 (플레이어)
        [SerializeField] private Vector3 _offset = new Vector3(0, 1.8f, -3f); // 대상(피벗)으로부터의 거리

        [Header("Mouse Settings")]
        [SerializeField] private float _mouseSensitivity = 2.0f; // Input System에서는 델타가 작으므로 약간 크게 설정
        [SerializeField] private float _minPitch = -20f;
        [SerializeField] private float _maxPitch = 45f;

        [Header("Smoothing")]
        [SerializeField] private float _moveSmoothTime = 0.12f;
        [SerializeField] private float _rotSmoothSpeed = 10f;

        private float _yaw;   // 수평 회전
        private float _pitch; // 수직 회전
        private Vector2 _lookInput;

        private void Start()
        {
            // 커서 잠금 및 숨김
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 초기 회전값 설정 (현재 카메라 회전 기준)
            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // 1. 마우스 입력 직접 가져오기 (Input System 사용)
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * _mouseSensitivity * 0.1f;
                _pitch -= delta.y * _mouseSensitivity * 0.1f;
                _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            }

            // 2. 회전 쿼터니언 계산
            Quaternion targetRotation = Quaternion.Euler(_pitch, _yaw, 0);
            transform.rotation = targetRotation;

            // 3. 카메라 위치 계산 (대상 위치 + 회전된 오프셋)
            Vector3 pivotPosition = _target.position + Vector3.up * 1.5f; 
            Vector3 targetPosition = pivotPosition + (targetRotation * new Vector3(0, 0, _offset.z)) + (Vector3.up * _offset.y);

            // 4. 위치 적용 (즉시 이동)
            transform.position = targetPosition;
        }

        /// <summary>
        /// 외부에서 타겟을 수동으로 설정할 때 사용합니다.
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        // 플레이어 매니저에서 참조할 현재 수평 회전값
        public float GetYaw() => _yaw;
    }
}
