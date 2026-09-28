using BattlePvp.CameraLogic;
using BattlePvp.Combat;
using Mirror;
using UnityEngine;

// Restore before animation, then aim the authored bow stance before the release-frame projectile query.
[DefaultExecutionOrder(-10)]
public sealed class BowAimRigTarget : NetworkBehaviour
{
    [SerializeField] private Transform _origin;
    // String-hand -> bow-hand direction sampled from Bow_AimHold, in character space.
    [SerializeField] private Vector3 _authoredAimDirection = new Vector3(-.97425f, -.12326f, -.18881f);
    private Transform _hips;
    private FollowCamera _followCamera;
    private PlayerCombat _combat;
    private Quaternion _baseHips, _baseSpine;
    private bool _applied, _aiming, _hasNetworkAimDirection;
    private Vector3 _networkAimDirection;
    private float _weight;

    public bool IsPosing => _aiming || _weight > 0f;

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        var animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (_origin == null) _origin = animator.GetBoneTransform(HumanBodyBones.Spine);
    }

    private void Update() => RestorePose();

    private void LateUpdate()
    {
        _weight = Mathf.MoveTowards(_weight, _aiming ? 1f : 0f, Time.deltaTime * 10f);
        if (_weight <= 0f || _hips == null || _origin == null) return;
        Vector3 direction = ResolveAimDirection();
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (horizontal.sqrMagnitude < .001f) horizontal = transform.forward;
        horizontal.Normalize();
        Vector3 authored = Vector3.ProjectOnPlane(transform.TransformDirection(_authoredAimDirection), Vector3.up).normalized;
        _baseHips = _hips.localRotation;
        _baseSpine = _origin.localRotation;
        // Turn the stance at the hips, rather than twisting the spine by the clip's ~98-degree sideways offset.
        Quaternion yaw = Quaternion.AngleAxis(Vector3.SignedAngle(authored, horizontal, Vector3.up), Vector3.up);
        _hips.rotation = Quaternion.Slerp(Quaternion.identity, yaw, _weight) * _hips.rotation;
        Quaternion pitch = Quaternion.FromToRotation(yaw * transform.TransformDirection(_authoredAimDirection).normalized, direction);
        _origin.rotation = Quaternion.Slerp(Quaternion.identity, pitch, _weight) * _origin.rotation;
        _applied = true;
    }

    private void RestorePose()
    {
        if (!_applied) return;
        if (_hips != null) _hips.localRotation = _baseHips;
        if (_origin != null) _origin.localRotation = _baseSpine;
        _applied = false;
    }

    private void OnDisable() { RestorePose(); _aiming = false; _weight = 0f; }

    // Keep the existing controller entry point and serialized component identity.
    public void SetYawOffsetActive(bool active) => _aiming = active;
    public void SetNetworkAimDirection(Vector3 direction)
    {
        _hasNetworkAimDirection = CombatValidation.IsFinite(direction) && direction.sqrMagnitude > .001f;
        if (_hasNetworkAimDirection) _networkAimDirection = direction.normalized;
    }
    public void ClearNetworkAimDirection() => _hasNetworkAimDirection = false;

    private Vector3 ResolveAimDirection()
    {
        bool local = isLocalPlayer || (!NetworkClient.active && !NetworkServer.active);
        if (local && (_combat == null || !_combat.IsServerTaunted))
        {
            if (_followCamera == null) _followCamera = FindFirstObjectByType<FollowCamera>();
            if (_followCamera != null && _followCamera.Target == transform) return _followCamera.GetAimDirection().normalized;
        }
        if (_combat != null && !_combat.IsServerTaunted && NetworkClient.active)
            return _combat.ReplicatedLookDirection;
        return _hasNetworkAimDirection ? _networkAimDirection : transform.forward;
    }
}
