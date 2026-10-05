using System;
using System.Collections;
using System.Collections.Generic;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using UnityEngine;

namespace BattlePvp.Combat
{
    [Serializable] public struct SkillRuntime
    {
        public double ActiveUntil, CooldownUntil, NextChargeAt;
        public int Charges;
    }
    [Serializable] public struct SkillTrapSnapshot { public Vector3 Position; public double ExpiresAt, ClosedAt; public bool Closed; }
    /// <summary>Server-owned execution of the additional selectable skills. Clients send only slot and aim intent.</summary>
    public sealed class ExpandedSkillController : NetworkBehaviour
    {
        public readonly SyncDictionary<int, SkillRuntime> States = new();
        public readonly SyncDictionary<int, SkillTrapSnapshot> Traps = new();
        [SyncVar] private int _maintainedCopy = -1;
        [SyncVar] public bool KnifeReady;
        [SyncVar] public bool TrapReady;
        [SyncVar] private double _trapPlaceUntil, _hookUntil;
        [SyncVar] private double _hookRetrieveUntil, _hookReleaseAt;
        [SyncVar] private double _hookedUntil;
        [SyncVar] private NetworkIdentity _hookCaster;
        [SyncVar] private Vector3 _hookLookPoint;
        private Transform _offlineHookCaster;
        private bool _trapFromCopy;
        private int _trapCast, _hookCast;
        private Animator _animator;
        private Transform _hips;
        private float _standingHipHeight;
        private Transform _head;
        private float _standingHeadHeight;
        [SyncVar] public int CopiedKind = -1;
        [SyncVar] private int _borrowedKind = -1;
        [SyncVar] private double _borrowedUntil;
        [SyncVar] private double _busyUntil, _stunnedUntil, _rootUntil, _vulnerableUntil, _ambushUntil;
        [SyncVar] private int _diceFace;
        [SyncVar] private double _diceStarted;
        [SyncVar] private double _chargeStarted;
        [SyncVar] private Vector3 _chargeDirection;
        private SkillLoadout _loadout;
        private StatManager _stats;
        private HealthSystem _health;
        private PlayerCombat _combat;
        private PlayerManager _movement;
        private SkillExpansionVisuals _visuals;
        private Vector3 _lastStealthPosition;
        [SyncVar] private Vector3 _rootRotation;
        private float _stealthTravel, _lastMoveMultiplier = -1;
        private double _nextUse, _nextKnife, _nextDrain, _nextAim, _lastTurn, _nextChargeMove;
        private bool _wasCharging, _cancelled;
        private readonly HashSet<IDamageReceiver> _chargeHits = new();
        private readonly List<Collider> _chargeContacts = new();
        private CharacterController _chargeController;
        private readonly List<SkillTrap> _traps = new();
        private const int MoveSource = 8100;
        public bool Authority => (netIdentity != null && isServer) || (!NetworkServer.active && !NetworkClient.active);
        public bool Owner => (netIdentity != null && isLocalPlayer) || (!NetworkServer.active && !NetworkClient.active);
        public double Now => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        public bool IsCharging => Active(JobSkillKind.Charge);
        public bool IsStealthed => Active(JobSkillKind.Stealth);
        public bool IsPlacingTrap => Now < _trapPlaceUntil;
        public bool IsHookActive => Now < _hookUntil;
        public bool IsRetrievingHook => IsHookActive && Now < _hookRetrieveUntil;
        public bool IsHoldingHook => IsHookActive && Now < _hookReleaseAt;
        public float HookRetrieveRemaining => (float)Math.Max(0,_hookRetrieveUntil-Now);
        public float TrapCameraDrop => IsPlacingTrap && _hips!=null
            ? Mathf.Clamp(_standingHipHeight-transform.InverseTransformPoint(_hips.position).y,0,.65f)*Mathf.Abs(transform.lossyScale.y) : 0;
        public float SkillCameraDrop => Active(JobSkillKind.Fortify) && _head!=null
            ? Mathf.Clamp(_standingHeadHeight-transform.InverseTransformPoint(_head.position).y,0,1.45f)*Mathf.Abs(transform.lossyScale.y) : TrapCameraDrop;
        public bool TryGetTrapPlacement(out Vector3 point) => SkillTargeting.TrapPlacement(this,out point);
        public Vector3 ThrowHandPosition => _animator!=null && _animator.isHuman && _animator.GetBoneTransform(HumanBodyBones.RightHand)!=null
            ? _animator.GetBoneTransform(HumanBodyBones.RightHand).position : ProjectileOrigin;
        public bool IsBeingHooked => Now < _hookedUntil;
        public bool IsStunned => Now < _stunnedUntil;
        public bool LookLocked => Now < _rootUntil || IsBeingHooked;
        public bool BlocksCombat => Now < _stunnedUntil || Now < _busyUntil || IsBeingHooked;
        public bool BlocksVoluntaryDisplacement => BlocksCombat || LookLocked || Active(JobSkillKind.Fortify);
        public float ChargeTurnRate => Value(JobSkillKind.Charge, "TurnDegreesPerSecond", 60);
        public int DiceFace => _diceFace;
        public double DiceStarted => _diceStarted;
        public bool Berserking => Active(JobSkillKind.Berserk);
        public bool HasAmbushBonus => Now < _ambushUntil;
        public float AttackMultiplier => (Berserking ? Value(JobSkillKind.Berserk,"AttackMultiplier",2) : 1) *
            (Now < _ambushUntil ? Value(JobSkillKind.Stealth,"AttackMultiplier",1.2f) : 1);
        public float AttackSpeedMultiplier => Berserking ? Value(JobSkillKind.Berserk,"AttackSpeedMultiplier",1.3f) : 1;
        public float IncomingMultiplier => (Active(JobSkillKind.WarCry) ? Value(JobSkillKind.WarCry,"IncomingMultiplier",.8f) : 1) *
            (Now < _vulnerableUntil ? Value(JobSkillKind.Bash,"IncomingMultiplier",2) : 1);
        public float ReflectMultiplier => Active(JobSkillKind.Thorns) ? Value(JobSkillKind.Thorns,"ReflectMultiplier",2) : 1;
        public float RegenMultiplier => Berserking ? 0 :
            (Now < Read(JobSkillKind.Berserk).CooldownUntil ? Value(JobSkillKind.Berserk,"RecoveryRegenMultiplier",.5f) : 1) *
            (Active(JobSkillKind.Recovery) ? Value(JobSkillKind.Recovery,"RegenMultiplier",2) : 1);
        public SkillInputLockFlags ControlFlags => BlocksCombat
            ? SkillInputLockFlags.Move | SkillInputLockFlags.Attack | SkillInputLockFlags.Jump | SkillInputLockFlags.Crouch
            : LookLocked || Active(JobSkillKind.Fortify) || IsCharging
            ? SkillInputLockFlags.Move | SkillInputLockFlags.Jump | SkillInputLockFlags.Crouch : SkillInputLockFlags.None;
        public bool AllowsBorrowed(JobSkillKind kind) => (int)kind == _borrowedKind && Now < _borrowedUntil;
        public static bool IsExpanded(JobSkillKind kind) => (int)kind >= 100 && (int)kind <= 112;
        public static float Value(JobSkillKind kind, string key, float fallback = 0) => SkillGameData.Number(kind,key,fallback);
        public static bool UsesCharges(JobSkillKind kind) => kind == JobSkillKind.Knife || kind == JobSkillKind.Trap ||
            kind == JobSkillKind.StrategistRoll || kind == JobSkillKind.PolymathRoll;
        public static int MaxCharges(JobSkillKind kind) => UsesCharges(kind) ? Mathf.Max(1,(int)Value(kind,"MaxCharges",kind == JobSkillKind.Knife || kind == JobSkillKind.Trap ? 3 : 2)) : 0;
        public static float RechargeSeconds(JobSkillKind kind) => Mathf.Max(.01f,kind == JobSkillKind.Knife ? Value(kind,"RechargeSeconds",8) : Value(kind,"CooldownSeconds",25));
        private static readonly JobSkillKind[] ChargeSkills = { JobSkillKind.Knife, JobSkillKind.Trap, JobSkillKind.StrategistRoll, JobSkillKind.PolymathRoll };
        public SkillRuntime Read(JobSkillKind kind) => States.TryGetValue((int)kind, out var state) ? state :
            new SkillRuntime { Charges = MaxCharges(kind) };
        public SkillRuntime ChargeState(JobSkillKind kind) => Recharged(kind,Read(kind),Now);
        private static SkillRuntime Recharged(JobSkillKind kind,SkillRuntime state,double now)
        {
            int maximum=MaxCharges(kind);
            while(state.Charges<maximum && state.NextChargeAt>0 && now>=state.NextChargeAt)
            { state.Charges++; state.NextChargeAt+=RechargeSeconds(kind); }
            if(state.Charges>=maximum) state.NextChargeAt=0;
            return state;
        }
        public bool SpendCharge(JobSkillKind kind)
        {
            if(!Authority || !AliveReady || !UsesCharges(kind)) return false;
            var state=ChargeState(kind); if(state.Charges<=0) return false;
            state.Charges--; if(state.NextChargeAt<=0) state.NextChargeAt=Now+RechargeSeconds(kind);
            States[(int)kind]=state; return true;
        }
        public bool Active(JobSkillKind kind) => Now < Read(kind).ActiveUntil;
        private void Awake()
        {
            SkillLoadout.EnableOfflineWrites(States); SkillLoadout.EnableOfflineWrites(Traps);
            _animator = GetComponentInChildren<Animator>();
            if(_animator!=null && _animator.isHuman)
            { _hips=_animator.GetBoneTransform(HumanBodyBones.Hips); if(_hips!=null) _standingHipHeight=transform.InverseTransformPoint(_hips.position).y; }
            if(_animator!=null && _animator.isHuman)
            { _head=_animator.GetBoneTransform(HumanBodyBones.Head); if(_head!=null) _standingHeadHeight=transform.InverseTransformPoint(_head.position).y; }
            _loadout = GetComponent<SkillLoadout>(); _stats = GetComponent<StatManager>();
            _health = GetComponent<HealthSystem>(); _combat = GetComponent<PlayerCombat>(); _movement = GetComponent<PlayerManager>();
            _chargeController = GetComponent<CharacterController>();
            _visuals = new SkillExpansionVisuals(this);
            if (Application.isPlaying && GetComponent<StunIndicator>() == null) gameObject.AddComponent<StunIndicator>();
        }
        private void OnEnable()
        {
            _visuals?.Resume();
            if (_health != null) _health.OnDied += Cancel;
            if (_stats != null) _stats.IdentityChanged += IdentityChanged;
            _cancelled = false;
        }
        private void OnDisable()
        {
            if (_health != null) _health.OnDied -= Cancel;
            if (_stats != null) _stats.IdentityChanged -= IdentityChanged;
            Cancel();
            if (Authority)
            {
                foreach (var trap in _traps) if (trap != null) trap.Expire();
                _traps.Clear(); Traps.Clear();
            }
            _visuals?.Dispose();
        }
        private void OnDestroy() { _visuals?.Dispose(); }
        private void IdentityChanged(Identity _) { Cancel(); _cancelled = false; }
        public void CancelForLoadout() { Cancel(); _cancelled = false; }
        private void Cancel()
        {
            if (Authority && !_cancelled)
            {
                EndCharge(); EndBerserk(); BreakStealth();
                var keys = new List<int>(States.Keys);
                foreach (int key in keys) { var state = States[key]; state.ActiveUntil = 0; States[key] = state; }
                KnifeReady = TrapReady = false; _trapFromCopy = false; _trapPlaceUntil = _hookUntil = _hookRetrieveUntil = _hookReleaseAt = 0; _trapCast++; _hookCast++;
                CopiedKind = _borrowedKind = -1; _borrowedUntil = 0;
                _busyUntil = _stunnedUntil = _rootUntil = _vulnerableUntil = _ambushUntil = 0;
                _hookedUntil=0; _hookCaster=null; _offlineHookCaster=null;
                _stats?.SetCombatMultipliers(1,1);
                // Placed traps own their lifetime; changing skills only cancels the current cast.
                StopAllCoroutines(); _cancelled = true;
            }
            _movement?.RemoveMovementEffect(MoveSource); _lastMoveMultiplier = -1;
            _visuals?.ResetOwnerEffects();
        }
        private bool AliveReady => _health != null && !_health.IsDead && _stats != null && (!NetworkServer.active || _stats.HasServerStats);
        public Vector3 ProjectileOrigin => transform.position + Vector3.up * (1.35f * Mathf.Abs(transform.lossyScale.y) - (_movement != null ? _movement.CrouchCameraDrop : 0));
        private Vector3 Aim(JobSkillKind kind)
        {
            Vector3 fallback=_combat.SkillAimDirection;
            if(kind!=JobSkillKind.Hook && kind!=JobSkillKind.Knife && kind!=JobSkillKind.Steal) return fallback;
            var camera=Camera.main != null ? Camera.main.GetComponent<BattlePvp.CameraLogic.FollowCamera>() : null;
            if(camera==null || camera.Target!=transform || !BattlePvp.Logic.InputModeRules.UsesFpsLook(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return fallback;
            Ray ray=camera.GetAimRay(); float range=Value(kind,"Range",4);
            Vector3 point=ray.GetPoint(range+Vector3.Distance(ray.origin,ProjectileOrigin));
            if(SkillTargeting.Cast(this,ray.origin,ray.direction,Vector3.Distance(ray.origin,point),0,out var hit)) point=hit.point;
            return (point-ProjectileOrigin).normalized;
        }
        public void RequestUse(int slot)
        {
            if (!Owner || !AliveReady || slot < 0 || slot > 1) return;
            if(!_loadout.Select(slot,out var kind)) return;
            Vector3 aim=Aim(kind==JobSkillKind.Steal && CopiedKind>=0 ? (JobSkillKind)CopiedKind : kind);
            if (NetworkClient.active && !isServer) CmdUse(slot, aim);
            else TryUse(slot, aim);
        }
        [Command] private void CmdUse(int slot, Vector3 aim) => TryUse(slot, aim);
        public bool TryUse(int slot, Vector3 aim)
        {
            if (!Authority || !AliveReady || !ValidAim(aim) || _loadout == null || !_loadout.Select(slot, out var kind)) return false;
            // Any equipped skill key cancels charge, including a legacy/cooling-down slot.
            if (IsCharging) { EndCharge(); return true; }
            if (Now < _nextUse || !IsExpanded(kind)) return false;
            _nextUse = Now + .12;
            if(kind == JobSkillKind.Steal && _maintainedCopy == (int)JobSkillKind.Berserk && Berserking) { EndBerserk(); _maintainedCopy = -1; return true; }
            // Turning a maintained skill off remains possible during its own movement lock.
            if (kind == JobSkillKind.Berserk && Berserking) { EndBerserk(); return true; }
            if (kind == JobSkillKind.Knife && KnifeReady) { KnifeReady = false; return true; }
            if (TrapReady && (kind == JobSkillKind.Trap || (kind == JobSkillKind.Steal && _trapFromCopy)))
            { TrapReady = false; _trapFromCopy = false; return true; }
            if (!_combat.CanBeginExpandedSkill || Now < _busyUntil || Now < _stunnedUntil || IsCharging) return false;
            if (kind == JobSkillKind.Steal && CopiedKind >= 0) return UseCopy(aim.normalized);
            return Begin(kind, aim.normalized, false);
        }
        private bool Begin(JobSkillKind kind, Vector3 aim, bool copied)
        {
            if (kind==JobSkillKind.Charge && BlocksVoluntaryDisplacement) return false;
            var state = Read(kind);
            if (!copied && ((!UsesCharges(kind) && state.CooldownUntil > Now) || state.ActiveUntil > Now)) return false;
            if (kind == JobSkillKind.Knife && state.Charges <= 0) return false;
            if (kind == JobSkillKind.Recovery && _health.CurrentHp <= _health.MaxHp * Value(kind,"HealthCostRatio",.3f)) return false;
            if (kind == JobSkillKind.Trap)
            {
                if(!copied && ChargeState(kind).Charges<=0) return false;
                // Readying a preview neither spends the copy nor starts the trap cooldown.
                KnifeReady = false; TrapReady = true; _trapFromCopy = copied; return true;
            }
            if (kind == JobSkillKind.Steal)
            {
                if (!SkillTargeting.Cast(this, ProjectileOrigin, aim, Value(kind,"Range",4), 0, out var hit)) return false;
                var target = hit.collider.GetComponentInParent<SkillLoadout>();
                if (target == null || target == _loadout || target.GetComponent<HealthSystem>()?.IsDead == true) return false;
                var candidates = new List<int>(2);
                for(int i=0;i<2;i++) if(target.Select(i,out var other) && other != JobSkillKind.Steal) candidates.Add((int)other);
                if (candidates.Count == 0) return false;
                CopiedKind = candidates[UnityEngine.Random.Range(0,candidates.Count)]; Cue(kind, hit.point); return true;
            }
            BreakStealth(); _cancelled = false; TrapReady = false; _trapFromCopy = false;
            if(kind != JobSkillKind.Knife) KnifeReady = false;
            if (kind != JobSkillKind.Knife && kind != JobSkillKind.Charge && kind != JobSkillKind.Berserk)
                state.CooldownUntil = Now + Value(kind,"CooldownSeconds");
            if (kind == JobSkillKind.Knife) { KnifeReady = true; return true; }
            state.ActiveUntil = Now + Value(kind,"DurationSeconds");
            if (kind == JobSkillKind.Berserk) { state.ActiveUntil = double.MaxValue; _nextDrain = Now + 1; }
            if (kind == JobSkillKind.Stealth) state.ActiveUntil = double.MaxValue;
            States[(int)kind] = state;
            switch(kind)
            {
                case JobSkillKind.Hook:
                    _hookRetrieveUntil=0; _hookReleaseAt=Now+Value(kind,"ThrowReleaseSeconds",.24f);
                    _busyUntil=_hookUntil=Now+3; StartCoroutine(Hook(aim,++_hookCast)); break;
                case JobSkillKind.Charge:
                    _chargeStarted = Now; _chargeDirection = Flat(aim); _lastTurn = Now; _nextChargeMove = 0; _chargeHits.Clear(); _wasCharging = true; break;
                case JobSkillKind.Stealth: _lastStealthPosition = transform.position; _stealthTravel = 0; break;
                case JobSkillKind.Recovery:
                    _health.SetCurrentHp(_health.CurrentHp - _health.MaxHp * Value(kind,"HealthCostRatio",.3f)); break;
                case JobSkillKind.Fortify: break;
                case JobSkillKind.Dice: _diceFace = UnityEngine.Random.Range(1,7); _diceStarted = Now; break;
            }
            Cue(kind, transform.position + aim);
            return true;
        }
        private bool UseCopy(Vector3 aim)
        {
            var kind = (JobSkillKind)CopiedKind;
            if (kind == JobSkillKind.Steal) return false;
            bool accepted;
            if (IsExpanded(kind)) accepted = Begin(kind,aim,true);
            else
            {
                _borrowedKind = CopiedKind; _borrowedUntil = Now + Mathf.Max(60, Value(kind,"DurationSeconds") + Value(kind,"CastSeconds") + 1);
                accepted = _combat.BeginCopiedLegacy(kind);
                if (!accepted) { _borrowedKind = -1; _borrowedUntil = 0; }
            }
            if (!accepted) return false;
            if (kind == JobSkillKind.Trap) return true;
            _maintainedCopy = kind == JobSkillKind.Berserk ? (int)kind : -1;
            CopiedKind = -1;
            var steal = Read(JobSkillKind.Steal); steal.CooldownUntil = Now + Value(JobSkillKind.Steal,"CooldownSeconds",20); States[(int)JobSkillKind.Steal] = steal;
            return true;
        }
        public bool HandleAttackInput()
        {
            if (!Owner || !AliveReady) return false;
            if (TrapReady)
            {
                if(TryGetTrapPlacement(out var point))
                { if(NetworkClient.active && !isServer) CmdPlaceTrap(point); else ConfirmTrap(point); }
                return true;
            }
            if (KnifeReady) { Vector3 aim=Aim(JobSkillKind.Knife); if (NetworkClient.active && !isServer) CmdThrow(aim); else ThrowKnife(aim); return true; }
            CancelChargeFromInput();
            return false;
        }
        public bool CancelChargeFromInput()
        {
            if (!Owner || !AliveReady || !IsCharging) return false;
            if (NetworkClient.active && !isServer) CmdEndCharge(); else EndCharge();
            _visuals?.InterruptCharge();
            return true;
        }
        [Command] private void CmdEndCharge() { if (AliveReady) EndCharge(); }
        [Command] private void CmdPlaceTrap(Vector3 point) => ConfirmTrap(point);
        public bool ConfirmTrap(Vector3 requestedPoint)
        {
            if(!Authority || !AliveReady || !TrapReady || BlocksCombat || IsCharging || !_combat.CanBeginExpandedSkill ||
                !CombatValidation.IsFinite(requestedPoint)) return false;
            bool copied=_trapFromCopy && CopiedKind==(int)JobSkillKind.Trap && _loadout.Has(JobSkillKind.Steal);
            if((_trapFromCopy && !copied) || (!copied && (!_loadout.Has(JobSkillKind.Trap) || ChargeState(JobSkillKind.Trap).Charges<=0))) return false;
            if(!TryGetTrapPlacement(out var point) || Vector3.Distance(requestedPoint,point)>.35f) return false;
            if(!copied && !SpendCharge(JobSkillKind.Trap)) return false;
            TrapReady=false; _trapFromCopy=false; KnifeReady=false; BreakStealth(); _cancelled=false;
            _busyUntil=_trapPlaceUntil=Now+Value(JobSkillKind.Trap,"CastSeconds",1.2f);
            if(copied)
            {
                CopiedKind=-1;
                var steal=Read(JobSkillKind.Steal); steal.CooldownUntil=Now+Value(JobSkillKind.Steal,"CooldownSeconds",20); States[(int)JobSkillKind.Steal]=steal;
            }
            Cue(JobSkillKind.Trap,point); StartCoroutine(PlaceTrap(point,++_trapCast)); return true;
        }
        [Command] private void CmdThrow(Vector3 aim) => ThrowKnife(aim);
        private void ThrowKnife(Vector3 aim)
        {
            if (!Authority || !AliveReady || !KnifeReady || !ValidAim(aim) || Now < _nextKnife || Now < _stunnedUntil || Now < _busyUntil) return;
            var state = Read(JobSkillKind.Knife); if (state.Charges <= 0) return;
            _nextKnife = Now + Value(JobSkillKind.Knife,"ThrowInterval",.3f);
            state.Charges--; if (state.NextChargeAt <= 0) state.NextChargeAt = Now + Value(JobSkillKind.Knife,"RechargeSeconds",8);
            States[(int)JobSkillKind.Knife] = state;
            KnifeReady = state.Charges > 0; NotifyAttackStarted();
            StartCoroutine(Projectile(JobSkillKind.Knife, aim.normalized)); Cue(JobSkillKind.Knife, transform.position + aim);
        }
        private IEnumerator Projectile(JobSkillKind kind, Vector3 aim)
        {
            yield return new WaitForSeconds(Value(kind,"ThrowReleaseSeconds",.12f));
            if(!AliveReady || Now<_stunnedUntil) yield break;
            Vector3 origin = SafeThrowOrigin();
            aim=ThrowDirection(origin,aim,Value(kind,"Range",10));
            Vector3 position = origin;
            float range = Value(kind,"Range",10), speed = Value(kind,"ProjectileSpeed",16), travelled = 0;
            ProjectileCue(kind, origin, aim, range / speed);
            while (travelled < range && AliveReady)
            {
                float step = Mathf.Min(speed * Time.deltaTime, range - travelled);
                if (SkillTargeting.Cast(this, position, aim, step, Value(kind,"Radius",.06f), out var hit))
                {
                    var receiver = hit.collider.GetComponentInParent<IDamageReceiver>();
                    var stats = hit.collider.GetComponentInParent<StatManager>();
                    if(receiver != null && stats != null && GetComponent<AttackProcessor>().ProcessSkillHit(Value(kind,"DamageMultiplier",.5f),stats,receiver,hit.point))
                        _combat.AddKnifePoison(receiver, hit.point);
                    yield break;
                }
                position += aim * step; travelled += step; yield return null;
            }
        }
        private IEnumerator Hook(Vector3 aim,int cast)
        {
            yield return new WaitForSeconds(Value(JobSkillKind.Hook,"ThrowReleaseSeconds",.2f));
            if(cast!=_hookCast) yield break;
            if(!AliveReady || Now<_stunnedUntil || !IsHookActive) { _busyUntil=_hookUntil=0; yield break; }
            Vector3 origin = SafeThrowOrigin();
            float range = Value(JobSkillKind.Hook,"Range",6), speed = Value(JobSkillKind.Hook,"ProjectileSpeed",12), travelled = 0;
            aim=ThrowDirection(origin,aim,range);
            Vector3 position = origin;
            ProjectileCue(JobSkillKind.Hook, origin, aim, range / speed);
            while(travelled < range && AliveReady && IsHookActive && cast==_hookCast)
            {
                float step = Mathf.Min(speed * Time.deltaTime, range - travelled);
                if(SkillTargeting.Cast(this,position,aim,step,Value(JobSkillKind.Hook,"Radius",.08f),out var hit))
                {
                    float recovery=Value(JobSkillKind.Hook,"RetrieveSeconds",.6f);
                    var receiver=hit.collider.GetComponentInParent<IDamageReceiverWithResult>();
                    var target=hit.collider.GetComponentInParent<PlayerManager>();
                    var dummy=hit.collider.GetComponentInParent<DummyHealth>();
                    if(receiver!=null && !ReferenceEquals(receiver,_health))
                    {
                        var damage=receiver.ApplyDamage(new DamageRequest(Value(JobSkillKind.Hook,"FixedDamage",10),
                            DamageSource.Fixed,0,_health,hit.point,DamageSource.Physical));
                        if(damage.Accepted) _combat.NotifyConfirmedHit(false);
                        // Invulnerability rejects both the damage and the displacement.
                        if(damage.Accepted && (dummy!=null || !damage.Killed) && (target!=null || dummy!=null))
                        {
                            Transform victim=target!=null ? target.transform : dummy.transform;
                            Vector3 destination=transform.position+Flat(aim)*Value(JobSkillKind.Hook,"StopDistance",1);
                            Vector3 delta=destination-victim.position; delta.y=0;
                            recovery=Mathf.Max(recovery,delta.magnitude/Value(JobSkillKind.Hook,"PullSpeed",8));
                            if(target!=null)
                            {
                                target.GetComponent<ExpandedSkillController>()?.ApplyHookPull(this,recovery);
                                Move(target,delta.normalized,delta.magnitude,recovery);
                            }
                            else dummy.PullTo(victim.position+delta,recovery);
                        }
                    }
                    BeginHookRetrieval(hit.point,recovery);
                    yield return new WaitForSeconds(recovery);
                    if(cast!=_hookCast) yield break;
                    _busyUntil = _hookUntil = _hookRetrieveUntil = 0; yield break;
                }
                position += aim * step; travelled += step; yield return null;
            }
            if(cast!=_hookCast || !AliveReady || !IsHookActive) yield break;
            float returnSeconds=Value(JobSkillKind.Hook,"RetrieveSeconds",.6f);
            BeginHookRetrieval(position,returnSeconds);
            yield return new WaitForSeconds(returnSeconds);
            if(cast==_hookCast) _busyUntil = _hookUntil = _hookRetrieveUntil = 0;
        }
        private void BeginHookRetrieval(Vector3 point,float seconds)
        {
            _busyUntil=_hookUntil=_hookRetrieveUntil=Now+seconds;
            if(NetworkServer.active) RpcHookRetrieval(point,seconds); else _visuals?.RetrieveHook(point,seconds);
        }
        [ClientRpc] private void RpcHookRetrieval(Vector3 point,float seconds) => _visuals?.RetrieveHook(point,seconds);
        private Vector3 ThrowDirection(Vector3 origin,Vector3 aim,float range)
        {
            Vector3 target=ProjectileOrigin+aim*range;
            if(SkillTargeting.Cast(this,ProjectileOrigin,aim,range,0,out var hit)) target=hit.point;
            return (target-origin).normalized;
        }
        private Vector3 SafeThrowOrigin()
        {
            // A hand extended through a nearby wall must not spawn a projectile on its far side.
            Vector3 delta=ThrowHandPosition-ProjectileOrigin;
            return SkillTargeting.Cast(this,ProjectileOrigin,delta.normalized,delta.magnitude,.09f,out var hit)
                ? ProjectileOrigin+delta.normalized*Mathf.Max(0,hit.distance-.02f) : ThrowHandPosition;
        }
        private IEnumerator PlaceTrap(Vector3 point,int cast)
        {
            yield return new WaitForSeconds(Value(JobSkillKind.Trap,"CastSeconds",1.2f));
            if(cast!=_trapCast) yield break;
            if (!AliveReady || _trapPlaceUntil<=0 || Now<_stunnedUntil || !SkillTargeting.TrapFootprint(this,point)) { _trapPlaceUntil=0; yield break; }
            _trapPlaceUntil=0;
            _traps.RemoveAll(t=>t==null);
            var trap = SkillTrap.Create(this,point,Now + Value(JobSkillKind.Trap,"Lifetime",60));
            _traps.Add(trap);
            Traps[trap.Id] = new SkillTrapSnapshot { Position = point, ExpiresAt = trap.ExpiresAt };
        }
        public void TrapTriggered(SkillTrap trap, Collider collider)
        {
            if(!Authority || trap == null || !Traps.TryGetValue(trap.Id,out var snapshot) || snapshot.Closed) return;
            if(Now>=snapshot.ExpiresAt) { CloseTrap(trap); return; }
            var target = collider.GetComponentInParent<IDamageReceiver>();
            if(target == null || ReferenceEquals(target,_health)) return;
            CloseTrap(trap);
            if(target is IDamageReceiverWithContext context) context.ApplyDamage(target.MaxHp * Value(JobSkillKind.Trap,"MaxHealthDamageRatio",.2f), DamageSource.Poison,0,_health,collider.transform.position);
            else target.ApplyDamage(target.MaxHp * Value(JobSkillKind.Trap,"MaxHealthDamageRatio",.2f),DamageSource.Poison,collider.transform.position);
            collider.GetComponentInParent<ExpandedSkillController>()?.ApplyControl(Value(JobSkillKind.Trap,"RootSeconds",5),true,false);
        }
        public void CloseTrap(SkillTrap trap)
        {
            if(!Authority || trap==null || !Traps.TryGetValue(trap.Id,out var state) || state.Closed) return;
            state.Closed=true; state.ClosedAt=Math.Min(Now,state.ExpiresAt); Traps[trap.Id]=state;
            _traps.Remove(trap); trap.Expire();
        }
        private readonly List<int> _finishedTraps = new();
        private void UpdateTraps()
        {
            if(Authority)
            {
                _finishedTraps.Clear();
                foreach(var pair in Traps)
                    if(pair.Value.Closed && Now>=pair.Value.ClosedAt+SkillTrapVisual.CloseLifetime) _finishedTraps.Add(pair.Key);
                foreach(int id in _finishedTraps) Traps.Remove(id);
            }
            _visuals?.TickTraps();
        }
        public void ApplyControl(float duration, bool root, bool vulnerable)
        {
            if (!Authority || !AliveReady || duration <= 0 || !float.IsFinite(duration)) return;
            if(root) { _rootUntil = Math.Max(_rootUntil,Now+duration); _rootRotation = transform.eulerAngles; }
            else _stunnedUntil = Math.Max(_stunnedUntil,Now+duration);
            if(vulnerable) _vulnerableUntil = Math.Max(_vulnerableUntil,Now+duration);
            if(!root) _combat.CancelCurrentAttack();
            EndCharge(); KnifeReady = TrapReady = false;
            if(IsPlacingTrap || IsHookActive) _busyUntil=0;
            _trapPlaceUntil = _hookUntil = _hookRetrieveUntil = _hookReleaseAt = 0; _trapFromCopy = false; _trapCast++; _hookCast++;
        }
        public void ApplyHookPull(ExpandedSkillController caster,float duration)
        {
            if(!Authority || !AliveReady || caster==null || duration<=0 || !float.IsFinite(duration)) return;
            ApplyControl(duration,false,false);
            _hookedUntil=Now+duration; _hookCaster=caster.netIdentity; _offlineHookCaster=caster.transform;
            _hookLookPoint=caster.transform.position+Vector3.up*1.2f;
        }
        public bool TryGetHookLookPoint(out Vector3 point)
        {
            var caster=NetworkServer.active || NetworkClient.active ? (_hookCaster!=null ? _hookCaster.transform : null) : _offlineHookCaster;
            point=caster!=null ? caster.position+Vector3.up*1.2f : _hookLookPoint;
            return IsBeingHooked;
        }
        public Quaternion RestrictRotation(Quaternion requested)
        {
            if(TryGetHookLookPoint(out var point)) return Quaternion.LookRotation(Flat(point-transform.position));
            return Now<_rootUntil ? Quaternion.Euler(_rootRotation) : requested;
        }
        public void NotifyDamaged() { if(Authority) BreakStealth(); }
        public void NotifyAttackStarted() { _visuals?.InterruptCharge(); if(Authority) { BreakStealth(); EndCharge(); } }
        public void NotifyPhysicalHit(IDamageReceiver target)
        {
            if(!Authority || !Active(JobSkillKind.Bash) || target is not Component component) return;
            var state = Read(JobSkillKind.Bash); state.ActiveUntil = 0; States[(int)JobSkillKind.Bash] = state;
            component.GetComponentInParent<ExpandedSkillController>()?.ApplyControl(Value(JobSkillKind.Bash,"StunSeconds",3),false,true);
            component.GetComponentInParent<DummyHealth>()?.ApplyStun(Value(JobSkillKind.Bash,"StunSeconds",3),true);
        }
        private void BreakStealth()
        {
            if(!IsStealthed) return;
            var state = Read(JobSkillKind.Stealth); state.ActiveUntil = 0; States[(int)JobSkillKind.Stealth] = state;
            _ambushUntil = Now + Value(JobSkillKind.Stealth,"BonusSeconds",3);
        }
        private void EndBerserk()
        {
            if(!Berserking) return;
            var state = Read(JobSkillKind.Berserk); state.ActiveUntil=0; state.CooldownUntil=Now+Value(JobSkillKind.Berserk,"CooldownSeconds",20); States[(int)JobSkillKind.Berserk]=state;
            if (_maintainedCopy == (int)JobSkillKind.Berserk) _maintainedCopy = -1;
        }
        private void EndCharge()
        {
            RestoreChargeContacts();
            if(!IsCharging && !_wasCharging) return;
            var state=Read(JobSkillKind.Charge); state.ActiveUntil=0; state.CooldownUntil=Now+Value(JobSkillKind.Charge,"CooldownSeconds",20); States[(int)JobSkillKind.Charge]=state;
            _wasCharging=false;
        }
        private void Update()
        {
            UpdateTraps();
            if(!AliveReady) { if(_health != null && _health.IsDead) Cancel(); return; }
            _visuals?.Resume();
            _cancelled=false;
            if(Owner && IsCharging && Now >= _nextAim)
            {
                _nextAim=Now+.05;
                if(NetworkClient.active && !isServer) CmdSteer(_combat.SkillAimDirection); else Steer(_combat.SkillAimDirection);
            }
            if(Authority)
            {
                if(_wasCharging && !IsCharging) EndCharge();
                Recharge(Now);
                if(IsStealthed)
                {
                    _stealthTravel += Vector3.Distance(_lastStealthPosition,transform.position); _lastStealthPosition=transform.position;
                    if(_stealthTravel >= Value(JobSkillKind.Stealth,"BreakDistance",1)) BreakStealth();
                }
                if(Berserking && Now >= _nextDrain)
                {
                    _nextDrain += 1;
                    float remaining = _health.CurrentHp - _health.MaxHp * Value(JobSkillKind.Berserk,"HealthDrainRatio",.03f);
                    // Self-drain cannot kill; use the normal cooldown and regeneration penalty.
                    bool exhausted = remaining <= 1f + .0001f;
                    _health.SetCurrentHp(exhausted ? 1f : remaining);
                    if (exhausted) EndBerserk();
                }
                if(Active(JobSkillKind.Recovery)) _health.Heal(_health.MaxHp * Value(JobSkillKind.Recovery,"HealRatio",.2f) / Value(JobSkillKind.Recovery,"DurationSeconds",10) * Time.deltaTime);
                if(IsCharging) TickCharge();
                float all=Active(JobSkillKind.Dice) ? 1+Value(JobSkillKind.Dice,"Face"+_diceFace) : 1;
                _stats.SetCombatMultipliers(all,Active(JobSkillKind.Fortify) ? Value(JobSkillKind.Fortify,"DefenseMultiplier",2) : 1);
            }
            float move=(Active(JobSkillKind.WarCry) ? Value(JobSkillKind.WarCry,"MoveMultiplier",1.2f) : 1)*(Berserking ? Value(JobSkillKind.Berserk,"MoveMultiplier",1.2f) : 1)*(_combat!=null ? _combat.MonostatStrMoveMultiplier : 1);
            if(move != _lastMoveMultiplier) { _movement.SetMovementEffect(MoveSource,move,86400f); _lastMoveMultiplier=move; }
            _visuals?.Tick();
        }
        private void Recharge(double now)
        {
            foreach(var kind in ChargeSkills)
                if(States.TryGetValue((int)kind,out var state) && state.NextChargeAt>0 && now>=state.NextChargeAt)
                    States[(int)kind]=Recharged(kind,state,now);
        }
        [Command(channel=Channels.Unreliable)] private void CmdSteer(Vector3 aim) => Steer(aim);
        private void Steer(Vector3 aim)
        {
            if(!Authority || !IsCharging || !ValidAim(aim)) return;
            float elapsed=Mathf.Clamp((float)(Now-_lastTurn),0,.15f); _lastTurn=Now;
            _chargeDirection=Vector3.RotateTowards(_chargeDirection,Flat(aim),ChargeTurnRate*Mathf.Deg2Rad*elapsed,0).normalized;
        }
        private void TickCharge()
        {
            if(Now >= _nextChargeMove)
            {
                _nextChargeMove=Now+.08;
                float speed=ResolveChargeSpeed(Now);
                float step=Mathf.Min(.1f,(float)(Read(JobSkillKind.Charge).ActiveUntil-Now));
                Move(_movement,_chargeDirection,speed*step,step);
            }
            foreach(var collider in Physics.OverlapCapsule(transform.position+Vector3.up*.35f,transform.position+Vector3.up*1.4f,.55f,~0,QueryTriggerInteraction.Collide))
            {
                var target=collider.GetComponentInParent<IDamageReceiver>();
                if(target==null || ReferenceEquals(target,_health) || !_chargeHits.Add(target)) continue;
                var stats=collider.GetComponentInParent<StatManager>();
                if(stats!=null && GetComponent<AttackProcessor>().ProcessSkillHit(Value(JobSkillKind.Charge,"DamageMultiplier",1),stats,target,collider.ClosestPoint(transform.position+Vector3.up)))
                {
                    collider.GetComponentInParent<ExpandedSkillController>()?.ApplyControl(Value(JobSkillKind.Charge,"StunSeconds",1),false,false);
                    collider.GetComponentInParent<DummyHealth>()?.ApplyStun(Value(JobSkillKind.Charge,"StunSeconds",1),false);
                    // Reflection may have killed the charger while damage was being applied.
                    if(IsCharging && AliveReady && target is Component body) PassChargeTarget(body);
                }
            }
        }
        private float ResolveChargeSpeed(double now)
        {
            var mono=new StatContainer(); mono.AGI.Invested=30;
            float maximum=StatBalanceCalculator.Calculate(mono,new Identity(IdentityType.Monostat,StatKind.AGI)).MoveSpeed*Value(JobSkillKind.Charge,"MaximumAgiMultiplier",1.3f);
            float initial=_stats.GetDerivedStats().MoveSpeed*Value(JobSkillKind.Charge,"InitialSpeedMultiplier",.8f);
            return Mathf.Lerp(initial,maximum,Mathf.Clamp01((float)(now-_chargeStarted)/Value(JobSkillKind.Charge,"AccelerationSeconds",2.5f)));
        }
        private void PassChargeTarget(Component target)
        {
            if(_chargeController==null) return;
            foreach(var contact in target.GetComponentsInChildren<Collider>())
            {
                if(contact==_chargeController || contact.isTrigger || !contact.enabled || Physics.GetIgnoreCollision(_chargeController,contact)) continue;
                Physics.IgnoreCollision(_chargeController,contact,true);
                _chargeContacts.Add(contact);
            }
        }
        private void RestoreChargeContacts()
        {
            if(_chargeController!=null)
                foreach(var contact in _chargeContacts)
                    if(contact!=null) Physics.IgnoreCollision(_chargeController,contact,false);
            _chargeContacts.Clear();
        }
        private void Move(PlayerManager target,Vector3 direction,float distance,float duration)
        { if(NetworkServer.active) target.ServerAuthorizeForcedMove(direction,distance,duration); else target.MoveBySkill(direction,distance,duration); }
        private static Vector3 Flat(Vector3 direction) { direction.y=0; return direction.sqrMagnitude>.001f ? direction.normalized : Vector3.forward; }
        private static bool ValidAim(Vector3 aim) => CombatValidation.IsFinite(aim) && aim.sqrMagnitude>.01f && aim.sqrMagnitude<4;
        public SkillHudState Hud(int slot,JobSkillKind kind)
        {
            var state=UsesCharges(kind) ? ChargeState(kind) : Read(kind); int displayed=kind==JobSkillKind.Steal && CopiedKind>=0 ? CopiedKind : (int)kind;
            var data=SkillPresentationCatalog.Data(displayed);
            string name=data != null ? data.DisplayName : kind.ToString();
            if(UsesCharges(kind)) name += " "+state.Charges+"/"+MaxCharges(kind);
            var phase=Active(kind) || (kind==JobSkillKind.Knife && KnifeReady) || (displayed==(int)JobSkillKind.Trap && TrapReady) || (kind==JobSkillKind.Steal && CopiedKind>=0) ? SkillHudPhase.Active : SkillHudPhase.Ready;
            double deadline=UsesCharges(kind) ? state.NextChargeAt : state.CooldownUntil;
            if(phase==SkillHudPhase.Ready && deadline>Now && (!UsesCharges(kind) || state.Charges==0)) phase=SkillHudPhase.Cooldown;
            float remaining=(UsesCharges(kind) || phase==SkillHudPhase.Cooldown) && deadline>Now ? (float)(deadline-Now) : 0;
            float duration=UsesCharges(kind) ? RechargeSeconds(kind) : Value(kind,"CooldownSeconds");
            if(kind==JobSkillKind.Dice && Active(kind)) { remaining=(float)(state.ActiveUntil-Now); duration=Value(kind,"DurationSeconds",15); }
            if(kind==JobSkillKind.Trap && IsPlacingTrap) { phase=SkillHudPhase.Casting; remaining=(float)(_trapPlaceUntil-Now); duration=Value(kind,"CastSeconds",1.1f); }
            float fill=phase==SkillHudPhase.Active && kind!=JobSkillKind.Dice && !UsesCharges(kind) ? 1 : Mathf.Clamp01(remaining/Mathf.Max(.01f,duration));
            return new SkillHudState(true,name,slot,2,phase,fill,remaining,data!=null ? data.IconSprite : null);
        }
        private void Cue(JobSkillKind kind,Vector3 point) { if(NetworkServer.active) RpcCue((int)kind,point); else _visuals.Play(kind,point); }
        [ClientRpc] private void RpcCue(int kind,Vector3 point) => _visuals?.Play((JobSkillKind)kind,point);
        private void ProjectileCue(JobSkillKind kind,Vector3 start,Vector3 direction,float duration)
        { if(NetworkServer.active) RpcProjectile((int)kind,start,direction,duration); else _visuals.Projectile(kind,start,direction,duration); }
        [ClientRpc] private void RpcProjectile(int kind,Vector3 start,Vector3 direction,float duration) => _visuals?.Projectile((JobSkillKind)kind,start,direction,duration);
    }
}
