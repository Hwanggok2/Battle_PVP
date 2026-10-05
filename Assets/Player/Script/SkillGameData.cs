using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BattlePvp.Combat
{
    [Serializable] public sealed class SkillParameter { public string Key; public float Value; }
    [Serializable] public sealed class SkillDefinition
    {
        public string Id, NameKey, DescriptionKey, DescriptionArgs;
        public int Kind;
        public float Cast, Duration, Cooldown;
        public SkillParameter[] Parameters = Array.Empty<SkillParameter>();
        public float Value(string key, float fallback = 0)
        { foreach (var p in Parameters) if (p.Key == key) return p.Value; return fallback; }
    }
    [Serializable] public sealed class JobSkillPool { public int Job; public int Kind; public int DefaultSlot = -1; }
    [Serializable] public sealed class JobDefinition { public int Index; public string Id, NameKey, RequirementKey, DescriptionKey; }
    [Serializable] public sealed class SkillString { public string SearchKey, Content_Kor, Content_Eng; public int FormatArgCount; }

    /// <summary>Generated from the validated Excel workbooks. Unity object references stay in the presentation catalog.</summary>
    public sealed class SkillGameData : ScriptableObject
    {
        public SkillDefinition[] Skills = Array.Empty<SkillDefinition>();
        public JobSkillPool[] Pools = Array.Empty<JobSkillPool>();
        public JobDefinition[] Jobs = Array.Empty<JobDefinition>();
        public SkillString[] Strings = Array.Empty<SkillString>();
        private readonly Dictionary<int, SkillDefinition> _skills = new();
        private readonly Dictionary<string, SkillString> _strings = new(StringComparer.Ordinal);
        private static SkillGameData _instance;
        public static SkillGameData Instance => _instance != null ? _instance : _instance = Resources.Load<SkillGameData>("SkillGameData");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void ResetCache() => _instance = null;
        private void OnEnable() => Reindex();
        public void Reindex()
        {
            _skills.Clear(); _strings.Clear();
            foreach (var row in Skills) _skills.Add(row.Kind, row);
            foreach (var row in Strings) _strings.Add(row.SearchKey, row);
        }
        public SkillDefinition Find(int kind) => _skills.TryGetValue(kind, out var row) ? row : null;
        public static float Number(JobSkillKind kind, string key, float fallback)
        {
            var row = Instance != null ? Instance.Find((int)kind) : null;
            if (row == null) return fallback;
            return key switch { "CastSeconds" => row.Cast, "DurationSeconds" => row.Duration, "CooldownSeconds" => row.Cooldown, _ => row.Value(key, fallback) };
        }
        public static string Text(string key, string fallback = "", bool english = false)
        {
            if (Instance == null || !Instance._strings.TryGetValue(key, out var row)) return fallback;
            return english ? row.Content_Eng : row.Content_Kor;
        }
        public static string Description(JobSkillKind kind, bool english = false)
        {
            var row = Instance != null ? Instance.Find((int)kind) : null;
            if (row == null) return string.Empty;
            string[] keys = string.IsNullOrEmpty(row.DescriptionArgs) ? Array.Empty<string>() : row.DescriptionArgs.Split(',');
            var args = new object[keys.Length];
            for (int i = 0; i < keys.Length; i++) args[i] = Number(kind, keys[i], 0);
            return string.Format(CultureInfo.InvariantCulture, Text(row.DescriptionKey, "", english), args);
        }
    }

}
