using UnityEngine;

namespace BattlePvp.Combat
{
    public enum BodyPart
    {
        Body = 0,
        Head = 1,
        Legs = 2
    }

    [DisallowMultipleComponent]
    public sealed class HitBodyPart : MonoBehaviour
    {
        [SerializeField] private BodyPart _bodyPart = BodyPart.Body;
        [SerializeField] private float _headDamageMultiplier = 1.5f;
        [SerializeField] private float _bodyDamageMultiplier = 1f;
        [SerializeField] private float _legsDamageMultiplier = 0.8f;

        public BodyPart Part => _bodyPart;

        public float DamageMultiplier
        {
            get
            {
                switch (_bodyPart)
                {
                    case BodyPart.Head:
                        return Mathf.Max(0f, _headDamageMultiplier);
                    case BodyPart.Legs:
                        return Mathf.Max(0f, _legsDamageMultiplier);
                    default:
                        return Mathf.Max(0f, _bodyDamageMultiplier);
                }
            }
        }
    }
}
