using UnityEngine;

namespace BattlePvp.Characters
{
    [CreateAssetMenu(menuName = "Battle PvP/Characters/Character", fileName = "NewCharacter")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [Tooltip("Stable, unique ID. Do not rename after release.")]
        public string Id;
        public string DisplayName;
        [TextArea] public string Description;
        [Tooltip("Creator, license and modification credit shown in the character selector.")]
        public string Attribution;
        public string SourceUrl;
        public string LicenseUrl;
        public Sprite Portrait;
        public bool UseDefaultBody;
        [Tooltip("Multipliers relative to the original character, after the same stat allocation and identity bonuses.")]
        public CharacterStatModifiers CombatModifiers = CharacterStatModifiers.Baseline;
        [Tooltip("Visual-only Humanoid prefab with its original proportions and skeleton.")]
        public GameObject VisualPrefab;
        [Tooltip("A skin from a model asset, bound to the existing player's skeleton and bind pose. No gameplay prefab is instantiated.")]
        public SkinnedMeshRenderer Body;
        [Tooltip("Optional material override. Supply one material per submesh, or leave empty to use the source materials.")]
        public Material[] Materials = System.Array.Empty<Material>();
    }
}
