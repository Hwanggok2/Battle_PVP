using BattlePvp.Combat;
using BattlePvp.Logic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerCombat
{
    private const float AxeHitAnimationMultiplier = 2f;
    [SyncVar] private bool _weaponGuard;
    [SyncVar] private bool _axeFollowupReady;
    private bool _axeSwingAccelerated;
    private float _axeRecoveryAnimationSpeed;
    [SyncVar] private double _guardStartedAt, _recoilUntil, _riposteUntil;
    private double _nextGuardRequest, _guardLeaseUntil;
    private bool _sentGuard, _shownGuard;
    private double _shownRecoil;
    public MeleeWeaponKind WeaponKind => GetComponent<WeaponLoadout>()?.Selected ?? MeleeWeaponKind.Sword;
    public float WeaponMeleeDamageMultiplier => WeaponCatalog.Instance?.Find(WeaponKind)?.MeleeDamageMultiplier ?? 1f;
    public bool IsWeaponGuarding => _weaponGuard && SkillTime >= _recoilUntil && !isAttacking && !_isBowEquipped;
    public bool IsWeaponRecoiling => SkillTime < _recoilUntil;
    public bool IsRiposteReady => SkillTime < _riposteUntil;
    private bool WeaponInputLocked => IsWeaponGuarding || IsWeaponRecoiling;
    internal bool MeleeEquipped => !_isBowEquipped;
    internal Vector3 WeaponLookDirection => Quaternion.AngleAxis(_lookPitch * _lookPoseWeight, transform.right) * transform.forward;
    internal float WeaponCameraDrop => Mathf.Max(_playerManager != null ? _playerManager.CrouchCameraDrop : 0f,
        _expanded != null ? _expanded.SkillCameraDrop : 0f);
    private string WeaponReadyState => WeaponCatalog.Instance?.Find(WeaponKind)?.ReadyState;
    private bool CanHoldWeaponPose => !_isBowEquipped && !IsWeaponRecoiling && !IsSkillAnimationOrActionLocked() &&
        (_healthSystem == null || !_healthSystem.IsDead) &&
        (_playerManager == null || (!_playerManager.IsEmoteBlockingAttack && !_playerManager.IsSkillAttackLocked));
    internal bool UsesTwoHandedGrip => CanHoldWeaponPose && animator != null &&
        (isAttacking || IsWeaponGuarding || animator.GetCurrentAnimatorStateInfo(1).IsTag("WeaponTwoHanded") ||
         (animator.IsInTransition(1) && animator.GetNextAnimatorStateInfo(1).IsTag("WeaponTwoHanded")));

    private AnimatorStateInfo AttackAnimationState
    {
        get
        {
            if(animator.IsInTransition(1))
            {
                var next=animator.GetNextAnimatorStateInfo(1);
                if(_meleeAnimationData!=null && next.IsName(_meleeAnimationData.animationName)) return next;
            }
            return animator.GetCurrentAnimatorStateInfo(1);
        }
    }

    public void ApplyWeaponLoadout(WeaponCatalog.Entry entry, bool resetCombat = true)
    {
        if (entry == null) return;
        // Changing equipment must not remove active buffs or reset skill state.
        if (resetCombat) { ResetWeaponCombat(); CancelCurrentAttack(); }
        comboList = entry.Attacks;
        SetWeaponFootworkWeight(entry.TwoHanded && isAttacking);
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

    private float BeginWeaponAttackSpeed()
    {
        float speed = ResolveCurrentAttackSpeed();
        _axeRecoveryAnimationSpeed = 0f;
        _axeSwingAccelerated = WeaponKind == MeleeWeaponKind.Axe && _axeFollowupReady;
        if (WeaponKind != MeleeWeaponKind.Axe) return speed;
        if (_axeSwingAccelerated) speed *= AxeHitAnimationMultiplier;
        // Consume only after accepting the swing. Clients predict from replicated
        // readiness; the acceptance reply corrects their speed without replaying it.
        if (NetworkServer.active || !NetworkClient.active) _axeFollowupReady = false;
        return speed;
    }

    internal void NotifyAcceptedMeleeHit(AttackData attack)
    {
        if ((NetworkClient.active && !NetworkServer.active) || WeaponKind != MeleeWeaponKind.Axe ||
            _isBowEquipped || (_healthSystem != null && _healthSystem.IsDead) ||
            comboList == null || comboList.Length == 0 || attack != comboList[0]) return;
        // One charge, even if this swing hits several opponents. Skill/projectile
        // damage never calls this melee-only confirmation.
        if (_axeFollowupReady) return;
        _axeFollowupReady = true;
        if (!isAttacking) return; // A late confirmed hit must not speed up idle/walking.
        float speed = _currentAttackSpeed * (_axeSwingAccelerated ? 1f : AxeHitAnimationMultiplier);
        ReceiveAxeHitAnimationSpeed(_currentAttackSequence, speed);
        if (NetworkServer.active) RpcAxeHitAnimationSpeed(_currentAttackSequence, speed);
    }

    [ClientRpc]
    private void RpcAxeHitAnimationSpeed(uint sequence, float speed)
    {
        if (!isServer) ReceiveAxeHitAnimationSpeed(sequence, speed);
    }

    private void ReceiveAxeHitAnimationSpeed(uint sequence, float speed)
    {
        if (sequence != _currentAttackSequence || !isAttacking || WeaponKind != MeleeWeaponKind.Axe || _isBowEquipped) return;
        _axeSwingAccelerated = true;
        _axeRecoveryAnimationSpeed = Mathf.Max(.01f, speed);
        _currentAttackSpeed = _axeRecoveryAnimationSpeed;
        // Continue at the current phase: the remaining swing and its recovery both
        // accelerate immediately. Stats and the locomotion rate remain unchanged.
        if (animator != null) animator.speed = _currentAttackSpeed;
    }

    private void UpdateWeaponCombat()
    {
        SetWeaponFootworkWeight(UsesTwoHandedGrip && isAttacking);
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
            if (!IsWeaponRecoiling && !isAttacking && CanHoldWeaponPose)
            {
                string state = WeaponKind == MeleeWeaponKind.SwordShield
                    ? (showGuard ? "Weapon_ShieldEnter" : "Weapon_ShieldExit")
                    : (showGuard ? "Weapon_SwordGuard" : "New State");
                animator.CrossFadeInFixedTime(state, .08f, 1);
                // Animator evaluates the requested transition after Update. Do not
                // replace it with the ready-pose exit during this same frame.
                return;
            }
        }
        UpdateWeaponReadyPose();
    }

    private void SetWeaponFootworkWeight(bool active)
    {
        if(animator==null) return;
        int layer=animator.GetLayerIndex("Weapon Footwork");
        if(layer>=0) animator.SetLayerWeight(layer,active && WeaponKind==MeleeWeaponKind.Greatsword ? 1f : 0f);
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
        if (WeaponKind != MeleeWeaponKind.SwordShield || !CanInterceptMelee(attacker)) return false;
        var catalog = WeaponCatalog.Instance;
        Vector3 toAttacker = Vector3.ProjectOnPlane(attacker.transform.position - transform.position, transform.up);
        if (toAttacker.sqrMagnitude < .0001f || Vector3.Angle(transform.forward, toAttacker) > (catalog?.GuardArcDegrees ?? 120f) * .5f) return false;
        attacker.ReceiveWeaponRecoil(catalog?.ShieldRecoilSeconds ?? .975f);
        if (NetworkServer.active) RpcShieldBlockSound();
        else GetComponent<CombatAudio>()?.PlayShieldBlock();
        return true;
    }

    [ClientRpc] private void RpcShieldBlockSound() => GetComponent<CombatAudio>()?.PlayShieldBlock();

    private bool CanInterceptMelee(PlayerCombat attacker)
    {
        return !((NetworkClient.active && !NetworkServer.active) || attacker == null || attacker == this ||
            !IsWeaponGuarding || (_healthSystem != null && _healthSystem.IsDead) ||
            (NetworkServer.active && SkillTime > _guardLeaseUntil));
    }

    // Only the swept weapon query calls this after a blade contact, before a body contact.
    // Holding guard is not an invisible cone or a timed invulnerability window.
    internal bool TryParryBlade(PlayerCombat attacker)
    {
        if (WeaponKind != MeleeWeaponKind.Greatsword || !CanInterceptMelee(attacker)) return false;
        if (attacker._hitTargetsThisAttack.Contains(_healthSystem) ||
            (netId != 0 && attacker._serverAttackHitTargetNetIds.Contains(netId))) return false;
        var catalog = WeaponCatalog.Instance;
        _riposteUntil = SkillTime + (catalog?.RiposteReadySeconds ?? 2.5f);
        _weaponGuard = false;
        attacker.ReceiveWeaponRecoil(catalog?.ParryRecoilSeconds ?? .65f);
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
        _axeFollowupReady = false;
        _axeSwingAccelerated = false;
        _axeRecoveryAnimationSpeed = 0f;
        _weaponGuard = _sentGuard = _shownGuard = false;
        _guardStartedAt = _guardLeaseUntil = _recoilUntil = _riposteUntil = 0;
    }
}
