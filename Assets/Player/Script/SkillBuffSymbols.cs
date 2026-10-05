using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>Six rising or falling holograms, batched into one mesh and draw per status group.</summary>
    public sealed class SkillBuffSymbols : IDisposable
    {
        private readonly GameObject[] _groups = new GameObject[4];
        private readonly Mesh[] _meshes = new Mesh[4];
        private readonly MeshRenderer[] _renderers = new MeshRenderer[4];
        private readonly MaterialPropertyBlock _properties = new();
        private Material _material;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor"), PhaseId = Shader.PropertyToID("_Phase"), OffsetId = Shader.PropertyToID("_Offset"), DirectionId = Shader.PropertyToID("_Direction");

        public void Tick(ExpandedSkillController owner, PlayerCombat combat, bool visible)
        {
            UpdateGroup(owner, 0, visible && combat != null && combat.IsMonostatStrLifestealActive, new Color(2.2f, .18f, .08f));
            UpdateGroup(owner, 1, visible && owner.Active(JobSkillKind.WarCry), new Color(.08f, 1.35f, 2f));
            UpdateGroup(owner, 2, visible && owner.Active(JobSkillKind.Recovery), new Color(.12f, 1.8f, .35f));
        }

        private void UpdateGroup(ExpandedSkillController owner, int kind, bool active, Color color)
        {
            UpdateGroup(owner.transform, owner.Now, kind, active, color, owner.IsStealthed ? .5f : 1f);
        }

        public void TickDebuff(Transform owner, double now, bool active, float alpha)
        {
            UpdateGroup(owner, now, 3, active, new Color(1.25f, .12f, 2.4f), alpha);
        }

        private void UpdateGroup(Transform owner, double now, int kind, bool active, Color color, float alpha)
        {
            if (active && _groups[kind] == null) Create(owner, kind);
            var group = _groups[kind];
            if (group == null) return;
            group.SetActive(active);
            if (!active) return;
            color.a = alpha;
            _properties.SetColor(ColorId, color);
            // Match the dice's 0.7 cycles/second and staggered upward movement.
            _properties.SetFloat(PhaseId, (float)((now * .7) % 1));
            _properties.SetFloat(OffsetId, kind * .38f);
            _properties.SetFloat(DirectionId, kind == 3 ? -1 : 1);
            _renderers[kind].SetPropertyBlock(_properties);
        }

        private void Create(Transform owner, int kind)
        {
            if (_material == null) _material = new Material(Resources.Load<Shader>("CombatVfx/BuffSymbols")) { name = "Rising buff holograms" };
            var go = new GameObject(kind == 0 ? "STR buff symbols" : kind == 1 ? "WarCry buff symbols" : kind == 2 ? "Recovery buff symbols" : "Debuff downward arrows");
            go.transform.SetParent(owner, false); go.layer = owner.gameObject.layer;
            var mesh = BuildMesh(kind);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _groups[kind] = go; _meshes[kind] = mesh; _renderers[kind] = renderer;
        }

        private static Mesh BuildMesh(int kind)
        {
            var vertices = new List<Vector3>(); var colors = new List<Color>();
            var seeds = new List<Vector2>(); var triangles = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                Vector2 seed = new Vector2(i / 6f, i * .17f);
                void Stroke(Vector2 offset, float scale, bool closed, params Vector2[] points)
                {
                    // A broad faint border and crisp core give each line a neon edge without a texture.
                    for (int layer = 0; layer < 2; layer++)
                    for (int p = 0; p < (closed ? points.Length : points.Length - 1); p++)
                    {
                        Vector2 a = offset + points[p] * scale, b = offset + points[(p + 1) % points.Length] * scale;
                        Vector2 d = (b - a).normalized;
                        Vector2 n = new Vector2(-d.y, d.x) * scale * (layer == 0 ? .065f : .025f);
                        int index = vertices.Count;
                        vertices.Add(a - n); vertices.Add(a + n); vertices.Add(b + n); vertices.Add(b - n);
                        for (int v = 0; v < 4; v++) { seeds.Add(seed); colors.Add(new Color(1, 1, 1, layer == 0 ? .14f : .9f)); }
                        triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
                        triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
                    }
                }
                Vector2 icon = new Vector2(-.075f, 0);
                if (kind == 0)
                {
                    // Clenched knuckles, folded thumb, palm and wrist; a raised fist silhouette.
                    Stroke(icon, .3f, true, new(-.34f,-.30f),new(-.42f,-.06f),new(-.43f,.25f),new(-.35f,.32f),new(-.27f,.28f),new(-.25f,.40f),new(-.15f,.43f),new(-.08f,.37f),new(-.05f,.47f),new(.05f,.47f),new(.12f,.38f),new(.20f,.40f),new(.28f,.33f),new(.28f,.05f),new(.39f,.04f),new(.41f,-.05f),new(.31f,-.19f),new(.18f,-.28f),new(.18f,-.43f),new(-.25f,-.43f),new(-.25f,-.29f));
                    Stroke(icon, .3f, false, new(-.27f,.27f),new(-.27f,.06f),new(-.18f,.01f),new(.25f,.04f));
                    Stroke(icon, .3f, false, new(-.08f,.37f),new(-.08f,.07f));
                    Stroke(icon, .3f, false, new(.12f,.37f),new(.12f,.08f));
                    Stroke(icon, .3f, false, new(.28f,.02f),new(.06f,-.03f),new(.02f,-.12f),new(.13f,-.19f),new(.27f,-.18f));
                    Stroke(icon, .3f, false, new(-.24f,-.29f),new(.17f,-.29f));
                }
                else if (kind == 1)
                {
                    // Sneaker side profile: ankle, laces, toe and a separate sole.
                    Stroke(icon, .32f, true, new(-.48f,-.22f),new(-.47f,.24f),new(-.21f,.24f),new(-.13f,.10f),new(.09f,.03f),new(.35f,-.08f),new(.49f,-.16f),new(.49f,-.28f),new(-.43f,-.28f));
                    Stroke(icon, .32f, false, new(-.46f,-.17f),new(.04f,-.17f),new(.17f,-.20f),new(.47f,-.20f));
                    Stroke(icon, .32f, false, new(-.46f,.08f),new(-.3f,.02f),new(-.15f,.10f));
                    for (int lace = 0; lace < 3; lace++)
                        Stroke(icon, .32f, false, new(-.08f+lace*.12f,.08f-lace*.05f),new(-.15f+lace*.12f,-.04f-lace*.045f));
                }
                Vector2 arrow = kind >= 2 ? Vector2.zero : new Vector2(.17f, .025f);
                Stroke(arrow, .3f, false, new(0,-.38f),new(0,.4f));
                Stroke(arrow, .3f, false, new(-.22f,.13f),new(0,.4f),new(.22f,.13f));
            }
            var result = new Mesh { name = kind == 0 ? "Fists and upward arrows" : kind == 1 ? "Shoes and upward arrows" : kind == 2 ? "Recovery upward arrows" : "Debuff arrows (shader inverted)" };
            result.SetVertices(vertices); result.SetColors(colors); result.SetUVs(0, seeds); result.SetTriangles(triangles, 0);
            // The shader moves the six glyphs around a body-sized volume.
            result.bounds = new Bounds(Vector3.up, new Vector3(2.4f, 3f, 2.4f));
            return result;
        }

        public void Dispose()
        {
            for (int i = 0; i < _groups.Length; i++)
            {
                if (_groups[i] != null) _groups[i].SetActive(false);
                Destroy(_groups[i]); Destroy(_meshes[i]); _groups[i] = null; _meshes[i] = null; _renderers[i] = null;
            }
            Destroy(_material); _material = null;
        }
        private static void Destroy(UnityEngine.Object value)
        { if (value == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
