using BattlePvp.Stats;

namespace BattlePvp.UI
{
    public static class JobGuideContent
    {
        public const int Count = 6;
        public static Identity IdentityAt(int index) => index switch
        {
            0 => new Identity(IdentityType.Monostat, StatKind.STR),
            1 => new Identity(IdentityType.Monostat, StatKind.CON),
            2 => new Identity(IdentityType.Monostat, StatKind.AGI),
            3 => new Identity(IdentityType.Monostat, StatKind.DEF),
            4 => new Identity(IdentityType.Strategist, StatKind.STR),
            _ => new Identity(IdentityType.Polymath, StatKind.STR)
        };
        public static int IndexOf(Identity identity) => identity.Type == IdentityType.Strategist ? 4 :
            identity.Type == IdentityType.Polymath ? 5 : identity.PrimaryStat switch
            { StatKind.STR => 0, StatKind.CON => 1, StatKind.AGI => 2, _ => 3 };
        public static string Name(int index) => index switch
        {
            0 => "힘 특화 · STR", 1 => "체력 특화 · CON", 2 => "민첩 특화 · AGI",
            3 => "방어 특화 · DEF", 4 => "전략가", _ => "팔방미인"
        };
        public static string Requirement(int index) => index < 4
            ? $"{IdentityAt(index).PrimaryStat}에 30 포인트 투자"
            : index == 4 ? "한 능력치에 집중하되 30 포인트 몰아주기는 피하기\n가장 높은 투자값과 낮은 투자값의 차이: 8 이상"
            : "능력치를 고르게 배분하기\n가장 높은 투자값과 낮은 투자값의 차이: 7 이하";
        public static string Description(int index) => index switch
        {
            0 => "강한 한 방과 흡혈로 정면 승부를 걸어보세요. 광폭이 지속되는 동안 검을 맞힐수록 체력을 회복합니다.",
            1 => "높은 체력으로 버티고 발차기로 거리를 벌리세요. 밀려난 적을 추격하거나 위험한 근접전에서 빠져나올 수 있습니다.",
            2 => "빠른 연속 공격에 독을 쌓아 압박하세요. 적에게 여러 번 닿을수록 지속 피해를 키울 수 있습니다.",
            3 => "도발을 묻힌 검으로 적의 움직임을 묶으세요. 적을 끌어들이고 받는 피해를 줄이며 공격을 되돌려줍니다.",
            4 => "대시로 위치를 바꾸고 준비한 능력치 배분으로 전환하세요. 전환 후 주 능력치에 맞는 추가 효과로 전투 흐름을 바꿉니다.",
            _ => "검과 활을 오가며 거리에 맞춰 싸우세요. 구르기로 자리를 잡고 활을 충전해 멀리 있는 적을 노릴 수 있습니다."
        };
    }
}
