using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>Local translucency and observer concealment without changing shared asset materials.</summary>
    public sealed class SkillStealthPresentation : IDisposable
    {
        private readonly Transform _root;
        private readonly List<Renderer> _renderers = new();
        private readonly List<Canvas> _canvases = new();
        private readonly Dictionary<Renderer, bool> _hidden = new();
        private readonly Dictionary<Canvas, bool> _hiddenCanvases = new();
        private readonly Dictionary<Renderer, Material[]> _original = new();
        private readonly Dictionary<Renderer, Material[]> _faded = new();
        private int _mode;
        public SkillStealthPresentation(Transform root) => _root = root;

        public void Apply(bool stealth, bool local, float alpha = .5f)
        {
            int mode = stealth ? (local ? 1 : 2) : 0;
            if (_mode != mode) { Restore(); _mode = mode; }
            if (mode == 0 || _root == null) return;
            _root.GetComponentsInChildren(true, _renderers);
            foreach (var renderer in _renderers)
            {
                if (mode == 2)
                {
                    if (!_hidden.ContainsKey(renderer)) _hidden.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
                else if ((renderer is SkinnedMeshRenderer || renderer is MeshRenderer) && !_original.ContainsKey(renderer))
                {
                    var original = renderer.sharedMaterials;
                    var faded = new Material[original.Length];
                    for (int i = 0; i < original.Length; i++)
                    {
                        if (original[i] == null) continue;
                        faded[i] = TransparentCopy(original[i], alpha);
                    }
                    _original.Add(renderer, original); _faded.Add(renderer, faded);
                    renderer.sharedMaterials = faded;
                }
            }
            if (mode != 2) return;
            _root.GetComponentsInChildren(true, _canvases);
            foreach (var canvas in _canvases)
            {
                if (!_hiddenCanvases.ContainsKey(canvas)) _hiddenCanvases.Add(canvas, canvas.enabled);
                canvas.enabled = false;
            }
        }

        internal static Material TransparentCopy(Material source, float alpha)
        {
            var mat = new Material(source) { name = source.name + " (local translucent)", renderQueue = (int)RenderQueue.Transparent };
            mat.SetOverrideTag("RenderType", "Transparent");
            Set(mat, "_Surface", 1); Set(mat, "_Mode", 2); Set(mat, "_Blend", 0);
            Set(mat, "_SrcBlend", (float)BlendMode.SrcAlpha); Set(mat, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Set(mat, "_SrcBlendAlpha", (float)BlendMode.One); Set(mat, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            Set(mat, "_ZWrite", 0); Set(mat, "_AlphaClip", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHATEST_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            foreach (string key in new[] { "_BaseColor", "_Color" })
                if (mat.HasProperty(key)) { var color = mat.GetColor(key); color.a = alpha; mat.SetColor(key, color); }
            return mat;
        }
        private static void Set(Material material, string property, float value)
        { if (material.HasProperty(property)) material.SetFloat(property, value); }
        private void Restore()
        {
            foreach (var pair in _hidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            foreach (var pair in _hiddenCanvases) if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in _original)
            {
                if (pair.Key == null) continue;
                var current = pair.Key.sharedMaterials; var faded = _faded[pair.Key];
                for (int i = 0; i < current.Length && i < faded.Length; i++)
                    if (current[i] == faded[i]) current[i] = pair.Value[i];
                pair.Key.sharedMaterials = current;
            }
            foreach (var materials in _faded.Values) foreach (var material in materials)
                if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); }
            _hidden.Clear(); _hiddenCanvases.Clear(); _original.Clear(); _faded.Clear();
        }
        public void Dispose() { Restore(); _mode = 0; }
    }
}
