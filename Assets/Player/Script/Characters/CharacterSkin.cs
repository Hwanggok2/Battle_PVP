using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.Characters
{
    /// <summary>Adds a visual skeleton while retaining the gameplay rig, animation, weapons and hitboxes.</summary>
    public sealed class CharacterSkin : IDisposable
    {
        private readonly SkinnedMeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Material[] _materials;
        private readonly Transform[] _bones;
        private readonly Bounds _bounds;
        private GameObject _visual;
        private CharacterDefinition _definition;
        public SkinnedMeshRenderer VisibleBody => _visual != null
            ? _visual.GetComponentInChildren<SkinnedMeshRenderer>() : _renderer;
        public void SyncPose() { if (_visual != null) _visual.GetComponent<CharacterPoseFollower>().SyncPose(); }
        public CharacterSkin(SkinnedMeshRenderer renderer)
        {
            _renderer = renderer;
            if (renderer == null) return;
            _mesh = renderer.sharedMesh; _materials = renderer.sharedMaterials;
            _bones = renderer.bones; _bounds = renderer.localBounds;
        }

        public bool Validate(CharacterDefinition definition, out string error) => Resolve(definition, out _, out _, out error);

        private bool Resolve(CharacterDefinition definition, out Transform[] bones, out Material[] materials, out string error)
        {
            bones = _bones; materials = _materials; error = null;
            if (_renderer == null || _mesh == null) { error = "기본 캐릭터 모델이 없습니다."; return false; }
            if (definition == null) { error = "등록되지 않은 캐릭터입니다."; return false; }
            if (!definition.UseDefaultBody && definition.VisualPrefab != null)
            {
                var animator = definition.VisualPrefab.GetComponentInChildren<Animator>();
                var driver = _renderer.GetComponentInParent<Animator>(true);
                var body = definition.VisualPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman ||
                    driver == null || driver.avatar == null || !driver.avatar.isValid || !driver.avatar.isHuman || body == null || body.sharedMesh == null ||
                    body.sharedMaterials.Length != body.sharedMesh.subMeshCount || Array.Exists(body.sharedMaterials, m => m == null))
                { error = "캐릭터의 원본 Humanoid 모델을 확인해 주세요."; return false; }
                return true;
            }
            Mesh mesh = _mesh;
            if (!definition.UseDefaultBody)
            {
                var source = definition.Body;
                if (source == null || source.sharedMesh == null) { error = "캐릭터 모델이 아직 등록되지 않았습니다."; return false; }
                mesh = source.sharedMesh;
                var sourceBones = source.bones;
                var sourcePoses = mesh.bindposes;
                var targetPoses = _mesh.bindposes;
                if (sourceBones.Length == 0 || sourceBones.Length != sourcePoses.Length || _bones.Length != targetPoses.Length)
                { error = "모델의 스킨/바인드 포즈가 올바르지 않습니다."; return false; }
                var indices = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < _bones.Length; i++)
                {
                    if (_bones[i] == null || !indices.TryAdd(BoneName(_bones[i].name), i))
                    { error = "기본 모델에 중복되거나 누락된 뼈대가 있습니다."; return false; }
                }
                bones = new Transform[sourceBones.Length];
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    if (sourceBones[i] == null || !indices.TryGetValue(BoneName(sourceBones[i].name), out int target))
                    { error = "기존 뼈대에 없는 본: " + (sourceBones[i] != null ? sourceBones[i].name : "null"); return false; }
                    // A Humanoid flag or a matching name alone does not guarantee compatible skinning.
                    for (int element = 0; element < 16; element++)
                        if (!float.IsFinite(sourcePoses[i][element]) || Mathf.Abs(sourcePoses[i][element] - targetPoses[target][element]) > .002f)
                        { error = "기존 뼈대의 바인드 포즈에 맞춰 리깅이 필요합니다: " + sourceBones[i].name; return false; }
                    bones[i] = _bones[target];
                }
                materials = source.sharedMaterials;
            }
            if (definition.Materials != null && definition.Materials.Length > 0) materials = definition.Materials;
            if (materials == null || materials.Length != mesh.subMeshCount || Array.Exists(materials, m => m == null))
            { error = "서브메시마다 재질을 지정해 주세요."; return false; }
            return true;
        }

        public bool Apply(CharacterDefinition definition, out string error)
        {
            if (!Resolve(definition, out var bones, out var materials, out error)) return false;
            if (_definition == definition && _visual != null) return true;
            Dispose();
            _definition = definition;
            if (!definition.UseDefaultBody && definition.VisualPrefab != null)
            {
                var animator = _renderer.GetComponentInParent<Animator>(true);
                _visual = UnityEngine.Object.Instantiate(definition.VisualPrefab, animator.transform, false);
                _visual.name = "Character visual - " + definition.Id;
                foreach (var t in _visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _renderer.gameObject.layer;
                // Keep the original renderer as the visibility owner. Its mesh is never replaced by a different layout.
                _renderer.sharedMesh = null;
                _visual.AddComponent<CharacterPoseFollower>().Initialize(animator, _renderer, _mesh);
                return true;
            }
            _renderer.sharedMesh = null;
            _renderer.sharedMesh = definition.UseDefaultBody ? _mesh : definition.Body.sharedMesh;
            _renderer.bones = bones;
            _renderer.sharedMaterials = materials;
            _renderer.localBounds = definition.UseDefaultBody ? _bounds : definition.Body.localBounds;
            // Do not touch enabled/forceRenderingOff: death and stealth own those flags.
            return true;
        }
        public void Restore()
        {
            if (_renderer == null) return;
            Dispose(); _definition = null;
            _renderer.sharedMesh = null;
            _renderer.sharedMesh = _mesh; _renderer.bones = _bones;
            _renderer.sharedMaterials = _materials; _renderer.localBounds = _bounds;
        }
        public void Dispose()
        {
            if (_visual == null) return;
            _visual.GetComponent<CharacterPoseFollower>()?.Release();
            _visual.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_visual); else UnityEngine.Object.DestroyImmediate(_visual);
            _visual = null;
        }
        private static string BoneName(string value) => value.Substring(value.LastIndexOf(':') + 1);
    }
}
