using BattlePvp.Combat;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;

public sealed class BowArrowProjectile : NetworkBehaviour
{
    [SyncVar] private uint _ownerNetId;
    [SyncVar] private Vector3 _direction = Vector3.forward;
    [SyncVar] private float _speed = 28f;
    [SyncVar] private float _lifeSeconds = 4f;
    [SyncVar] private float _damageMultiplier = 1f;
    [SyncVar] private double _spawnedAt;
    [SyncVar] private Color _trailColor;
    [SerializeField] private Material _trailMaterial;
    private ArrowFlightTrail _trail;

    private bool _hasHit;
    private PlayerCombat _offlineOwner;
    private bool _offlineShot;
    private bool IsOfflineShot => _offlineShot && !NetworkServer.active && !NetworkClient.active;
    private BoxCollider _shape;
    private readonly CombatPhysicsQuery _query = new CombatPhysicsQuery();

    private struct Impact
    {
        public Collider Collider;
        public IDamageReceiver Target;
        public StatManager Stats;
        public HitBodyPart BodyPart;
        public Vector3 Point;
        public float Distance;
        public bool IsEnvironment => Target == null;
    }

    public void Initialize(uint ownerNetId, Vector3 direction, float speed, float lifeSeconds, float damageMultiplier)
    {
        _offlineShot = false; _offlineOwner = null;
        _ownerNetId = ownerNetId;
        _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        _speed = Mathf.Max(0.01f, speed);
        _lifeSeconds = Mathf.Max(0.1f, lifeSeconds);
        _damageMultiplier = Mathf.Max(0f, damageMultiplier);
        _spawnedAt = NetworkTime.time;
        _hasHit = false;
        var owner = ResolveOwner();
        _trailColor = StatVfxColor.Resolve(owner != null ? owner.GetComponent<StatManager>() : null);
    }

    public void InitializeOffline(PlayerCombat owner, Vector3 direction, float speed, float lifeSeconds, float damageMultiplier)
    {
        if (NetworkServer.active || NetworkClient.active || owner == null) return;
        Initialize(0, direction, speed, lifeSeconds, damageMultiplier);
        _offlineOwner = owner; _offlineShot = true; _spawnedAt = Time.timeAsDouble;
        _trailColor = StatVfxColor.Resolve(owner.GetComponent<StatManager>());
    }

    private void Start()
    {
        if (_hasHit || _trailMaterial == null || (NetworkServer.active && !NetworkClient.active)) return;
        _trail = ArrowFlightTrail.Acquire(_trailMaterial, _trailColor, transform.position, gameObject.scene);
    }

    private void LateUpdate() { if (_trail != null && !_hasHit) _trail.Sample(transform.position); }
    private void OnDestroy() => FinishTrail(transform.position);
    private void FinishTrail(Vector3 position)
    {
        if (_trail != null) _trail.Finish(position);
        _trail = null; // A fading trail can be rented by another shot after it finishes.
    }

    private void FinishFlight()
    {
        _hasHit = true;
        FinishTrail(transform.position);
        if (isServer)
        {
            RpcFinishFlight(transform.position);
            NetworkServer.Destroy(gameObject);
        }
        else if (IsOfflineShot) Destroy(gameObject);
    }

    [ClientRpc]
    private void RpcFinishFlight(Vector3 finalPosition)
    {
        if (isServer) return;
        _hasHit = true;
        transform.position = finalPosition;
        FinishTrail(finalPosition);
    }

