using System;
using System.Collections.Generic;
using BattlePvp.Networking;
using BattlePvp.Stats;
using NUnit.Framework;
using PlayFab.ClientModels;
using PlayFab.Json;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class ProfileDataIntegrityTests
    {
        [Test]
        public void OnlyAbsentProfileDataIsEmpty()
        {
            Assert.That(NetworkProfileRepository.Decode(null).Status, Is.EqualTo(ProfileLoadStatus.Empty));
            Assert.That(NetworkProfileRepository.Decode(Data("unrelated", "value")).Status, Is.EqualTo(ProfileLoadStatus.Empty));
            AssertFailure(Data("AGI", "12"));
            AssertFailure(Data("STR", "30"));
            AssertFailure(Data("Preset2_AGI", "12"));
            AssertFailure(Data("StrategistPreset_CON", "8"));
        }

        [TestCase("0")]
        [TestCase("30")]
        public void CompleteLegacyStatsIncludingZeroRemainCompatible(string strength)
        {
            var data = LegacyStats(strength);
            data["LifetimeKills"] = Record("42");
            PlayerProfileLoadResult result = NetworkProfileRepository.Decode(data);
            Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Success));
            Assert.That(result.Profile.SlotUsed[0], Is.True);
            Assert.That(result.Profile.Slots[0].STR.Invested, Is.EqualTo(float.Parse(strength)));
            Assert.That(result.Kills, Is.EqualTo(42));
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("invalid")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("-1")]
        [TestCase("31")]
        public void PresentInvalidLegacyValuesAreFailures(string value)
        {
            var data = LegacyStats(value);
            AssertFailure(data);
            data["STR"] = null;
            AssertFailure(data);
        }

        [Test]
        public void CompleteLegacyPresetsMigrateAndIncompleteMetadataFails()
        {
            var data = Data("SelectedStatPresetSlot", "1");
            for (int i = 1; i <= 3; i++)
            {
                string prefix = "Preset" + i;
                data[prefix + "_Used"] = Record(i == 2 ? "1" : "0");
                foreach (var stat in LegacyStats(i == 2 ? "30" : "0")) data[prefix + "_" + stat.Key] = stat.Value;
            }
            PlayerProfileLoadResult result = NetworkProfileRepository.Decode(data);
            Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Success));
            Assert.That(result.Profile.SelectedSlot, Is.EqualTo(1));
            Assert.That(result.Profile.Stats.STR.Invested, Is.EqualTo(30));
            data.Remove("Preset2_CON");
            AssertFailure(data);
            data["Preset2_CON"] = Record("0");
            data["Preset2_Used"] = Record("2");
            AssertFailure(data);
            data["Preset2_Used"] = Record("1");
            data["SelectedStatPresetSlot"] = Record("3");
            AssertFailure(data);
            data["SelectedStatPresetSlot"] = Record("1");
            data.Remove("Preset3_Used");
            AssertFailure(data);
        }

        [TestCase("1.5")]
        [TestCase("-1")]
        [TestCase("2147483648")]
        [TestCase("invalid")]
        public void InvalidCountersDoNotSilentlyBecomeZero(string value)
        {
            AssertFailure(Data("LifetimeKills", value));
            AssertFailure(Data("LifetimeDeaths", value));
        }

        [Test]
        public void CompleteVersionOneDocumentRoundTrips()
        {
            var snapshot = new PlayerProfileSnapshot { Revision = "revision", SelectedSlot = 1 };
            snapshot.Stats.STR.Invested = snapshot.Slots[1].STR.Invested = 30;
            snapshot.SlotUsed[1] = true;
            PlayerProfileLoadResult result = NetworkProfileRepository.Decode(Document(JsonUtility.ToJson(snapshot)));
            Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Success));
            Assert.That(result.Profile.Stats.STR.Invested, Is.EqualTo(30));
            Assert.That(result.Profile.Slots[1].STR.Invested, Is.EqualTo(30));
            Assert.That(NetworkProfileRepository.Decode(Document(JsonUtility.ToJson(new PlayerProfileSnapshot()))).Status,
                Is.EqualTo(ProfileLoadStatus.Success));
        }

        [TestCase("SchemaVersion")]
        [TestCase("Stats")]
        [TestCase("Slots")]
        [TestCase("SlotUsed")]
        [TestCase("SelectedSlot")]
        [TestCase("StrategistPreset")]
        [TestCase("HasStrategistPreset")]
        public void MissingDocumentFieldsCannotBeDeserializedAsDefaults(string key)
        {
            var document = NewJsonDocument();
            document.Remove(key);
            AssertFailure(Document(PlayFabSimpleJson.SerializeObject(document)));
        }

        [Test]
        public void InvalidNestedDocumentValuesAndTypesFail()
        {
            var document = NewJsonDocument();
            var stats = (IDictionary<string, object>)document["Stats"];
            var strength = (IDictionary<string, object>)stats["STR"];
            strength.Remove("Invested");
            AssertFailure(Document(PlayFabSimpleJson.SerializeObject(document)));
            strength["Invested"] = "invalid";
            AssertFailure(Document(PlayFabSimpleJson.SerializeObject(document)));
            strength["Invested"] = 31;
            AssertFailure(Document(PlayFabSimpleJson.SerializeObject(document)));
            strength["Invested"] = 0;
            document["SelectedSlot"] = 1.5;
            AssertFailure(Document(PlayFabSimpleJson.SerializeObject(document)));
            document["SelectedSlot"] = 0;
            ((System.Collections.IList)document["SlotUsed"])[0] = "false";
            AssertFailure(Document(PlayFabSimpleJson.SerializeObject(document)));
        }

        private static IDictionary<string, object> NewJsonDocument() =>
            (IDictionary<string, object>)PlayFabSimpleJson.DeserializeObject(JsonUtility.ToJson(new PlayerProfileSnapshot()));

        private static Dictionary<string, UserDataRecord> LegacyStats(string strength) =>
            new Dictionary<string, UserDataRecord>
            {
                { "STR", Record(strength) }, { "CON", Record("0") }, { "AGI", Record("0") }, { "DEF", Record("0") }
            };

        private static Dictionary<string, UserDataRecord> Document(string json) => Data(NetworkProfileRepository.ProfileKey, json);
        private static Dictionary<string, UserDataRecord> Data(string key, string value) =>
            new Dictionary<string, UserDataRecord> { { key, Record(value) } };
        private static UserDataRecord Record(string value) => new UserDataRecord { Value = value };
        private static void AssertFailure(Dictionary<string, UserDataRecord> data)
        {
            PlayerProfileLoadResult result = NetworkProfileRepository.Decode(data);
            Assert.That(result.Status, Is.EqualTo(ProfileLoadStatus.Failure));
            Assert.That(result.Profile, Is.Null);
        }
    }
}
