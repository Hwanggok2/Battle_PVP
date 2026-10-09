using UnityEngine;

namespace BattlePvp.Combat
{
    public enum MeleeWeaponKind { Sword, SwordShield, Greatsword, Axe }

    [CreateAssetMenu(menuName = "Battle PvP/Weapons/Catalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        [System.Serializable] public sealed class Entry
        {
            public MeleeWeaponKind Kind;
            public string Name;
            [TextArea] public string Description;
            public Mesh Mesh;
            public Material[] Materials;
            public Vector3 HitCenter, HitSize, BladeBase, BladeTip;
            public AttackData[] Attacks;
            [Min(0f)] public float MeleeDamageMultiplier = 1f;
            public string ReadyState;
            public Vector3 RightGrip, LeftGrip;
            public bool TwoHanded => Kind == MeleeWeaponKind.Greatsword || Kind == MeleeWeaponKind.Axe;
        }
        public Entry[] Weapons;
        public Mesh ShieldMesh;
        public Material[] ShieldMaterials;
        public float ShieldRecoilSeconds = .975f;
        public float ParryRecoilSeconds = .65f;
        public float ParryWindowSeconds = .3f;
        public float RiposteReadySeconds = 2.5f;
        public float GuardArcDegrees = 120f;
        private static WeaponCatalog _instance;
        public static WeaponCatalog Instance => _instance != null ? _instance : _instance = Resources.Load<WeaponCatalog>("WeaponCatalog");
        public Entry Find(MeleeWeaponKind kind) => Weapons == null ? null : System.Array.Find(Weapons, w => w.Kind == kind);
    }
}
