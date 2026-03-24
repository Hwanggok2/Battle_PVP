using System;

namespace BattlePvp.Stats
{
    /// <summary>
    /// 개별 스탯에 대해 "Invested(투자)"와 "Item(아이템)"을 분리해 저장한다.
    /// </summary>
    [Serializable]
    public struct StatSlot
    {
        // 0..30 범위를 권장 (Identity 판정에 사용)
        public float Invested;

        // 0..10 범위를 권장 (FinalTotal 계산에 사용)
        public float Item;
    }
}

