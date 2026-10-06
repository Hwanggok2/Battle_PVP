using System.Reflection;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BattlePvp.EditorTests
{
    public sealed class WebRenderBudgetTests
    {
        [TestCase(0, 1280, 720)]
        [TestCase(1, 1280, 720)]
        [TestCase(2, 1280, 720)]
        public void FullscreenAndRetinaRenderingStayInsideQualityPixelBudget(int quality, int width, int height)
        {
            var budget = new WebFrameBudget();
            budget.Configure(quality, 60);
            foreach (var size in new[] { (960, 600), (1920, 1080), (3840, 2160), (3440, 1440), (1080, 2400) })
            {
                float scale = budget.RenderScale(size.Item1, size.Item2);
                Assert.That(scale, Is.InRange(.1f, 1f));
                Assert.That(size.Item1 * (double)size.Item2 * scale * scale, Is.LessThanOrEqualTo(width * (double)height + 1));
            }
        }

        [Test]
        public void SustainedSlowFramesReduceCostWithoutUserSettingsChanges()
        {
            var budget = new WebFrameBudget(); budget.Configure(1, 60);
            Assert.That(budget.Tier, Is.EqualTo(2));
            Feed(budget, 8, 1d / 30);
            Assert.That(budget.Tier, Is.EqualTo(1));
            Assert.That(budget.Quality, Is.Zero);
            Feed(budget, 8, 1d / 30);
            Assert.That(budget.Tier, Is.Zero);
            Assert.That(budget.PixelBudget, Is.EqualTo(960 * 540));
            Feed(budget, 20, 1d / 10);
            Assert.That(budget.Tier, Is.Zero, "Bound quality reduction even on unsupported hardware.");
        }

        [Test]
        public void BackgroundLoadingAndBriefHitchesDoNotLowerQuality()
        {
            var budget = new WebFrameBudget(); budget.Configure(1, 60);
            Feed(budget, 10, 1d / 60);
            budget.Observe(.3, true);
            Feed(budget, 5, 1d / 60);
            Feed(budget, 30, .25, false);
            budget.Observe(2, true);
            Feed(budget, 2, 1d / 15);
            Assert.That(budget.Tier, Is.EqualTo(2));
        }

        [Test]
        public void VerySlowActiveDevicesStillReduceQualityInsteadOfTreatingEveryFrameAsLoading()
        {
            var budget = new WebFrameBudget(); budget.Configure(1, 60);
            Feed(budget, 24, .75);
            Assert.That(budget.Tier, Is.Zero);
        }

        [Test]
        public void RecoveryWaitsForSustainedHeadroomAndRespectsSelectedCeiling()
        {
            var budget = new WebFrameBudget(); budget.Configure(1, 60);
            Feed(budget, 8, 1d / 30);
            Feed(budget, 15, 1d / 60);
            Assert.That(budget.Tier, Is.EqualTo(1), "Do not oscillate after a short recovery.");
            Feed(budget, 15, 1d / 60);
            Assert.That(budget.Tier, Is.EqualTo(2));
            Feed(budget, 60, 1d / 60);
            Assert.That(budget.Tier, Is.EqualTo(2));
            budget.Configure(2, 60);
            Feed(budget, 30, 1d / 60);
            Assert.That(budget.Tier, Is.EqualTo(3));
            Assert.That(budget.PixelBudget, Is.EqualTo(1920 * 1080));
        }

        [Test]
        public void ThirtyFpsPreferenceAndUnrelatedSettingsDoNotTriggerDegradationOrReset()
        {
            var budget = new WebFrameBudget(); budget.Configure(1, 30);
            Feed(budget, 60, 1d / 30);
            Assert.That(budget.Tier, Is.EqualTo(2));
            Feed(budget, 8, 1d / 15);
            Assert.That(budget.Tier, Is.EqualTo(1));
            budget.Configure(1, 30); budget.ResetObservation();
            Assert.That(budget.Tier, Is.EqualTo(1), "Volume changes and scene loads preserve the learned budget.");
        }

        [Test]
        public void WebPipelineReducesRenderPassCostWithoutMutatingTheSourceAsset()
        {
            var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            int originalMsaa = source.msaaSampleCount;
            var runtime = Object.Instantiate(source);
            var apply = typeof(LocalGameSettings).GetMethod("ApplyWebPipeline", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                apply.Invoke(null, new object[] { runtime, 1 });
                Assert.That(runtime.msaaSampleCount, Is.EqualTo(1));
                Assert.That(runtime.shadowDistance, Is.EqualTo(20));
                Assert.That(runtime.maxAdditionalLightsCount, Is.EqualTo(2));
                Assert.That(runtime.supportsCameraOpaqueTexture || runtime.supportsCameraDepthTexture, Is.False);
                Assert.That(runtime.supportsSoftShadows || runtime.supportsAdditionalLightShadows, Is.False);
                apply.Invoke(null, new object[] { runtime, 0 });
                Assert.That(runtime.supportsHDR, Is.False);
                Assert.That(runtime.shadowDistance, Is.Zero);
                Assert.That(runtime.maxAdditionalLightsCount, Is.Zero);
                Assert.That(source.msaaSampleCount, Is.EqualTo(originalMsaa));
            }
            finally { Object.DestroyImmediate(runtime); }
        }

        private static void Feed(WebFrameBudget budget, double seconds, double frame, bool focused = true)
        {
            for (int i = 0; i < System.Math.Ceiling(seconds / frame); i++) budget.Observe(frame, focused);
        }
    }
}
