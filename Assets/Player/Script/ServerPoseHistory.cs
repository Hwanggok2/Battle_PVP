using System;
using System.Collections.Generic;
using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;

[DefaultExecutionOrder(1050)]
internal sealed class ServerPoseHistory : MonoBehaviour
{
    private const int Capacity = 64;
    private const double RecordInterval = 1d / 30d;

    private readonly double[] _times = new double[Capacity];
    private readonly Vector3[] _positions = new Vector3[Capacity];
    private readonly bool[] _heldSamples = new bool[Capacity];
    private int _lastWrittenIndex;
    private int _nextIndex;
    private int _count;
    private double _lastRecordTime = double.NegativeInfinity;
    private Collider[] _bodyColliders = Array.Empty<Collider>();
    private HitBodyPart[] _bodyParts = Array.Empty<HitBodyPart>();
    private readonly Bounds[][] _bodyBounds = new Bounds[Capacity][];
    private readonly Dictionary<Collider, HitBodyPart> _partsByCollider = new Dictionary<Collider, HitBodyPart>();
    private readonly Dictionary<Collider, Transform> _parentsByCollider = new Dictionary<Collider, Transform>();
    private HealthSystem _ownerHealth;
    private StatManager _ownerStats;
    private NetworkIdentity _ownerIdentity;
    private bool _hasNetworkPose;
    private double _networkPoseTime;
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;

    private void Awake() => RefreshBodyParts();

    public void ResetHistory()
    {
        _count = 0;
        _nextIndex = 0;
        _lastRecordTime = double.NegativeInfinity;
        _hasNetworkPose = false;
        if (NetworkServer.active) RecordPose(NetworkTime.time, transform.position, true);
    }

    private void OnTransformChildrenChanged() => RefreshBodyParts();

    public void RefreshBodyParts()
    {
        _ownerHealth = GetComponent<HealthSystem>();
        _ownerStats = GetComponent<StatManager>();
        _ownerIdentity = GetComponent<NetworkIdentity>();
        _partsByCollider.Clear();
        _parentsByCollider.Clear();
        _bodyColliders = GetComponentsInChildren<Collider>(true);
        _bodyParts = new HitBodyPart[_bodyColliders.Length];
        for (int i = 0; i < _bodyColliders.Length; i++)
        {
            _bodyParts[i] = _bodyColliders[i].GetComponentInParent<HitBodyPart>();
            _partsByCollider[_bodyColliders[i]] = _bodyParts[i];
            _parentsByCollider[_bodyColliders[i]] = _bodyColliders[i].transform.parent;
        }
        for (int i = 0; i < Capacity; i++)
            _bodyBounds[i] = new Bounds[_bodyColliders.Length];
        _count = 0;
    }

    public bool TryResolveHitCollider(Collider collider, out HitBodyPart part,
        out HealthSystem health, out StatManager stats)
    {
        if (!_partsByCollider.TryGetValue(collider, out part) ||
            !_parentsByCollider.TryGetValue(collider, out Transform cachedParent) || cachedParent != collider.transform.parent ||
            (part == null && collider.GetComponentInParent<HitBodyPart>() != null))
        {
            // A new collider can be added below a nested model without a root hierarchy callback.
            RefreshBodyParts();
            _partsByCollider.TryGetValue(collider, out part);
        }
        health = _ownerHealth;
        stats = _ownerStats;
        return health != null && stats != null;
    }

    public bool TryValidateBodyPart(double time, BodyPart part, Vector3 point,
        out Vector3 validatedPoint, out float multiplier)
    {
        validatedPoint = default;
        multiplier = 0f;
        if (!double.IsFinite(time) || !CombatValidation.IsFinite(point)) return false;
        int nearest = -1;
        double delta = 0.15d;
        for (int i = 0; i < _count; i++)
        {
            double candidateDelta = Math.Abs(_times[i] - time);
            if (candidateDelta <= delta) { nearest = i; delta = candidateDelta; }
        }
        if (nearest < 0) return false;
        for (int i = 0; i < _bodyParts.Length; i++)
        {
            HitBodyPart candidate = _bodyParts[i];
            if (candidate == null || candidate.Part != part || _bodyColliders[i] == null) continue;
            Bounds bounds = _bodyBounds[nearest][i];
            if (bounds.size.sqrMagnitude <= 0f) continue;
            Bounds tolerantBounds = bounds;
            tolerantBounds.Expand(0.3f);
            if (!tolerantBounds.Contains(point)) continue;
            validatedPoint = bounds.ClosestPoint(point);
            multiplier = candidate.DamageMultiplier;
            return true;
        }
        return false;
    }

    private void LateUpdate()
    {
        if (!NetworkServer.active)
            return;

        CombatPhysicsQuery.SyncAnimatedTransforms();
        RecordCurrentPose(NetworkTime.time);
    }

    public void RecordNetworkPose(double time, Vector3 position)
        => RecordNetworkPose(time, position, transform.rotation);

