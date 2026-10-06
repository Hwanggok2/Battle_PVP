using UnityEngine;
 
namespace BattlePvp.CameraLogic
{
    /// <summary>
    /// 로비 궤도 카메라와 기존 전투 시점을 씬 입력 정책에 맞춰 제어합니다.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class FollowCamera : MonoBehaviour
    {
        [Header("Target Settings")]
        [SerializeField] private Transform _target;           // 추적할 대상 (플레이어)
        public Transform Target => _target;
        
        /// <summary>
        /// 외부(CharacterScaler 등)에서 실시간으로 카메라 거리를 조절할 수 있도록 노출합니다.
        /// </summary>
        public Vector3 Offset = new Vector3(0.3f, 0.2f, -1.0f); 

        [Header("Mouse Settings")]
        [SerializeField] private float _mouseSensitivity = 2.0f; // Input System에서는 델타가 작으므로 약간 크게 설정
        [SerializeField] private float _minPitch = -20f;
        [SerializeField] private float _maxPitch = 45f;

        [Header("Lobby Orbit")]
        [SerializeField] private float _lobbyDistance = 6f;
        [SerializeField] private float _lobbyMinDistance = 2.2f;
        [SerializeField] private float _lobbyMaxDistance = 9f;
        private bool _viewInitialized;
        private bool IsLobby => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby";

        [Header("Smoothing")]
        [SerializeField] private float _moveSmoothTime = 0.12f;
        [SerializeField] private float _rotSmoothSpeed = 10f;

        private float _yaw;   // 수평 회전
        private float _pitch; // 수직 회전
        private Vector2 _lookInput;
        private bool _useTemporaryOffset;
        private Vector3 _temporaryOffset;
        private Vector3 _temporaryRotationOffset;
        private Transform _forcedLookTarget;
        private PlayerManager _targetPlayer;
        private BattlePvp.Combat.ExpandedSkillController _targetSkills;
        private float _crouchDrop, _crouchVelocity;
        private bool HasForcedLook => _forcedLookTarget!=null || (_targetSkills!=null && _targetSkills.IsBeingHooked);

        /// <summary>
        /// ESC 토글 등에 의해 카메라 회전만 막아야 할 때 설정합니다.
        /// </summary>
        public bool IsLocked { get; set; } = false;

        private void Start()
        {
            if (!_viewInitialized) InitializeView();
        }

        private void Update()
        {
            if (_target == null) return;
            if (!BattlePvp.Logic.InputModeRules.CanTrackCamera(
                BattlePvp.Logic.GameInputController.CurrentMode,
                BattlePvp.Logic.GameInputController.IsPaused,
                BattlePvp.Logic.GameInputController.IsTextInputActive)) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool overUi = IsLobby && UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            var skillControl = _target.GetComponentInParent<BattlePvp.Combat.ExpandedSkillController>();
            bool canLook = (skillControl == null || !skillControl.LookLocked) && (!IsLobby || (mouse != null && mouse.rightButton.isPressed && !overUi));
            if (!IsLocked && !HasForcedLook && canLook)
            {
                if (mouse != null)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    var settings = BattlePvp.UI.LocalGameSettings.Current;
                    float yawDelta = delta.x * _mouseSensitivity * 0.1f * settings.sensitivity;
                    if (skillControl != null && skillControl.IsCharging) yawDelta = Mathf.Clamp(yawDelta, -skillControl.ChargeTurnRate * Time.deltaTime, skillControl.ChargeTurnRate * Time.deltaTime);
                    _yaw += yawDelta;
                    _pitch -= delta.y * _mouseSensitivity * 0.1f * settings.sensitivity * (settings.invertY ? -1f : 1f);
                    _pitch = Mathf.Clamp(_pitch, IsLobby ? -10f : _minPitch, IsLobby ? 65f : _maxPitch);
                }
            }
            if (IsLobby && mouse != null && !IsLocked && !overUi)
                // Input System uses a uniform scroll delta of 1 per wheel notch.
                _lobbyDistance = Mathf.Clamp(_lobbyDistance - mouse.scroll.ReadValue().y * .75f, _lobbyMinDistance, _lobbyMaxDistance);
            UpdateForcedLookYaw();
        }

        private void LateUpdate()
        {
            if (_target == null || !BattlePvp.Logic.InputModeRules.CanTrackCamera(
                BattlePvp.Logic.GameInputController.CurrentMode,
                BattlePvp.Logic.GameInputController.IsPaused,
                BattlePvp.Logic.GameInputController.IsTextInputActive)) return;

            // 2. 회전 쿼터니언 계산
            UpdateCrouchHeight(Time.deltaTime);
            UpdateForcedLookYaw();
            Quaternion targetRotation = GetActiveRotation();

            // 3. 카메라 위치 계산 (대상 위치 + 회전된 오프셋)
            Vector3 targetPosition = GetActiveCameraPosition(targetRotation);

            if (HasForcedLook)
            {
                Vector3 lookDirection = GetForcedLookPoint() - targetPosition;
                if (lookDirection.sqrMagnitude > 0.001f)
                    targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }

            // 4. 위치 적용 (즉시 이동)
            transform.rotation = targetRotation;
            transform.position = targetPosition;
        }

