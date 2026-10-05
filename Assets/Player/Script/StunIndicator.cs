using UnityEngine;
using UnityEngine.Rendering;

namespace BattlePvp.Combat
{
    /// <summary>Reads replicated control expiry so players and training targets show the same stun.</summary>
    [DisallowMultipleComponent]
    public sealed class StunIndicator : MonoBehaviour
    {
        private ExpandedSkillController _player;
        private DummyHealth _dummy;
        private Transform _head, _visual;
        private Material _material;
        private readonly Transform[] _sparks = new Transform[3];
        public bool Visible => _visual != null && _visual.gameObject.activeSelf;

        private void Awake()
        {
            _player = GetComponent<ExpandedSkillController>();
            _dummy = GetComponent<DummyHealth>();
            var animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman) _head = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        private void LateUpdate()
        {
            bool stunned = (_player != null && _player.isActiveAndEnabled && _player.IsStunned) ||
                (_dummy != null && _dummy.isActiveAndEnabled && _dummy.IsStunned);
            if (!stunned) { Hide(); return; }
            if (_visual == null) CreateVisual();
            _visual.gameObject.SetActive(true);
            _visual.position = _head != null ? _head.position + Vector3.up * .38f : transform.position + Vector3.up * 2.3f;
            _visual.rotation = Quaternion.Euler(12, 0, 0);
            for (int i = 0; i < _sparks.Length; i++)
            {
                float angle = Time.time * 3f + i * Mathf.PI * 2 / _sparks.Length;
                _sparks[i].localPosition = new Vector3(Mathf.Cos(angle) * .32f, .025f, Mathf.Sin(angle) * .32f);
                _sparks[i].localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 45);
            }
        }

        private void CreateVisual()
        {
            _visual = new GameObject("Stun indicator").transform;
            _visual.SetParent(transform, false);
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _material.SetColor("_BaseColor", new Color(1f, .76f, .15f));
            var ring = _visual.gameObject.AddComponent<LineRenderer>();
            ring.sharedMaterial = _material; ring.useWorldSpace = false; ring.loop = true;
            ring.widthMultiplier = .024f; ring.positionCount = 40;
            ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * .32f, 0, Mathf.Sin(angle) * .32f));
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "Stun spark"; spark.transform.SetParent(_visual, false);
                spark.transform.localScale = new Vector3(.085f, .085f, .045f);
                var collider = spark.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                var renderer = spark.GetComponent<Renderer>(); renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                _sparks[i] = spark.transform;
            }
        }

        private void Hide() { if (_visual != null) _visual.gameObject.SetActive(false); }
        private void OnDisable() => Hide();
        private void OnDestroy()
        {
            if (_visual != null) Destroy(_visual.gameObject);
            if (_material != null) Destroy(_material);
        }
    }
}
