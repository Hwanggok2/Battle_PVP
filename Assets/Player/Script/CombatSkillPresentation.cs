using System;
using BattlePvp.UI;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Owns skill rendering and audio resources. It does not read combat or network state.</summary>
    public sealed class CombatSkillPresentation : IDisposable
    {
        private readonly GameObject _owner;
        private readonly Animator _animator;
        private readonly AudioSource _audioSource;
        private CombatHitFeedback _hitFeedback;
        private GameObject _sword;
        private Renderer[] _skillSwordRenderers;
        private Material[][] _skillSwordOriginalMaterials;
        private Material _activeSkillSwordMaterial;
        private bool _isSkillSwordVisualActive;

        public CombatSkillPresentation(GameObject owner, Animator animator)
        {
            _owner = owner;
            _animator = animator;
            _audioSource = owner.GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = owner.AddComponent<AudioSource>();
        }

        public void Cancel()
        {
            RestoreSwordMaterials();
        }

        public void Dispose()
        {
            Cancel();
            _skillSwordRenderers = null;
            _sword = null;
        }

        public void PlaySound(AudioClip clip, float volume)
        {
            if (clip != null && _audioSource != null) _audioSource.PlayOneShot(clip, volume);
        }

        public void PlayAnimation(string stateName, int layer, double visualStartedAt, double now)
        {
            if (_animator == null || _animator.layerCount <= 0 || string.IsNullOrWhiteSpace(stateName))
                return;

            int safeLayer = Mathf.Clamp(layer, 0, _animator.layerCount - 1);
            int stateHash = Animator.StringToHash(stateName);
            if (!_animator.HasState(safeLayer, stateHash))
            {
                Debug.LogWarning(
                    $"[PlayerCombat] Animator state '{stateName}' was not found on layer {safeLayer}. Check the skill SO animation name/layer.",
                    _animator);
                return;
            }

            _animator.speed = 1f;
            _animator.Play(stateName, safeLayer, 0f);
            _animator.Update(0f);

            if (double.IsNaN(visualStartedAt))
                return;

            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(safeLayer);
            if (!stateInfo.IsName(stateName) || stateInfo.length <= 0f)
                return;

            float normalizedTime = GetNormalizedAnimationTime(visualStartedAt, now, stateInfo.length);
            if (normalizedTime <= 0.001f)
                return;

            _animator.Play(stateName, safeLayer, normalizedTime);
            _animator.Update(0f);
        }

        public static float GetNormalizedAnimationTime(double startedAt, double now, float stateLength)
        {
            if (double.IsNaN(startedAt) || stateLength <= 0f) return 0f;
            double elapsed = Math.Max(0d, now - startedAt);
            return Mathf.Clamp((float)(elapsed / stateLength), 0f, 0.98f);
        }

        public bool IsAnimationFinished(string stateName, int animationLayer)
        {
            if (string.IsNullOrWhiteSpace(stateName))
                return true;

            if (_animator == null || _animator.layerCount <= 0)
                return true;

            int layer = Mathf.Clamp(animationLayer, 0, _animator.layerCount - 1);
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(layer);
            if (!stateInfo.IsName(stateName))
                return true;

            return !_animator.IsInTransition(layer) && stateInfo.normalizedTime >= 1f;
        }

        public void SetSwordMaterial(GameObject sword, Material swordMaterial)
        {
            if (_sword != sword)
            {
                RestoreSwordMaterials();
                _sword = sword;
                _skillSwordRenderers = null;
            }
            bool active = swordMaterial != null;

            if (!active && !_isSkillSwordVisualActive)
                return;

            if (active && _isSkillSwordVisualActive && _activeSkillSwordMaterial == swordMaterial)
                return;

            ResolveSwordRenderers();

            if (_skillSwordRenderers == null || _skillSwordRenderers.Length == 0)
                return;

            if (_isSkillSwordVisualActive)
                RestoreSwordMaterials();

            if (active)
            {
                _skillSwordOriginalMaterials = new Material[_skillSwordRenderers.Length][];
                for (int i = 0; i < _skillSwordRenderers.Length; i++)
                {
                    Renderer rendererComponent = _skillSwordRenderers[i];
                    if (rendererComponent == null)
                        continue;

                    Material[] originalMaterials = rendererComponent.sharedMaterials;
                    _skillSwordOriginalMaterials[i] = originalMaterials;

                    int count = Mathf.Max(1, originalMaterials != null ? originalMaterials.Length : 0);
                    Material[] skillMaterials = new Material[count];
                    for (int j = 0; j < count; j++)
                        skillMaterials[j] = swordMaterial;

                    rendererComponent.sharedMaterials = skillMaterials;
                }

                _isSkillSwordVisualActive = true;
                _activeSkillSwordMaterial = swordMaterial;
                return;
            }

            RestoreSwordMaterials();
        }

        private void RestoreSwordMaterials()
        {
            if (_skillSwordOriginalMaterials != null)
            {
                for (int i = 0; i < _skillSwordRenderers.Length; i++)
                {
                    Renderer rendererComponent = _skillSwordRenderers[i];
                    if (rendererComponent != null && i < _skillSwordOriginalMaterials.Length && _skillSwordOriginalMaterials[i] != null)
                        rendererComponent.sharedMaterials = _skillSwordOriginalMaterials[i];
                }
            }

            _skillSwordOriginalMaterials = null;
            _activeSkillSwordMaterial = null;
            _isSkillSwordVisualActive = false;
        }

        public void PlayConfirmedHit(bool isHeadshot)
        {
            if (_hitFeedback == null)
                _hitFeedback = _owner.GetComponent<CombatHitFeedback>();
            if (_hitFeedback == null)
                _hitFeedback = _owner.AddComponent<CombatHitFeedback>();

            _hitFeedback.Play(isHeadshot);
        }

        private void ResolveSwordRenderers()
        {
            if (_skillSwordRenderers != null && _skillSwordRenderers.Length > 0) return;
            _skillSwordRenderers = _sword != null
                ? _sword.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
        }
    }
}
