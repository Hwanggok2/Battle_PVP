from pathlib import Path
root=Path(__file__).resolve().parents[2]
def edit(path, old, new, count=1):
 p=root/path; s=p.read_text(encoding='utf-8-sig')
 assert s.count(old)>=count,(path,old[:80],s.count(old))
 p.write_text(s.replace(old,new,count),encoding='utf-8')

p='Assets/Player/Script/CombatSkillRules.cs'
edit(p,'PolymathWeaponSwap = 22','PolymathWeaponSwap = 22,\n        Hook = 100, WarCry = 101, Charge = 102, Stealth = 103, Knife = 104,\n        Berserk = 105, Recovery = 106, Bash = 107, Thorns = 108, Fortify = 109,\n        Trap = 110, Dice = 111, Steal = 112')
edit(p,'IdentityType.Monostat => 1','IdentityType.Monostat => 2')
edit(p,'switch (identity.Type)\n            {','''if (slot == 1 && identity.Type == IdentityType.Monostat)
            {
                kind = identity.PrimaryStat switch { StatKind.STR => JobSkillKind.Hook, StatKind.CON => JobSkillKind.Berserk,
                    StatKind.AGI => JobSkillKind.Stealth, _ => JobSkillKind.Bash }; return true;
            }
            switch (identity.Type)
            {''')
edit(p,'for (int slot = 0; slot < SlotCount(identity); slot++)','''int job = BattlePvp.UI.JobGuideContent.IndexOf(identity);
            int[][] additions = { new[] {100,101,102}, new[] {105,102,106}, new[] {103,104}, new[] {107,108,109}, new[] {110,111}, new[] {110,112} };
            if (System.Array.IndexOf(additions[job], (int)kind) >= 0) return true;
            for (int slot = 0; slot < SlotCount(identity); slot++)''')
p='Assets/Player/Script/PlayerCombat.cs'
edit(p,'private bool _skillButtonRequest;','''private bool _skillButtonRequest;
    private SkillLoadout _loadout;
    private ExpandedSkillController _expanded;
    public Vector3 SkillAimDirection => GetCurrentAimDirection();
    private bool TrySelectEquipped(int slot, out JobSkillKind kind)
    {
        if (_loadout == null) _loadout = GetComponent<SkillLoadout>();
        if (_loadout != null) return _loadout.Select(slot, out kind);
        kind = default; return _statManager != null && CombatSkillRules.TrySelect(_statManager.CurrentIdentity, slot, out kind);
    }
    public bool AllowsEquipped(JobSkillKind kind)
    {
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (_expanded != null && _expanded.AllowsBorrowed(kind)) return true;
        return (TrySelectEquipped(0, out var first) && first == kind) || (TrySelectEquipped(1, out var second) && second == kind);
    }
    public bool CanBeginExpandedSkill => HasAuthoritativeCombatStats && !IsBattleLoadingOrNotStarted() &&
        (_healthSystem == null || !_healthSystem.IsDead) && !isAttacking && !IsAimingBow && !IsSkillCastingOrAttackLocked();
    public bool BeginCopiedLegacy(JobSkillKind kind)
    {
        if (!AllowsEquipped(kind) || !CanBeginExpandedSkill) return false;
        if (kind == JobSkillKind.MonostatStrLifesteal) { _monostatStrSkillCooldownUntil = SkillTime; return BeginMonostatStrSkill(SkillTime, NextSkillSequence()); }
        if (kind == JobSkillKind.MonostatAgiPoison) { _monostatAgiSkillCooldownUntil = SkillTime; return BeginMonostatAgiSkill(SkillTime, NextSkillSequence()); }
        SetAdvancedCooldownUntil((int)kind, SkillTime);
        return BeginAdvancedSkill((int)kind, SkillAimDirection, SkillTime, NextSkillSequence());
    }
    public void AddKnifePoison(IDamageReceiver target, Vector3 position) => ApplyMonostatAgiPoisonStack(target, position);''')
for slot in ['_selectedSkillIndex','index']:
 old=f'CombatSkillRules.TrySelect(_statManager.CurrentIdentity, {slot}, out JobSkillKind kind)'
 s=(root/p).read_text(); n=s.count(old)
 if n: edit(p,old,f'TrySelectEquipped({slot}, out JobSkillKind kind)',n)
edit(p,'if (kind == JobSkillKind.MonostatStrLifesteal)\n        {','''if ((int)kind >= 100)
        { if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>(); _expanded?.RequestUse(_selectedSkillIndex); return; }
        if (kind == JobSkillKind.MonostatStrLifesteal)
        {''')
