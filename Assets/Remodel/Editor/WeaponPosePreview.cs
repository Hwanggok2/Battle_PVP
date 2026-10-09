using System;
using System.IO;
using System.Reflection;
using System.Linq;
using BattlePvp.Characters;
using BattlePvp.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace BattlePvp.Remodel.Editor
{
    public static class WeaponPosePreview
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static string CaptureSource(string character, string take, int frames = 24)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var definition = CharacterCatalog.Instance.Find(character);
            var model = Object.Instantiate(definition.VisualPrefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model, scene);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = model.GetComponentInChildren<Animator>();
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.Rebind(); animator.Update(0);
            string sourcePath = take.StartsWith("Assets/") ? take : "Assets/Remodel/Weapons/Source/Mixamo/"+take+".fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
            string label = Path.GetFileNameWithoutExtension(take);
            var graph = PlayableGraph.Create("Source only"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph,clip);
            AnimationPlayableOutput.Create(graph,"Body",animator).SetSourcePlayable(playable); graph.Play();
            var cameraGo = new GameObject("Camera",typeof(Camera)); var lightGo = new GameObject("Light",typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);
            var camera = cameraGo.GetComponent<Camera>(); camera.enabled=false; camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.14f,.18f);
            camera.fieldOfView=35; camera.nearClipPlane=.02f; camera.farClipPlane=30;
            var body=model.GetComponentInChildren<SkinnedMeshRenderer>();
            float height=2.2f;
            var target=new Vector3(0,height*.55f,0);
            camera.transform.position=target+new Vector3(.3f,.1f,2.5f)*height;
            camera.transform.LookAt(target);
            var light=lightGo.GetComponent<Light>(); light.type=LightType.Directional; light.intensity=1.6f;
            light.transform.rotation=camera.transform.rotation*Quaternion.Euler(20,-25,0);
            string folder=Path.GetFullPath("Reports/Weapons/Review");
            var log=new System.Text.StringBuilder();
            var bodies=model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var baked=bodies.Select(b=>new Mesh()).ToArray();
            for(int k=0;k<bodies.Length;k++)
            {
                var display=new GameObject("Baked source frame",typeof(MeshFilter),typeof(MeshRenderer));
                display.transform.SetParent(bodies[k].transform,false);
                display.GetComponent<MeshFilter>().sharedMesh=baked[k];
                display.GetComponent<MeshRenderer>().sharedMaterials=bodies[k].sharedMaterials;
            }
            try
            {
                foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.enabled=true; r.forceRenderingOff=false; r.updateWhenOffscreen=true; }
                for(int i=0;i<frames;i++)
                {
                    playable.SetTime(clip.length*i/(frames-1)); graph.Evaluate(0);
                    model.transform.position-=Vector3.ProjectOnPlane(animator.GetBoneTransform(HumanBodyBones.Hips).position,Vector3.up);
                    for(int k=0;k<bodies.Length;k++) { bodies[k].BakeMesh(baked[k]); bodies[k].enabled=false; }
                    var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var middle=animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                    var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                    var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
                    var shaft=Vector3.ProjectOnPlane(index.position-little.position,middle.position-hand.position).normalized;
                    log.AppendLine(i+" R="+(hand.position-animator.GetBoneTransform(HumanBodyBones.Chest).position)+" shaft="+shaft+" gap="+Vector3.Distance(WeaponLoadout.GripCenter(animator,false),WeaponLoadout.GripCenter(animator,true)));
                    var rt=new RenderTexture(900,900,24); var image=new Texture2D(900,900,TextureFormat.RGB24,false);
                    var previous=RenderTexture.active;
                    try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,900,900),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(folder,"source-"+label+"-"+i+".png"),image.EncodeToPNG()); }
                    finally { camera.targetTexture=null; RenderTexture.active=previous; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
                }
                return log.ToString();
            }
            finally { graph.Destroy(); foreach(var m in baked) Object.DestroyImmediate(m); Object.DestroyImmediate(model); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(lightGo); EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static string Capture(string character, MeleeWeaponKind kind, string state, float phase, string label, bool wide = false, string sourceTake = null, int frames = 1, bool blendEntry = false, float pitch = 0)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var combat = player.GetComponent<PlayerCombat>();
            var loadout = player.GetComponent<WeaponLoadout>();
            var animator = player.GetComponent<Animator>();
            animator.fireEvents = false; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            using var skin = new CharacterSkin(player.GetComponentInChildren<SkinnedMeshRenderer>());
            Mesh[] baked = Array.Empty<Mesh>();
            var cameraGo = new GameObject("Weapon inspection camera", typeof(Camera));
            var lightGo = new GameObject("Weapon inspection light", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            try
            {
                typeof(Mirror.NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Private).Invoke(player.GetComponent<Mirror.NetworkIdentity>(), null);
                typeof(PlayerCombat).GetMethod("Awake", Private).Invoke(combat, null);
                typeof(WeaponLoadout).GetMethod("Awake", Private).Invoke(loadout, null);
                typeof(HealthSystem).GetField("_currentHp", Private).SetValue(player.GetComponent<HealthSystem>(), 100f);
                if (!skin.Apply(CharacterCatalog.Instance.Find(character), out string error)) throw new Exception(error);
                if (sourceTake != null)
                {
                    var overrides = new AnimatorOverrideController(animator.runtimeAnimatorController);
                    overrides["Weapon_Greatsword1"] = AssetDatabase.LoadAllAssetsAtPath("Assets/Remodel/Weapons/Source/Mixamo/"+sourceTake+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
                    animator.runtimeAnimatorController = overrides;
                    state = "Weapon_Greatsword1";
                }
                animator.Rebind(); animator.Update(0);
                typeof(WeaponLoadout).GetMethod("Apply", Private).Invoke(loadout, new object[] { kind });
                var entry=WeaponCatalog.Instance.Find(kind);
                var data=entry.Attacks.FirstOrDefault(a=>a!=null && a.animationName==state);
                var aim=(MeleeAimPose)typeof(PlayerCombat).GetField("_meleeAimPose",Private).GetValue(combat);
                var visible=(Animator)typeof(CharacterPoseFollower).GetMethod("GetViewAnimator",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{animator});
                bool nativeAim=kind==MeleeWeaponKind.Greatsword && visible!=animator;
                if(nativeAim)
                {
                    aim=new MeleeAimPose(player.transform,visible);
                    typeof(PlayerCombat).GetField("_meleeAimPose",Private).SetValue(combat,aim);
                }
                Vector3 direction=Quaternion.AngleAxis(pitch,Vector3.right)*Vector3.forward;
                typeof(PlayerCombat).GetField("_lookPitch",Private).SetValue(combat,pitch);
                typeof(PlayerCombat).GetField("_lookPoseWeight",Private).SetValue(combat,1f);
                animator.SetLayerWeight(animator.GetLayerIndex("Weapon Footwork"),data!=null && kind==MeleeWeaponKind.Greatsword ? 1 : 0);
                if(data!=null)
                {
                    typeof(PlayerCombat).GetField("_meleeAnimationData",Private).SetValue(combat,data);
                    typeof(PlayerCombat).GetField("isAttacking",Private).SetValue(combat,true);
                    typeof(PlayerCombat).GetField("_meleeAimWeight",Private).SetValue(combat,1f);
                    typeof(PlayerCombat).GetMethod("SetMeleeAim",Private).Invoke(combat,new object[]{direction*1.4f});
                }
                Action fitPose=()=>
                {
                    var sample=animator.IsInTransition(1) ? animator.GetNextAnimatorStateInfo(1) : animator.GetCurrentAnimatorStateInfo(1);
                    if(nativeAim) skin.SyncPose();
                    if(data!=null && sourceTake==null) aim.ApplyCalibrated(direction,aim.SelectReference(data,1.4f),Mathf.Clamp01(sample.normalizedTime),1,direction,data.maxAimCalibration,data.aimInRootSpace,data.CrossingPhase(aim.Avatar));
                    else if(sourceTake==null) aim.Apply(direction);
                    if(!nativeAim) skin.SyncPose();
                    if(sourceTake==null) typeof(WeaponLoadout).GetMethod("SyncVisual",Private).Invoke(loadout,null);
                };
                animator.Play("Movement", 0, 0); animator.Play(state, 1, phase); animator.Update(0);
                fitPose();
                foreach (var other in player.GetComponentsInChildren<Camera>(true)) other.enabled = false;
                foreach (var renderer in player.GetComponentsInChildren<Renderer>())
                {
                    renderer.forceRenderingOff = false;
                    if (renderer is SkinnedMeshRenderer body) { body.updateWhenOffscreen = true; body.enabled = body.sharedMaterials.Length > 0; }
                }
                // CharacterSkin intentionally hides the driver body when a native avatar is present.
                if (character != "default") player.GetComponentInChildren<SkinnedMeshRenderer>().enabled = false;
                var camera = cameraGo.GetComponent<Camera>(); camera.enabled = false;
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .14f, .18f);
                camera.fieldOfView = 35; camera.nearClipPlane = .02f; camera.farClipPlane = 20;
                var light = lightGo.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f;
                light.transform.rotation = Quaternion.Euler(35, 145, 0);
                string folder = Path.GetFullPath("Reports/Weapons/Review"); Directory.CreateDirectory(folder);
                var angles = new[] { new Vector3(.6f,.2f,2.4f), new Vector3(-1.5f,.15f,1.5f), new Vector3(.8f,.2f,-1.8f) };
                var target = new Vector3(0, 1.35f, .08f);
                var bodies = player.GetComponentsInChildren<SkinnedMeshRenderer>().Where(b=>b.enabled).ToArray();
                baked = bodies.Select(b=>new Mesh()).ToArray();
                for(int k=0;k<bodies.Length;k++)
                {
                    var display=new GameObject("Frozen inspection frame",typeof(MeshFilter),typeof(MeshRenderer));
                    display.transform.SetParent(bodies[k].transform,false);
                    display.GetComponent<MeshFilter>().sharedMesh=baked[k];
                    display.GetComponent<MeshRenderer>().sharedMaterials=bodies[k].sharedMaterials;
                }
                float seconds = animator.GetCurrentAnimatorStateInfo(1).length;
                var fx=player.GetComponent<BlockAttackVfx>();
                MeshFilter trailDisplay=null;
                if(data!=null && frames>1 && sourceTake==null)
                {
                    typeof(BlockAttackVfx).GetMethod("Awake",Private).Invoke(fx,null);
                    typeof(BlockAttackVfx).GetField("_start",Private).SetValue(fx,Time.time);
                    typeof(BlockAttackVfx).GetField("_emitting",Private).SetValue(fx,true);
                    typeof(BlockAttackVfx).GetField("_animationState",Private).SetValue(fx,animator.GetCurrentAnimatorStateInfo(1).fullPathHash);
                    typeof(BlockAttackVfx).GetField("_emissionEndPhase",Private).SetValue(fx,AnimationHitWindow.Melee(animator,1).End);
                    var display=new GameObject("Corrected blade trail",typeof(MeshFilter),typeof(MeshRenderer));
                    display.transform.SetParent(player.transform,false);
                    display.GetComponent<MeshRenderer>().sharedMaterial=(Material)typeof(BlockAttackVfx).GetField("_bladeMaterial",Private).GetValue(fx);
                    trailDisplay=display.GetComponent<MeshFilter>();
                }
                if(blendEntry)
                {
                    aim.Restore();
                    animator.Play(string.IsNullOrEmpty(entry.ReadyState) ? "New State" : entry.ReadyState,1,0);
                    animator.Update(0);
                    animator.CrossFadeInFixedTime(state,.08f,1,0);
                }
                for (int i = 0; i < (frames > 1 ? frames : angles.Length); i++)
                {
                    if (frames > 1)
                    {
                        aim.Restore();
                        if(blendEntry) animator.Update(i==0 ? 0 : seconds/(frames-1));
                        else { animator.Play("Movement",0,0); animator.Play(state,1,i/(float)(frames-1)); animator.Update(0); }
                        fitPose();
                        if(trailDisplay!=null)
                        {
                            typeof(BlockAttackVfx).GetMethod("UpdateBladeTrail",Private).Invoke(fx,new object[]{seconds*i/(frames-1)});
                            trailDisplay.sharedMesh=(Mesh)typeof(BlockAttackVfx).GetField("_bladeMesh",Private).GetValue(fx);
                            trailDisplay.GetComponent<MeshRenderer>().SetPropertyBlock((MaterialPropertyBlock)typeof(BlockAttackVfx).GetField("_bladeProperties",Private).GetValue(fx));
                        }
                    }
                    // Explicit CPU skinning avoids Unity reusing the first pose's
                    // render buffer when several frames are sampled in one editor tick.
                    for(int k=0;k<bodies.Length;k++) { bodies[k].BakeMesh(baked[k]); bodies[k].enabled=false; }
                    camera.transform.position = target + angles[frames > 1 ? 0 : i] * (wide ? (kind==MeleeWeaponKind.Axe ? 2.3f : 1.9f) : 1f); camera.transform.LookAt(target);
                    light.transform.rotation = camera.transform.rotation * Quaternion.Euler(20, -25, 0);
                    var rt = new RenderTexture(900, 900, 24); var image = new Texture2D(900, 900, TextureFormat.RGB24, false);
                    var previous = RenderTexture.active;
                    try
                    {
                        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                        image.ReadPixels(new Rect(0,0,900,900),0,0); image.Apply();
                        File.WriteAllBytes(Path.Combine(folder, label + "-" + i + ".png"), image.EncodeToPNG());
                    }
                    finally { camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
                }
                return folder + "/" + label + "-0.png";
            }
            finally { foreach(var mesh in baked) Object.DestroyImmediate(mesh); skin.Dispose(); Object.DestroyImmediate(player); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(lightGo); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
