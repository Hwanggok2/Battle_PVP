using UnityEngine;

[CreateAssetMenu(fileName = "BowAttackSettings", menuName = "Battle PVP/Combat/Bow Attack Settings")]
public sealed class BowAttackSettings : ScriptableObject
{
    [SerializeField] private BowArrowProjectile _projectilePrefab;
    [SerializeField] private string _drawAnimationStateName = "Bow_Draw";
    [SerializeField] private string _aimHoldAnimationStateName = "Bow_AimHold";
    [SerializeField] private string _resetAnimationStateName = "New State";
    [SerializeField] private string _releaseTriggerName = "BowRelease";
    [SerializeField] private string _releaseAnimationStateName = "Bow_Release";
    [SerializeField] private int _animationLayer = 1;
    [Min(.1f)] [SerializeField] private float _animationPlaybackSpeed = 3f;
    [SerializeField] private float _projectileSpeed = 28f;
    [SerializeField] private float _projectileLifeSeconds = 4f;
    [SerializeField] private float _releaseInputLockFallbackSeconds = 1f;

    public BowArrowProjectile ProjectilePrefab => _projectilePrefab;
    public string DrawAnimationStateName => _drawAnimationStateName;
    public string AimHoldAnimationStateName => _aimHoldAnimationStateName;
    public string ResetAnimationStateName => _resetAnimationStateName;
    public string ReleaseTriggerName => _releaseTriggerName;
    public string ReleaseAnimationStateName => _releaseAnimationStateName;
    public int AnimationLayer => _animationLayer;
    public float AnimationPlaybackSpeed => Mathf.Max(.1f, _animationPlaybackSpeed);
    public float ProjectileSpeed => _projectileSpeed;
    public float ProjectileLifeSeconds => _projectileLifeSeconds;
    public float ReleaseInputLockFallbackSeconds => _releaseInputLockFallbackSeconds;
}
