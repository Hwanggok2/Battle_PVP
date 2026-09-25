using System;
using System.Collections.Generic;
using System.Globalization;
using BattlePvp.Stats;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Json;
using UnityEngine;

namespace BattlePvp.Networking
{
    public enum ProfileLoadStatus { Success, Empty, Failure }

    // Client editable preferences only. This document must never authorize combat or rewards.
    [Serializable]
    public sealed class PlayerProfileSnapshot
    {
        public int SchemaVersion = 1;
        public string Revision;
        public StatContainer Stats;
        public StatContainer[] Slots = new StatContainer[3];
        public bool[] SlotUsed = new bool[3];
        public int SelectedSlot;
        public StatContainer StrategistPreset;
        public bool HasStrategistPreset;
    }

    public sealed class PlayerProfileLoadResult
    {
        public ProfileLoadStatus Status { get; }
        public PlayerProfileSnapshot Profile { get; }
        public int Kills { get; }
        public int Deaths { get; }
        public string Error { get; }
        public bool Succeeded => Status != ProfileLoadStatus.Failure;

        public PlayerProfileLoadResult(ProfileLoadStatus status, PlayerProfileSnapshot profile = null,
            int kills = 0, int deaths = 0, string error = null)
        {
            Status = status;
            Profile = profile;
            Kills = kills;
            Deaths = deaths;
            Error = error;
        }
    }

    // No scene, player, HUD or global store dependency. API delegates also allow failure/order tests.
    public sealed class NetworkProfileRepository
    {
        public const string ProfileKey = "PlayerProfileV1";
        // Deliberately not cleared on manager disable/replacement or SubsystemRegistration.
        // The managed domain owns these live SDK writes; a timeout is not server cancellation.
        private static readonly ProfileWriteCoordinator _productionWrites = new ProfileWriteCoordinator();
        private readonly ProfileRequestQueue<PlayerProfileLoadResult> _requests;

        public NetworkProfileRepository() : this(
            (success, failure) => PlayFabClientAPI.GetUserData(new GetUserDataRequest(),
                result => success(result.Data), error => failure(error.ErrorMessage)),
            null, accountKey: CreateProductionAccountKey(),
            writeWithUnconfirmed: (data, success, failure, unconfirmed) => PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest
            {
                Data = data,
                Permission = UserDataPermission.Private
            }, _ => success(), error =>
            {
                if (IsConfirmedWriteRejection(error)) failure(error.ErrorMessage);
                else unconfirmed("The profile write could not be confirmed.");
            }), writeCoordinator: _productionWrites) { }

        public NetworkProfileRepository(
            Action<Action<Dictionary<string, UserDataRecord>>, Action<string>> read,
            Action<Dictionary<string, string>, Action, Action<string>> write,
            Func<double> clock = null, Func<string> accountKey = null, Action<Exception> callbackError = null,
            Action<Dictionary<string, string>, Action, Action<string>, Action<string>> writeWithUnconfirmed = null,
            ProfileWriteCoordinator writeCoordinator = null)
        {
            _requests = new ProfileRequestQueue<PlayerProfileLoadResult>(
                (success, failure) => read(data => success(Decode(data)), failure), write,
                error => new PlayerProfileLoadResult(ProfileLoadStatus.Failure, error: error),
                clock ?? (() => Time.realtimeSinceStartupAsDouble), accountKey ?? (() => "repository"),
                callbackError ?? (error => Debug.LogException(error)), writeWithUnconfirmed, writeCoordinator);
        }

        private static Func<string> CreateProductionAccountKey()
        {
            string previousTitle = null;
            string previousPlayer = null;
            string key = null;
            return () =>
            {
                string title = PlayFabSettings.staticSettings.TitleId ?? string.Empty;
                string player = PlayFabSettings.staticPlayer.PlayFabId ?? string.Empty;
                if (key == null || !string.Equals(title, previousTitle, StringComparison.Ordinal) ||
                    !string.Equals(player, previousPlayer, StringComparison.Ordinal))
                {
                    previousTitle = title;
                    previousPlayer = player;
                    key = title + "/" + player;
                }
                return key;
            };
        }