edit(p,'return CombatSkillRules.Allows(_statManager.CurrentIdentity, kind)','return AllowsEquipped(kind)')
edit(p,'if (!IsMonostatStr())','if (!AllowsEquipped(JobSkillKind.MonostatStrLifesteal))',2)
edit(p,'if (!IsMonostatAgi())','if (!AllowsEquipped(JobSkillKind.MonostatAgiPoison))',2)
edit(p,'public float AttackPowerBonusMultiplier => SkillTime < _attackPowerBonusUntil ? Mathf.Max(0f, _attackPowerBonusMultiplier) : 1f;',
'''public float AttackPowerBonusMultiplier => (SkillTime < _attackPowerBonusUntil ? Mathf.Max(0f, _attackPowerBonusMultiplier) : 1f) * (GetComponent<ExpandedSkillController>()?.AttackMultiplier ?? 1f);''')
edit(p,'return Mathf.Max(0.01f, attackSpeed);','return Mathf.Max(0.01f, attackSpeed * (GetComponent<ExpandedSkillController>()?.AttackSpeedMultiplier ?? 1f));')
edit(p,'_lastAttackPressedAt = pressedAt;','''_lastAttackPressedAt = pressedAt;
        if (_expanded == null) _expanded = GetComponent<ExpandedSkillController>();
        if (!BattlePvp.Logic.GameInputController.IsPaused && !BattlePvp.Logic.GameInputController.IsTextInputActive &&
            !_isPointerOverUI && _expanded != null && _expanded.HandleAttackInput()) return;''')
edit(p,'isAttacking = true;\n        hasComboReserved = false;','''GetComponent<ExpandedSkillController>()?.NotifyAttackStarted();
        isAttacking = true;
        hasComboReserved = false;''')
edit(p,'public void NotifyPhysicalDamageDealt(float actualDamage, IDamageReceiver defender = null, Vector3 hitPosition = default)\n    {','''public void NotifyPhysicalDamageDealt(float actualDamage, IDamageReceiver defender = null, Vector3 hitPosition = default)
    {
        if (actualDamage > 0) GetComponent<ExpandedSkillController>()?.NotifyPhysicalHit(defender);''')
edit(p,'JobSkillData data;\n        bool casting = false;','''TrySelectEquipped(index, out JobSkillKind equipped);
        if ((int)equipped >= 100 && TryGetComponent<ExpandedSkillController>(out var extra)) return extra.Hud(index, equipped);
        JobSkillData data;
        bool casting = false;''')
edit(p,'if (IsMonostatStr() && index == 0)','if (equipped == JobSkillKind.MonostatStrLifesteal)')
edit(p,'else if (IsMonostatAgi() && index == 0)','else if (equipped == JobSkillKind.MonostatAgiPoison)')
edit(p,'_ => null\n        };\n    }\n\n    private void ApplyMonostatAgiPoisonStack','_ => SkillPresentationCatalog.Data(skillId)\n        };\n    }\n\n    private void ApplyMonostatAgiPoisonStack')

p='Assets/Player/Script/Stats/StatManager.cs'
edit(p,'private DerivedCombatStats _cachedDerivedStats;','''[SyncVar(hook = nameof(OnCombatMultiplierChanged))] private float _allCombatMultiplier = 1f;
        [SyncVar(hook = nameof(OnCombatMultiplierChanged))] private float _defenseCombatMultiplier = 1f;
        private void OnCombatMultiplierChanged(float oldValue, float newValue) { _derivedStatsDirty = true; DerivedStatsChanged?.Invoke(); }
        public void SetCombatMultipliers(float all, float defense)
        {
            if (NetworkClient.active && !isServer) return;
            if (!float.IsFinite(all) || !float.IsFinite(defense) || all <= 0 || defense <= 0) return;
            if (Mathf.Approximately(all, _allCombatMultiplier) && Mathf.Approximately(defense, _defenseCombatMultiplier)) return;
            _allCombatMultiplier = all; _defenseCombatMultiplier = defense; OnCombatMultiplierChanged(0, 0);
        }
        private DerivedCombatStats _cachedDerivedStats;''')
