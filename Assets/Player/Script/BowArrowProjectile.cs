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

    private bool _hasHit;

    public void Initialize(uint ownerNetId, Vector3 direction, float speed, float lifeSeconds, float damageMultiplier)
    {
        _ownerNetId = ownerNetId;
        _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        _speed = Mathf.Max(0.01f, speed);
        _lifeSeconds = Mathf.Max(0.1f, lifeSeconds);
        _damageMultiplier = Mathf.Max(0f, damageMultiplier);
        _spawnedAt = NetworkTime.time;
    }

    private void Update()
    {
        Vector3 direction = _direction.sqrMagnitude > 0.001f ? _direction.normalized : transform.forward;
        transform.position += direction * (_speed * Time.deltaTime);
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        if (isServer && NetworkTime.time - _spawnedAt >= _lifeSeconds)
            NetworkServer.Destroy(gameObject);
    }

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    [ServerCallback]
    private void OnCollisionEnter(Collision collision)
    {
        TryHit(collision.collider);
    }

    [Server]
    private void TryHit(Collider other)
    {
        if (_hasHit || other == null)
            return;

        PlayerCombat ownerCombat = ResolveOwnerCombat();
        if (ownerCombat != null && other.transform.root == ownerCombat.transform.root)
            return;

        IDamageReceiver target = other.GetComponentInParent<IDamageReceiver>();
        StatManager targetStats = other.GetComponentInParent<StatManager>();
        if (target == null || targetStats == null)
            return;

        _hasHit = true;
        Vector3 hitPoint = other.ClosestPoint(transform.position);
        ownerCombat?.ProcessBowProjectileHit(_damageMultiplier, targetStats, target, hitPoint);
        NetworkServer.Destroy(gameObject);
    }

    [Server]
    private PlayerCombat ResolveOwnerCombat()
    {
        if (_ownerNetId == 0 || !NetworkServer.spawned.TryGetValue(_ownerNetId, out NetworkIdentity ownerIdentity))
            return null;

        return ownerIdentity.GetComponent<PlayerCombat>();
    }
}
