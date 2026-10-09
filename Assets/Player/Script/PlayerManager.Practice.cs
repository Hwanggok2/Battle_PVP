using BattlePvp.Networking;
using Mirror;
using UnityEngine;

public partial class PlayerManager
{
    private bool _practiceSteering;
    private Vector3 _practiceDirection, _practiceFacing;

    [Server]
    public void ServerSetPracticeSteering(Vector3 direction, Vector3 facing)
    {
        if (!(NetworkManager.singleton is BattleNetworkManager manager) || !manager.IsPractice || isLocalPlayer) return;
        _practiceSteering = true;
        _practiceDirection = direction;
        _practiceFacing = facing;
    }

    private void UpdatePracticeMovement()
    {
        if (controller == null || !controller.enabled) return;
        bool canMove = _healthSystem != null && !_healthSystem.IsDead && !IsBattleLoadingOrNotStarted();
        Vector3 direction = canMove ? _practiceDirection : Vector3.zero;
        if (canMove && _practiceFacing.sqrMagnitude > .001f && !isAttacking && !IsSkillMoveLocked)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(_practiceFacing), 540f * Time.deltaTime);
        Vector3 before = transform.position;
        MoveBallistic(Vector3.ClampMagnitude(direction, 1f) * RecordServerMovementControls().Speed, Mathf.Min(Time.deltaTime, .1f));
        Vector3 moved = transform.InverseTransformDirection(transform.position - before);
        Vector2 locomotion = new Vector2(moved.x, moved.z).sqrMagnitude > .000001f
            ? new Vector2(moved.x, moved.z).normalized : Vector2.zero;
        SetServerLocomotionState(PackLocomotion(locomotion));
        _remoteLocomotionTarget = locomotion;
        _serverMovement.CommitServerMove(transform.position, NetworkTime.time, controller.isGrounded, velocityY);
        GetComponent<ServerPoseHistory>()?.RecordNetworkPose(NetworkTime.time, transform.position, transform.rotation);
        if (NetworkTime.time >= _nextForcedPoseAt)
        {
            _nextForcedPoseAt = NetworkTime.time + Mathf.Max(.01f, _transformSyncInterval);
            RpcSyncTransform(transform.position, transform.rotation, NetworkTime.time, _movementEpoch);
        }
        UpdateRemoteLocomotionAnimation();
    }
}
