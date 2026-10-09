using BattlePvp.Combat;
using Mirror;
using UnityEngine;

public partial class PlayerManager
{
    private bool _airJumpUsed;
    private bool _awaitingAirJump;
    private uint _pendingAirJumpEpoch;
    private double _lastJumpAt = double.NegativeInfinity;
    private bool CanAirJump => !_airJumpUsed && LocalInputTime - _lastJumpAt > .08d &&
        GetComponent<PassiveLoadout>()?.Has(PassiveKind.DoubleJump) == true;

    // Send the pre-launch pose on the reliable channel, before spending the extra jump.
    [Command] private void CmdAirJump(Vector3 position, Quaternion rotation, double sampleTime, uint epoch)
    {
        try
        {
            if (epoch != _movementEpoch || _serverMotionActive || IsSkillJumpLocked || IsSkillMoveLocked || IsEmoteBlockingJump ||
                _healthSystem == null || _healthSystem.IsDead || IsBattleLoadingOrNotStarted() ||
                GetComponent<ExpandedSkillController>()?.BlocksVoluntaryDisplacement == true ||
                GetComponent<PassiveLoadout>()?.Has(PassiveKind.DoubleJump) != true) return;
            ApplySyncedTransformOnServer(position, rotation, sampleTime, true);
            if (System.Math.Abs(_serverMovement.LastSampleTime - sampleTime) > .0001d ||
                Vector3.Distance(_serverMovement.Position, position) > .01f) return;
            _serverMovement.TryBeginAirJump(jumpHeight, sampleTime);
        }
        finally { TargetAirJumpProcessed(connectionToClient, epoch); }
    }

    [TargetRpc] private void TargetAirJumpProcessed(NetworkConnectionToClient target, uint epoch)
    {
        if (epoch == _pendingAirJumpEpoch) _awaitingAirJump = false;
    }
}
