using System;
using System.Collections.Generic;
using BattlePvp.Stats;
using Mirror;
using UnityEngine;

namespace BattlePvp.Combat
{
    public enum PassiveKind
    {
        None, Counterattack, Backstab, Purification, HealingShield, Concussion,
        Berserker, Sniper, Victory, Scholar, DoubleJump, Haste, Vitality, Ironclad
    }

    /// <summary>Two server-approved passive slots and their per-life combat triggers.</summary>
    public sealed class PassiveLoadout : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnSelectionChanged))] private int _selection;
        private bool _initialized;
        private double _initialDeadline, _nextRequest, _counterUntil, _counterReadyAt, _healReadyAt, _backstabReadyAt;
        private HealthSystem _health;
        private readonly Dictionary<Component, HeadWindow> _heads = new();
        public event Action Changed;
        private bool Authority => isServer || (!NetworkServer.active && !NetworkClient.active);
        internal double Now => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        public static bool CanEdit => LoadoutEditRules.CanEditSkills(StatManager.Local?.gameObject);
        private void Awake() => _health = GetComponent<HealthSystem>();
        private void OnEnable()
        {
            if (_health == null) _health = GetComponent<HealthSystem>();
            if (_health != null) { _health.OnDied += ResetLife; _health.OnRevived += ResetLife; }
        }
        private void OnDisable()
        {
            if (_health != null) { _health.OnDied -= ResetLife; _health.OnRevived -= ResetLife; }
            ResetLife();
        }
        private void Start() { if (!NetworkServer.active && !NetworkClient.active) Apply(PassiveStore.Read()); }
        public override void OnStartServer() { _initialDeadline = Now + 5; }
        public override void OnStartLocalPlayer() => CmdSet(PassiveStore.Read());
        public int[] Snapshot() => new[] { _selection & 255, (_selection >> 8) & 255 };
        public bool Has(PassiveKind kind) => kind != PassiveKind.None &&
            ((_selection & 255) == (int)kind || ((_selection >> 8) & 255) == (int)kind);
        public static bool Validate(int[] choices) => choices != null && choices.Length == 2 &&
            choices[0] >= 0 && choices[0] <= 13 && choices[1] >= 0 && choices[1] <= 13 &&
            (choices[0] == 0 || choices[0] != choices[1]);
        public bool Request(int[] choices)
        {
            if (!CanEdit || !Validate(choices)) return false;
            if (NetworkClient.active) { if (!isLocalPlayer) return false; CmdSet(choices); }
            else { Apply(choices); PassiveStore.Save(choices); }
            return true;
        }
        [Command] private void CmdSet(int[] choices)
        {
            if (Now < _nextRequest) return;
            _nextRequest = Now + .2;
            if (!TrySetChoices(choices)) return;
            TargetAccepted(connectionToClient, choices);
        }
        internal bool TrySetChoices(int[] choices)
        {
            if (!Authority || !Validate(choices) ||
                (!(!_initialized && Now <= _initialDeadline) && !LoadoutEditRules.CanEditSkills(gameObject))) return false;
            _initialized = true; Apply(choices); return true;
        }
        [TargetRpc] private void TargetAccepted(NetworkConnectionToClient target, int[] choices)
        {
            // Apply the acknowledged pair atomically, even before the SyncVar update arrives.
            Apply(choices); PassiveStore.Save(choices);
        }
        private void Apply(int[] choices)
        {
            if (!Validate(choices)) return;
            int value = choices[0] | (choices[1] << 8);
            if (_selection == value) { Changed?.Invoke(); return; }
            int previous = _selection; _selection = value;
            OnSelectionChanged(previous, value);
        }
        private void OnSelectionChanged(int previous, int current)
        {
            GetComponent<StatManager>()?.RefreshCharacterStats();
            Changed?.Invoke();
        }
        private void ResetLife()
        {
            _counterUntil = _counterReadyAt = _healReadyAt = _backstabReadyAt = 0;
            _heads.Clear();
        }
        public float CooldownMultiplier => Has(PassiveKind.Scholar) ? .88f : 1f;
        public float DebuffMultiplier => Has(PassiveKind.Purification) ? .85f : 1f;
        private float BerserkerBonus => Has(PassiveKind.Berserker) && _health != null && _health.MaxHp > 0
            ? .15f * (1 - Mathf.Clamp01(_health.CurrentHp / _health.MaxHp)) : 0;
        public float AttackSpeedMultiplier => 1 + (Has(PassiveKind.Haste) ? .1f : 0) + BerserkerBonus;
        public static float DebuffDuration(Component target, float seconds) => seconds *
            (target != null ? target.GetComponentInParent<PassiveLoadout>()?.DebuffMultiplier ?? 1f : 1f);
        public DerivedCombatStats ModifyStats(DerivedCombatStats value)
        {
            float haste = Has(PassiveKind.Haste) ? .1f : 0;
            return new DerivedCombatStats(value.AttackPower * (1 + BerserkerBonus), value.PenetrationPercent,
                value.MaxHp * (Has(PassiveKind.Vitality) ? 1.1f : 1), value.RegenPerSecond,
                Mathf.Min(value.DefenseEfficiencyPercent * (Has(PassiveKind.Ironclad) ? 1.1f : 1), StatBalanceCalculator.Config.DefenseEfficiencyHardCap * 100),
                value.DefenseBonusNormalized, value.MoveSpeed * (1 + haste), value.AttackSpeed * AttackSpeedMultiplier, value.IncomingDamageMultiplier);
        }
        public void BlockSucceeded(bool shield)
        {
            if (!Authority || _health == null || _health.IsDead) return;
            if (Has(PassiveKind.Counterattack) && Now >= _counterReadyAt) { _counterUntil = Now + 2; _counterReadyAt = Now + 5; }
            if (shield && Has(PassiveKind.HealingShield) && Now >= _healReadyAt)
            { _healReadyAt = Now + 6; HealCapped(.02f); }
        }
        private bool CanBackstab(Component target) => Has(PassiveKind.Backstab) && target != null && Now >= _backstabReadyAt &&
            IsBehind(target.transform, transform.position);
        internal static bool IsBehind(Transform victim, Vector3 attackerPosition)
        {
            Vector3 offset = Vector3.ProjectOnPlane(attackerPosition - victim.position, Vector3.up);
            return offset.sqrMagnitude > .0001f && Vector3.Dot(victim.forward, offset.normalized) <= -.7071068f;
        }
        public float DamageMultiplier(Component target, DamageDelivery delivery)
        {
            float bonus = delivery == DamageDelivery.Ranged && Has(PassiveKind.Sniper) ? .12f : 0;
            if (delivery == DamageDelivery.Melee)
            {
                if (Has(PassiveKind.Counterattack) && Now < _counterUntil) bonus += .1f;
                if (CanBackstab(target)) bonus += .1f;
            }
            return 1 + bonus;
        }
        public void HitAccepted(Component target, BodyPart part, DamageDelivery delivery, DamageResult result)
        {
            if (!Authority || target == null || !result.Accepted || (result.HpDamage <= 0 && result.ShieldDamage <= 0)) return;
            if (delivery == DamageDelivery.Melee)
            {
                _counterUntil = 0;
                if (CanBackstab(target)) _backstabReadyAt = Now + 4;
            }
            if (part != BodyPart.Head || !Has(PassiveKind.Concussion) || result.Killed) return;
            uint life = target.GetComponent<HealthSystem>()?.DeathSequence ?? 0;
            if (!_heads.TryGetValue(target, out var window)) _heads[target] = window = new HeadWindow();
            if (!window.Hit(Now, life)) return;
            target.GetComponent<ExpandedSkillController>()?.ApplyControl(1, false, false);
            target.GetComponent<DummyHealth>()?.ApplyStun(1, false);
        }
        public void EnemyKilled(HealthSystem victim)
        {
            if (Authority && victim != null && victim != _health && Has(PassiveKind.Victory)) HealCapped(.2f);
        }
        private void HealCapped(float ratio)
        {
            if (_health != null && !_health.IsDead && _health.CurrentHp < _health.MaxHp) _health.Heal(_health.MaxHp * ratio);
        }
        internal sealed class HeadWindow
        {
            private readonly Queue<double> _times = new(4);
            private uint _life;
            public bool Hit(double now, uint life)
            {
                if (_life != life) { _times.Clear(); _life = life; }
                while (_times.Count > 0 && now - _times.Peek() > 4) _times.Dequeue();
                _times.Enqueue(now);
                if (_times.Count < 4) return false;
                _times.Clear(); return true;
            }
        }
    }
    public static class PassiveStore
    {
        [Serializable] private sealed class Saved { public int[] Choices; }
        private static string Key => "BattlePvp.Passives.v1." + (PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
        public static int[] Read()
        {
            try { var saved = JsonUtility.FromJson<Saved>(PlayerPrefs.GetString(Key, "")); if (saved != null && PassiveLoadout.Validate(saved.Choices)) return saved.Choices; }
            catch (ArgumentException) { }
            return new int[2];
        }
        public static void Save(int[] choices)
        {
            if (!PassiveLoadout.Validate(choices)) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Saved { Choices = choices })); PlayerPrefs.Save();
        }
    }
}
