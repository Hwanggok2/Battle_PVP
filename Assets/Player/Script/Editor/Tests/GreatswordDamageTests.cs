using System.Collections;
using System.Reflection;
using BattlePvp.Characters;
using BattlePvp.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorTests
{
    public sealed class GreatswordDamageTests
    {
        private GameObject _player, _dummy, _camera;
        private CharacterSkin _skin;
        private float _captureDelta;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private | BindingFlags.Public).Invoke(target, args);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);

        [UnityTest]
        public IEnumerator FullComboDamagesTheTargetOnEachBodyAtLowFrameRate()
        {
            yield return new EnterPlayMode();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Battle_waiting"));
            _captureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 10f;
            var origin = new Vector3(4000, 4000, 4000);
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), origin, Quaternion.identity);
            // Keep the real combat coroutine, events, aim, equipment and swept collision.
            // Disable session/input/movement setup so the fixture is independent of login.
            foreach (var behaviour in _player.GetComponents<MonoBehaviour>())
                behaviour.enabled = behaviour is PlayerCombat || behaviour is WeaponLoadout ||
                    behaviour is BlockAttackVfx || behaviour is MeleeAimDriver;
            Set(_player.GetComponent<HealthSystem>(), "_currentHp", 10000f);
            var combat = _player.GetComponent<PlayerCombat>();
            _camera = new GameObject("Crosshair camera");
            _camera.transform.position = origin + new Vector3(0, 1.7f, -.6f);
            var camera = _camera.AddComponent<BattlePvp.CameraLogic.FollowCamera>();
            camera.enabled = false;
            float pitch = Mathf.Atan2(.5f, 1.8f) * Mathf.Rad2Deg;
            Set(camera, "_pitch", pitch);
            Set(combat, "_networkLookPitch", pitch);
            Set(combat, "_followCamera", camera);
            var animator = _player.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = true;
            _dummy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remodel/Prefabs/training-dummy.prefab"), origin + Vector3.forward * 1.2f, Quaternion.identity);
            var health = _dummy.GetComponent<DummyHealth>();
            float initializedAt = Time.time + .3f;
            while (Time.time < initializedAt) yield return null;
            _skin = new CharacterSkin(_player.GetComponentInChildren<SkinnedMeshRenderer>());
            foreach (string character in new[] { "default", "brute", "security-officer", "casual-1", "megumi", "picochan" })
            // Reach still follows the physical blade and the avatar's arm length.
            // Brute also reproduces the original miss near the tip at 1.8 metres.
            foreach (float distance in new[] { 1.2f, character == "brute" ? 1.8f : 1.35f })
            {
                Assert.That(_skin.Apply(CharacterCatalog.Instance.Find(character), out var error), Is.True, error);
                yield return null;
                Set(health, "_currentHp", health.MaxHp);
                _dummy.transform.position = origin + Vector3.forward * distance;
                pitch = Mathf.Atan2(.5f, distance + .6f) * Mathf.Rad2Deg;
                Set(camera, "_pitch", pitch); Set(combat, "_networkLookPitch", pitch);
                Physics.SyncTransforms();
                Call(_player.GetComponent<WeaponLoadout>(), "Apply", MeleeWeaponKind.Greatsword);
                float before = health.CurrentHp;
                var damage = new float[3];
                int previous = 0;
                float thirdHitPhase = -1;
                Call(combat, "StartAttack", 0, false, Vector3.forward, false);
                Assert.That(combat.WeaponKind, Is.EqualTo(MeleeWeaponKind.Greatsword));
                float deadline = Time.time + 9;
                while (combat.IsAttackActive && Time.time < deadline)
                {
                    int index = (int)typeof(PlayerCombat).GetField("currentComboIndex", Private).GetValue(combat);
                    if (index != previous)
                    {
                        damage[previous] = before - health.CurrentHp;
                        before = health.CurrentHp;
                        previous = index;
                    }
                    Set(combat, "hasComboReserved", index < 2);
                    var state = (AnimatorStateInfo)typeof(PlayerCombat).GetProperty("AttackAnimationState", Private).GetValue(combat);
                    if (index == 2 && health.CurrentHp < before && thirdHitPhase < 0) thirdHitPhase = state.normalizedTime;
                    Assert.That(state.IsName("Weapon_Greatsword" + (index + 1)), Is.True, "Run the equipped greatsword clip.");
                    yield return null;
                }
                yield return null;
                damage[previous] = before - health.CurrentHp;
                Assert.That(previous, Is.EqualTo(2), "The real combo must reach its third strike.");
                for (int index = 0; index < 3; index++)
                    Assert.That(damage[index], Is.GreaterThan(0), character + " distance=" + distance + " strike " + (index + 1));
                Assert.That(thirdHitPhase, Is.InRange(0f,.6f), character + " third strike must damage during its forward sweep, not wait for the late pose");
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            _skin?.Dispose();
            if (_player != null) { _player.SetActive(false); Object.Destroy(_player); }
            if (_dummy != null) Object.Destroy(_dummy);
            if (_camera != null) Object.Destroy(_camera);
            Time.captureDeltaTime = _captureDelta;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
