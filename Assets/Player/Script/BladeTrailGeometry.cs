using System.Collections.Generic;
using UnityEngine;

namespace BattlePvp.Combat
{
    /// <summary>Append-only world-space blade spans. Existing geometry only changes opacity.</summary>
    public sealed class BladeTrailGeometry
    {
        public const float Lifetime = .18f;
        private const int Capacity = 512;
        private readonly Span[] _spans = new Span[Capacity];
        private int _first, _count;
        private bool _hasSample;
        private Vector3 _base, _tip, _baseVelocity;
        private float _time, _distance;

        private struct Span
        {
            public Vector3 BaseA, TipA, BaseB, TipB;
            public float TimeA, TimeB;
            public bool Overlap;
        }

        // A new combo must not join to the previous one or erase its remaining afterimage.
        public void BeginStroke() { _hasSample = false; _baseVelocity = Vector3.zero; _distance = 0; }
        public void Clear() { _first = _count = 0; BeginStroke(); }

        public void Sample(Vector3 bladeBase, Vector3 bladeTip, float now)
        {
            float length = Vector3.Distance(bladeBase, bladeTip);
            if (!CombatValidation.IsFinite(bladeBase) || !CombatValidation.IsFinite(bladeTip) ||
                !float.IsFinite(now) || !float.IsFinite(length) || length < .001f) return;
            if (!_hasSample)
            {
                _base = bladeBase; _tip = bladeTip; _time = now; _hasSample = true;
                return;
            }
            float dt = now - _time;
            if (dt <= 0) return;
            Vector3 delta = bladeBase - _base;
            float travel = Mathf.Max(delta.magnitude, Vector3.Distance(_tip, bladeTip));
            // Teleports and a restarted animation must never draw a bridge across the scene.
            if (delta.magnitude > length * 3f || dt > Lifetime)
            {
                BeginStroke(); Sample(bladeBase, bladeTip, now); return;
            }
            if (travel > .001f)
            {
                Vector3 oldBlade = _tip - _base, newBlade = bladeTip - bladeBase;
                float angle = Vector3.Angle(oldBlade, newBlade);
                int divisions = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(travel / .035f, angle / 3f)), 1, 64);
                Vector3 tangent = Vector3.Dot(_baseVelocity, delta) > 0 ?
                    Vector3.ClampMagnitude(_baseVelocity * dt, delta.magnitude * 1.5f) : Vector3.zero;
                Vector3 previousBase = _base, previousTip = _tip;
                float previousTime = _time;
                for (int i = 1; i <= divisions; i++)
                {
                    float t = i / (float)divisions, t2 = t * t, t3 = t2 * t;
                    Vector3 nextBase = (2 * t3 - 3 * t2 + 1) * _base + (t3 - 2 * t2 + t) * tangent +
                        (-2 * t3 + 3 * t2) * bladeBase + (t3 - t2) * delta;
                    Vector3 nextTip = nextBase + Vector3.Slerp(oldBlade, newBlade, t);
                    float nextTime = Mathf.Lerp(_time, now, t);
                    float step = Vector3.Distance((previousBase + previousTip) * .5f, (nextBase + nextTip) * .5f);
                    float middle = _distance + step * .5f, spacing = length * .8f;
                    Add(new Span {
                        BaseA = previousBase, TipA = previousTip, BaseB = nextBase, TipB = nextTip,
                        TimeA = previousTime, TimeB = nextTime,
                        Overlap = middle >= spacing && middle % spacing < length * .2f
                    });
                    _distance += step;
                    previousBase = nextBase; previousTip = nextTip; previousTime = nextTime;
                }
                _baseVelocity = delta / dt;
            }
            else _baseVelocity = Vector3.zero;
            _base = bladeBase; _tip = bladeTip; _time = now;
        }

        private void Add(Span span)
        {
            if (_count == Capacity) { _first = (_first + 1) % Capacity; _count--; }
            _spans[(_first + _count++) % Capacity] = span;
        }

        public void Build(float now, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            vertices.Clear(); colors.Clear(); triangles.Clear();
            while (_count > 0 && now - _spans[_first].TimeB >= Lifetime)
            { _first = (_first + 1) % Capacity; _count--; }
            for (int i = 0; i < _count; i++)
            {
                Span span = _spans[(_first + i) % Capacity];
                Append(span, now, vertices, colors, triangles);
                if (span.Overlap) Append(span, now, vertices, colors, triangles);
            }
        }

        private static void Append(Span span, float now, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            int index = vertices.Count;
            vertices.Add(span.BaseA); vertices.Add(span.TipA); vertices.Add(span.TipB); vertices.Add(span.BaseB);
            var a = new Color(1, 1, 1, Mathf.Clamp01((Lifetime - now + span.TimeA) / .08f));
            var b = new Color(1, 1, 1, Mathf.Clamp01((Lifetime - now + span.TimeB) / .08f));
            colors.Add(a); colors.Add(a); colors.Add(b); colors.Add(b);
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
        }
    }
}
