using System.Reflection;
using BattlePvp.UI;
using NUnit.Framework;

namespace BattlePvp.EditorTests
{
    public sealed class WebRenderBudgetTests
    {
        [TestCase(0, 1280, 720)]
        [TestCase(1, 1600, 900)]
        [TestCase(2, 1920, 1080)]
        public void FullscreenAndRetinaRenderingStayInsideQualityPixelBudget(int quality, int width, int height)
        {
            var scaleMethod = typeof(LocalGameSettings).GetMethod("WebRenderScale", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var size in new[] { (960, 600), (1920, 1080), (3840, 2160), (3440, 1440), (1080, 2400) })
            {
                float scale = (float)scaleMethod.Invoke(null, new object[] { quality, size.Item1, size.Item2 });
                Assert.That(scale, Is.InRange(.1f, 1f));
                Assert.That(size.Item1 * (double)size.Item2 * scale * scale, Is.LessThanOrEqualTo(width * (double)height + 1));
            }
        }
    }
}
