using System;
using UnityEngine;

namespace BattlePvp.Characters
{
    [CreateAssetMenu(menuName = "Battle PvP/Characters/Catalog", fileName = "CharacterCatalog")]
    public sealed class CharacterCatalog : ScriptableObject
    {
        public const string DefaultId = "default";
        public CharacterDefinition[] Characters = Array.Empty<CharacterDefinition>();
        public GameObject PreviewRig;
        public RuntimeAnimatorController PreviewController;
        private static CharacterCatalog _instance;
        public static CharacterCatalog Instance => _instance != null ? _instance : _instance = Resources.Load<CharacterCatalog>("CharacterCatalog");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _instance = null;

        public CharacterDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || Characters == null) return null;
            CharacterDefinition match = null;
            foreach (var entry in Characters)
            {
                if (entry == null || !string.Equals(entry.Id, id, StringComparison.Ordinal)) continue;
                if (match != null) return null; // Ambiguous IDs must never depend on catalog ordering.
                match = entry;
            }
            return match;
        }
        public string ResolveId(string id) => Find(id) != null ? id : DefaultId;
    }
}