        private void UpdateCrouchHeight(float deltaTime)
        {
            float drop=Mathf.Max(_targetPlayer != null ? _targetPlayer.CrouchCameraDrop : 0f,
                _targetSkills!=null ? _targetSkills.SkillCameraDrop : 0f);
            _crouchDrop = Mathf.SmoothDamp(_crouchDrop, drop,
                ref _crouchVelocity, .1f, Mathf.Infinity, deltaTime);
        }

        /// <summary>
        /// 외부에서 타겟을 수동으로 설정할 때 사용합니다.
        /// </summary>
        public void SetTarget(Transform target)
        {
            if (_target == target && _viewInitialized) return;
            _target = target;
            InitializeView();
        }

        private void InitializeView()
        {
            _viewInitialized = true;
            _targetPlayer = _target != null ? _target.GetComponent<PlayerManager>() : null;
            _targetSkills = _target != null ? _target.GetComponent<BattlePvp.Combat.ExpandedSkillController>() : null;
            _crouchDrop = _targetPlayer != null ? _targetPlayer.CrouchCameraDrop : 0f;
            _crouchVelocity = 0f;
            _yaw = transform.eulerAngles.y;
            _pitch = Mathf.DeltaAngle(0, transform.eulerAngles.x);
            if (_target == null) return;
            if (IsLobby)
            {
                Vector3 direction = _target.position + Vector3.up * 1.2f - transform.position;
                _lobbyDistance = Mathf.Clamp(direction.magnitude, _lobbyMinDistance, _lobbyMaxDistance);
                if (direction.sqrMagnitude > .001f)
                {
                    var angles = Quaternion.LookRotation(direction).eulerAngles;
                    _yaw = angles.y; _pitch = Mathf.Clamp(Mathf.DeltaAngle(0, angles.x), -10f, 65f);
                }
            }
            else if (BattlePvp.Logic.InputModeRules.UsesFpsLook(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                _yaw = _target.eulerAngles.y;
                _pitch = 0f;
            }
        }

        public void SetForcedLookTarget(Transform target)
        {
            _forcedLookTarget = target;
        }

        public void SetTemporaryOffset(bool active, Vector3 offset)
        {
            _useTemporaryOffset = active;
            _temporaryOffset = offset;
            _temporaryRotationOffset = Vector3.zero;
        }

        public void SetTemporaryOffset(bool active, Vector3 offset, Vector3 rotationOffset)
        {
            _useTemporaryOffset = active;
            _temporaryOffset = offset;
            _temporaryRotationOffset = rotationOffset;
        }

        // 플레이어 매니저에서 참조할 현재 수평 회전값
        public float GetYaw() => _yaw;
        public float GetPitch() => _pitch;

        public Vector3 GetAimDirection()
        {
            return GetActiveRotation() * Vector3.forward;
        }

        public Ray GetAimRay()
        {
            Quaternion activeRotation = GetActiveRotation();
            Vector3 origin = _target != null ? GetActiveCameraPosition(activeRotation) : transform.position;
            return new Ray(origin, activeRotation * Vector3.forward);
        }

        private Quaternion GetActiveRotation()
        {
            Vector3 activeRotationOffset = _useTemporaryOffset ? _temporaryRotationOffset : Vector3.zero;
            return Quaternion.Euler(
                _pitch + activeRotationOffset.x,
                _yaw + activeRotationOffset.y,
                activeRotationOffset.z);
        }

        private void UpdateForcedLookYaw()
        {
            if (!HasForcedLook || _target == null)
                return;

            Vector3 direction = GetForcedLookPoint() - _target.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                _yaw = Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y;
            if(_targetSkills!=null && _targetSkills.IsBeingHooked)
            {
                var look=GetForcedLookPoint()-GetActiveCameraPosition(GetActiveRotation());
                if(look.sqrMagnitude>.001f) _pitch=Mathf.DeltaAngle(0,Quaternion.LookRotation(look).eulerAngles.x);
            }
        }

        private Vector3 GetForcedLookPoint()
        {
            if(_targetSkills!=null && _targetSkills.TryGetHookLookPoint(out var point)) return point;
            return _forcedLookTarget.position + Vector3.up * 1.2f;
        }

        private Vector3 GetActiveCameraPosition(Quaternion activeRotation)
        {
            if (_target == null)
                return transform.position;

            float visualScale = BattlePvp.Characters.CharacterPoseFollower.GetViewScale(_target);
            float scale = visualScale * Mathf.Abs(_target.lossyScale.y);
            if (IsLobby) return _target.position + Vector3.up * (1.2f * scale - _crouchDrop * visualScale) + activeRotation * Vector3.back * (_lobbyDistance * scale);

            Vector3 pivotPosition = _target.position + Vector3.up * (1.5f * scale - _crouchDrop * visualScale);
            Vector3 activeOffset = _useTemporaryOffset ? _temporaryOffset : Offset;
            return pivotPosition + ((activeRotation * new Vector3(activeOffset.x, 0f, activeOffset.z)) + (Vector3.up * activeOffset.y)) * scale;
        }
    }
}
