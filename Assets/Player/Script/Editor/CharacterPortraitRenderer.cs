using System.IO;
using BattlePvp.Characters;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace BattlePvp.EditorData
{
    public static class CharacterPortraitRenderer
    {
        public static void Render(CharacterDefinition definition, string path, string clipPath, float time,
            bool portrait = false)
        {
            var preview = new PreviewRenderUtility();
            PlayableGraph graph = default;
            Texture2D image = null;
            Mesh snapshot = null;
            try
            {
                var rig = preview.InstantiatePrefabInScene(
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Anim/Ch10_nonPBR.fbx"));
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var body = rig.GetComponentInChildren<SkinnedMeshRenderer>();
                var skin = new CharacterSkin(body);
                if (!skin.Apply(definition, out string error)) throw new System.ArgumentException(error);
                var animator = rig.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null)
                {
                    graph = PlayableGraph.Create("Character portrait");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                    output.SetSourcePlayable(playable);
                    playable.SetTime(time); graph.Play(); graph.Evaluate(0);
                }
                skin.SyncPose(); body = skin.VisibleBody;
                // A static snapshot also renders reliably when editor skinning has not ticked yet.
                snapshot = new Mesh(); body.BakeMesh(snapshot);
                var staticBody = new GameObject("Portrait pose");
                staticBody.transform.SetParent(body.transform, false);
                staticBody.AddComponent<MeshFilter>().sharedMesh = snapshot;
                staticBody.AddComponent<MeshRenderer>().sharedMaterials = body.sharedMaterials;
                body.enabled = false;
                preview.cameraFieldOfView = 32;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.025f, .055f, .08f);
                var visualAnimator = body.GetComponentInParent<Animator>();
                snapshot.RecalculateBounds();
                Vector3 focus = portrait ? visualAnimator.GetBoneTransform(HumanBodyBones.Head).position
                    : body.transform.TransformPoint(snapshot.bounds.center);
                float distance = portrait ? 1.05f : snapshot.bounds.size.y * 2.05f;
                preview.camera.transform.position = focus + new Vector3(0, .04f, distance);
                preview.camera.transform.LookAt(focus);
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 20;
                preview.lights[0].intensity = .9f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, 155, 0);
                preview.lights[1].intensity = .65f;
                preview.lights[1].transform.rotation = Quaternion.Euler(340, 210, 0);
                preview.ambientColor = new Color(.3f, .3f, .3f);
                preview.BeginStaticPreview(new Rect(0, 0, portrait ? 256 : 700, portrait ? 256 : 800));
                preview.Render(true);
                image = preview.EndStaticPreview();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (image != null) Object.DestroyImmediate(image);
                preview.Cleanup();
                if (snapshot != null) Object.DestroyImmediate(snapshot);
            }
            if (path.StartsWith("Assets/"))
            {
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                definition.Portrait = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets();
            }
        }
    }
}
