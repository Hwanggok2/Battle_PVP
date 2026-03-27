using UnityEngine;
using BattlePvp.Stats;

namespace BattlePvp.Combat
{
    [CreateAssetMenu(fileName = "NewDummyStatData", menuName = "Combat/DummyStatData")]
    public class DummyStatData : ScriptableObject
    {
        [Header("Base Stats (Invested)")]
        public float STR = 10f;
        public float CON = 10f;
        public float AGI = 10f;
        public float DEF = 10f;
    }
}
