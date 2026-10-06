using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.Characters
{
    /// <summary>An isolated visual rig. Never clones the network player or its gameplay scripts.</summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        private GameObject _stage, _rig;
        private Camera _camera;
        private RenderTexture _texture;
        private CharacterSkin _skin;
        private RawImage _image;
        private Light _light;
        private Mesh _posedMesh;
        private GameObject _snapshot;
        public void Initialize(RawImage image)
        {
            if (_stage != null) return;
            _image = image;
            var catalog = CharacterCatalog.Instance;
            if (catalog == null || catalog.PreviewRig == null) return;
            _stage = new GameObject("Character preview stage");
            _stage.transform.position = new Vector3(10000, -10000, 10000);
            _rig = Instantiate(catalog.PreviewRig, _stage.transform);
            _rig.transform.localPosition = Vector3.zero; _rig.transform.localRotation = Quaternion.identity;
            foreach (var t in _rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var animator = _rig.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = catalog.PreviewController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.Rebind();
                animator.Update(0f);
            }
            var body = _rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
            _skin = new CharacterSkin(body);
            Rect viewport = image.rectTransform.rect;
            float aspect = viewport.width > 0f && viewport.height > 0f ? viewport.width / viewport.height : 1f;
            int height = Mathf.Clamp(Mathf.RoundToInt(512f / aspect), 128, 1024);
            _texture = new RenderTexture(512, height, 24) { name = "Character preview", antiAliasing = 2 };
            _texture.Create(); _image.texture = _texture;
            _image.color = Color.white;
            _camera = new GameObject("Preview camera").AddComponent<Camera>();
            _camera.enabled = false; // Render the static pose only on selection or rotation.
            _camera.transform.SetParent(_stage.transform, false);
            _camera.targetTexture = _texture; _camera.cullingMask = 1 << 31;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(.025f, .055f, .08f);
            _camera.fieldOfView = 30; _camera.nearClipPlane = .01f; _camera.farClipPlane = 100;
            _camera.aspect = aspect;
            _light = new GameObject("Preview light").AddComponent<Light>();
            _light.transform.SetParent(_stage.transform, false);
            _light.type = LightType.Point; _light.intensity = 1.8f; _light.range = 10; _light.cullingMask = 1 << 31;
            Frame(body);
        }
        private void Frame(SkinnedMeshRenderer body)
        {
            if (body == null || body.sharedMesh == null) return;
            // Renderer bounds can still describe the bind pose on the first UI frame.
            // Native models have different vertex layouts. Reusing the same uploaded Mesh
            // for another layout can leave a stale graphics-buffer stride on WebGL.
            if (_posedMesh != null) DestroyObject(_posedMesh);
            _posedMesh = new Mesh { name = "Character preview pose" };
            // The snapshot inherits this transform; compensate here so character size is not applied twice.
            body.BakeMesh(_posedMesh, true);
            _posedMesh.RecalculateBounds();
            Bounds local = _posedMesh.bounds;
            var bounds = new Bounds(body.transform.TransformPoint(local.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(body.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
            float distance = Mathf.Max(bounds.extents.x / _camera.aspect, bounds.extents.y) / Mathf.Tan(15 * Mathf.Deg2Rad) * 1.2f + bounds.extents.z;
            _camera.transform.position = bounds.center + Vector3.forward * distance;
            _camera.transform.LookAt(bounds.center);
            _light.transform.position = bounds.center + new Vector3(.8f, .8f, 1.7f);
        }
        public bool Show(CharacterDefinition definition, out string error)
        {
            error = "미리보기 모델이 없습니다.";
            if (_skin == null) return false;
            if (_snapshot != null) { _snapshot.SetActive(false); DestroyObject(_snapshot); }
            var animator = _rig.GetComponent<Animator>();
            if (animator != null) { animator.enabled = true; animator.Update(0f); }
            if (!_skin.Apply(definition, out error)) { _skin.Restore(); return false; }
            _skin.SyncPose(); Frame(_skin.VisibleBody);
            // A posed static preview avoids deferred GPU skinning uploads on first open.
            // Bake only when selecting, while preserving the same original mesh and materials.
            var visible = _skin.VisibleBody;
            _snapshot = new GameObject("Preview pose", typeof(MeshFilter), typeof(MeshRenderer));
            _snapshot.layer = 31; _snapshot.transform.SetParent(visible.transform, false);
            _snapshot.GetComponent<MeshFilter>().sharedMesh = _posedMesh;
            _snapshot.GetComponent<MeshRenderer>().sharedMaterials = visible.sharedMaterials;
            _snapshot.GetComponent<MeshRenderer>().forceMeshLod = 0;
            foreach (var body in _rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)) body.forceRenderingOff = true;
            foreach (var follower in _rig.GetComponentsInChildren<CharacterPoseFollower>()) follower.enabled = false;
            if (animator != null) animator.enabled = false;
            _camera.Render();
            return true;
        }
        public void Rotate(float degrees)
        {
            if (_rig == null) return;
            _rig.transform.Rotate(Vector3.up, degrees, Space.World);
            _camera.Render();
        }
        private void OnEnable() { if (_stage != null) _stage.SetActive(true); }
        private void OnDisable() { if (_stage != null) _stage.SetActive(false); }
        private void OnDestroy()
        {
            _skin?.Dispose();
            if (_image != null) _image.texture = null;
            if (_camera != null) _camera.targetTexture = null;
            if (_stage != null) DestroyObject(_stage);
            if (_texture != null) { _texture.Release(); DestroyObject(_texture); }
            if (_posedMesh != null) DestroyObject(_posedMesh);
        }
        private static void DestroyObject(Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
