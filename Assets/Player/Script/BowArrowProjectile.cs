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
    private bool _hasPredictedHit;

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
        if (isServer)
            TryHit(other);
        else if (isClient)
            TryPredictHit(other);
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
        HitBodyPart bodyPart = ResolveBodyPart(other);
        if (target == null || targetStats == null)
            return;
        if (target is HealthSystem && bodyPart == null)
            return;

        _hasHit = true;
        Vector3 hitPoint = other.ClosestPoint(transform.position);
        float bodyPartMultiplier = bodyPart != null ? bodyPart.DamageMultiplier : 1f;
        BodyPart part = bodyPart != null ? bodyPart.Part : BodyPart.Body;
        ownerCombat?.ProcessBowProjectileHit(_damageMultiplier, targetStats, target, hitPoint, bodyPartMultiplier, part);
        NetworkServer.Destroy(gameObject);
    }

    [Server]
    private PlayerCombat ResolveOwnerCombat()
    {
        if (_ownerNetId == 0 || !NetworkServer.spawned.TryGetValue(_ownerNetId, out NetworkIdentity ownerIdentity))
            return null;

        return ownerIdentity.GetComponent<PlayerCombat>();
    }

    [Client]
    private void TryPredictHit(Collider other)
    {
        if (_hasPredictedHit || other == null || _ownerNetId == 0)
            return;
        if (!NetworkClient.spawned.TryGetValue(_ownerNetId, out NetworkIdentity ownerIdentity) || !ownerIdentity.isLocalPlayer)
            return;
        if (other.transform.root == ownerIdentity.transform.root)
            return;

        HealthSystem targetHealth = other.GetComponentInParent<HealthSystem>();
        StatManager targetStats = other.GetComponentInParent<StatManager>();
        AttackProcessor attackProcessor = ownerIdentity.GetComponent<AttackProcessor>();
        HitBodyPart bodyPart = ResolveBodyPart(other);
        if (targetHealth == null || targetStats == null || attackProcessor == null)
            return;
        if (bodyPart == null)
            return;

        float predictedDamage = attackProcessor.PredictSkillHitDamage(_damageMultiplier, targetStats, bodyPart.DamageMultiplier);
        Vector3 hitPoint = other.ClosestPoint(transform.position);
        targetHealth.ShowPredictedPhysicalDamagePopup(hitPoint, predictedDamage, _ownerNetId);
        _hasPredictedHit = true;
    }

    private static HitBodyPart ResolveBodyPart(Collider other)
    {
        HitBodyPart bodyPart = other.GetComponent<HitBodyPart>();
        return bodyPart != null ? bodyPart : other.GetComponentInParent<HitBodyPart>();
    }
}
