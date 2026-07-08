using UnityEngine;

[DisallowMultipleComponent]
public sealed class BowAnimationEventReceiver : MonoBehaviour
{
    private BowAttackController _bowAttackController;

    public void OnBowNockArrow()
    {
        if (_bowAttackController == null)
            _bowAttackController = GetComponentInParent<BowAttackController>();

        if (_bowAttackController != null)
            _bowAttackController.OnBowNockArrow();
    }

    public void OnBowDrawReady()
    {
        if (_bowAttackController == null)
            _bowAttackController = GetComponentInParent<BowAttackController>();

        if (_bowAttackController != null)
            _bowAttackController.OnBowDrawReady();
    }

    public void OnBowReleaseArrow()
    {
        if (_bowAttackController == null)
            _bowAttackController = GetComponentInParent<BowAttackController>();

        if (_bowAttackController != null)
            _bowAttackController.OnBowReleaseArrow();
    }

    public void OnBowReleaseFinished()
    {
        if (_bowAttackController == null)
            _bowAttackController = GetComponentInParent<BowAttackController>();

        if (_bowAttackController != null)
            _bowAttackController.OnBowReleaseFinished();
    }
}