    public void RecordNetworkPose(double time, Vector3 position, Quaternion rotation)
    {
        if (!NetworkServer.active || !double.IsFinite(time) || !CombatValidation.IsFinite(position) ||
            !ServerMovementValidator.IsValidRotation(rotation))
            return;

        StoreNetworkPose(time, position, rotation.normalized);
    }

    private void StoreNetworkPose(double time, Vector3 position, Quaternion rotation)
    {
        if (_hasNetworkPose && time < _networkPoseTime) return;
        _hasNetworkPose = true;
        _networkPoseTime = time;
        _networkPosition = position;
        _networkRotation = rotation;
        RecordAcceptedPose(time, position, rotation, true);
        // A network packet can also arrive after newer periodic hold samples were recorded.
        // Those holds must use the newly known pose, not retain a false backwards movement.
        for (int i = 0; i < _count; i++)
        {
            if (!_heldSamples[i] || _times[i] <= time) continue;
            _positions[i] = position;
            Array.Copy(_bodyBounds[_lastWrittenIndex], _bodyBounds[i], _bodyColliders.Length);
        }
    }

    private void RecordCurrentPose(double time)
    {
        // A host renders remote players behind their latest accepted network pose.
        // Never insert that displayed root as a newer authoritative position.
        if (_hasNetworkPose && (_ownerIdentity == null || !_ownerIdentity.isLocalPlayer))
            RecordAcceptedPose(time, _networkPosition, _networkRotation, false, held: true);
        else
            RecordPose(time, transform.position, false);
    }

    private void RecordPose(double time, Vector3 position, bool force)
        => RecordAcceptedPose(time, position, transform.rotation, force);

    private void RecordAcceptedPose(double time, Vector3 position, Quaternion rotation, bool force, bool held = false)
    {
        if (!CombatValidation.IsFinite(position) || !double.IsFinite(time)) return;
        if (!force && time - _lastRecordTime < RecordInterval)
            return;

        // A delayed accepted packet can share a timestamp with a periodic hold sample.
        // Replace that entire sample so position and body lookup cannot select different copies.
        int index = _nextIndex;
        bool replacing = false;
        for (int i = 0; i < _count; i++)
            if (_times[i] == time) { index = i; replacing = true; break; }
        _times[index] = time;
        _positions[index] = position;
        _heldSamples[index] = held;
        _lastWrittenIndex = index;
        for (int i = 0; i < _bodyColliders.Length; i++)
            _bodyBounds[index][i] = _bodyParts[i] != null && _bodyColliders[i] != null &&
                _bodyColliders[i].enabled ? RebaseBounds(_bodyColliders[i].bounds,
                    transform.position, transform.rotation, position, rotation) : default;
        if (!replacing)
        {
            _nextIndex = (_nextIndex + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }
        _lastRecordTime = Math.Max(_lastRecordTime, time);
    }

    private static Bounds RebaseBounds(Bounds bounds, Vector3 displayedPosition, Quaternion displayedRotation,
        Vector3 acceptedPosition, Quaternion acceptedRotation)
    {
        if (bounds.size.sqrMagnitude <= 0f) return default;
        Quaternion delta = acceptedRotation * Quaternion.Inverse(displayedRotation);
        Vector3 center = acceptedPosition + delta * (bounds.center - displayedPosition);
        // Rotation of an existing world AABB stays conservative; no live collider is moved.
        Vector3 x = delta * new Vector3(bounds.extents.x, 0f, 0f);
        Vector3 y = delta * new Vector3(0f, bounds.extents.y, 0f);
        Vector3 z = delta * new Vector3(0f, 0f, bounds.extents.z);
        Vector3 extents = new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(center, extents * 2f);
    }

    public bool TrySample(double time, out Vector3 position)
    {
        position = transform.position;
        if (_count == 0)
            return false;

        int beforeIndex = -1;
        int afterIndex = -1;
        double beforeTime = double.NegativeInfinity;
        double afterTime = double.PositiveInfinity;

        for (int i = 0; i < _count; i++)
        {
            double sampleTime = _times[i];
            if (sampleTime <= time && sampleTime > beforeTime)
            {
                beforeTime = sampleTime;
                beforeIndex = i;
            }

            if (sampleTime >= time && sampleTime < afterTime)
            {
                afterTime = sampleTime;
                afterIndex = i;
            }
        }

        if (beforeIndex < 0)
            beforeIndex = afterIndex;
        if (afterIndex < 0)
            afterIndex = beforeIndex;
        if (beforeIndex < 0 || afterIndex < 0)
            return false;

        if (beforeIndex == afterIndex || afterTime <= beforeTime)
        {
            position = _positions[beforeIndex];
            return true;
        }

        float t = Mathf.Clamp01((float)((time - beforeTime) / (afterTime - beforeTime)));
        position = Vector3.Lerp(_positions[beforeIndex], _positions[afterIndex], t);
        return true;
    }
}
