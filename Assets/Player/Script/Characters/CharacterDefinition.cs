using UnityEngine;
using System.Collections;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("VisualPrefab"), SerializeField] private GameObject _visualPrefab;
        [SerializeField] private string _visualResource;
        [System.NonSerialized] private GameObject _loadedVisual;
        [System.NonSerialized] private ResourceRequest _visualRequest;
        public bool HasVisual => _visualPrefab != null || !string.IsNullOrEmpty(_visualResource);
        public bool IsVisualLoaded => _visualPrefab != null || _loadedVisual != null || !HasVisual;
        public GameObject VisualPrefab
        {
            get => _visualPrefab != null ? _visualPrefab : _loadedVisual != null ? _loadedVisual :
                string.IsNullOrEmpty(_visualResource) ? null : _loadedVisual = Resources.Load<GameObject>(_visualResource);
            set { _visualPrefab = value; _visualResource = null; _loadedVisual = null; _visualRequest = null; }
        }
        public IEnumerator LoadVisualAsync()
        {
            if (IsVisualLoaded) yield break;
            var request = _visualRequest ??= Resources.LoadAsync<GameObject>(_visualResource);
            yield return request;
            _loadedVisual = request.asset as GameObject;
            if (_visualRequest == request) _visualRequest = null;
        }
#if UNITY_EDITOR
        public void SetVisualResource(string path)
        { _visualResource = path; _visualPrefab = null; _loadedVisual = null; _visualRequest = null; }
#endif
        [Tooltip("A skin from a model asset, bound to the existing player's skeleton and bind pose. No gameplay prefab is instantiated.")]
        public SkinnedMeshRenderer Body;
        [Tooltip("Optional material override. Supply one material per submesh, or leave empty to use the source materials.")]
        public Material[] Materials = System.Array.Empty<Material>();
    }
}
