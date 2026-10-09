using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
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
    public sealed class WeaponRuntimePresentationTests
    {
        private GameObject _player, _opponent, _recorder;
        private CharacterSkin _skin;
        private float _captureDelta;
        public static bool CaptureFrames
        {
            get => SessionState.GetBool("BattlePvp.WeaponTests.CaptureFrames",false);
            set => SessionState.SetBool("BattlePvp.WeaponTests.CaptureFrames",value);
        }
        public static string Outcome;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Call(object target,string method,params object[] args) =>
            target.GetType().GetMethod(method,Private|BindingFlags.Public).Invoke(target,args);
        private static void Set(object target,string field,object value) => target.GetType().GetField(field,Private).SetValue(target,value);

        [UnityTest]
        public IEnumerator NativeFinishersKeepTheirLiveTrailsOnTheBladeAtNormalAndLowFrameRates()
        {
            yield return new EnterPlayMode();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Lobby"));
            _captureDelta=Time.captureDeltaTime;
            _player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            _player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var behaviour in _player.GetComponents<MonoBehaviour>())
                behaviour.enabled=behaviour is WeaponLoadout || behaviour is BlockAttackVfx;
            Set(_player.GetComponent<HealthSystem>(),"_currentHp",100f);
            yield return null;
            _skin=new CharacterSkin(_player.GetComponentInChildren<SkinnedMeshRenderer>());
            _player.AddComponent<WeaponAimPlaybackDriver>();
            var probe=_player.AddComponent<FinisherTrailProbe>();
            WeaponPlaybackRecorder recorder=null;
            if(CaptureFrames) { _recorder=new GameObject("Native finisher capture"); recorder=_recorder.AddComponent<WeaponPlaybackRecorder>(); }
            var animator=_player.GetComponent<Animator>(); animator.fireEvents=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var combat=_player.GetComponent<PlayerCombat>();
            foreach(string character in new[]{"megumi","security-officer","picochan"}) foreach(int fps in new[]{60,15})
            {
                Time.captureDeltaTime=1f/fps;
                Call(combat,"CancelCurrentAttack"); Call(combat,"RestoreMeleeAimPose");
                Assert.That(_skin.Apply(CharacterCatalog.Instance.Find(character),out var error),Is.True,error);
                Call(_player.GetComponent<WeaponLoadout>(),"Apply",MeleeWeaponKind.Greatsword);
                animator.Play("Weapon_GreatswordReady",1,0); animator.Update(0);
                yield return null;
                float pitch=fps==60 ? -35 : 35;
                var look=Quaternion.AngleAxis(pitch,Vector3.right)*Vector3.forward;
                Set(combat,"_lookPitch",pitch); Set(combat,"_lookPoseWeight",1f);
                Call(combat,"SetMeleeAim",look*1.4f);
                Set(combat,"_currentAttackSpeed",.8f); Set(combat,"currentComboIndex",2); Set(combat,"isAttacking",true);
                probe.Frames=0; probe.MaxError=0; probe.Checking=true;
                recorder?.Begin(character+"-finisher-"+fps);
                Call(combat,"PlayAttackAnimation",2);
                float deadline=Time.time+2f;
                while(Time.time<deadline) yield return null;
                probe.Checking=false; recorder?.End();
                Assert.That(probe.Frames,Is.GreaterThan(2),character+" / "+fps+" FPS: visible third-strike trail");
                Assert.That(probe.MaxError,Is.LessThan(.012f),character+" / "+fps+" FPS: effect must stay on the actual weapon");
            }
        }

        [UnityTest]
        public IEnumerator ShieldImpactOnlySoundsOnAConfirmedBlockAndExtendsTheLock()
        {
            yield return new EnterPlayMode();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Battle_waiting"));
            _captureDelta=Time.captureDeltaTime;
            _player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            _opponent=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            foreach(var player in new[]{_player,_opponent})
            {
                foreach(var behaviour in player.GetComponents<MonoBehaviour>()) behaviour.enabled=false;
                Set(player.GetComponent<HealthSystem>(),"_currentHp",100f);
            }
            _player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            _opponent.transform.position=Vector3.back;
            var defender=_player.GetComponent<PlayerCombat>(); var attacker=_opponent.GetComponent<PlayerCombat>();
            Call(_player.GetComponent<WeaponLoadout>(),"Apply",MeleeWeaponKind.SwordShield);
            Assert.That(Call(defender,"SetWeaponGuard",true),Is.True);
            var audio=_player.GetComponent<CombatAudio>();
            var source=(AudioSource)typeof(CombatAudio).GetField("_worldSource",Private).GetValue(audio);
            var clip=Resources.Load<AudioClip>("CombatAudio/shield-block");
            Assert.That(clip,Is.Not.Null); Assert.That(clip.length,Is.InRange(.2f,.7f));
            var samples=new float[clip.samples]; clip.GetData(samples,0);
            Assert.That(System.Array.Exists(samples,x=>Mathf.Abs(x)>.2f),Is.True,"The impact asset must not be silent.");
            Assert.That(Call(defender,"TryBlockMelee",attacker),Is.False);
            Assert.That(source.isPlaying,Is.False,"A rear hit must not play shield feedback.");
            _opponent.transform.position=Vector3.forward;
            Assert.That(Call(defender,"TryBlockMelee",attacker),Is.True);
            Assert.That(source.isPlaying,Is.True,"A confirmed block must play the impact immediately.");
            double until=(double)typeof(PlayerCombat).GetField("_recoilUntil",Private).GetValue(attacker);
            Assert.That(until-Time.timeAsDouble,Is.EqualTo(.975d).Within(.002));
            float deadline=Time.time+.70f; while(Time.time<deadline) yield return null;
            Assert.That(attacker.IsWeaponRecoiling,Is.True,"Still locked beyond the old .65-second duration.");
            deadline=(float)until+.05f; while(Time.time<deadline) yield return null;
            Assert.That(attacker.IsWeaponRecoiling,Is.False);
        }

        [UnityTest]
        public IEnumerator RealAnimationEventsDoNotExtinguishWeaponTrailsDuringWindup()
        {
            yield return new EnterPlayMode();
            var scene = SceneManager.CreateScene("Lobby");
            SceneManager.SetActiveScene(scene);
            Outcome = "Running";
            _captureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 30f;
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            _player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // Isolate presentation from input/network/session setup. Animator, animation
            // events, native retargeting, equipment and VFX keep their real frame order.
            foreach (var behaviour in _player.GetComponents<MonoBehaviour>())
                behaviour.enabled = behaviour is WeaponLoadout || behaviour is BlockAttackVfx;
            var health = _player.GetComponent<HealthSystem>();
            typeof(HealthSystem).GetField("_currentHp", Private).SetValue(health, 100f);
            yield return null; // Let offline loadout Start complete before choosing a weapon.
            _skin = new CharacterSkin(_player.GetComponentInChildren<SkinnedMeshRenderer>());
            Assert.That(_skin.Apply(CharacterCatalog.Instance.Find("brute"), out var error), Is.True, error);
            var recorder = CaptureFrames ? new GameObject("Runtime weapon capture").AddComponent<WeaponPlaybackRecorder>() : null;
            var animator = _player.GetComponent<Animator>();
            animator.fireEvents = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var combat = _player.GetComponent<PlayerCombat>();
            _player.AddComponent<WeaponAimPlaybackDriver>();
            var loadout = _player.GetComponent<WeaponLoadout>();
            var fx = _player.GetComponent<BlockAttackVfx>();
            foreach (var kind in new[] { MeleeWeaponKind.Greatsword, MeleeWeaponKind.Axe })
            {
                for (int attack = 0; attack < (kind == MeleeWeaponKind.Axe ? 1 : 3); attack++)
                {
                    typeof(WeaponLoadout).GetMethod("Apply", Private | BindingFlags.Public).Invoke(loadout, new object[] { kind });
                    yield return null;
                    typeof(PlayerCombat).GetField("isAttacking", Private).SetValue(combat, false);
                    typeof(PlayerCombat).GetMethod("SetWeaponFootworkWeight", Private).Invoke(combat, new object[] { false });
                    animator.speed = 1;
                    animator.SetFloat("MoveY", kind == MeleeWeaponKind.Axe ? 1 : 0);
                    animator.SetFloat("LocomotionRate", 1);
                    animator.Play("New State", 1, 0); animator.Update(0);
                    typeof(PlayerCombat).GetMethod("UpdateWeaponReadyPose", Private).Invoke(combat, null);
                    yield return new WaitForSeconds(.3f);
                    if (recorder != null) recorder.Begin(kind + "-" + attack);
                    typeof(PlayerCombat).GetField("_currentAttackSpeed", Private).SetValue(combat, .5f);
                    typeof(PlayerCombat).GetField("isAttacking", Private).SetValue(combat, true);
                    typeof(PlayerCombat).GetMethod("PlayAttackAnimation", Private).Invoke(combat, new object[] { attack });
                    // PlayerManager normally compensates gait speed for attack speed.
                    // It is disabled in this isolated fixture, so retain a normal walk.
                    animator.SetFloat("LocomotionRate", 1f / animator.speed);
                    float until = Time.time + .55f;
                    int visibleFrames = 0;
                    while (Time.time < until)
                    {
                        yield return null;
                        if (((List<Vector3>)typeof(BlockAttackVfx).GetField("_bladeVertices", Private).GetValue(fx)).Count > 4)
                            visibleFrames++;
                    }
                    Assert.That((bool)typeof(BlockAttackVfx).GetField("_emitting", Private).GetValue(fx), Is.True,
                        kind + " attack " + attack + " lost its trail before the closing hit event.");
                    Assert.That(visibleFrames, Is.GreaterThan(2), kind + " attack " + attack + " never rendered a sweep.");
                    Outcome = kind + " " + attack + ": visible frames=" + visibleFrames;
                    // Include the complete recovery of the slower imported axe take.
                    yield return new WaitForSeconds(kind == MeleeWeaponKind.Axe ? 2.9f : 1.9f);
                    Assert.That((bool)typeof(BlockAttackVfx).GetField("_emitting", Private).GetValue(fx), Is.False,
                        "The real closing phase must end the stroke.");
                    typeof(PlayerCombat).GetField("isAttacking", Private).SetValue(combat, false);
                    typeof(PlayerCombat).GetMethod("SetWeaponFootworkWeight", Private).Invoke(combat, new object[] { false });
                    animator.speed = 1;
                    animator.SetFloat("LocomotionRate", 1);
                    for (int frame = 0; frame < 12; frame++)
                    {
                        typeof(PlayerCombat).GetMethod("UpdateWeaponReadyPose", Private).Invoke(combat, null);
                        yield return null;
                    }
                    Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName(kind == MeleeWeaponKind.Axe ? "New State" : WeaponCatalog.Instance.Find(kind).ReadyState), Is.True,
                        "The normal ready-pose update must restore the configured hold or one-handed locomotion after recovery.");
                    if (recorder != null) recorder.End();
                }
            }
            Outcome = "PASS: all four attacks emitted and stopped correctly with real animation events and native Brute retargeting.";
            Directory.CreateDirectory("Reports/Weapons/Review");
            File.WriteAllText("Reports/Weapons/Review/runtime-events-result.txt", Outcome);
            Time.captureDeltaTime = _captureDelta;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_player != null) { _player.SetActive(false); Object.Destroy(_player); }
            _skin?.Dispose();
            if(_opponent!=null) Object.Destroy(_opponent);
            if(_recorder!=null) Object.Destroy(_recorder);
            Time.captureDeltaTime = _captureDelta;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }

    [DefaultExecutionOrder(1500)]
    public sealed class FinisherTrailProbe : MonoBehaviour
    {
        public bool Checking;
        public int Frames;
        public float MaxError;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private void LateUpdate()
        {
            if(!Checking) return;
            var state=GetComponent<Animator>().GetCurrentAnimatorStateInfo(1);
            if(!state.IsName("Weapon_Greatsword3") || state.normalizedTime<.15f || state.normalizedTime>.5f) return;
            var fx=GetComponent<BlockAttackVfx>();
            // Once the closing hit phase passes, the old afterimage correctly stays
            // in world space while the blade returns to its ready position.
            if(!(bool)typeof(BlockAttackVfx).GetField("_emitting",Private).GetValue(fx)) return;
            var vertices=(List<Vector3>)typeof(BlockAttackVfx).GetField("_bladeVertices",Private).GetValue(fx);
            if(vertices.Count<4) return;
            var blade=(Transform)typeof(MeleeHitBox).GetProperty("PoseSource",Private).GetValue(GetComponentInChildren<MeleeHitBox>(true));
            var entry=WeaponCatalog.Instance.Find(MeleeWeaponKind.Greatsword);
            MaxError=Mathf.Max(MaxError,Vector3.Distance(vertices[vertices.Count-2],blade.TransformPoint(entry.BladeTip)),
                Vector3.Distance(vertices[vertices.Count-1],blade.TransformPoint(entry.BladeBase)));
            Frames++;
        }
    }

    // Input/network polling is disabled by the fixture. Drive the same production
    // aim methods after native retargeting, without altering animation or FX state.
    [DefaultExecutionOrder(1020)]
    public sealed class WeaponAimPlaybackDriver : MonoBehaviour
    {
        private PlayerCombat _combat;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private void Awake() => _combat=GetComponent<PlayerCombat>();
        private void Update() => typeof(PlayerCombat).GetMethod("RestoreMeleeAimPose",Private).Invoke(_combat,null);
        private void LateUpdate()
        {
            if(_combat.WeaponKind!=MeleeWeaponKind.Greatsword)
            { typeof(PlayerCombat).GetMethod("UpdateMeleeAimPose",Private).Invoke(_combat,null); return; }
            var driver=GetComponent<Animator>();
            var visual=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{driver});
            typeof(PlayerCombat).GetMethod("UpdateVisualMeleeAimPose",Private).Invoke(_combat,new object[]{visual});
        }
    }

    // Records the normal Play Mode render after equipment (1055) and VFX (1200).
    // It never writes animation poses, trail state, meshes, or materials on the player.
    [DefaultExecutionOrder(1500)]
    public sealed class WeaponPlaybackRecorder : MonoBehaviour
    {
        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _image;
        private string _label;
        private int _frame;
        private float _next;
        private void Awake()
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            _camera = gameObject.AddComponent<Camera>(); _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.10f,.12f,.15f);
            _camera.fieldOfView = 35; _camera.nearClipPlane = .02f; _camera.farClipPlane = 30;
            transform.position = new Vector3(3.2f,2.8f,5.6f); transform.LookAt(new Vector3(0,1.35f,0));
            var light = new GameObject("Capture light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.6f;
            light.transform.rotation = transform.rotation * Quaternion.Euler(20,-25,0);
            _target = new RenderTexture(800,800,24); _camera.targetTexture = _target;
            _image = new Texture2D(800,800,TextureFormat.RGB24,false);
        }
        public void Begin(string label) { _label = label; _frame = 0; _next = 0; }
        public void End() { _label = null; }
        private void LateUpdate()
        {
            if (_label == null || Time.time < _next) return;
            _next = Time.time + .045f;
            _camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = _target;
            _image.ReadPixels(new Rect(0,0,800,800),0,0); _image.Apply(); RenderTexture.active = previous;
            Directory.CreateDirectory("Reports/Weapons/Review");
            File.WriteAllBytes("Reports/Weapons/Review/runtime-"+_label+"-"+(_frame++).ToString("D3")+".png", _image.EncodeToPNG());
        }
        private void OnDestroy()
        {
            if (_target != null) { _target.Release(); Object.Destroy(_target); }
            if (_image != null) Object.Destroy(_image);
        }
    }
}
