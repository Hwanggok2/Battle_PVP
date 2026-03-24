using BattlePvp.Stats;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>
    /// Identity 미리보기용 스프라이트 매핑 데이터.
    /// </summary>
    [CreateAssetMenu(fileName = "IdentitySpriteSet", menuName = "BattlePVP/UI/IdentitySpriteSet")]
    public sealed class IdentitySpriteSet : ScriptableObject
    {
        [Header("By Type")]
        [SerializeField] private Sprite _polymath;
        [SerializeField] private Sprite _strategist;

        [Header("Monostat By Primary Stat")]
        [SerializeField] private Sprite _monoStr;
        [SerializeField] private Sprite _monoAgi;
        [SerializeField] private Sprite _monoCon;
        [SerializeField] private Sprite _monoDef;

        public Sprite Resolve(in Identity identity)
        {
            if (identity.Type == IdentityType.Monostat)
            {
                return identity.PrimaryStat switch
                {
                    StatKind.STR => _monoStr,
                    StatKind.AGI => _monoAgi,
                    StatKind.CON => _monoCon,
                    _ => _monoDef,
                };
            }

            return identity.Type == IdentityType.Polymath ? _polymath : _strategist;
        }
    }
}

