using BattlePvp.CameraLogic;
using Mirror;
using UnityEngine;

public sealed class BowAimRigTarget : NetworkBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private Transform _origin;
    [SerializeField] private float _distance = 8f;
    [SerializeField] private float _yawOffsetDegrees;
    [SerializeField] private bool _ignorePitch = true;
    [SerializeField] private Vector3 _localOffset = new Vector3(0f, 1.35f, 0f);

    private FollowCamera _followCamera;
    private bool _useYawOffset;

    private void LateUpdate()
    {
        UpdateTarget();
    }

    public void SetYawOffsetActive(bool active)
    {
        _useYawOffset = active;
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        if (_target == null)
            return;

        if (_origin == null)
            _origin = transform;

        Vector3 aimDirection = ResolveAimDirection();
        if (_useYawOffset && Mathf.Abs(_yawOffsetDegrees) > 0.001f)
            aimDirection = Quaternion.AngleAxis(_yawOffsetDegrees, Vector3.up) * aimDirection;

        Vector3 origin = _origin.position + _origin.TransformVector(_localOffset);
        _target.position = origin + aimDirection * Mathf.Max(0.1f, _distance);
        _target.rotation = Quaternion.LookRotation(aimDirection, Vector3.up);
    }

    private Vector3 ResolveAimDirection()
    {
        Vector3 aimDirection = Vector3.zero;
        if (isLocalPlayer)
        {
            if (_followCamera == null)
                _followCamera = FindFirstObjectByType<FollowCamera>();

            if (_followCamera != null)
                aimDirection = _followCamera.GetAimDirection();
        }

        if (aimDirection.sqrMagnitude <= 0.001f)
            aimDirection = _origin != null ? _origin.forward : transform.forward;

        if (_ignorePitch)
            aimDirection.y = 0f;

        if (aimDirection.sqrMagnitude <= 0.001f)
        {
            Vector3 fallback = _origin != null ? _origin.forward : transform.forward;
            fallback.y = 0f;
            aimDirection = fallback.sqrMagnitude > 0.001f ? fallback : Vector3.forward;
        }

        return aimDirection.normalized;
    }
}
