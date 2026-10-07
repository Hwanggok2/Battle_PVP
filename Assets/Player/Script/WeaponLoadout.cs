using System;
using BattlePvp.Characters;
using Mirror;
using UnityEngine;

namespace BattlePvp.Combat
{
    [DefaultExecutionOrder(1055)]
    public sealed class WeaponLoadout : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnSelected))] private MeleeWeaponKind _selected;
        private PlayerCombat _combat;
        private Animator _animator;
        private MeleeHitBox _blade;
        private MeshRenderer _swordRenderer, _shield;
        private BlockAttackVfx _trail;
        private double _initialDeadline, _nextRequest;
        private bool _initialized;
        private bool _visualInitialized;
        private MeleeWeaponKind _materialWeapon = (MeleeWeaponKind)(-1);
        private Transform _materialPose;
        private Vector3 _originalBladePosition;
        public MeleeWeaponKind Selected => _selected;
        public event Action Changed;
        public static bool CanEdit => PlayerAppearance.CanEdit;
        private static string SaveKey => "BattlePvp.Weapon.v1." + (PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
        private static MeleeWeaponKind Saved => (MeleeWeaponKind)Mathf.Clamp(PlayerPrefs.GetInt(SaveKey, 0), 0, 3);
        private void Awake()
        {
            _combat = GetComponent<PlayerCombat>(); _animator = GetComponent<Animator>();
            _blade = GetComponentInChildren<MeleeHitBox>(true);
            if (_blade != null) { _swordRenderer = _blade.GetComponent<MeshRenderer>(); _originalBladePosition = _blade.transform.localPosition; }
            _trail = GetComponent<BlockAttackVfx>();
        }
        private void Start() { if (!NetworkClient.active && !NetworkServer.active) Apply(Saved); }
        public override void OnStartServer() { _initialDeadline = NetworkTime.time + 10; Apply(MeleeWeaponKind.Sword); }
        public override void OnStartClient() => ApplySelection(_selected, false);
        public override void OnStartLocalPlayer() => CmdSelect(Saved, true);
        public bool Request(MeleeWeaponKind kind)
        {
            if (!CanEdit || WeaponCatalog.Instance?.Find(kind) == null) return false;
            if (NetworkClient.active) { if (!isLocalPlayer) return false; CmdSelect(kind, false); }
            else { Apply(kind); Save(kind); }
            return true;
        }
        [Command] private void CmdSelect(MeleeWeaponKind kind, bool initial)
        {
            if (!TrySelect(kind, initial)) return;
            TargetAccepted(connectionToClient, kind);
        }
        internal bool TrySelect(MeleeWeaponKind kind, bool initial)
        {
            bool first = initial && !_initialized && NetworkTime.time <= _initialDeadline;
            if ((!first && !PlayerAppearance.CanEditScene(gameObject.scene.name)) || NetworkTime.time < _nextRequest ||
                GetComponent<HealthSystem>() is { IsDead: true } || WeaponCatalog.Instance?.Find(kind) == null) return false;
            _nextRequest = NetworkTime.time + .2; _initialized = true; Apply(kind); return true;
        }
        [TargetRpc] private void TargetAccepted(NetworkConnectionToClient target, MeleeWeaponKind kind) { Save(kind); Changed?.Invoke(); }
        private static void Save(MeleeWeaponKind kind) { PlayerPrefs.SetInt(SaveKey, (int)kind); PlayerPrefs.Save(); }
        private void OnSelected(MeleeWeaponKind previous, MeleeWeaponKind next) => ApplySelection(next, _visualInitialized);
        internal void Apply(MeleeWeaponKind kind) => ApplySelection(kind, true);
        private void ApplySelection(MeleeWeaponKind kind, bool resetCombat)
        {
            var entry = WeaponCatalog.Instance?.Find(kind); if (entry == null) return;
            if (_blade != null) _blade.transform.localPosition = _originalBladePosition;
            _selected = kind; _combat?.ApplyWeaponLoadout(entry, resetCombat); _visualInitialized = true; SyncVisual(); Changed?.Invoke();
        }
        private void LateUpdate() => SyncVisual();
        internal void SyncVisual()
        {
            var catalog = WeaponCatalog.Instance; var entry = catalog?.Find(_selected);
            if (entry == null || _blade == null || _animator == null) return;
            var source = _blade.PoseSource;
            if (entry.TwoHanded)
            {
                var rig = CharacterPoseFollower.GetViewAnimator(_animator);
                if (_combat != null && _combat.UsesTwoHandedGrip)
                {
                    FitTwoHandedGrip(rig, source, entry);
                    CharacterPoseFollower.SyncHitboxes(_animator);
                }
                else FitBladeToPalm(rig, source, entry.RightGrip);
            }
            var mesh = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (mesh != null && mesh.sharedMesh != entry.Mesh) mesh.sharedMesh = entry.Mesh;
            if (_materialWeapon != _selected || _materialPose != source)
            {
                if (_materialWeapon != _selected && _swordRenderer != null) _swordRenderer.sharedMaterials = entry.Materials;
                if (renderer != null) renderer.sharedMaterials = _swordRenderer != null ? _swordRenderer.sharedMaterials : entry.Materials;
                _materialWeapon = _selected; _materialPose = source;
            }
            var box = _blade.GetComponent<BoxCollider>();
            if (box != null) { box.center = entry.HitCenter; box.size = entry.HitSize; }
            _trail?.SetBladeEndpoints(entry.BladeBase, entry.BladeTip);
            if (_selected == MeleeWeaponKind.SwordShield && _shield == null)
            {
                var go = new GameObject("Equipped shield"); go.layer = source.gameObject.layer;
                go.transform.SetParent(transform, false); go.AddComponent<MeshFilter>().sharedMesh = catalog.ShieldMesh;
                _shield = go.AddComponent<MeshRenderer>(); _shield.sharedMaterials = catalog.ShieldMaterials;
            }
            if (_shield == null) return;
            _shield.gameObject.SetActive(_selected == MeleeWeaponKind.SwordShield && (_combat == null || _combat.MeleeEquipped));
            var visible = CharacterPoseFollower.GetViewAnimator(_animator);
            Quaternion frame = CharacterEquipmentVisual.HandFrame(visible, HumanBodyBones.LeftHand, out _);
            _shield.transform.SetPositionAndRotation(CharacterEquipmentVisual.PalmCenter(visible, HumanBodyBones.LeftHand) + frame * Vector3.right * .07f * transform.lossyScale.y, frame * Quaternion.Euler(0,180,0));
            _shield.transform.localScale = Vector3.one;
            if (renderer != null) { _shield.enabled = renderer.enabled; _shield.forceRenderingOff = renderer.forceRenderingOff; }
        }

        public static void FitBladeToPalm(Animator rig, Transform weapon, Vector3 grip)
        {
            weapon.position = CharacterEquipmentVisual.PalmCenter(rig, HumanBodyBones.RightHand) -
                weapon.rotation * Vector3.Scale(grip, weapon.lossyScale);
        }

        public static void FitTwoHandedGrip(Animator rig, Transform weapon, WeaponCatalog.Entry entry)
        {
            var right = rig.GetBoneTransform(HumanBodyBones.RightHand);
            var left = rig.GetBoneTransform(HumanBodyBones.LeftHand);
            if (right == null || left == null) return;
            Quaternion weaponRotation = weapon.rotation, rightRotation = right.rotation, leftRotation = left.rotation;
            Vector3 rightPalm = CharacterEquipmentVisual.PalmCenter(rig, HumanBodyBones.RightHand);
            Vector3 leftPalm = CharacterEquipmentVisual.PalmCenter(rig, HumanBodyBones.LeftHand);
            Vector3 rightGrip = weaponRotation * Vector3.Scale(entry.RightGrip, weapon.lossyScale);
            Vector3 leftGrip = weaponRotation * Vector3.Scale(entry.LeftGrip, weapon.lossyScale);
            Vector3 position = (rightPalm + leftPalm - rightGrip - leftGrip) * .5f;
            // A shared handle belongs to both arms. Keep the center reachable on every
            // body type instead of stretching the support arm toward the dominant hand.
            for (int i = 0; i < 4; i++)
            {
                position += ReachCorrection(rig, false, position + rightGrip - (rightPalm - right.position));
                position += ReachCorrection(rig, true, position + leftGrip - (leftPalm - left.position));
            }
            FitArm(rig, false, position + rightGrip - (rightPalm - right.position), rightRotation);
            FitArm(rig, true, position + leftGrip - (leftPalm - left.position), leftRotation);
            weapon.SetPositionAndRotation(position, weaponRotation);
        }
        private static Vector3 ReachCorrection(Animator rig, bool left, Vector3 wrist)
        {
            var arm = rig.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var elbow = rig.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = rig.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            float reach = Vector3.Distance(arm.position, elbow.position) + Vector3.Distance(elbow.position, hand.position) - .002f;
            Vector3 delta = wrist - arm.position;
            return delta.sqrMagnitude > reach * reach ? delta.normalized * reach - delta : Vector3.zero;
        }
        private static void FitArm(Animator rig, bool left, Vector3 wrist, Quaternion rotation)
        {
            var arm = rig.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var elbow = rig.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = rig.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            SolveArm(arm, elbow, hand, wrist, elbow.position + rig.transform.right * (left ? -.12f : .12f));
            hand.rotation = rotation;
        }
        public static void SolveArm(Transform arm, Transform elbow, Transform hand, Vector3 wrist, Vector3 hint)
        {
            float upper = Vector3.Distance(arm.position, elbow.position), lower = Vector3.Distance(elbow.position, hand.position);
            Vector3 direction = (wrist - arm.position).normalized;
            float distance = Mathf.Clamp(Vector3.Distance(arm.position, wrist), Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            Vector3 bend = Vector3.ProjectOnPlane(hint - arm.position, direction).normalized;
            Vector3 targetElbow = arm.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            arm.rotation = Quaternion.FromToRotation(elbow.position - arm.position, targetElbow - arm.position) * arm.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, wrist - elbow.position) * elbow.rotation;
        }
    }
}
