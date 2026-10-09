using System;
using BattlePvp.Stats;
using BattlePvp.UI;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Combat
{
    /// <summary>Server-approved two-slot choices for all six jobs. Choices cannot reset combat cooldowns.</summary>
    public sealed class SkillLoadout : NetworkBehaviour
    {
        public readonly SyncList<int> Choices = new();
        private StatManager _stats;
        private double _nextRequest;
        private bool _initialized;
        private double _initialDeadline;
        private int[] _acknowledged;
        public event Action Changed;
        public static bool CanEdit => LoadoutEditRules.CanEditSkills(StatManager.Local?.gameObject);
        private void Awake()
        {
            _stats = GetComponent<StatManager>();
            EnableOfflineWrites(Choices);
        }
        // Mirror rejects SyncObject writes without a network session. Lobby practice uses the
        // same state, while connected clients retain Mirror's original server authority checks.
        internal static void EnableOfflineWrites(SyncObject state)
        {
            var writable = state.IsWritable; var recording = state.IsRecording;
            state.IsWritable = () => (!NetworkServer.active && !NetworkClient.active) || writable();
            state.IsRecording = () => (NetworkServer.active || NetworkClient.active) && recording();
        }
        private void Start()
        {
            if ((!NetworkClient.active && !NetworkServer.active)) Apply(SkillLoadoutStore.Read());
        }
        public override void OnStartServer() { _initialDeadline=NetworkTime.time+5; Apply(Defaults()); }
        public override void OnStartLocalPlayer() { CmdSet(SkillLoadoutStore.Read()); }
        public static int[] Defaults()
        {
            var result = new int[12];
            for (int job = 0; job < 6; job++) for (int slot = 0; slot < 2; slot++)
            { CombatSkillRules.TrySelect(JobGuideContent.IdentityAt(job), slot, out var kind); result[job * 2 + slot] = (int)kind; }
            if (SkillGameData.Instance != null) foreach (var row in SkillGameData.Instance.Pools) if (row.DefaultSlot >= 0) result[row.Job * 2 + row.DefaultSlot] = row.Kind;
            return result;
        }
        public static bool Validate(int[] choices)
        {
            if (choices == null || choices.Length != 12) return false;
            for (int job = 0; job < 6; job++)
                if (choices[job * 2] == choices[job * 2 + 1] ||
                    !Allowed(job, choices[job * 2]) || !Allowed(job, choices[job * 2 + 1])) return false;
            return true;
        }
        private static bool Allowed(int job, int kind)
        {
            if (SkillGameData.Instance != null) { foreach (var row in SkillGameData.Instance.Pools) if (row.Job == job && row.Kind == kind) return true; return false; }
            return CombatSkillRules.Allows(JobGuideContent.IdentityAt(job), (JobSkillKind)kind);
        }
        public int[] Snapshot()
        {
            if (NetworkClient.active && netIdentity != null && isLocalPlayer && _acknowledged != null) return (int[])_acknowledged.Clone();
            var result = Defaults(); for (int i = 0; i < Math.Min(12, Choices.Count); i++) result[i] = Choices[i]; return result;
        }
        public bool Select(int slot, out JobSkillKind kind)
        {
            kind = default;
            if (_stats == null || slot < 0 || slot > 1) return false;
            int index = JobGuideContent.IndexOf(_stats.CurrentIdentity) * 2 + slot;
            if (Choices.Count == 12) { kind = (JobSkillKind)Choices[index]; return Allowed(index / 2, (int)kind); }
            return CombatSkillRules.TrySelect(_stats.CurrentIdentity, slot, out kind);
        }
        public bool Has(JobSkillKind kind) => (Select(0, out var first) && first == kind) || (Select(1, out var second) && second == kind);
        public bool Request(int[] choices)
        {
            if (!CanEdit || !Validate(choices)) return false;
            if (NetworkClient.active) { if (!isLocalPlayer) return false; CmdSet(choices); }
            else { Apply(choices); SkillLoadoutStore.Save(choices); }
            return true;
        }
        [Command] private void CmdSet(int[] choices)
        {
            if (NetworkTime.time < _nextRequest) return;
            _nextRequest = NetworkTime.time + .2;
            if (!TrySetChoices(choices)) return;
            TargetAccepted(connectionToClient, choices);
        }
        internal bool TrySetChoices(int[] choices)
        {
            bool first = !_initialized && NetworkTime.time <= _initialDeadline;
            if ((!first && !LoadoutEditRules.CanEditSkills(gameObject)) || !Validate(choices)) return false;
            _initialized = true; Apply(choices); return true;
        }
        [TargetRpc] private void TargetAccepted(NetworkConnectionToClient target, int[] choices)
        { _acknowledged=(int[])choices.Clone(); SkillLoadoutStore.Save(choices); Changed?.Invoke(); }
        // Server-owned actors have no client to submit their initial choices or save preferences.
        internal bool InitializeServerChoices(int[] choices)
        {
            if (!isServer || connectionToClient != null || _initialized || NetworkTime.time > _initialDeadline || !Validate(choices)) return false;
            _initialized = true;
            Apply(choices);
            return true;
        }
        private void Apply(int[] choices)
        {
            if (Choices.Count == 12)
            {
                GetComponent<ExpandedSkillController>()?.CancelForLoadout();
                GetComponent<PlayerCombat>()?.OnSkillLoadoutChanged();
            }
            Choices.Clear(); foreach (int kind in choices) Choices.Add(kind); Changed?.Invoke();
        }
    }
    public static class SkillLoadoutStore
    {
        [Serializable] private sealed class Saved { public int[] Choices; }
        // Account-scoped local preferences; never used as server authority.
        private static string Key => "BattlePvp.SkillLoadout.v1." + (PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
        public static int[] Read()
        {
            try { var saved = JsonUtility.FromJson<Saved>(PlayerPrefs.GetString(Key, "")); if (saved != null && SkillLoadout.Validate(saved.Choices)) return saved.Choices; }
            catch (ArgumentException) { }
            return SkillLoadout.Defaults();
        }
        public static void Save(int[] choices) { if (!SkillLoadout.Validate(choices)) return; PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Saved { Choices = choices })); PlayerPrefs.Save(); }
    }
}
