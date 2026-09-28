using System.Collections.Generic;
using System.Linq;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class BladeSweepGeometryTests
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        [TestCase(-185f)] [TestCase(245f)] [TestCase(-310f)]
        public void SmoothPlanarMotionRetainsFullBladeWidthAndSmallAngularSteps(float sweep)
        {
            var trail = new BladeTrailGeometry();
            for (int i = 0; i <= 32; i++) SampleCircle(trail, sweep * i / 32, i * .004f);
            trail.Build(.128f, _vertices, _colors, _triangles);
            Assert.That(_vertices.Count, Is.GreaterThan(100));
            foreach (var point in _vertices) Assert.That(Mathf.Abs(point.y), Is.LessThan(.0001f));
            for (int i = 0; i < _vertices.Count; i += 4)
            {
                Assert.That(Vector3.Distance(_vertices[i], _vertices[i + 1]), Is.EqualTo(1).Within(.0001f));
                Assert.That(Vector3.Distance(_vertices[i + 2], _vertices[i + 3]), Is.EqualTo(1).Within(.0001f));
                Assert.That(Vector3.Angle(_vertices[i + 1] - _vertices[i], _vertices[i + 2] - _vertices[i + 3]), Is.LessThan(3.1f));
            }
        }

        [Test] public void ExistingGeometryDoesNotMoveWhenTheNextBladePoseRotates()
        {
            var trail = new BladeTrailGeometry();
            SampleCircle(trail, -40, 0); SampleCircle(trail, 0, .03f);
            trail.Build(.03f, _vertices, _colors, _triangles); var existing = _vertices.ToArray();
            trail.Sample(new Vector3(.3f, .2f, .5f), new Vector3(.7f, 1f, 1f), .06f);
            trail.Build(.06f, _vertices, _colors, _triangles);
            Assert.That(_vertices.Take(existing.Length), Is.EqualTo(existing));
            Assert.That(_vertices.Count, Is.GreaterThan(existing.Length));
        }

        [Test] public void FadeChangesOnlyOpacityThenRemovesExpiredSpans()
        {
            var trail = new BladeTrailGeometry();
            SampleCircle(trail, -30, 0); SampleCircle(trail, 30, .03f);
            trail.Build(.04f, _vertices, _colors, _triangles);
            var positions = _vertices.ToArray(); var alpha = _colors.Select(c => c.a).ToArray();
            trail.Build(.14f, _vertices, _colors, _triangles);
            Assert.That(_vertices, Is.EqualTo(positions));
            for (int i = 0; i < alpha.Length; i++) Assert.That(_colors[i].a, Is.LessThanOrEqualTo(alpha[i]));
            Assert.That(_colors.Any(c => c.a < .9f), Is.True);
            trail.Build(.22f, _vertices, _colors, _triangles);
            Assert.That(_vertices, Is.Empty);
        }

        [Test] public void ANewComboPreservesAfterimagesWithoutJoiningTheTwoStrokes()
        {
            var trail = new BladeTrailGeometry();
            SampleCircle(trail, 0, 0); SampleCircle(trail, 30, .02f);
            trail.Build(.02f, _vertices, _colors, _triangles); var old = _vertices.ToArray();
            trail.BeginStroke();
            trail.Sample(Vector3.right * 20, Vector3.right * 20 + Vector3.forward, .04f);
            trail.Sample(Vector3.right * 21, Vector3.right * 21 + Vector3.forward, .06f);
            trail.Build(.06f, _vertices, _colors, _triangles);
            Assert.That(_vertices.Take(old.Length), Is.EqualTo(old));
            for (int i = 0; i < _vertices.Count; i += 4)
                Assert.That(_vertices.Skip(i).Take(4).All(p => p.x < 2) || _vertices.Skip(i).Take(4).All(p => p.x >= 20), Is.True);
        }

        [Test] public void LeadingEdgeReachesTheNewBladeAndClearRemovesAllHistory()
        {
            var trail = new BladeTrailGeometry();
            var b = new Vector3(.3f, .6f, 1); var t = b + new Vector3(.5f, .4f, .8f).normalized;
            trail.Sample(Vector3.zero, Vector3.forward, 0); trail.Sample(b, t, .03f);
            trail.Build(.03f, _vertices, _colors, _triangles);
            Assert.That(Vector3.Distance(_vertices[_vertices.Count - 1], b), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(_vertices[_vertices.Count - 2], t), Is.LessThan(.0001f));
            trail.Clear(); trail.Build(.04f, _vertices, _colors, _triangles);
            Assert.That(_vertices, Is.Empty);
        }

        [Test] public void StationaryBladeDoesNotKeepRefreshingAnAfterimage()
        {
            var trail = new BladeTrailGeometry();
            SampleCircle(trail, 0, 0); SampleCircle(trail, 30, .02f);
            for (int i = 1; i <= 20; i++) SampleCircle(trail, 30, .02f + i * .02f);
            trail.Build(.42f, _vertices, _colors, _triangles);
            Assert.That(_vertices, Is.Empty);
        }

        [Test] public void TeleportDoesNotDrawALongConnectingFace()
        {
            var trail = new BladeTrailGeometry();
            SampleCircle(trail, 0, 0); SampleCircle(trail, 20, .02f);
            trail.Build(.02f, _vertices, _colors, _triangles); var old = _vertices.ToArray();
            trail.Sample(Vector3.right * 100, Vector3.right * 100 + Vector3.forward, .03f);
            trail.Build(.03f, _vertices, _colors, _triangles);
            Assert.That(_vertices, Is.EqualTo(old));
        }

        [Test] public void InvalidAndRepeatedTimestampsCannotPoisonTheMesh()
        {
            var trail = new BladeTrailGeometry();
            SampleCircle(trail, 0, 0);
            trail.Sample(Vector3.one * float.NaN, Vector3.forward, .01f);
            trail.Sample(Vector3.zero, Vector3.up, float.PositiveInfinity);
            SampleCircle(trail, 50, 0);
            trail.Build(.01f, _vertices, _colors, _triangles);
            Assert.That(_vertices, Is.Empty);
            SampleCircle(trail, 30, .02f); trail.Build(.02f, _vertices, _colors, _triangles);
            Assert.That(_vertices.All(CombatValidation.IsFinite), Is.True);
        }

        [Test] public void WorldSpaceHistoryRemainsBoundedUnderRapidMotion()
        {
            var trail = new BladeTrailGeometry();
            for (int i = 0; i < 1000; i++) SampleCircle(trail, i * 80, i * .0001f);
            trail.Build(.1f, _vertices, _colors, _triangles);
            Assert.That(_vertices.Count, Is.InRange(4, 512 * 8));
        }

        private static void SampleCircle(BladeTrailGeometry trail, float angle, float time)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            trail.Sample(direction * .5f, direction * 1.5f, time);
        }
    }
}