        public static bool IsConfirmedWriteRejection(PlayFabError error)
        {
            // The installed SDK may synthesize HTTP 400/ServiceUnavailable from an unparseable
            // transport response. HTTP status alone is not evidence that a write was rejected.
            if (error == null || error.HttpCode < 400 || error.HttpCode >= 500 || error.HttpCode == 408) return false;
            switch (error.Error)
            {
                case PlayFabErrorCode.InvalidParams:
                case PlayFabErrorCode.AccountNotFound:
                case PlayFabErrorCode.AccountBanned:
                case PlayFabErrorCode.InvalidTitleId:
                case PlayFabErrorCode.BodyTooLarge:
                case PlayFabErrorCode.InvalidTypeInBody:
                case PlayFabErrorCode.InvalidRequest:
                case PlayFabErrorCode.NotAuthenticated:
                case PlayFabErrorCode.InvalidAccount:
                case PlayFabErrorCode.APINotEnabledForGameClientAccess:
                case PlayFabErrorCode.NotAuthorized:
                case PlayFabErrorCode.InvalidSessionTicket:
                case PlayFabErrorCode.KeyLengthExceeded:
                case PlayFabErrorCode.DataLengthExceeded:
                case PlayFabErrorCode.TooManyKeys:
                    return true;
                default:
                    return false;
            }
        }

        public void Load(Action<PlayerProfileLoadResult> completed) => _requests.Load(completed);
        public void ResetSession() => _requests.ResetSession();
        public void Tick() => _requests.Tick();

        public void Save(PlayerProfileSnapshot snapshot, Action<bool, string> completed = null)
        {
            // Serialize now, before entering the queue: subsequent UI edits cannot mutate this save.
            snapshot.Revision = Guid.NewGuid().ToString("N");
            _requests.Save(new Dictionary<string, string> { { ProfileKey, JsonUtility.ToJson(snapshot) } }, completed);
        }

        public void SaveCombatRecord(int kills, int deaths, Action<bool, string> completed = null)
        {
            _requests.Save(new Dictionary<string, string>
            {
                { "LifetimeKills", Math.Max(0, kills).ToString(CultureInfo.InvariantCulture) },
                { "LifetimeDeaths", Math.Max(0, deaths).ToString(CultureInfo.InvariantCulture) }
            }, completed);
        }

        public static PlayerProfileLoadResult Decode(Dictionary<string, UserDataRecord> data)
        {
            try
            {
                var profile = new PlayerProfileSnapshot();
                bool hasData = false;
                if (data != null && data.TryGetValue(ProfileKey, out UserDataRecord document))
                {
                    // JsonUtility supplies defaults for missing fields. Check the persisted shape
                    // before deserializing so a partial document cannot become a new empty profile.
                    ValidateDocumentShape(document?.Value);
                    profile = JsonUtility.FromJson<PlayerProfileSnapshot>(document.Value);
                    if (profile == null || profile.SchemaVersion != 1 || profile.Slots == null ||
                        profile.SlotUsed == null || profile.Slots.Length != 3 || profile.SlotUsed.Length != 3 ||
                        profile.SelectedSlot < 0 || profile.SelectedSlot >= 3)
                        throw new FormatException("Unsupported or incomplete player profile document.");
                    hasData = true;
                }
                else if (data != null)
                {
                    // A legacy group is either absent or complete. Present invalid values are
                    // errors, never implicit zeroes that a later migration would overwrite.
                    bool hasStats = TryReadLegacyStats(data, "", out profile.Stats);
                    bool hasPresets = data.ContainsKey("SelectedStatPresetSlot");
                    for (int i = 1; i <= 3; i++)
                        hasPresets |= HasLegacyStatKey(data, "Preset" + i + "_") || data.ContainsKey("Preset" + i + "_Used");
                    if (hasPresets)
                    {
                        profile.SelectedSlot = ReadRequiredInt(data, "SelectedStatPresetSlot", 2);
                        for (int i = 0; i < 3; i++)
                        {
                            string prefix = "Preset" + (i + 1);
                            profile.SlotUsed[i] = ReadRequiredInt(data, prefix + "_Used", 1) == 1;
                            bool present = TryReadLegacyStats(data, prefix + "_", out profile.Slots[i]);
                            if (profile.SlotUsed[i] && !present)
                                throw new FormatException("A used legacy preset is incomplete.");
                        }
                        profile.Stats = profile.SlotUsed[profile.SelectedSlot] ? profile.Slots[profile.SelectedSlot] : default;
                    }
                    else if (hasStats)
                    {
                        profile.Slots[0] = profile.Stats;
                        profile.SlotUsed[0] = true;
                    }

                    bool hasStrategist = TryReadLegacyStats(data, "StrategistPreset_", out profile.StrategistPreset);
                    if (hasStrategist || data.ContainsKey("StrategistPresetUsed"))
                    {
                        profile.HasStrategistPreset = ReadRequiredInt(data, "StrategistPresetUsed", 1) == 1;
                        if (profile.HasStrategistPreset && !hasStrategist)
                            throw new FormatException("The legacy strategist preset is incomplete.");
                    }
                    hasData = hasStats || hasPresets || hasStrategist || data.ContainsKey("StrategistPresetUsed");
                }

                ValidatePreset(profile.Stats);
                foreach (StatContainer slot in profile.Slots) ValidatePreset(slot);
                ValidatePreset(profile.StrategistPreset);
                int kills = ReadOptionalInt(data, "LifetimeKills");
                int deaths = ReadOptionalInt(data, "LifetimeDeaths");
                hasData |= data != null && (data.ContainsKey("LifetimeKills") || data.ContainsKey("LifetimeDeaths"));
                return new PlayerProfileLoadResult(hasData ? ProfileLoadStatus.Success : ProfileLoadStatus.Empty,
                    profile, kills, deaths);
            }
            catch (Exception)
            {
                return new PlayerProfileLoadResult(ProfileLoadStatus.Failure,
                    error: "The saved profile is incomplete or contains invalid values.");
            }
        }

