# 📖 Reference: Combat & Stat Formulae (v1.0)
이 문서는 프로젝트의 모든 수치 연산의 절대적 기준입니다. 모든 코드는 이 공식을 최우선으로 따릅니다.

## 1. 스탯 체계 (Stat System)
- **PureTotal (판정용)**: `Base(5) + Invested(Max 30)` -> 아이템 수치 배제
- **FinalTotal (계산용)**: `Base(5) + Invested + Item(Max 10)`

## 2. 아이덴티티 판정 (Identity Logic)
- **우선순위**: Monostat > Polymath > Strategist
- **Monostat**: 특정 스탯의 `Invested == 30`
- **Polymath**: `(Max PureTotal - Min PureTotal) <= 7`
- **Strategist**: 위 조건 미충족 시. (3개 이상 중복 시 우선순위: STR > CON > AGI > DEF)

## 3. 데미지 및 방어 공식 (Combat Logic)
- **최종 데미지 공식**: 
  `FinalDamage = 공격력 * (1 - (방어율 * (1 - 관통력/100) / 100))`
- **방어 효율 승산 중첩**: 
  `FinalDEF_Eff = 1 - (1 - CurrentDEF) * (1 - BonusEff)`
- **방어 상한선(Hard Cap)**: `0.75 (75%)`

## 4. 가시 메커니즘 (Thorns)
- **반사 데미지**: `공격자 ATK * 0.15` (고정 피해)
- **데미지 상한**: `나의 MaxHP * 0.07`
- **제약**: 가시 데미지는 재반사 불가 (Recursion 무력화)

## 5. 전략가 스왑 및 오버플로우 (Strategist Swap)
- **HP 고정 이전**: 스왑 직후 `CurrentHP` 수치 보존.
- **Glitch Overflow**: `CurrentHP > NewMaxHP`인 경우, 초당 `NewMaxHP * 0.10`만큼 HP 감소.
- **무방비 패널티**: 1.5초간 `IncomingDamage * 1.2` (예시 수치) 적용.

