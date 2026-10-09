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
        private WeaponParrySurface _parrySurface;
        private double _initialDeadline, _nextRequest;
        private bool _initialized;
        private bool _visualInitialized;
        private MeleeWeaponKind _materialWeapon = (MeleeWeaponKind)(-1);
        private Transform _materialPose;
        private Vector3 _originalBladePosition;
        private Quaternion _originalBladeRotation;
        public MeleeWeaponKind Selected => _selected;
        public event Action Changed;
        public static bool CanEdit => LoadoutEditRules.CanEditWeapon(BattlePvp.Stats.StatManager.Local?.gameObject);
        private static string SaveKey => "BattlePvp.Weapon.v1." + (PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
        private static MeleeWeaponKind Saved => (MeleeWeaponKind)Mathf.Clamp(PlayerPrefs.GetInt(SaveKey, 0), 0, 3);
        private void Awake()
        {
            _combat = GetComponent<PlayerCombat>(); _animator = GetComponent<Animator>();
            _blade = GetComponentInChildren<MeleeHitBox>(true);
            if (_blade != null)
            {
                _swordRenderer = _blade.GetComponent<MeshRenderer>();
                _originalBladePosition = _blade.transform.localPosition; _originalBladeRotation = _blade.transform.localRotation;
            }
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
            if ((!first && !LoadoutEditRules.CanEditWeapon(gameObject)) || NetworkTime.time < _nextRequest ||
                WeaponCatalog.Instance?.Find(kind) == null) return false;
            if (!first && GetComponent<HealthSystem>() is not { IsDead: true } && _combat != null &&
                (_combat.IsBusyForEmote || _combat.IsWeaponGuarding || _combat.IsWeaponRecoiling)) return false;
            _nextRequest = NetworkTime.time + .2; _initialized = true; Apply(kind); return true;
        }
        [TargetRpc] private void TargetAccepted(NetworkConnectionToClient target, MeleeWeaponKind kind) { Save(kind); Changed?.Invoke(); }
        private static void Save(MeleeWeaponKind kind) { PlayerPrefs.SetInt(SaveKey, (int)kind); PlayerPrefs.Save(); }
        private void OnSelected(MeleeWeaponKind previous, MeleeWeaponKind next) => ApplySelection(next, _visualInitialized);
        internal void Apply(MeleeWeaponKind kind) => ApplySelection(kind, true);
        private void ApplySelection(MeleeWeaponKind kind, bool resetCombat)
        {
            var entry = WeaponCatalog.Instance?.Find(kind); if (entry == null) return;
            if (_blade != null) { _blade.transform.localPosition = _originalBladePosition; _blade.transform.localRotation = _originalBladeRotation; }
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
                if (_selected == MeleeWeaponKind.Axe)
                {
                    // Keep the sword's relaxed arm pose, but roll the axe around its
                    // +Z haft so its -X cutting edge faces down. Reset before fitting
                    // every frame; native hitbox sync carries the previous swing pose.
                    _blade.transform.localRotation = _originalBladeRotation * Quaternion.AngleAxis(180f, Vector3.forward);
                    Quaternion rotation = _blade.transform.rotation;
                    if (rig != _animator)
                        rotation = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.RightHand, out _) *
                            Quaternion.Inverse(CharacterEquipmentVisual.HandFrame(_animator, HumanBodyBones.RightHand, out _)) * rotation;
                    source.rotation = rotation;
                }
                if (_combat != null && _combat.UsesTwoHandedGrip)
                {
                    FitTwoHandedGrip(rig, source, entry, _animator);
                    CharacterPoseFollower.SyncHitboxes(_animator);
                }
                else
                {
                    if (_selected != MeleeWeaponKind.Axe && source == _blade.transform) source.localRotation = _originalBladeRotation;
                    FitBladeToPalm(rig, source, entry.RightGrip);
                }
            }
            if (_selected == MeleeWeaponKind.Sword && _combat != null && _combat.MeleeEquipped)
            {
                float support = ThrustSupportWeight(_animator);
                if (support > 0f)
                {
                    FitThrustSupportGrip(CharacterPoseFollower.GetViewAnimator(_animator), source, support);
                    CharacterPoseFollower.SyncHitboxes(_animator);
                }
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
            if (_selected == MeleeWeaponKind.Greatsword && _parrySurface == null && _combat != null)
            {
                var surface = new GameObject("Greatsword parry blade");
                surface.layer = source.gameObject.layer;
                surface.transform.SetParent(transform, false);
                _parrySurface = surface.AddComponent<WeaponParrySurface>();
                _parrySurface.Initialize(_combat);
            }
            if (_parrySurface != null)
                _parrySurface.Sync(source, entry, _selected == MeleeWeaponKind.Greatsword &&
                    _combat != null && _combat.IsWeaponGuarding && _combat.MeleeEquipped);
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
            Quaternion shieldRotation = frame * Quaternion.Euler(0, 180, 0);
            // The mesh faces +X. Its front must be beyond the fist, with the hand behind the panel.
            _shield.transform.SetPositionAndRotation(GripCenter(visible, true) +
                shieldRotation * Vector3.right * .07f * transform.lossyScale.y, shieldRotation);
            _shield.transform.localScale = Vector3.one;
            if (renderer != null) { _shield.enabled = renderer.enabled; _shield.forceRenderingOff = renderer.forceRenderingOff; }
        }

        public static void FitBladeToPalm(Animator rig, Transform weapon, Vector3 grip)
        {
            weapon.position = CharacterEquipmentVisual.PalmCenter(rig, HumanBodyBones.RightHand) -
                weapon.rotation * Vector3.Scale(grip, weapon.lossyScale);
        }

        public static void FitTwoHandedGrip(Animator rig, Transform weapon, WeaponCatalog.Entry entry, Animator poseSource = null)
        {
            if (entry.Kind == MeleeWeaponKind.Axe)
            {
                FitAxeGrip(rig, poseSource != null ? poseSource : rig, weapon, entry);
                return;
            }
            // The imported two-handed motion owns BOTH arms. Fit the prop to the
            // authored fists instead of pulling an elbow/shoulder onto a fixed socket.
            Vector3 right = GripCenter(rig, false), left = GripCenter(rig, true);
            Quaternion frame = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.RightHand, out _);
            var source = poseSource != null ? poseSource : rig;
            bool guard = source.GetCurrentAnimatorStateInfo(1).IsName("Weapon_SwordGuard") ||
                (source.IsInTransition(1) && source.GetNextAnimatorStateInfo(1).IsName("Weapon_SwordGuard"));
            Vector3 shaft = guard ? source.transform.right : right - left;
            float wristWeight = !guard && rig != source && CharacterPoseFollower.HasWristDrivenFinisher(source)
                ? FinisherWristWeight(source) : 0f;
            if (wristWeight > 0f)
            {
                // The native fists nearly cross on some avatars. Their tiny separation
                // is not a reliable blade axis. Transfer the source sweep through the
                // right wrist instead; it also carries the native torso's aim correction.
                Quaternion sourceFrame = CharacterEquipmentVisual.HandFrame(source, HumanBodyBones.RightHand, out _);
                Vector3 sourceShaft = GripCenter(source, false) - GripCenter(source, true);
                Vector3 wristShaft = frame * Quaternion.Inverse(sourceFrame) * sourceShaft;
                shaft = Vector3.Slerp(shaft.normalized, wristShaft.normalized, wristWeight);
                float scale = rig.humanScale * rig.transform.lossyScale.y / (source.humanScale * source.transform.lossyScale.y);
                float gap = Mathf.Clamp(sourceShaft.magnitude * scale, .10f * weapon.lossyScale.y, .24f * weapon.lossyScale.y);
                Quaternion leftFrame = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.LeftHand, out _);
                var hand = rig.GetBoneTransform(HumanBodyBones.LeftHand);
                Quaternion supportRotation = Quaternion.FromToRotation(leftFrame * Vector3.up, shaft) * hand.rotation;
                PlaceFist(rig, true, Vector3.Lerp(left, right - shaft.normalized * gap, wristWeight),
                    Quaternion.Slerp(hand.rotation, supportRotation, wristWeight), true);
            }
            if (shaft.sqrMagnitude < .0001f) shaft = frame * Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(shaft, frame * Vector3.forward);
            if (guard)
            {
                // The aimed torso already carries the authored guard and both arms.
                // A world-space eye-height target pulls the wrists behind the shoulders
                // when looking down. Only reconcile the small retargeted grip error.
                Vector3 center = (right + left) * .5f;
                Vector3 half = rotation * Vector3.forward * Mathf.Clamp(Vector3.Distance(right,left),.12f,.26f) * .5f;
                Quaternion rightRotation = Quaternion.FromToRotation(frame * Vector3.up, shaft) * rig.GetBoneTransform(HumanBodyBones.RightHand).rotation;
                Quaternion leftFrame = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.LeftHand, out _);
                Quaternion leftRotation = Quaternion.FromToRotation(leftFrame * Vector3.up, shaft) * rig.GetBoneTransform(HumanBodyBones.LeftHand).rotation;
                PlaceFist(rig, true, center - half, leftRotation, true);
                PlaceFist(rig, false, center + half, rightRotation, true);
                right = GripCenter(rig, false);
            }
            weapon.SetPositionAndRotation(right - rotation * Vector3.Scale(entry.RightGrip, weapon.lossyScale), rotation);
        }

        private static float ThrustSupportWeight(Animator animator)
        {
            static float Weight(AnimatorStateInfo state) => state.IsName("Weapon_Thrust") ? 1f : 0f;
            float weight = Weight(animator.GetCurrentAnimatorStateInfo(1));
            return animator.IsInTransition(1) ? Mathf.Lerp(weight, Weight(animator.GetNextAnimatorStateInfo(1)),
                animator.GetAnimatorTransitionInfo(1).normalizedTime) : weight;
        }

        public static void FitThrustSupportGrip(Animator rig, Transform weapon, float weight)
        {
            // The existing dominant arm and sword own the thrust. Cup the pommel
            // just behind that fist; never move the blade to meet the support hand.
            var arm = rig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var elbow = rig.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var hand = rig.GetBoneTransform(HumanBodyBones.LeftHand);
            Quaternion frame = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.LeftHand, out _);
            Vector3 right = GripCenter(rig, false);
            CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.RightHand, out float size);
            Vector3 support = right - weapon.forward * Mathf.Clamp(size * .7f, .035f, .07f);
            Vector3 fingers = Vector3.ProjectOnPlane(rig.transform.right, weapon.forward).normalized;
            if (fingers.sqrMagnitude < .01f) fingers = Vector3.ProjectOnPlane(rig.transform.up, weapon.forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(fingers, weapon.forward) * Quaternion.Inverse(frame) * hand.rotation;
            Vector3 palm = Vector3.Lerp(GripCenter(rig, true), support, weight);
            rotation = Quaternion.Slerp(hand.rotation, rotation, weight);
            hand.rotation = rotation;
            Vector3 wrist = palm - (GripCenter(rig, true) - hand.position);
            float reach = Vector3.Distance(arm.position, elbow.position) + Vector3.Distance(elbow.position, hand.position);
            var clavicle = rig.GetBoneTransform(HumanBodyBones.LeftShoulder);
            if (clavicle != null)
            {
                // Protract only as much as needed, within the chest-relative shoulder
                // range. This adds reach without stretching either arm segment.
                Vector3 toward = wrist - clavicle.position, link = arm.position - clavicle.position;
                float distance = toward.magnitude, length = link.magnitude;
                float cosine = (distance * distance + length * length - (reach - .008f) * (reach - .008f)) /
                    Mathf.Max(.0001f, 2f * distance * length);
                float allowed = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
                float turn = Mathf.Max(0f, Vector3.Angle(link, toward) - allowed);
                Vector3 direction = Vector3.RotateTowards(link, toward, turn * Mathf.Deg2Rad * weight, 0f);
                var opposite = rig.GetBoneTransform(HumanBodyBones.RightShoulder);
                Vector3 lateral = opposite != null ? clavicle.position - opposite.position : -rig.transform.right;
                direction = Vector3.RotateTowards(lateral.normalized, direction.normalized, 110f * Mathf.Deg2Rad, 0f);
                clavicle.rotation = Quaternion.FromToRotation(link, direction) * clavicle.rotation;
            }
            Vector3 hint = arm.position + (rig.transform.TransformDirection(new Vector3(-.6f, -.8f, .1f))) * reach;
            SolveArm(arm, elbow, hand, wrist, hint, 8f, 145f);
            hand.rotation = rotation;
        }

        private static float FinisherWristWeight(Animator source)
        {
            static float Weight(AnimatorStateInfo state) => state.IsName("Weapon_Greatsword3")
                ? Mathf.SmoothStep(0f, 1f, state.normalizedTime / .12f) *
                  (1f - Mathf.SmoothStep(0f, 1f, (state.normalizedTime - .6f) / .25f)) : 0f;
            float weight = Weight(source.GetCurrentAnimatorStateInfo(1));
            return source.IsInTransition(1) ? Mathf.Lerp(weight, Weight(source.GetNextAnimatorStateInfo(1)),
                source.GetAnimatorTransitionInfo(1).normalizedTime) : weight;
        }

        private static void FitAxeGrip(Animator rig, Animator source, Transform weapon, WeaponCatalog.Entry entry)
        {
            // The right wrist owns the authored swing, including its release.
            // A line between retargeted fists can reverse on short-armed avatars.
            Quaternion frame = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.RightHand, out _);
            float attack = source.GetCurrentAnimatorStateInfo(1).IsName("Weapon_AxeChop") ? 1 : 0;
            if (source.IsInTransition(1))
                attack = Mathf.Lerp(attack, source.GetNextAnimatorStateInfo(1).IsName("Weapon_AxeChop") ? 1 : 0,
                    Mathf.Clamp01(source.GetAnimatorTransitionInfo(1).normalizedTime));
            Quaternion attackRotation = Quaternion.LookRotation(frame * Vector3.up, frame * Vector3.forward) *
                Quaternion.AngleAxis(-90f, Vector3.forward);
            Quaternion rotation = Quaternion.Slerp(weapon.rotation, attackRotation, attack);
            Vector3 shaft = rotation * Vector3.forward;
            Vector3 right = Vector3.Lerp(CharacterEquipmentVisual.PalmCenter(rig, HumanBodyBones.RightHand), GripCenter(rig, false), attack);
            Vector3 left = GripCenter(rig, true);
            float scale = Mathf.Abs(weapon.lossyScale.z);
            Vector3 grip = entry.RightGrip;
            float gap = Mathf.Clamp(Vector3.Dot(right - left, shaft), .10f * scale,
                (grip.z - entry.Mesh.bounds.min.z - .06f) * scale);
            var leftHand = rig.GetBoneTransform(HumanBodyBones.LeftHand);
            Quaternion leftFrame = CharacterEquipmentVisual.HandFrame(rig, HumanBodyBones.LeftHand, out _);
            Quaternion leftRotation = Quaternion.FromToRotation(leftFrame * Vector3.up, shaft) * leftHand.rotation;
            var arm = rig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var elbow = rig.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Vector3 palmOffset = leftRotation * Quaternion.Inverse(leftHand.rotation) * (left - leftHand.position);
            Vector3 origin = right - palmOffset - arm.position;
            float reach = Vector3.Distance(arm.position, elbow.position) + Vector3.Distance(elbow.position, leftHand.position) - .004f;
            float center = Vector3.Dot(origin, shaft);
            float squared = reach * reach - (origin.sqrMagnitude - center * center);
            if (squared >= 0)
            {
                // Slide the support hand along the actual haft rather than fully
                // straightening a short arm at the shoulder wind-up.
                float radius = Mathf.Sqrt(squared);
                float min = Mathf.Max(.10f * scale, center - radius);
                float max = Mathf.Min((grip.z - entry.Mesh.bounds.min.z - .06f) * scale, center + radius);
                if (min <= max) gap = Mathf.Clamp(gap, min, max);
            }
            PlaceFist(rig, true, Vector3.Lerp(left, right - shaft * gap, attack),
                Quaternion.Slerp(leftHand.rotation, leftRotation, attack));
            weapon.SetPositionAndRotation(right - rotation * Vector3.Scale(grip, weapon.lossyScale), rotation);
        }

        private static void PlaceFist(Animator rig, bool left, Vector3 palm, Quaternion rotation, bool guard = false)
        {
            var arm = rig.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var elbow = rig.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = rig.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Quaternion authoredWrist = hand.localRotation;
            Vector3 originalDirection = hand.position - arm.position;
            Vector3 originalElbow = elbow.position - arm.position;
            hand.rotation = rotation;
            Vector3 wrist = palm - (GripCenter(rig, left) - hand.position);
            Vector3 hint = arm.position + Quaternion.FromToRotation(originalDirection, wrist - arm.position) * originalElbow;
            SolveArm(arm, elbow, hand, wrist, hint, guard ? 10f : 0f, guard ? 145f : 180f);
            hand.rotation = rotation;
            if (guard) hand.localRotation = Quaternion.RotateTowards(authoredWrist, hand.localRotation, 40f);
        }

        public static Vector3 SupportGrip(Animator rig, Animator source, WeaponCatalog.Entry entry, Transform weapon)
        {
            // Proportion-dependent spacing from the actual imported fists.
            return new Vector3(0, 0, weapon.InverseTransformPoint(GripCenter(rig, true)).z);
        }

        public static Vector3 GripCenter(Animator rig, bool left)
        {
            var knuckle = rig.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var tip = rig.GetBoneTransform(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
            return knuckle != null && tip != null ? (knuckle.position + tip.position) * .5f :
                CharacterEquipmentVisual.PalmCenter(rig, left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        }
        public static void SolveArm(Transform arm, Transform elbow, Transform hand, Vector3 wrist, Vector3 hint,
            float minFlexion = 0f, float maxFlexion = 180f)
        {
            float upper = Vector3.Distance(arm.position, elbow.position), lower = Vector3.Distance(elbow.position, hand.position);
            Vector3 direction = (wrist - arm.position).normalized;
            float minReach = Mathf.Sqrt(Mathf.Max(0, upper * upper + lower * lower + 2 * upper * lower * Mathf.Cos(maxFlexion * Mathf.Deg2Rad)));
            float maxReach = Mathf.Sqrt(Mathf.Max(0, upper * upper + lower * lower + 2 * upper * lower * Mathf.Cos(minFlexion * Mathf.Deg2Rad)));
            float distance = Mathf.Clamp(Vector3.Distance(arm.position, wrist), minReach + .001f, maxReach - .001f);
            // Both segments must solve toward the same reachable endpoint.
            wrist = arm.position + direction * distance;
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            Vector3 bend = Vector3.ProjectOnPlane(hint - arm.position, direction).normalized;
            if (bend.sqrMagnitude < .01f)
                bend = Vector3.ProjectOnPlane(elbow.position - arm.position, direction).normalized;
            if (bend.sqrMagnitude < .01f)
                bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < .9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 targetElbow = arm.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            arm.rotation = Quaternion.FromToRotation(elbow.position - arm.position, targetElbow - arm.position) * arm.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, wrist - elbow.position) * elbow.rotation;
        }
    }
}