edit(p,'public float GetFinalTotal(StatKind kind) => StatMath.FinalTotal(kind, _stats);','public float GetFinalTotal(StatKind kind) => StatMath.FinalTotal(kind, _stats) * _allCombatMultiplier * (kind == StatKind.DEF ? _defenseCombatMultiplier : 1f);')
edit(p,'_cachedDerivedStats = StatBalanceCalculator.Calculate(_stats, CurrentIdentity, config);','_cachedDerivedStats = StatBalanceCalculator.Calculate(GetFinalTotal(StatKind.STR), GetFinalTotal(StatKind.CON), GetFinalTotal(StatKind.AGI), GetFinalTotal(StatKind.DEF), CurrentIdentity);')
p='Assets/Player/Script/HealthSystem.cs'
edit(p,'float hpBeforeDamage = _currentHp;','''var expanded = GetComponent<ExpandedSkillController>();
            if (expanded != null) { expanded.NotifyDamaged(); amount *= expanded.IncomingMultiplier; }
            float hpBeforeDamage = _currentHp;''')
edit(p,'float thorns = _damageCalculator.PredictThornsReflectDamage(attackerAttackPower, attacker.MaxHp);','float thorns = _damageCalculator.PredictThornsReflectDamage(attackerAttackPower, attacker.MaxHp) * (GetComponent<ExpandedSkillController>()?.ReflectMultiplier ?? 1f);')
edit(p,'if (effectiveRegen > 0f && _currentHp < _maxHp)','effectiveRegen *= GetComponent<ExpandedSkillController>()?.RegenMultiplier ?? 1f;\n                if (effectiveRegen > 0f && _currentHp < _maxHp)')
p='Assets/Player/Script/PlayerManager.cs'
edit(p,'public bool IsSkillInputLocked(SkillInputLockFlags flag) => (_inputLocks.Evaluate(MovementTime) & flag) != 0;', '''public bool IsSkillInputLocked(SkillInputLockFlags flag) => ((_inputLocks.Evaluate(MovementTime) |
        (GetComponent<ExpandedSkillController>()?.ControlFlags ?? SkillInputLockFlags.None)) & flag) != 0;''')
p='Assets/Player/Script/FollowCamera.cs'
edit(p,'bool canLook = !IsLobby || (mouse != null && mouse.rightButton.isPressed && !overUi);','''var skillControl = _target.GetComponentInParent<BattlePvp.Combat.ExpandedSkillController>();
            bool canLook = (skillControl == null || !skillControl.LookLocked) && (!IsLobby || (mouse != null && mouse.rightButton.isPressed && !overUi));''')
edit(p,'_yaw += delta.x * _mouseSensitivity * 0.1f * settings.sensitivity;','''float yawDelta = delta.x * _mouseSensitivity * 0.1f * settings.sensitivity;
                    if (skillControl != null && skillControl.IsCharging) yawDelta = Mathf.Clamp(yawDelta, -skillControl.ChargeTurnRate * Time.deltaTime, skillControl.ChargeTurnRate * Time.deltaTime);
                    _yaw += yawDelta;''')
p='Assets/Player/Script/SkillDescription.cs'
edit(p,'public static string Effect(JobSkillData data)\n        {\n            if (data == null) return string.Empty;', '''public static string Effect(JobSkillData data)
        {
            if (data == null) return string.Empty;
            string generated = SkillGameData.Description(data.SkillKind);
            if (!string.IsNullOrEmpty(generated)) return generated;''')
# Excel owns numeric balance values; serialized fields remain backward compatible fallbacks.
p=root/'Assets/Player/Script/SO/JobSkillData.cs'; s=p.read_text()
import re
s=re.sub(r'public (float|int) (\w+) => (_\w+);',lambda m:f'public {m[1]} {m[2]} => '+('(int)' if m[1]=='int' else '')+f'SkillGameData.Number(_skillKind, "{m[2]}", {m[3]});',s)
s=s.replace('public string DisplayName => _displayName;', 'public string DisplayName => SkillGameData.Instance != null && SkillGameData.Instance.Find((int)_skillKind) != null ? SkillGameData.Text(SkillGameData.Instance.Find((int)_skillKind).NameKey, _displayName) : _displayName;')
p.write_text(s)
# Every Unity ScriptableObject has a matching source filename.
p=root/'Assets/Player/Script/SkillGameData.cs'; s=p.read_text(); start=s.index('    [Serializable] public sealed class SkillPresentationEntry')
(root/'Assets/Player/Script/SkillPresentationCatalog.cs').write_text('using System;\nusing UnityEngine;\nnamespace BattlePvp.Combat\n{\n'+s[start:])
p.write_text(s[:start]+'}\n')