    private void Update()
    {
        if (_hasHit) return;
        if (_offlineShot && (!IsOfflineShot || _offlineOwner == null)) { Destroy(gameObject); return; }
        if ((isServer || IsOfflineShot) && (IsOfflineShot ? Time.timeAsDouble : NetworkTime.time) - _spawnedAt >= _lifeSeconds)
        {
            FinishFlight();
            return;
        }
        Vector3 direction = _direction.sqrMagnitude > 0.001f ? _direction.normalized : transform.forward;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        Vector3 displacement = direction * (_speed * Time.deltaTime);
        if (isServer || IsOfflineShot) AdvanceServer(displacement, ResolveOwner());
        else transform.position += displacement;
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCollision(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollision(collision.collider);
    }

    private void HandleCollision(Collider other)
    {
        // Resolve the complete overlap instead of trusting callback order at a wall/body boundary.
        if ((isServer || IsOfflineShot) && other != null && !_hasHit)
            AdvanceServer(Vector3.zero, ResolveOwner());
    }

    private void AdvanceServer(Vector3 displacement, NetworkIdentity owner)
    {
        if (_hasHit || !isActiveAndEnabled || !CombatValidation.IsFinite(displacement)) return;
        if (!TryFindFirstImpact(displacement, owner != null ? owner.transform : null, out Impact impact))
        {
            transform.position += displacement;
            return;
        }
        transform.position += displacement.sqrMagnitude > 0f ? displacement.normalized * impact.Distance : Vector3.zero;
        _hasHit = true;
        try
        {
            if (impact.Target != null && owner != null)
            {
                PlayerCombat ownerCombat = owner.GetComponent<PlayerCombat>();
                ownerCombat?.ProcessBowProjectileHit(_damageMultiplier, impact.Stats, impact.Target, impact.Point,
                    impact.BodyPart != null ? impact.BodyPart.DamageMultiplier : 1f,
                    impact.BodyPart != null ? impact.BodyPart.Part : BodyPart.Body, netId);
            }
        }
        finally
        {
            FinishFlight();
        }
    }

    private bool TryFindFirstImpact(Vector3 displacement, Transform owner, out Impact closest)
    {
        if (_shape == null) _shape = GetComponent<BoxCollider>();
        Vector3 center = _shape != null ? _shape.transform.TransformPoint(_shape.center) : transform.position;
        Vector3 scale = _shape != null ? _shape.transform.lossyScale : Vector3.one;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 extents = _shape != null ? Vector3.Scale(_shape.size * 0.5f, scale) : Vector3.one * 0.01f;
        Quaternion rotation = _shape != null ? _shape.transform.rotation : transform.rotation;
        closest = default;
        bool found = false;

        // Casts do not reliably report colliders containing the starting volume.
        int overlaps = _query.OverlapBox(center, extents, rotation);
        for (int i = 0; i < overlaps; i++)
        {
            Collider collider = _query.Colliders[i];
            if (collider != null && TryCreateImpact(collider, collider.ClosestPoint(center), 0f, owner, out Impact candidate))
                SelectClosest(candidate, ref found, ref closest);
        }
        if (found) return true;

        float distance = displacement.magnitude;
        if (distance <= 0f) return false;
        int hits = _query.BoxCast(center, extents, rotation, displacement / distance, distance);
        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = _query.Hits[i];
            if (TryCreateImpact(hit.collider, hit.point, hit.distance, owner, out Impact candidate))
                SelectClosest(candidate, ref found, ref closest);
        }
        return found;
    }

    private bool TryCreateImpact(Collider other, Vector3 point, float distance, Transform owner, out Impact impact)
    {
        impact = default;
        if (other == null || !other.enabled || !other.gameObject.activeInHierarchy ||
            other.transform.IsChildOf(transform) || (owner != null && other.transform.IsChildOf(owner)) ||
            Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer) ||
            (_shape != null && Physics.GetIgnoreCollision(_shape, other))) return false;

        CombatHitTargets.Resolve(other, out IDamageReceiver target, out StatManager stats, out HitBodyPart bodyPart);
        // Player controllers and weapon triggers are not damage regions. Keep authored body-part rules.
        if (target is HealthSystem && bodyPart == null) return false;
        if (target is Behaviour behaviour && !behaviour.isActiveAndEnabled) target = null;
        if (stats == null) target = null;
        if (target == null && other.isTrigger) return false;
        impact = new Impact { Collider = other, Target = target, Stats = stats, BodyPart = bodyPart,
            Point = point, Distance = distance };
        return true;
    }

    private static void SelectClosest(Impact candidate, ref bool found, ref Impact closest)
    {
        if (!found || candidate.Distance < closest.Distance ||
            (candidate.Distance == closest.Distance && candidate.IsEnvironment && !closest.IsEnvironment))
        {
            closest = candidate;
            found = true;
        }
    }

    private NetworkIdentity ResolveOwner()
    {
        if (IsOfflineShot) return _offlineOwner != null ? _offlineOwner.GetComponent<NetworkIdentity>() : null;
        if (_ownerNetId == 0 || !NetworkServer.spawned.TryGetValue(_ownerNetId, out NetworkIdentity ownerIdentity))
            return null;
        return ownerIdentity;
    }

}
