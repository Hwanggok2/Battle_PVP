using System;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Logic;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class HudAndInputLifecycleTests
    {
        [Test]
        public void HudReenableRestoresExactlyOneSubscriptionTenTimes()
        {
            var root = new GameObject("HUD lifecycle test");
            root.SetActive(false);
            try
            {
                var display = new GameObject("View");
                display.transform.SetParent(root.transform);
                HudRecordingView view = display.AddComponent<HudRecordingView>();
                HudStatusStub status = root.AddComponent<HudStatusStub>();
                PlayerHUD hud = root.AddComponent<PlayerHUD>();
                SetField(hud, "_view", view);
                SetField(hud, "_healthSource", status);
                EditorTestLifecycle.Invoke(hud, "Awake");
                for (int i = 0; i < 10; i++)
                {
                    status.ReportHp(i % 2 == 0 ? 150f : 70f);
                    EditorTestLifecycle.SetActive(hud, true);
                    Assert.That(status.SubscriberCount, Is.EqualTo(1));
                    Assert.That(view.IsOverflow, Is.EqualTo(i % 2 == 0), "Reenable must restore overflow changes missed while disabled.");
                    int before = view.HpUpdates;
                    status.ReportHp(70f - i);
                    Assert.That(view.HpUpdates, Is.EqualTo(before + 1));
                    Assert.That(view.CurrentHp, Is.EqualTo(70f - i));
                    EditorTestLifecycle.SetActive(hud, false);
                    Assert.That(status.SubscriberCount, Is.Zero);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void EscapeAndSubmitAreConsumedOncePerFrame()
        {
            var input = new FrameInputGate();
            Assert.That(input.TryConsumeEscape(100), Is.True);
            Assert.That(input.TryConsumeEscape(100), Is.False);
            Assert.That(input.TryConsumeEscape(101), Is.True);
            input.ConsumeSubmit(101);
            Assert.That(input.IsSubmitConsumed(101), Is.True);
            Assert.That(input.IsSubmitConsumed(102), Is.False);
        }

        [Test]
        public void ChatCancellationDoesNotRemoveResultsOrDeathMode()
        {
            Assert.That(InputModeRules.Resolve(true, false, false, false, true), Is.EqualTo(GameInputMode.TextInput));
            Assert.That(InputModeRules.Resolve(false, false, false, false, true), Is.EqualTo(GameInputMode.Results));
            Assert.That(InputModeRules.Resolve(false, false, true, false, false), Is.EqualTo(GameInputMode.Spectating));
            Assert.That(InputModeRules.CanToggleMenu(GameInputMode.Spectating), Is.False);
            Assert.That(InputModeRules.CanTrackCamera(GameInputMode.Spectating, true, false), Is.True);
        }

        [Test]
        public void PopupCorrelationExpiresWithoutDroppingOtherVictims()
        {
            var cache = new PopupPredictionCache();
            Assert.That(cache.TryClaim(1, 2, 3, 0f), Is.True);
            Assert.That(cache.TryClaim(1, 2, 3, 1f), Is.False);
            Assert.That(cache.TryClaim(1, 4, 3, 20f), Is.True);
            Assert.That(cache.TryClaim(1, 2, 3, 30f), Is.True);
            Assert.That(cache.TryClaim(1, 4, 3, 30f), Is.False);
            Assert.That(cache.Count, Is.EqualTo(2));
            cache.Clear();
            Assert.That(cache.TryClaim(1, 4, 3, 30f), Is.True);
        }

        [Test]
        public void RepeatedSkillHudTicksDoNotAllocateKeyLabelsAndStillReflectRebinding()
        {
            var root = new GameObject("Skill allocation test", typeof(RectTransform));
            string previousKey = LocalGameSettings.Current.skill1;
            try
            {
                var label = new GameObject("Index", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                label.transform.SetParent(root.transform, false);
                var text = label.GetComponent<TMPro.TextMeshProUGUI>();
                var skill = root.AddComponent<SkillUI>();
                SetField(skill, "_indexText", text); SetField(skill, "_useDirectKeyLabel", true);
                var state = new SkillHudState(true, "Ready", 0, 1, SkillHudPhase.Ready, 0, 0);
                LocalGameSettings.Current.skill1 = "q";
                skill.SetState(state);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) skill.SetState(state);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.LessThan(4096), "Unchanged HUD ticks must reuse the uppercase key label.");
                LocalGameSettings.Current.skill1 = "r";
                skill.SetState(state);
                Assert.That(text.text, Is.EqualTo("R"));
            }
            finally { LocalGameSettings.Current.skill1 = previousKey; UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    public sealed class HudStatusStub : MonoBehaviour, IPlayerStatusSource, IDamageReceiver
    {
        public event Action<float, float> HpChanged;
        public event Action<bool, float> OverflowChanged { add { } remove { } }
        public int SubscriberCount => HpChanged?.GetInvocationList().Length ?? 0;
        public float CurrentHp { get; private set; } = 100f;
        public float MaxHp => 100f;
        public void ReportHp(float hp) { CurrentHp = hp; HpChanged?.Invoke(hp, MaxHp); }
        public void ApplyDamage(float amount, DamageSource source, Vector3 hitPosition) => ReportHp(CurrentHp - amount);
    }

    public sealed class HudRecordingView : MonoBehaviour, IPlayerHudView
    {
        public bool IsOverflow { get; private set; }
        public int HpUpdates { get; private set; }
        public float CurrentHp { get; private set; }
        public void SetHp(float current, float max) { CurrentHp = current; HpUpdates++; }
        public void SetShield(float shield) { }
        public void SetIdentity(Identity identity) { }
        public void SetSkill(SkillHudState state) { }
        public void SetOverflow(bool isOverflow, float overlapPercent) { IsOverflow = isOverflow; }
        public void SetMatchTimer(float seconds) { }
        public void SetCountdown(string text, bool active) { }
        public void SetScore(int points) { }
        public void SetDeathOverlay(bool active, string text = "", Color? textColor = null) { }
        public void SetLoadingOverlay(bool active) { }
    }
}
