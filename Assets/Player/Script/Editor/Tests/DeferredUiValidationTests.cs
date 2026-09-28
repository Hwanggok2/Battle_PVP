using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using BattlePvp.Audio;
using BattlePvp.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class DeferredUiValidationTests
    {
        private GameObject _root;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [UnityTest]
        public IEnumerator SkillValidationFromWorkerDefersSpriteAndHierarchyChanges()
        {
            _root = new GameObject("Deferred skill validation", typeof(RectTransform));
            _root.SetActive(false);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(_root.transform, false);
            var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(_root.transform, false);
            var view = _root.AddComponent<SkillUI>();
            var image = overlay.GetComponent<Image>();
            typeof(SkillUI).GetField("_overlayImage", Private).SetValue(view, image);
            var validation = typeof(SkillUI).GetMethod("OnValidate", Private);
            Task task = Task.Run(() => { for (int i = 0; i < 20; i++) validation.Invoke(view, null); });
            Assert.That(task.Wait(5000), Is.True);
            Assert.That(image.sprite, Is.Null, "OnValidate itself must not allocate Unity resources.");
            Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
            yield return null;
            Assert.That(image.sprite, Is.Not.Null);
            Assert.That(image.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(_root.activeSelf, Is.False, "Preview initialization must not open a hidden HUD.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BgmValidationFromWorkerDefersAudioSourceMutation()
        {
            _root = new GameObject("Deferred audio validation");
            _root.SetActive(false);
            var source = _root.AddComponent<AudioSource>();
            source.volume = 0.2f;
            var manager = _root.AddComponent<BgmManager>();
            typeof(BgmManager).GetField("_audioSource", Private).SetValue(manager, source);
            typeof(BgmManager).GetField("_volume", Private).SetValue(manager, 0.7f);
            var validation = typeof(BgmManager).GetMethod("OnValidate", Private);
            Task task = Task.Run(() => validation.Invoke(manager, null));
            Assert.That(task.Wait(5000), Is.True);
            Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.001f));
            yield return null;
            Assert.That(source.volume, Is.EqualTo(0.7f * BattlePvp.UI.LocalGameSettings.Current.music).Within(0.001f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DestroyBeforeEditorUpdateDiscardsPendingValidation()
        {
            _root = new GameObject("Destroyed before validation", typeof(RectTransform));
            _root.SetActive(false);
            var skill = _root.AddComponent<SkillUI>();
            var bgm = _root.AddComponent<BgmManager>();
            EditorTestLifecycle.Invoke(skill, "OnValidate");
            EditorTestLifecycle.Invoke(bgm, "OnValidate");
            Object.DestroyImmediate(_root);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
