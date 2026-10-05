# Battle PVP 스킬 데이터 관리

2026-10-04. SimpleGame의 `GameData_StructureGuide.md`, `GameDataExcelImporter.cs`, `OpenXmlWorkbookReader.cs`, `GameStringTable.cs`를 참고해 적용했다. SimpleGame 파일은 수정하지 않았다.

## 파일과 편집 위치

| 원본 | 시트 | 내용 |
| --- | --- | --- |
| GameData_Skill.xlsx | SkillDefinition | 고정 SkillId/Kind, 이름·설명 String 키, 설명 인자, 시전·지속·쿨타임 |
| GameData_Skill.xlsx | SkillParameter | 해당 스킬에서 실제 사용하는 수치. 거리·배율·충전·주사위 결과 등 |
| GameData_Character.xlsx | JobDefinition | 6개 직업의 String 키와 장착 개수 |
| GameData_Character.xlsx | JobSkillPool | 직업별 장착 후보, 표시 순서, 기본 슬롯 |
| GameData_String.xlsx | SkillString | 스킬 이름·설명의 한국어/영어 원문 |
| GameData_String.xlsx | UiString | 직업 이름·조건·설명, 선택 화면, 주사위 결과 안내 |

각 시트의 **1행은 필드명, 2행은 타입, 3행은 All 범위, 4행은 설명, 5행부터 실제 데이터**다. 식별자는 기존 저장값과 네트워크 enum에 연결되므로 변경하지 않는다. `ExportType=NONE`인 기존 팔방미인 프리셋은 호환용 보관 행이며 장착 대상이 아니다.

현재 활성 스킬은 기존 8개 + 신규 13개 = 21개다. 돌진과 덫은 여러 직업이 동일한 정의를 공유한다. 직업별 후보 행은 총 23개다. 민첩 특화는 3개, 다른 5개 직업은 각각 4개의 후보 중 서로 다른 2개를 장착한다.

## 수치 수정과 반영

1. Excel에서 원본을 수정하고 저장한다.
2. Unity Play Mode를 종료한다.
3. **Battle PvP > Data > Import Skill Workbooks**를 실행한다.
4. 오류가 없으면 `Assets/Resources/SkillGameData.asset`에 반영된다. 이후 실행하거나 플레이어를 다시 빌드한다.

실행 중에는 XLSX를 읽지 않고 생성된 에셋을 사용한다. 생성된 에셋을 수동으로 수정하면 다음 가져오기에서 덮어써진다. 원본에 오류가 있으면 기존 생성 에셋을 유지한다.

- 초 단위: `CastSeconds`, `DurationSeconds`, `CooldownSeconds`, `...Seconds`.
- 유닛 단위: `Range`, `PlaceDistance`, `BreakDistance`, `...Distance`.
- 비율: `0.3`은 최대 체력의 30%. 배율: `1.2`는 기존 값의 120%.
- 나이프는 `CooldownSeconds=0`이고 `RechargeSeconds=8`, `MaxCharges=3`을 사용한다.
- 광전사·은신은 종료 조건이 별도로 있으므로 `DurationSeconds=0`이다.
- `TargetPresetSTR/CON/AGI/DEF`가 모두 0이면 기존 기본 프리셋 규칙을 사용한다. 직접 지정하면 합계 30이어야 한다. 사용자가 저장한 프리셋은 이 기본값보다 우선한다.
- `InputLockSeconds=0`은 기존 스킬별 기본 잠금 시간을 사용한다.
- `DefaultSlot`은 0 또는 1이며, 기본 미장착 후보는 -1이다. 같은 직업에서 각 슬롯은 정확히 한 번 지정한다.
- `SortOrder`는 직업 내부 표시 순서다. 현재 패널은 직업당 최대 4개의 후보를 표시한다.
- `DescriptionArgs`는 설명의 `{0}`, `{1}` 순서에 대응하는 수치 키 목록이다. `FormatArgCount`와 일치해야 한다. 숫자를 설명에 직접 중복 입력하지 않는다.

검증은 중복 ID·enum, 참조가 없는 String/스킬, 누락된 인자·필수 수치, 비정상 숫자, 직업 기본 슬롯, 체력 비율, 충전 정수, 주사위 상승/하락 부호 등을 검사한다.

## Unity 리소스와의 분리

아이콘·모션·효과음·프리팹·폰트는 `Assets/Resources/SkillPresentationCatalog.asset`와 연결된 `JobSkillData`에서 관리한다. `Uses Game Data`가 켜진 스킬의 게임 수치는 Excel을 우선한다. ScriptableObject의 기존 수치는 데이터가 없는 경우의 호환 기본값이다.

`Battle PvP > Skills > Install Expanded Skills`는 최초 설치 및 리소스 재생성 메뉴다. 아이콘·파생 모션·소품·선택 UI를 재생성하므로, 리소스를 직접 다듬은 후 일반 수치 수정에 이 메뉴를 사용하지 않는다. 시전 길이를 바꾼 경우 필요에 따라 연결된 모션의 길이도 조정한다.

`Tools/SkillExpansion/seed_data.py`, `build_workbooks.mjs`는 이번 최초 이전을 재현하기 위한 도구다. **운영 중에는 Excel이 원본이다.** 이 도구를 다시 실행하면 수동으로 수정한 Excel을 초기 데이터로 덮어쓰므로 일반 밸런스 작업에 사용하지 않는다.

## 저장 및 네트워크

장착 구성은 직업 6개 × 슬롯 2개의 배열로 계정별 PlayerPrefs에 저장한다. 클라우드 동기화는 추가하지 않았다. 서버는 후보 직업·중복·슬롯 개수·변경 가능 시점을 검사하며, 확정된 장착값과 스킬 상태만 동기화한다. 전투 중에는 변경할 수 없고, 최초 스폰의 설정 요청은 제한 시간 내 한 번만 허용한다. 스킬을 교체해도 기존 재사용 대기나 나이프 충전 시간이 초기화되지 않는다.

기존 CloudScript 변경이나 재배포는 이 기능에 필요하지 않다. 서로 다른 버전의 플레이어가 같은 방에 참가하는 테스트는 피하고 호스트와 클라이언트를 동일한 새 빌드로 검증한다.
