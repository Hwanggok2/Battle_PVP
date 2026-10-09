using BattlePvp.Combat;
using BattlePvp.Networking;
using Mirror;
using UnityEngine;

public partial class PlayerCombat
{
    private Vector3 _practiceAimDirection;
    private bool IsPracticeActor => isServer && !isLocalPlayer &&
        NetworkManager.singleton is BattleNetworkManager manager && manager.IsPractice && GetComponent<PracticeBot>() != null;

    [Server]
    public void ServerSetPracticeAim(Vector3 direction)
    {
        if (IsPracticeActor && CombatValidation.IsFinite(direction) && direction.sqrMagnitude > .001f)
            _practiceAimDirection = direction.normalized;
    }

    [Server]
    public bool ServerPracticeUseSkill(int slot, Vector3 direction)
    {
        if (!IsPracticeActor || IsServerTaunted || !CanBeginExpandedSkill ||
            !CombatValidation.IsFinite(direction) || direction.sqrMagnitude < .001f ||
            !TrySelectEquipped(slot, out var kind)) return false;
        ServerSetPracticeAim(direction);
        if (ExpandedSkillController.IsExpanded(kind))
        {
            if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
            return _expanded != null && _expanded.TryUse(slot, direction.normalized);
        }

        uint sequence = CombatRequestSequences.Next(_lastServerSkillSequence);
        if (kind == JobSkillKind.StrategistPresetChange)
            SetRuntimeStrategistTargetPreset(PracticeBot.RandomStats(), true);
        bool accepted = kind == JobSkillKind.MonostatStrLifesteal ? BeginMonostatStrSkill(SkillTime, sequence) :
            kind == JobSkillKind.MonostatAgiPoison ? BeginMonostatAgiSkill(SkillTime, sequence) :
            BeginAdvancedSkill((int)kind, direction.normalized, SkillTime, sequence);
        if (accepted) _lastServerSkillSequence = sequence;
        return accepted;
    }

    [Server]
    public void ServerPracticeBowAttack(Vector3 direction, bool hasTarget)
    {
        if (!IsPracticeActor || IsServerTaunted || _bowAttackController == null) return;
        if (hasTarget && CanServerUseBow && CombatValidation.IsFinite(direction) && direction.sqrMagnitude > .001f)
            _bowAttackController.ServerTickAutomatedAttack(direction.normalized);
        else if (_bowAttackController.IsCharging) _bowAttackController.CancelCharge();
    }

    [Server]
    public bool ServerPracticeAttack(Vector3 direction)
    {
        if (!IsPracticeActor || isAttacking || IsServerTaunted || _isBowEquipped ||
            IsBattleLoadingOrNotStarted() || !CombatValidation.IsFinite(direction) || direction.sqrMagnitude < .001f ||
            Vector3.Dot(transform.forward, direction.normalized) < .7f) return false;
        uint sequence = CombatRequestSequences.Next(_lastServerAttackSequence);
        _currentAttackSequence = sequence;
        StartAttack(0, false, direction);
        if (!isAttacking) return false;
        _lastServerAttackSequence = sequence;
        RpcStartAttackFast(0, direction, sequence, _currentAttackSpeed);
        RpcStartAttack(0, direction, sequence, _currentAttackSpeed);
        return true;
    }
}
