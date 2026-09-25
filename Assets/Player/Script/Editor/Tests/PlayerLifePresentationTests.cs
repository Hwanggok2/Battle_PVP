using BattlePvp.CameraLogic;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class PlayerLifePresentationTests
    {
        [Test]
        public void RespawnCountdownKeepsOriginalDeadlineAcrossPresentationPauses()
        {
            var countdown = new PlayerRespawnCountdown(10d);
            Assert.That(countdown.SecondsRemaining(10d), Is.EqualTo(5));
            Assert.That(countdown.SecondsRemaining(11.01d), Is.EqualTo(4));
            Assert.That(countdown.IsReady(14.999d), Is.False);
            // Reusing the value after a hidden UI interval must not start another five seconds.
            Assert.That(countdown.IsReady(15d), Is.True);
            Assert.That(countdown.SecondsRemaining(100d), Is.Zero);
            Assert.That(countdown.ReadyAt, Is.EqualTo(15d));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidClockNeverOpensRespawnInput(double now)
        {
            Assert.That(new PlayerRespawnCountdown(10d).IsReady(now), Is.False);
            Assert.That(new PlayerRespawnCountdown(now).IsReady(100d), Is.False);
            Assert.That(default(PlayerRespawnCountdown).IsReady(100d), Is.False);
        }

        [Test]
        public void ModelRestoresMixedEnabledStatesAcrossTenDeathsWithoutChangingMovementController()
        {
            var player = new GameObject("Life visibility test");
            try
            {
                CharacterController controller = player.AddComponent<CharacterController>();
                var visible = new GameObject("Visible model");
                visible.transform.SetParent(player.transform);
                MeshRenderer visibleRenderer = visible.AddComponent<MeshRenderer>();
                BoxCollider visibleCollider = visible.AddComponent<BoxCollider>();
                var hidden = new GameObject("Disabled model");
                hidden.transform.SetParent(player.transform);
                MeshRenderer hiddenRenderer = hidden.AddComponent<MeshRenderer>();
                BoxCollider hiddenCollider = hidden.AddComponent<BoxCollider>();
                hiddenRenderer.enabled = hiddenCollider.enabled = false;
                var visibility = new PlayerModelVisibility(player.transform);

                for (int life = 0; life < 10; life++)
                {
                    controller.enabled = life % 2 == 0;
                    bool expectedController = controller.enabled;
                    visibility.Hide();
                    visibility.Hide();
                    Assert.That(visibleRenderer.enabled || visibleCollider.enabled, Is.False);
                    Assert.That(controller.enabled, Is.EqualTo(expectedController));
                    visibility.Restore();
                    visibility.Restore();
                    Assert.That(visibleRenderer.enabled && visibleCollider.enabled, Is.True);
                    Assert.That(hiddenRenderer.enabled || hiddenCollider.enabled, Is.False);
                    Assert.That(controller.enabled, Is.EqualTo(expectedController));
                }
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void DestroyedHiddenPartsDoNotPreventRemainingPartsFromRestoring()
        {
            var player = new GameObject("Destroyed model part test");
            try
            {
                MeshRenderer remaining = player.AddComponent<MeshRenderer>();
                var removed = new GameObject("Removed part");
                removed.transform.SetParent(player.transform);
                removed.AddComponent<MeshRenderer>();
                var visibility = new PlayerModelVisibility(player.transform);
                visibility.Hide();
                Object.DestroyImmediate(removed);
                Assert.DoesNotThrow(visibility.Restore);
                Assert.That(remaining.enabled, Is.True);
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void SuspendingAndResumingAnElapsedDeathRestoresTheModelThenHidesItAgain()
        {
            var player = new GameObject("Suspended death test");
            try
            {
                MeshRenderer renderer = player.AddComponent<MeshRenderer>();
                BoxCollider collider = player.AddComponent<BoxCollider>();
                PlayerLifePresentation presentation = player.AddComponent<PlayerLifePresentation>();
                presentation.Initialize(player.transform, null, "Respawn", Color.red);
                var elapsed = new PlayerRespawnCountdown(Time.timeAsDouble - 6d);
                for (int life = 0; life < 10; life++)
                {
                    presentation.BeginLocalDeath(elapsed);
                    Assert.That(renderer.enabled || collider.enabled, Is.False);
                    presentation.enabled = false;
                    Assert.That(renderer.enabled && collider.enabled, Is.True);
                    presentation.enabled = true;
                    presentation.BeginLocalDeath(elapsed);
                    Assert.That(renderer.enabled || collider.enabled, Is.False);
                    presentation.ShowLocalRevived();
                    Assert.That(renderer.enabled && collider.enabled, Is.True);
                }
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void MatchEndAndReviveChooseCameraTargetsWithoutChangingPlayerPosition()
        {
            var player = new GameObject("Life presentation test");
            var winner = new GameObject("Winner");
            var cameraObject = new GameObject("Life camera");
            try
            {
                player.transform.position = new Vector3(3f, 2f, 1f);
                FollowCamera camera = cameraObject.AddComponent<FollowCamera>();
                PlayerLifePresentation presentation = player.AddComponent<PlayerLifePresentation>();
                presentation.Initialize(player.transform, null, "Respawn", Color.red);
                presentation.AttachCamera(camera);
                camera.SetTarget(winner.transform);
                presentation.PlayDeathAnimation();
                presentation.PlayReviveAnimation();
                Assert.That(camera.Target, Is.EqualTo(winner.transform), "Observer animation events do not own the local camera.");
                for (int transition = 0; transition < 10; transition++)
                {
                    presentation.ShowMatchEnd(winner.transform, false);
                    Assert.That(camera.Target, Is.EqualTo(winner.transform));
                    Assert.That(camera.IsLocked, Is.True);
                    presentation.Suspend();
                    presentation.ShowLocalRevived();
                    Assert.That(camera.Target, Is.EqualTo(player.transform));
                    Assert.That(camera.IsLocked, Is.False);
                    presentation.ShowMatchEnd(winner.transform, true);
                    Assert.That(camera.Target, Is.EqualTo(player.transform));
                    Assert.That(camera.IsLocked, Is.False);
                    Assert.That(player.transform.position, Is.EqualTo(new Vector3(3f, 2f, 1f)));
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(winner);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DepartedWinnerFallsBackToLocalCameraWithoutUnlockingSpectator()
        {
            var player = new GameObject("Spectator fallback test");
            var winner = new GameObject("Departing winner");
            var cameraObject = new GameObject("Spectator camera");
            try
            {
                player.transform.position = new Vector3(3f, 2f, 1f);
                FollowCamera camera = cameraObject.AddComponent<FollowCamera>();
                PlayerLifePresentation presentation = player.AddComponent<PlayerLifePresentation>();
                presentation.Initialize(player.transform, null, "Respawn", Color.red);
                presentation.AttachCamera(camera);
                presentation.ShowMatchEnd(winner.transform, false);
                presentation.RefreshSpectateTarget();
                Assert.That(camera.Target, Is.EqualTo(winner.transform), "A live winner remains the target.");

                Object.DestroyImmediate(winner);
                Assert.That(camera.Target == null, Is.True);
                presentation.RefreshSpectateTarget();
                presentation.RefreshSpectateTarget();
                Assert.That(camera.Target, Is.EqualTo(player.transform));
                Assert.That(camera.IsLocked, Is.True, "Losing the winner's object must not grant winner input.");
                Assert.That(player.transform.position, Is.EqualTo(new Vector3(3f, 2f, 1f)));
            }
            finally
            {
                Object.DestroyImmediate(player);
                if (winner != null) Object.DestroyImmediate(winner);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SuspendedSpectatorWaitsForExplicitRestoreAndReviveClearsFallback(bool disableComponent)
        {
            var player = new GameObject("Suspended spectator test");
            var winner = new GameObject("Departing winner");
            var cameraObject = new GameObject("Suspended spectator camera");
            try
            {
                FollowCamera camera = cameraObject.AddComponent<FollowCamera>();
                PlayerLifePresentation presentation = player.AddComponent<PlayerLifePresentation>();
                presentation.Initialize(player.transform, null, "Respawn", Color.red);
                presentation.AttachCamera(camera);
                Transform originalTarget = winner.transform;
                presentation.ShowMatchEnd(originalTarget, false);
                if (disableComponent) presentation.enabled = false;
                else presentation.Suspend();
                Object.DestroyImmediate(winner);

                presentation.RefreshSpectateTarget();
                Assert.That(camera.Target == null, Is.True, "Suspension releases camera maintenance.");
                if (disableComponent) presentation.enabled = true;
                presentation.RefreshSpectateTarget();
                Assert.That(camera.Target == null, Is.True, "Enabling alone must not restore stale presentation state.");

                // PlayerManager explicitly restores its match state after reactivation.
                presentation.ShowMatchEnd(originalTarget, false);
                Assert.That(camera.Target, Is.EqualTo(player.transform));
                Assert.That(camera.IsLocked, Is.True);
                presentation.ShowLocalRevived();
                Assert.That(camera.IsLocked, Is.False);
                camera.SetTarget(null);
                presentation.RefreshSpectateTarget();
                Assert.That(camera.Target == null, Is.True, "Revive ends spectator fallback ownership.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                if (winner != null) Object.DestroyImmediate(winner);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
