using System.Reflection;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class TemporaryHudInitializationTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _owner;
        private Sprite _skillIcon;

        [SetUp]
        public void SetUp()
        {
            _owner = new GameObject("Temporary HUD initial state fixture");
            _owner.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_owner);
            if (_skillIcon != null) Object.DestroyImmediate(_skillIcon);
        }

        [Test]
        public void CharacterDetailsStartClosedAndLaterVisibilityIsNotReset()
        {
            var controller = _owner.AddComponent<CharacterInfoController>();
            GameObject panel = Child("Character details");
            Set(controller, "_infoPanel", panel);
            Assert.That(panel.activeSelf, Is.True, "Simulate an editor-opened panel.");

            InvokeAwake(controller);
            Assert.That(panel.activeSelf, Is.False);
            // Only the first initialization owns the default. Subsequent UI visibility is runtime state.
            panel.SetActive(true);
            InvokeAwake(controller);
            Assert.That(panel.activeSelf, Is.True);
        }

        [Test]
        public void HudClosesEditorOpenedDeathLoadingAndCountdownAndClearsSampleDamage()
        {
            PlayerHudView view = CreateView(out GameObject death, out GameObject loading,
                out TextMeshProUGUI countdown, out TextMeshProUGUI received, out _);
            countdown.text = "Editor countdown";
            received.text = "-999";
            received.color = Color.red;

            InvokeAwake(view);

            Assert.That(death.activeSelf, Is.False);
            Assert.That(loading.activeSelf, Is.False);
            Assert.That(countdown.gameObject.activeSelf, Is.False);
            Assert.That(countdown.text, Is.Empty);
            Assert.That(received.text, Is.Empty);
            Assert.That(received.color.a, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitHudStateSurvivesInitializationBeforeOrAfterItsFirstAwake(bool requestedBeforeAwake)
        {
            PlayerHudView view = CreateView(out GameObject death, out GameObject loading,
                out TextMeshProUGUI countdown, out TextMeshProUGUI received, out TextMeshProUGUI deathText);
            if (!requestedBeforeAwake) InvokeAwake(view);

            view.SetDeathOverlay(true, "Revive in 5", Color.cyan);
            view.SetLoadingOverlay(true);
            view.SetCountdown("Get Ready!", true);
            Assert.That(view.ShowReceivedDamage(12f, Color.yellow), Is.True);
            InvokeAwake(view);

            Assert.That(death.activeSelf, Is.True);
            Assert.That(loading.activeSelf, Is.True);
            Assert.That(countdown.gameObject.activeSelf, Is.True);
            Assert.That(countdown.text, Is.EqualTo("Get Ready!"));
            Assert.That(deathText.text, Does.Contain(BattleActionPrompt.Respawn).And.Contain("Revive in 5"));
            Assert.That(deathText.color, Is.EqualTo(Color.white));
            Assert.That(received.text, Is.EqualTo("-12"));
            Assert.That(received.color.a, Is.EqualTo(1f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SkillPreviewStartsHiddenButExplicitCooldownOrHiddenStateSurvivesAwake(bool requestedBeforeAwake)
        {
            GameObject root = Child("Skill UI");
            SkillUI skill = root.AddComponent<SkillUI>();
            GameObject icon = Child("Icon");
            icon.transform.SetParent(root.transform, false);
            Image baseImage = icon.AddComponent<Image>();
            GameObject overlay = Child("Overlay");
            overlay.transform.SetParent(root.transform, false);
            Image image = overlay.AddComponent<Image>();
            _skillIcon = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
            image.sprite = _skillIcon;
            baseImage.sprite = _skillIcon;
            TextMeshProUGUI timer = Child("Timer").AddComponent<TextMeshProUGUI>();
            timer.transform.SetParent(overlay.transform, false);
            Set(skill, "_root", root.GetComponent<RectTransform>());
            Set(skill, "_baseImage", baseImage);
            Set(skill, "_overlayImage", image);
            Set(skill, "_timerText", timer);
            if (!requestedBeforeAwake)
            {
                InvokeAwake(skill);
                Assert.That(root.activeSelf, Is.False, "An editor preview is hidden until gameplay supplies a state.");
            }

            skill.SetState(new SkillHudState(true, "Fixture skill", 0, 1,
                SkillHudPhase.Cooldown, 0.5f, 3f, _skillIcon));
            InvokeAwake(skill);
            Assert.That(root.activeSelf, Is.True);
            Assert.That(overlay.activeSelf, Is.True);
            Assert.That(timer.gameObject.activeSelf, Is.True);
            Assert.That(timer.text, Is.EqualTo("3"));
            Assert.That(image.fillAmount, Is.EqualTo(0.5f));
            Assert.That(image.fillClockwise, Is.False, "Awake must not reconfigure an accepted cooldown as a hidden preview.");

            skill.SetState(new SkillHudState(false, string.Empty, 0, 0, SkillHudPhase.Hidden, 0f, 0f));
            InvokeAwake(skill);
            Assert.That(root.activeSelf, Is.False, "An explicit hidden state is also a received runtime state.");
        }

        private PlayerHudView CreateView(out GameObject death, out GameObject loading,
            out TextMeshProUGUI countdown, out TextMeshProUGUI received, out TextMeshProUGUI deathText)
        {
            PlayerHudView view = _owner.AddComponent<PlayerHudView>();
            death = Child("DeathOverlay");
            loading = Child("LoadingOverlay");
            countdown = Child("Countdown").AddComponent<TextMeshProUGUI>();
            received = Child("ReceivedDamage").AddComponent<TextMeshProUGUI>();
            deathText = Child("DeathText").AddComponent<TextMeshProUGUI>();
            deathText.transform.SetParent(death.transform, false);
            deathText.color = Color.green;
            Set(view, "_deathDimObject", death);
            Set(view, "_loadingDimObject", loading);
            Set(view, "_countdownText", countdown);
            Set(view, "_receivedDamageText", received);
            Set(view, "_deathCountdownText", deathText);
            return view;
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(_owner.transform, false);
            return child;
        }

        private static void InvokeAwake(object target) => target.GetType().GetMethod("Awake", Private).Invoke(target, null);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);
    }
}
