using BattlePvp.Combat;
using BattlePvp.Logic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerCombat
{
    [SyncVar] private bool _weaponGuard;
    [SyncVar] private double _guardStartedAt, _recoilUntil, _riposteUntil;
    private double _nextGuardRequest, _guardLeaseUntil;
    private bool _sentGuard, _shownGuard;
    private double _shownRecoil;
    public MeleeWeaponKind WeaponKind => GetComponent<WeaponLoadout>()?.Selected ?? MeleeWeaponKind.Sword;
    public bool IsWeaponGuarding => _weaponGuard && SkillTime >= _recoilUntil && !isAttacking && !_isBowEquipped;
    public bool IsWeaponRecoiling => SkillTime < _recoilUntil;
    public bool IsRiposteReady => SkillTime < _riposteUntil;
    private bool WeaponInputLocked => IsWeaponGuarding || IsWeaponRecoiling;
    internal bool MeleeEquipped => !_isBowEquipped;
    private string WeaponReadyState => WeaponCatalog.Instance?.Find(WeaponKind)?.ReadyState;
    private bool CanHoldWeaponPose => !_isBowEquipped && !IsWeaponRecoiling && !IsSkillAnimationOrActionLocked() &&
        (_healthSystem == null || !_healthSystem.IsDead) &&
        (_playerManager == null || (!_playerManager.IsEmoteBlockingAttack && !_playerManager.IsSkillAttackLocked));
    internal bool UsesTwoHandedGrip => CanHoldWeaponPose && animator != null &&
        (isAttacking || IsWeaponGuarding || animator.GetCurrentAnimatorStateInfo(1).IsTag("WeaponTwoHanded") ||
         (animator.IsInTransition(1) && animator.GetNextAnimatorStateInfo(1).IsTag("WeaponTwoHanded")));

    public void ApplyWeaponLoadout(WeaponCatalog.Entry entry, bool resetCombat = true)
    {
        if (entry == null) return;
        if (resetCombat) CancelAllCombatActions();
        comboList = entry.Attacks;
        if (resetCombat && animator != null && animator.isActiveAndEnabled)
            animator.Play(CanHoldWeaponPose && !string.IsNullOrEmpty(entry.ReadyState) ? entry.ReadyState : "New State", 1, 0);
    }

    private bool WeaponAttackAllowed(int index)
    {
        if (index == 3) return WeaponKind == MeleeWeaponKind.Sword;
        if (index == 4) return WeaponKind == MeleeWeaponKind.Greatsword && SkillTime < _riposteUntil;
        if (index == 5) return WeaponKind == MeleeWeaponKind.Greatsword &&
            ((isAttacking && currentComboIndex == 4) || _serverCombo.CanStart(5, false, 4, 1, SkillTime));
        return index >= 0 && index <= (WeaponKind == MeleeWeaponKind.Axe ? 0 : 2);
    }

    private bool CanStartWeaponSequence(int index, float progress)
    {
        if (index == 3 || index == 4) return !isAttacking;
        return _serverCombo.CanStart(index, isAttacking, currentComboIndex, progress, SkillTime);
    }
    private bool CanContinueWeaponCombo(int index) => comboList != null && index >= 0 && index + 1 < comboList.Length && comboList[index + 1] != null &&
        (index == 4 || (index < 2 && WeaponKind != MeleeWeaponKind.Axe));

    private void UpdateWeaponCombat()
    {
        bool authority = NetworkServer.active || !NetworkClient.active;
        bool invalid = _healthSystem != null && _healthSystem.IsDead || _isBowEquipped || IsServerTaunted || IsBattleLoadingOrNotStarted() ||
            (_expanded != null && _expanded.BlocksCombat) || (_playerManager != null && _playerManager.IsSkillAttackLocked);
        if (authority && (invalid || (NetworkServer.active && SkillTime > _guardLeaseUntil))) _weaponGuard = false;
        if (ShouldHandleLocalInput)
        {
            bool overUi = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            bool input = !invalid && !GameInputController.IsPaused && !GameInputController.IsTextInputActive &&
                (GameInputController.IsPointerCaptured || !overUi) && Mouse.current != null;
            bool held = input && Mouse.current.rightButton.isPressed;
            bool guarding = held && !IsRiposteReady && (WeaponKind == MeleeWeaponKind.SwordShield || WeaponKind == MeleeWeaponKind.Greatsword);
            if (guarding != _sentGuard || (guarding && SkillTime >= _nextGuardRequest))
            {
                _sentGuard = guarding; _nextGuardRequest = SkillTime + .15;
                if (NetworkClient.active && isLocalPlayer) CmdWeaponGuard(guarding);
                else SetWeaponGuard(guarding);
            }
            if (input && Mouse.current.rightButton.wasPressedThisFrame && WeaponKind == MeleeWeaponKind.Sword &&
                !isAttacking && !IsSkillCastingOrAttackLocked()) StartAttack(3, true, GetCurrentMeleeAimDirection(3));
        }
        if (animator == null || !animator.isActiveAndEnabled) return;
        if (IsWeaponRecoiling && _shownRecoil != _recoilUntil)
        {
            _shownRecoil = _recoilUntil; CancelCurrentAttack();
            animator.Play("Weapon_Recoil", 1, 0);
        }
        bool showGuard = IsWeaponGuarding;
        if (showGuard != _shownGuard)
        {
            _shownGuard = showGuard;
            if (!IsWeaponRecoiling && !isAttacking) animator.CrossFadeInFixedTime(showGuard ?
                (WeaponKind == MeleeWeaponKind.SwordShield ? "Weapon_ShieldGuard" : "Weapon_SwordGuard") : "New State", .08f, 1);
        }
        UpdateWeaponReadyPose();
    }

    private void UpdateWeaponReadyPose()
    {
        string ready = WeaponReadyState;
        if (string.IsNullOrEmpty(ready) || animator.IsInTransition(1)) return;
        var state = animator.GetCurrentAnimatorStateInfo(1);
        bool hold = CanHoldWeaponPose && !isAttacking && !IsWeaponGuarding;
        if (hold && state.IsName("New State")) animator.CrossFadeInFixedTime(ready, .12f, 1);
        else if (!hold && state.IsName(ready)) animator.CrossFadeInFixedTime("New State", .08f, 1);
    }

    [Command] private void CmdWeaponGuard(bool held) => SetWeaponGuard(held);
    internal bool SetWeaponGuard(bool held)
    {
        if (!held) { _weaponGuard = false; return true; }
        if ((WeaponKind != MeleeWeaponKind.SwordShield && WeaponKind != MeleeWeaponKind.Greatsword) ||
            !HasAuthoritativeCombatStats || IsBattleLoadingOrNotStarted() || IsServerTaunted || _isBowEquipped || isAttacking || IsWeaponRecoiling ||
            (_healthSystem != null && _healthSystem.IsDead) || (_playerManager != null && (_playerManager.IsSkillAttackLocked || _playerManager.IsEmoteBlockingAttack)) ||
            (!_weaponGuard && IsSkillCastingOrAttackLocked())) return false;
        if (!_weaponGuard) _guardStartedAt = SkillTime;
        _weaponGuard = true; _guardLeaseUntil = SkillTime + .5;
        return true;
    }

    // Called only from the authoritative melee hit path, before HP/shield damage and on-hit effects.
    internal bool TryBlockMelee(PlayerCombat attacker)
    {
        if ((NetworkClient.active && !NetworkServer.active) || attacker == null || attacker == this ||
            !IsWeaponGuarding || (_healthSystem != null && _healthSystem.IsDead) ||
            (NetworkServer.active && SkillTime > _guardLeaseUntil)) return false;
        var catalog = WeaponCatalog.Instance;
        Vector3 toAttacker = Vector3.ProjectOnPlane(attacker.transform.position - transform.position, transform.up);
        if (toAttacker.sqrMagnitude < .0001f || Vector3.Angle(transform.forward, toAttacker) > (catalog?.GuardArcDegrees ?? 120f) * .5f) return false;
        bool parry = WeaponKind == MeleeWeaponKind.Greatsword && SkillTime - _guardStartedAt <= (catalog?.ParryWindowSeconds ?? .3f);
        if (WeaponKind == MeleeWeaponKind.Greatsword && !parry) return false;
        if (parry) { _riposteUntil = SkillTime + (catalog?.RiposteReadySeconds ?? 2.5f); _weaponGuard = false; }
        attacker.ReceiveWeaponRecoil(catalog?.ShieldRecoilSeconds ?? .65f);
        return true;
    }

    private void ReceiveWeaponRecoil(float seconds)
    {
        CancelCurrentAttack();
        _serverReportableAttackSequence = 0; _serverAttackHitTargetNetIds.Clear();
        _weaponGuard = false; _riposteUntil = 0;
        _recoilUntil = SkillTime + seconds;
        if (NetworkServer.active) RpcWeaponRecoil(_recoilUntil);
    }
    [ClientRpc] private void RpcWeaponRecoil(double until)
    { _recoilUntil = until; CancelCurrentAttack(); }
    private void ResetWeaponCombat()
    {
        _weaponGuard = _sentGuard = _shownGuard = false;
        _guardStartedAt = _guardLeaseUntil = _recoilUntil = _riposteUntil = 0;
    }
}