        private static bool TryReadLegacyStats(Dictionary<string, UserDataRecord> data, string prefix, out StatContainer stats)
        {
            stats = default;
            if (!HasLegacyStatKey(data, prefix)) return false;
            stats.STR.Invested = ReadRequiredFloat(data, prefix + "STR");
            stats.CON.Invested = ReadRequiredFloat(data, prefix + "CON");
            stats.AGI.Invested = ReadRequiredFloat(data, prefix + "AGI");
            stats.DEF.Invested = ReadRequiredFloat(data, prefix + "DEF");
            ValidatePreset(stats);
            return true;
        }

        private static bool HasLegacyStatKey(Dictionary<string, UserDataRecord> data, string prefix) =>
            data.ContainsKey(prefix + "STR") || data.ContainsKey(prefix + "CON") ||
            data.ContainsKey(prefix + "AGI") || data.ContainsKey(prefix + "DEF");

        private static float ReadRequiredFloat(Dictionary<string, UserDataRecord> data, string key)
        {
            if (data.TryGetValue(key, out UserDataRecord value) && value != null &&
                float.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) &&
                float.IsFinite(number)) return number;
            throw new FormatException("A legacy stat value is missing or invalid.");
        }

        private static int ReadOptionalInt(Dictionary<string, UserDataRecord> data, string key) =>
            data == null || !data.ContainsKey(key) ? 0 : ReadRequiredInt(data, key, int.MaxValue);

        private static int ReadRequiredInt(Dictionary<string, UserDataRecord> data, string key, int maximum)
        {
            if (data.TryGetValue(key, out UserDataRecord value) && value != null &&
                int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) &&
                number >= 0 && number <= maximum) return number;
            throw new FormatException("A legacy profile field is missing or invalid.");
        }

        private static void ValidatePreset(StatContainer stats)
        {
            if (!StatValidation.IsValidPreset(stats)) throw new FormatException("Invalid saved stat preset.");
        }

        private static void ValidateDocumentShape(string json)
        {
            var document = PlayFabSimpleJson.DeserializeObject(json) as IDictionary<string, object>;
            if (document == null || !document.TryGetValue("SchemaVersion", out object schema) || !(Equals(schema, 1UL) || Equals(schema, 1L)) ||
                !document.TryGetValue("SelectedSlot", out object selected) || !IsJsonSlotIndex(selected) ||
                !document.TryGetValue("HasStrategistPreset", out object strategistUsed) || !(strategistUsed is bool) ||
                !document.TryGetValue("Slots", out object slotsValue) || !(slotsValue is System.Collections.IList slots) || slots.Count != 3 ||
                !document.TryGetValue("SlotUsed", out object usedValue) || !(usedValue is System.Collections.IList used) || used.Count != 3)
                throw new FormatException("Incomplete profile document.");
            ValidateStatShape(document.TryGetValue("Stats", out object stats) ? stats : null);
            ValidateStatShape(document.TryGetValue("StrategistPreset", out object strategist) ? strategist : null);
            for (int i = 0; i < 3; i++)
            {
                if (!(used[i] is bool)) throw new FormatException("Invalid preset flag.");
                ValidateStatShape(slots[i]);
            }
        }

        private static void ValidateStatShape(object value)
        {
            if (!(value is IDictionary<string, object> stats)) throw new FormatException("Incomplete saved stats.");
            foreach (string key in new[] { "STR", "CON", "AGI", "DEF" })
            {
                if (!stats.TryGetValue(key, out object slotValue) || !(slotValue is IDictionary<string, object> slot) ||
                    !slot.TryGetValue("Invested", out object invested) || !IsJsonNumber(invested) ||
                    !slot.TryGetValue("Item", out object item) || !IsJsonNumber(item))
                    throw new FormatException("Incomplete saved stat slot.");
            }
        }

        private static bool IsJsonSlotIndex(object value) =>
            value is ulong unsigned && unsigned <= 2 || value is long signed && signed >= 0 && signed <= 2;

        private static bool IsJsonNumber(object value) => value is long || value is ulong || value is double number && double.IsFinite(number);
    }
}
