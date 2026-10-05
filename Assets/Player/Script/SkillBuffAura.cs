using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>Reusable, animated body shells for temporary stat changes. No extra lights or colliders.</summary>
    public sealed class SkillBuffAura : IDisposable
    {
        private readonly List<(SkinnedMeshRenderer body, SkinnedMeshRenderer glow, bool wave)> _bodies = new();
        private Material _material;
        private PlayerCombat _combat;
        private HealthSystem _health;
        private Transform _hips, _head;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor"), PhaseId = Shader.PropertyToID("_Phase"), CenterId = Shader.PropertyToID("_Center"), WaveId = Shader.PropertyToID("_Moving");
        private readonly MaterialPropertyBlock _properties = new();
        private readonly SkillBuffSymbols _symbols = new();

        public void Tick(ExpandedSkillController owner, bool concealed)
        {
            if (_combat == null) _combat = owner.GetComponent<PlayerCombat>();
            if (_health == null) _health = owner.GetComponent<HealthSystem>();
            bool visible = !concealed && (_health == null || !_health.IsDead);
            _symbols.Tick(owner, _combat, visible);
            bool active = TryColor(owner, out Color color) && visible;
            if (active && _material == null) Create(owner.transform);
            if (_material == null) return;
            // The owner remains translucent during stealth; observers never see this aura.
            color.a = owner.IsStealthed ? .5f : 1f;
            _properties.SetColor(ColorId, color);
            _properties.SetFloat(PhaseId, (float)(owner.Now % 1.1) / 1.1f);
            _properties.SetVector(CenterId, _hips != null && _head != null ? (_hips.position + _head.position) * .5f : owner.transform.position + Vector3.up);
            foreach (var pair in _bodies)
            {
                if (pair.glow == null) continue;
                pair.glow.enabled = active && pair.body != null && pair.body.enabled && !pair.body.forceRenderingOff;
                if (pair.glow.enabled)
                { _properties.SetFloat(WaveId, pair.wave ? 1 : 0); pair.glow.SetPropertyBlock(_properties); }
            }
        }

        private bool TryColor(ExpandedSkillController owner, out Color color)
        {
            color = new Color(1.25f, .12f, 2.4f);
            if ((owner.Active(JobSkillKind.Dice) && ExpandedSkillController.Value(JobSkillKind.Dice, "Face" + owner.DiceFace) < 0) ||
                (!owner.Berserking && owner.RegenMultiplier < 1f)) return true;
            // Keep Fortify unmistakably blue even when several bonuses overlap.
            color = new Color(.08f, .5f, 2.5f);
            if (owner.Active(JobSkillKind.Fortify) || (_health != null && _health.HasDefensiveSkillBuff)) return true;
            color = new Color(2.2f, 1.8f, .06f);
            if (owner.Berserking) return true;
            color = new Color(2.4f, .15f, .08f);
            if (owner.HasAmbushBonus ||
                (owner.Active(JobSkillKind.Dice) && owner.DiceFace >= 4) ||
                (_combat != null && (_combat.HasAttackPowerSkillBonus || _combat.HasNextAttackSkillBonus))) return true;
            color = new Color(.12f, 1.8f, .65f);
            if (owner.Active(JobSkillKind.Recovery) ||
                (_combat != null && (_combat.HasAttackSpeedSkillBonus || _combat.HasMovementSkillBonus))) return true;
            return false;
            // Shields, invulnerability and charge do not imply a stat-enhancement aura.
        }

        private void Create(Transform owner)
        {
            _material = new Material(Resources.Load<Shader>("CombatVfx/BuffAura")) { name = "Skill body radiance" };
            var animator = owner.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            { _hips = animator.GetBoneTransform(HumanBodyBones.Hips); _head = animator.GetBoneTransform(HumanBodyBones.Head); }
            // Share the original skin and bones, so crouching, Fortify and attacks keep the exact silhouette.
            foreach (var body in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (body.sharedMesh == null || body.name == "Skill buff body glow" || body.name == "Skill buff radiance" || body.GetComponentInParent<Canvas>() != null) continue;
                for (int shell = 0; shell < 2; shell++)
                {
                    var go = new GameObject(shell == 0 ? "Skill buff body glow" : "Skill buff radiance") { layer = body.gameObject.layer };
                    go.transform.SetParent(body.transform, false);
                    var glow = go.AddComponent<SkinnedMeshRenderer>();
                    glow.sharedMesh = body.sharedMesh; glow.bones = body.bones; glow.rootBone = body.rootBone;
                    glow.quality = body.quality; glow.updateWhenOffscreen = body.updateWhenOffscreen;
                    var bounds = body.localBounds; bounds.Expand(.4f); glow.localBounds = bounds;
                    var materials = new Material[body.sharedMesh.subMeshCount];
                    for (int i = 0; i < materials.Length; i++) materials[i] = _material;
                    glow.sharedMaterials = materials;
                    glow.shadowCastingMode = ShadowCastingMode.Off; glow.receiveShadows = false;
                    glow.lightProbeUsage = LightProbeUsage.Off; glow.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    glow.enabled = false;
                    _bodies.Add((body, glow, shell == 1));
                }
            }
        }

        public void Dispose()
        {
            _symbols.Dispose();
            foreach (var pair in _bodies)
                if (pair.glow != null) { pair.glow.enabled = false; Destroy(pair.glow.gameObject); }
            _bodies.Clear(); Destroy(_material); _material = null;
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
