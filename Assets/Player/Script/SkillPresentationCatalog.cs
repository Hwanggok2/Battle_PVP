using System;
using UnityEngine;
namespace BattlePvp.Combat
{
    [Serializable] public sealed class SkillPresentationEntry
    {
        public int Kind; public JobSkillData Data; public GameObject Prefab;
    }
    public sealed class SkillPresentationCatalog : ScriptableObject
    {
        public TMPro.TMP_FontAsset Font;
        public SkillPresentationEntry[] Entries = Array.Empty<SkillPresentationEntry>();
        private static SkillPresentationCatalog _instance;
        public static SkillPresentationCatalog Instance => _instance != null ? _instance : _instance = Resources.Load<SkillPresentationCatalog>("SkillPresentationCatalog");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void ResetCache() => _instance = null;
        public SkillPresentationEntry Find(int kind) { foreach (var row in Entries) if (row.Kind == kind) return row; return null; }
        public static JobSkillData Data(int kind) => Instance != null ? Instance.Find(kind)?.Data : null;
    }
}
