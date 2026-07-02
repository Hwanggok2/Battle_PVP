using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class KickSkillHitBox : MonoBehaviour
{
    [SerializeField] private PlayerCombat _owner;
    [SerializeField] private bool _logOverlapCount;

    private Collider _collider;
    private BoxCollider _boxCollider;
    private readonly Collider[] _overlapResults = new Collider[32];

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        _boxCollider = _collider as BoxCollider;
        _collider.isTrigger = true;
        _collider.enabled = false;

        if (_owner == null)
            _owner = GetComponentInParent<PlayerCombat>();
    }

    public void Initialize(PlayerCombat owner)
    {
        _owner = owner;

        if (_collider == null)
            _collider = GetComponent<Collider>();
        _boxCollider = _collider as BoxCollider;

        if (_collider != null)
        {
            _collider.isTrigger = true;
            _collider.enabled = false;
        }
    }

    public void SetActive(bool active)
    {
        if (_collider == null)
            _collider = GetComponent<Collider>();

        if (_collider != null)
            _collider.enabled = active;
    }

    public void ProcessCurrentOverlaps()
    {
        if (_owner == null)
            return;

        if (!TryGetOverlapBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation))
            return;

        int count = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            _overlapResults,
            rotation,
            ~0,
            QueryTriggerInteraction.Collide);

        if (_logOverlapCount)
            Debug.Log($"[KickSkillHitBox] Overlap count: {count}", this);

        for (int i = 0; i < count; i++)
            _owner.TryProcessKickHit(_overlapResults[i]);
    }

    public bool TryGetOverlapBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        if (_collider == null)
            _collider = GetComponent<Collider>();

        if (_boxCollider == null)
            _boxCollider = _collider as BoxCollider;

        if (_boxCollider != null)
        {
            center = transform.TransformPoint(_boxCollider.center);
            halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, Abs(transform.lossyScale));
            rotation = transform.rotation;
            return true;
        }

        if (_collider != null)
        {
            Bounds bounds = _collider.bounds;
            center = bounds.center;
            halfExtents = bounds.extents;
            rotation = transform.rotation;
            return true;
        }

        center = default;
        halfExtents = default;
        rotation = Quaternion.identity;
        return false;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void OnTriggerEnter(Collider other)
    {
        _owner?.TryProcessKickHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        _owner?.TryProcessKickHit(other);
    }
}
