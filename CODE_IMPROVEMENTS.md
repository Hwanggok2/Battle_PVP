# 코드 개선 기록

작성일: 2026-09-06

## 작업 기준

- 앞선 두 차례 리뷰의 문제를 아래 목록으로 통합한다. 보안·데이터 손실·동기화 오류부터 수정한다.
- 기존 미커밋 변경(카메라, 전투, 이동, 스탯, 방 목록, Player 프리팹)을 보존한다.
- 완료 판정은 [COMPLETION_CRITERIA.md](COMPLETION_CRITERIA.md)를 따른다. 코드 반영만으로 완료 처리하지 않으며 필요한 컴파일/회귀/다중 클라이언트/성능 검증을 통과해야 한다.
- PlayFab 서버 설정과 배포된 CloudScript처럼 저장소 밖의 작업은 별도 후속 사항으로 남긴다. 로컬 수정으로 서버 운영 보안이 완성되었다고 간주하지 않는다.
- 사용자가 MCP 연결 없이 가능한 수정을 계속 승인하여 2–4단계의 독립적인 코드 수정과 로컬 검사를 진행했다. 파일 소유권과 공유 API를 정해 통합했으며, 이 승인은 앞 단계의 Unity 실행 검증이나 후속 성능 측정을 통과한 것으로 처리할 근거가 아니다.

## 우선순위 및 진행표

2026-09-06 사용자 범위 승인 후 1단계 구현을 재개했고, MCP 없이 가능한 후속 수정/R01 책임 분리, S11 방 권한 보강, 성능 수집/분석 준비를 반영했다. 2026-09-07에는 PlayFab–Mirror 참가자 인증과 계정별 경기 기록/이탈·재접속 보존을 추가했다. S01–S10, C01–C10, O01–O10, R01–R04는 `구현 완료·검증 대기`다. 실제 Unity/Mirror·물리·다중 클라이언트·성능 검증을 통과한 것으로 집계하지 않는다. S11은 로컬 인증/기록까지 반영했지만 실제 인증/권한 검증과 서버 경기 등록·결과 제출·원자적 보상 백엔드가 남아 `외부 구현·검증 대기`다. 현재 전체 합격 조건을 충족한 항목은 없다.

확정된 범위: 최대 8명(호스트 포함), 신뢰하는 호스트에서 참가 클라이언트 조작 방지, Windows PC와 WebGL 모두 60 FPS. 구체적인 단계별 합격 조건은 COMPLETION_CRITERIA.md에 기록했다.

| ID | 우선순위 | 개선 항목 | 상태 |
|---|---|---|---|
| S01 | P0 | 임의 점수 Command 제거, 서버 처치 경로로 제한 | 구현 완료·검증 대기 |
| S02 | P0 | 스탯 예산·유한값·아이템·변경 가능 상태 검증 | 구현 완료·검증 대기 |
| S03 | P0 | 활 차징·발사 간격·피해 배율·장착/생존 서버 검증 | 구현 완료·검증 대기 |
| S04 | P0 | 발차기 서버 타격 창·범위·시퀀스 검증 | 구현 완료·검증 대기 |
| S05 | P0 | 근접 공격 방향·장애물·신체 부위 검증 | 구현 완료·검증 대기 |
| S06 | P0 | 이동 좌표·시간·속도 검증, 스킬/부활 이동 승인 | 구현 완료·검증 대기 |
| S07 | P0 | 부활 요청 시점 검증, 관찰자 생존 상태 동기화 | 구현 완료·검증 대기 |
| S08 | P0 | Relay 분할 용량·최대 메시지·배치 임계값 일치 | 구현 완료·검증 대기 |
| S09 | P0 | 클라우드 로드 성공/없음/실패 구별 및 기존 데이터 보존 | 구현 완료·검증 대기 |
| S10 | P0 | 프로필 저장 스냅샷·완료 결과·요청 직렬화 | 구현 완료·검증 대기 |
| S11 | P0 | 경쟁 기록의 서버 검증·경기 ID 중복 방지 | 외부 구현·검증 대기 |
| C01 | P1 | 미리보기와 실제 스탯 계산 통합 | 구현 완료·검증 대기 |
| C02 | P1 | 원격 스탯의 로컬 카메라 변경 차단 | 구현 완료·검증 대기 |
| C03 | P1 | 방 목록 요청 역전·비활성 UI 응답·중복 항목 | 구현 완료·검증 대기 |
| C04 | P1 | 공동 우승과 보상 순위 통일 | 구현 완료·검증 대기 |
| C05 | P1 | 이동 버프/감속/입력 잠금의 원인별 소유권 | 구현 완료·검증 대기 |
| C06 | P1 | 피해 적용 결과로 무적·실드·후속 효과 구별 | 구현 완료·검증 대기 |
| C07 | P1 | 사망/무기 전환/거절 시 모든 동작 취소 | 구현 완료·검증 대기 |
| C08 | P1 | 강제 이동과 원격 보간의 중복 위치 작성 방지 | 구현 완료·검증 대기 |
| C09 | P1 | HUD 재활성화 이벤트 구독 복원 | 구현 완료·검증 대기 |
| C10 | P1 | 입력/커서 모드와 ESC 소비의 단일 관리 | 구현 완료·검증 대기 |
| O01 | P2 | 원격 위치 스냅샷 링 버퍼 | 구현 완료·검증 대기 |
| O02 | P2 | 피해 팝업 풀 및 만료 큐 | 구현 완료·검증 대기 |
| O03 | P2 | 충돌 NonAlloc 통일·버퍼 포화 대응·부위 캐시 | 구현 완료·검증 대기 |
| O04 | P2 | 파생 스탯 변경 시 재계산 | 구현 완료·검증 대기 |
| O05 | P2 | 방 ID 기반 데이터/UI 갱신, 기존 순위 목록 재사용 | 구현 완료·검증 대기 |
| O06 | P2 | 매 프레임 채팅 핸들러 재등록 제거 | 구현 완료·검증 대기 |
| O07 | P2 | Relay 반복 할당과 복사 축소 | 구현 완료·검증 대기 |
| O08 | P2 | 스킬 UI 정적 설정과 동적 갱신 분리 | 구현 완료·검증 대기 |
| O09 | P2 | 슬라이더 연출 중첩 방지·초기 크기 보존 | 구현 완료·검증 대기 |
| O10 | P2 | 채팅 레이아웃 갱신 통합·불필요한 Raycaster 제거 | 구현 완료·검증 대기 |
| R01 | P3 | 전투·이동·생명주기 상태의 책임 분리 | 구현 완료·검증 대기 |
| R02 | P3 | 프로필 저장·외부 API·플레이어 연결 책임 분리 | 구현 완료·검증 대기 |
| R03 | P3 | 경기 상태와 결과 화면/입력/스폰 책임 분리 | 구현 완료·검증 대기 |
| R04 | P3 | 스탯 UI와 적용 절차 분리 | 구현 완료·검증 대기 |

## 검증 기록

### 수정 전

- `dotnet build Battle_PVP.sln -m:1 -nodeReuse:false -v:q`: 성공, 오류 0, 기존 경고 11.
- 일반 샌드박스에서는 Windows SDK 검색 경로 접근이 거부되어 승인된 빌드 실행 권한으로 확인했다.
- Unity MCP 연결은 처음 확인 시 `C:/Github/SimpleGame`이었다. 다른 프로젝트에 변경/테스트 명령을 보내지 않았다.
- 이번 작업의 Unity 대상 경로는 반드시 `C:/Github/Battle_PVP`로 검증한다.

## 변경 상세

아래는 단계별 코드 반영 내역이다. 통합 diff에는 작업 전부터 있던 사용자 변경이 포함되어 있으므로 파일 전체 변경량을 이번 구현 실적으로 간주하지 않는다. 특히 기존 카메라/전투/이동/스탯/방 목록/Player 프리팹 변경은 보존했다. 후속 C/O/R 구현의 근거는 아래에 명시한 추가 변경이며, 기존 변경을 사후에 이번 구현으로 재분류하지 않는다.


### 1단계 통합 검사 — 2026-09-06

| 검사 | 실행/결과 | 판정 범위 |
|---|---|---|
| 일반 C# 통합 빌드 | `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets` 전체 소스 재컴파일: 오류 0, 기존 미사용 필드 경고 6개. 테스트 namespace 통일 후 마지막 증분 빌드: 오류 0, 경고 0 | 증분 빌드의 경고 0을 기존 경고 제거로 해석하지 않음. C# 컴파일만 확인하며 Unity/Mirror 코드 생성 및 플레이 검증은 대체하지 않음 |
| 방 정원 모의 검사 | `node Tools/Tests/NetworkProfileRoomCapacity.test.js` 통과. roomRegistry.js/combinedCloudScript.js 각각 순차·중복·9번째 입장 거절·동시 입장 초과 롤백 확인 | 로컬 JavaScript 모의 API 검사. 배포된 CloudScript/실제 동시성 검증 아님 |
| Unity EditMode CLI | Battle_PVP를 대상으로 실행 시도했으나 종료 코드 1, 테스트 결과 파일 없음. 이미 열린 Battle_PVP 에디터를 프로세스로 확인했으며 중복 에디터 잠금으로 별도 CLI 실행이 막힌 상태로 판단 | 실행 성공이나 테스트 통과로 기록하지 않음. 열린 에디터 연결 후 재실행 필요 |
| Unity MCP | 인스턴스 목록에는 C:/Github/SimpleGame만 있음. 열린 Battle_PVP 에디터의 MCP 연결 전환 요청 중 | 다른 프로젝트로 변경/테스트 명령을 보내지 않음. Battle_PVP의 실제 에디터 검증 미완료 |
| NUnit 회귀 소스 | `Assets/Player/Script/Editor/Tests/`의 CombatAuthorityTests, StatAndMovementSecurityTests, HealthUiLifeAndDamageTests, NetworkProfileRegressionTests | 실패/정상 입력 검사 코드를 추가했으나 Unity 실행 결과 미확보. 소스/일반 빌드 통과와 테스트 실행 통과를 구분 |
| 공백/patch 검사 | 이번 수정 경로로 제한한 `git diff --check` 통과. 전체 diff 검사는 기존 사용자 변경 FollowCamera.cs:16 및 RoomListManager.cs:79의 공백 2개 지적 | 기존 사용자 변경은 수정하지 않음. 이번 범위 검사와 전체 작업 트리 결과를 구분 |
| Windows/WebGL 다중 참가·성능 | 실제 호스트+원격 2명 기능 검사, 호스트+원격 7명 부하 검사, 지연/손실 및 p95/p99 측정 미실행 | 모든 관련 네트워크/성능 합격 조건 검증 대기 |

`Battle_PVP.slnx`는 현재 통합 빌드 진입점으로 사용한다. 기존 사용자 파일을 임의로 정리하지 않으며 새 소스를 포함하는 보조 targets와 테스트 파일은 별도 변경으로 기록한다. 신규 C# 파일 10개와 Tests 폴더의 Unity 메타데이터도 추가했다.

### S01 — 서버 처치 점수와 중복 방지

- 문제: 참가자가 임의 점수를 Command로 요청할 수 있었고 동일 처치의 재처리를 식별하는 명시적 기준이 없었다.
- 변경: `Managers/ScoreSystem.cs`의 `CmdAddPoint`를 제거하고 `AddPoint`를 비공개 서버 경로로 제한했다. 피해자의 서버 `HealthSystem.DeathSequence`를 확인해 사망 상태의 새 처치만 1회 반영한다. `DummyHealth.cs`의 의미 없는 0점 요청도 제거했다.
- 검사 소스: 클라이언트 점수 변경 API 부재, 생존/0 시퀀스 거절, 동일 사망 1회 처리와 다음 사망 처리.
- 상태: 구현 완료·검증 대기. 실제 서버 처치와 중복 전달·부활 후 재처치는 다중 클라이언트에서 확인해야 한다.

### S02 — 서버 스탯 검증

- 변경 파일: `Stats/StatValidation.cs`, `Stats/StatManager.cs`.
- 정책: 기존 투자 예산 30을 유지한다. 각 투자값·아이템값의 유한값/범위, 총예산, 서버가 보유한 아이템값과의 일치를 검사한다. 클라이언트 요청은 투자값만 갱신할 수 있다.
- 동작: 최초 스탯 초기화를 서버의 10초 기한 안에 받고 이후 변경 가능한 씬/생존 상태를 검사한다. 초기 스탯 미승인 동안 이동·공격·스킬·활 및 직접 피해 처리를 제한한다. 요청 간격을 제한하고, 실제 네트워크 플레이어의 능력치는 승인된 서버 스냅샷으로 적용한다. 서버에서 사용하는 프리셋 적용 경로도 검증 함수를 거친다.
- 검사 소스: 음수·NaN·Infinity·예산/아이템 변조·정상 초기화/사망 시 변경과 전투 중 변경 제한.
- 상태: 구현 완료·검증 대기. 거절 후 소유자/서버/관찰자의 스탯·능력치 일치를 실제 실행으로 확인해야 한다.

### S03–S05 — 활·발차기·근접 공격 승인

- 변경 파일: `CombatValidation.cs`, `BowAttackController.cs`, `PlayerCombat.cs`, `MeleeHitBox.cs`, `AttackProcessor.cs`.
- 활: 서버가 차징 시작·해제·예약 발사의 상태와 시간을 기록한다. 장착·정체성·생존·스탯 준비 상태, 발사 위치/방향을 검사하며 서버 차징 시간으로 피해 배율을 계산한다. 예약 발사는 1회 소비한다.
- 발차기: 서버가 승인된 스킬 시퀀스와 타격 시간에 따라 범위를 구성한다. 클라이언트가 보낸 박스 크기·중심으로 판정을 확대하지 않고 대상 중복을 제한한다.
- 근접: 승인된 공격 시퀀스/시각과 기록된 위치를 바탕으로 방향·거리·장애물·대상 신체 부위를 검사한다. 서버 무기 OBB와 대상 부위 Bounds의 64개 기록을 사용하고 공격자 시각과 대상 보간 시각을 구분하며 중복 보고를 차단한다. 부활/서버 순간이동 때 기록을 초기화한다.
- 데이터 충돌 보정: 발차기 데이터의 `CastSeconds=0.3`은 실제 애니메이션의 타격 시작 약 0.8초/종료 약 1.333초와 달랐다. 서버에서 클립의 타격 시작/끝 이벤트를 읽어 유효 시간을 구성하도록 연결했다. 임의의 0.3초 타격 창으로 정상 발차기를 거절하지 않도록 한 변경이며 실제 애니메이터 재생 속도·전이 환경의 검증은 남아 있다.
- 활 발사 후 입력 잠금은 `OnBowReleaseFinished` 이벤트 시각과 기존 fallback 중 빠른 값(`min`, 최소 0.1초)을 채택한다. 현재 이벤트 1.9초보다 fallback 1초가 빠르므로 서버 간격도 1초다. 이벤트가 없으면 fallback을 사용한다. 새 공격 주기를 임의로 0.3초 등으로 단축하지 않는다.
- 검사 소스: 차징 경계/연속 발사/취소/예약 만료, 타격 창·중복·범위/부위 유효성. 상태: 구현 완료·검증 대기. 특히 실제 애니메이션 이벤트, 호스트/원격 화면, RTT 100/200ms 및 손실 2%에서 정상 타격 유지 여부가 미검증이다.

### S06 — 이동 승인과 위치 기록

- 변경 파일: `ServerMovementValidator.cs`, `MovementEffects.cs`, `PlayerManager.cs`, `Managers/BattleStateMachine.cs` 및 전투의 이동 효과 호출부.
- 서버가 위치·회전·시각 유한값과 순서를 확인하고, 이동 속도/점프/중력/접지/경사·장애물 경로에 따른 허용 이동량을 검사한다. 거절한 위치는 서버 위치 기록에 넣지 않고 주기를 제한해 보정 위치를 보낸다.
- 대시·넉백은 서버가 승인한 방향·거리·시간을 검증기에 전달한다. 효과별 이동 배율/잠금 수명을 최소 연결해 정상 스킬 이동을 임의 순간이동으로 오인하지 않도록 했다.
- 경기 시작 스폰과 부활은 `ServerTeleport` 경로로 검증 원점·이동 세대 번호·원격 보간 기록을 함께 초기화한다. 이동 세대는 SyncVar/RPC로 전달하며 이전 세대 패킷은 새 위치에 적용하지 않는다. 대기 후 점프의 정상 수직 이동을 오인 거절하지 않도록 검사도 보완했다.
- 검사 소스: 비정상/오래된 시각·순간이동·이동 예산·정상 승인 이동. 상태: 구현 완료·검증 대기. 실제 지형·계단·경사·대시/넉백 조합·부활·패킷 손실에서 오인 거절이 없는지 확인해야 한다.

### S07 — 서버 생존 상태와 부활

- 변경 파일: `HealthSystem.cs`, `HealthUiLifeRules.cs`, `DummyHealth.cs`, `CombatContracts.cs`, `PlayerManager.cs`.
- 사망은 서버가 결정하고 SyncVar로 생존 상태를 전달한다. 소유자/관찰자의 이벤트는 동기화된 상태에서 발생시키며 중복 알림을 억제한다.
- 사망 시 서버 부활 가능 시각(사망 뒤 5초)을 기록하고 생존/조기/중복/경기 종료 상태, 비정상 회복 비율/시각의 요청을 거절한다. 호스트도 같은 요청 검증을 거친다. 승인된 부활만 스폰 이동, 체력/실드·입력 상태 복원을 수행한다.
- 네트워크 참가 클라이언트가 체력·회복·실드·재생을 직접 변경하지 못하도록 변경 주체를 제한한다. 사망 개체가 재활성화·스탯 갱신으로 자동 회복되는 경로도 제한한다.
- 검사 소스: 조기/반복 부활 및 생존 상태 알림/피해 처리 경계. 상태: 구현 완료·검증 대기. 호스트+원격 2명에서 사망/부활 10회 반복과 실제 입력 복원을 확인해야 한다.

### S08 — Relay 메시지 용량과 정원

- 변경 파일: `Managers/UnityRelayTransport.cs`, `Managers/BattleNetworkManager.cs`, `Assets/Prefabs/NetworkManager.prefab`, `Assets/PlayFabCloudScript/roomRegistry.js`, `combinedCloudScript.js`.
- Reliable 최대 메시지와 실제 fragmentation 용량을 60,000바이트로 맞추고 배치 임계값은 1,200바이트로 별도 설정했다. 최대 초과 송신은 오류로 보고하며 서버 초기화 실패 시 드라이버를 정리한다.
- Mirror 정원 8명/Relay 원격 할당 7명으로 맞췄다. 로컬 방 스크립트는 실제 Members로 중복 참가를 식별하고 정원 초과를 거절하며, 동시 추가 후 초과가 발견되면 해당 멤버십을 되돌린다.
- 초기 구현에 포함된 연결 목록 및 송수신 버퍼 재사용은 유지했다. Mirror의 수신 콜백 안에서 복사되는 데이터 수명에 맞췄으나 O07 성능 개선 합격으로 집계하지 않는다.
- node 모의 검사는 통과했다. 상태: 구현 완료·검증 대기. 실제 4KB 초과·60,000 경계·초과 메시지·대량 스폰·연결 유지와 Windows/WebGL 검증이 필요하다. CloudScript는 로컬 수정이며 미배포, Shared Group은 트랜잭션이 아니므로 실제 동시 예약/반영 상태는 별도 검증 대상이다.

### S09–S10 — 프로필 결과와 저장 단위

- 변경 파일: `Managers/NetworkProfileRepository.cs`, `PlayFabBattleManager.cs`, `GlobalDataManager.cs`, `PlayFabAuthManager.cs`.
- 저장소가 `Success/Empty/Failure`를 반환한다. 실패는 기본 0 데이터로 간주하지 않으며 완료 캐시/플레이어 주입을 수행하지 않는다. 로그인 자체의 성공과 프로필 요청 실패를 구별해 로비에서 재시도할 수 있다.
- 기존 개별 키 읽기는 유지한다. 저장은 기본 스탯·슬롯/선택·전략가 프리셋을 `PlayerProfileV1` 한 키의 JSON 문서로 직렬화하며, 요청 순간 직렬화한 스냅샷을 FIFO로 전달한다. 성공/실패 콜백은 전체 문서에 대해 1회 반환한다.
- 세션 변경 뒤 이전 로드 응답과 대기 중 저장을 적용하지 않는다. 프로필 로드 전 저장은 거절하고, 아직 누적 전적을 읽지 못했을 때 발생한 로컬 증가분은 초기값을 덮어쓰지 않도록 보류한다.
- 검사 소스: 오류/재시도/신규 계정/잘못된 문서, 연속 저장 중 원본 변경, 실패/중복 콜백, 세션 전환과 오래된 응답. 상태: 구현 완료·검증 대기. 실제 PlayFab 저장/재로드·오류·동시 장치 편집은 미검증이며 다중 장치 충돌 병합은 제공하지 않는다.

### S11 — 경쟁 기록 신뢰 경계: 1단계 당시의 이력

- 결과 TargetRpc에서 클라이언트 리더보드 갱신 호출을 제거했다. 기존 개인 누적 킬/데스 저장은 호환용 미검증 표시 데이터이며 경쟁 보상의 근거가 아니다.
- [NETWORK_PROFILE_BOUNDARIES.md](NETWORK_PROFILE_BOUNDARIES.md)에 인증된 호스트/참가자/경기 ID 연결, 서버 검증, 동일 ID 1회 보상 반영, 응답 유실 후 동일 ID 재시도 계약을 정리했다.
- 상태: 외부 구현·검증 대기. 인증된 호스트 제출 엔드포인트, PlayFab 직접 통계 쓰기 권한 차단, 트랜잭션/멱등 반영, 테스트 백엔드 검증과 라이브 배포는 수행하지 않았다. 이번 로컬 코드 변경만으로 S11 완료나 운영 백엔드 보안을 주장하지 않는다.

### 후속 단계와 최소 연동 구분 — 1단계 반영 당시의 이력

| 항목 | 현재 반영 범위 | 남은 범위 |
|---|---|---|
| C05 | 서버 이동 검증에 필요한 원인별 이동 효과/잠금 API 및 일부 전투 호출 연결 | 모든 중첩 조합·만료 순서·전체 호출부 소유권 검증 |
| C06 | DamageRequest/DamageResult로 피해 거절·실드 흡수·HP 피해를 구분하고 공격 후속 처리에 최소 연결 | 모든 공격/독/반사/넉백/팝업 조합의 정책 확인과 실제 플레이 검증 |
| C07 | 서버 거절·사망·무기 상태 변경에서 활 예약/차징 및 관련 스킬 취소 경로 보강 | 각 동작 단계의 전체 취소 상태·히트박스·잠금 잔존/다음 동작 검증 |
| C08 | 서버 강제 이동 승인 및 순간이동 세대/보간 초기화 연결 | 원격 위치 작성 주체 전체 정리와 지연/손실 화면 확인 |
| R02 | 외부 API 결과·프로필 직렬화/요청 대기열을 NetworkProfileRepository로 분리 | GlobalDataManager의 플레이어/HUD 바인딩 책임은 남아 있음 |
| O07 | 일시 중단 전 시작한 Relay 버퍼/목록 재사용 수정 보존 | 최적화 단계 착수·GC/복사 비용 전후 측정·연속/재진입 안전성 실행 확인 |

이 표는 후속 코드 수정 승인 전 상태다. 당시 C01–C04, C09–C10, 나머지 O/R 항목은 해당 단계 작업을 진행하지 않은 대기 상태였으며 최소 연동을 전체 개선 완료로 처리하지 않았다. 이후 구현 범위와 현재 상태는 다음 기록 및 상단 진행표를 따른다.

### MCP 연결 없이 진행한 후속 통합 — 2026-09-06

사용자의 추가 승인에 따라 독립적으로 구현·검사할 수 있는 C/O/R 항목을 반영했다. 아래는 R01 추가 분리 전의 구현 이력이다. 상태표의 `구현 완료·검증 대기`는 합격 조건을 위한 코드 변경을 반영했다는 뜻이며 Unity 컴파일/Mirror 코드 생성, 실제 생명주기·물리·네트워크와 성능 합격은 별도다. 당시 남았던 R01 책임 분리의 후속 결과는 문서 끝의 R01 기록을 따른다.

#### C01·C02·O04·R04 — 스탯 계산과 적용 절차

- 변경 파일: `Stats/StatManager.cs`, `Stats/StatBalanceConfig.cs`, `Stats/StatPresetApplication.cs`, `UI/StatCustomizerController.cs`, `Editor/StatSimulatorWindow.cs`, `PlayerHUD.cs`, `HealthSystem.cs`.
- 실제 파생값과 미리보기가 공통 `StatBalanceCalculator` 계산을 사용한다. `StatManager`는 스탯·정체성·설정 참조/Revision 변경을 기준으로 파생값을 캐시하고 `DerivedStatsChanged`로 소비자에게 알린다. 반복 조회는 같은 캐시를 사용하며 설정 변경은 캐시 무효화 경로를 거친다.
- 원격 스탯 갱신이 로컬 카메라를 바꾸지 않도록 로컬 플레이어 적용 경로를 제한했다. DEF의 최대 HP 등을 포함해 미리보기와 실제 계산을 대조하는 검사 소스를 추가했다.
- UI의 적용 절차는 `StatPresetApplication`으로 옮겼다. 일반 프리셋은 `RequestApplyStats` 서버 승인 뒤 선택 슬롯을 반영하고 단일 프로필 저장 완료를 기다린다. 이미 승인된 스탯을 재요청하지 않도록 `SaveStatPresetSlot(..., applyToPlayer: false)`를 사용한다. 전략가 전환 프리셋은 유효성 검사 뒤 저장하며 현재 플레이어 스탯 적용과 구별한다.
- 서버 거절은 저장하지 않고, 저장 실패는 이미 적용된 스탯과 분리한 실패 결과를 UI에 전달한다. UI는 성공 전에 완료로 닫지 않으며 적용/회복/저장/HUD 재연결을 직접 순서대로 조정하지 않는다.
- 남은 검증: 실제 서버 승인/거절/시간 초과·저장 실패, 설정 편집 후 수치와 HP/HUD 동기화, 호스트/원격 카메라, 기존 프리팹 참조 호환성. 파생값 계산 횟수와 프레임 비용도 실행 측정해야 한다.

#### C03·C04·O05 — 방 목록과 공동 순위

- 변경 파일: `UI/RoomListState.cs`, `RoomListManager.cs`, `RoomListItem.cs`, `RankingUIManager.cs`, `RankingEntryUI.cs`, `Managers/CompetitionRanking.cs`, `ScoreSystem.cs`, `BattleStateMachine.cs`.
- 방 목록은 요청 세대 번호로 최신 응답만 적용하고 비활성화 시 이전 응답을 무효화한다. 방 ID를 키로 보유해 같은 ID의 행을 중복 생성하지 않으며 표시 데이터가 바뀐 행만 갱신하고 사라진 행만 제거한다. 사용자 기존의 간소화된 방 목록 구현을 보존했다.
- 방 메타데이터는 기존 제공자의 캐시/동시 요청 통합 경로를 사용한다. 동일 표시 데이터에 생성/파괴·텍스트·레이아웃 변경을 반복하지 않고 안정적인 이름/ID 정렬을 적용한다. 이벤트 해제는 구독 당시 인스턴스와 자신의 리스너를 대상으로 한다.
- 결과 표시·실시간 순위·보상은 공통 경쟁 순위 계산을 사용한다. 공동 1위가 두 명이면 `1, 1, 3`이고 입력 목록 순서가 보상을 바꾸지 않는다. 동점 표시 순서는 이름/네트워크 ID로 안정화한다.
- 순위 행은 필요한 수만 활성화하고 여분은 재사용하며 값이 달라진 텍스트만 갱신한다. 작은 참가자 목록은 기존 List를 유지한다.
- 검사 소스: 응답 역전, 비활성화/재활성화, 동일 응답, 수정/삭제, 잘못된 ID, 동점·입력 순서·보상. 실제 방 입퇴장·화면 종료와 Unity UI 생성/레이아웃 비용은 미검증이다.

#### C05–C08·O01·O03·R01 — 전투 효과·위치 이력·조회

- 변경 파일: `MovementEffects.cs`, `FixedRingBuffer.cs`, `ServerPoseHistory.cs`, `CombatPhysicsQuery.cs`, `PlayerManager.cs`, `PlayerCombat.cs`, `BowAttackController.cs`, `BowArrowProjectile.cs`, `MeleeHitBox.cs`, `KickSkillHitBox.cs`, `AttackProcessor.cs`, `HealthSystem.cs`.
- 이동 효과와 입력 잠금은 원인별로 추가/교체/해제하고 만료를 평가한다. 차징·버프·감속·잠금 하나의 종료가 다른 원인의 효과를 제거하지 않도록 적용 경로를 연결했다.
- 피해 후속 효과는 서버의 `DamageResult`를 기준으로 한다. 흡혈은 실제 `HpDamage`를 사용하고 독·도발·넉백·피드백 및 가드 파괴는 승인된 피해 뒤 처리한다. 거절된 요청은 피해 수치나 적중 피드백을 확정 표시하지 않는다.
- 클라이언트의 추정 피해 숫자와 추정 적중 피드백을 제거하고 서버 확정 결과로 통일했다. 공격 애니메이션과 적중 보고는 유지한다. 원격 플레이어의 피해 숫자/적중 피드백에는 왕복 통신 지연이 반영되므로 RTT 100/200ms에서 체감과 타이밍을 확인해야 한다.
- 사망·비활성·무기 전환·서버 거절의 취소 경로를 통합해 차징/예약 발사/시전/히트박스/관련 잠금을 종료한다. 강제 이동은 서버 승인과 소유자 이동 경로로 연결하고 관찰자는 보간으로 표시한다.
- 원격 스냅샷은 고정 용량 `FixedRingBuffer`를 사용해 앞부분 삭제 때 배열을 이동하지 않는다. 서버 위치·신체 부위 이력은 `ServerPoseHistory`로 분리하고 순간이동/부활 시 초기화한다.
- `CombatPhysicsQuery`는 충돌/레이캐스트 결과 배열을 재사용한다. 포화되면 용량을 늘려 재조회하고 재사용 상한에서도 포화되면 전체 결과 조회로 누락을 피한다. 이 예외 경로는 배열 할당이 발생하므로 모든 조건에서 무할당이라고 주장하지 않는다. 부위/대상 캐시는 모델 변경에 맞춰 갱신한다.
- 이 시점의 C05–C08/O01/O03은 구현 완료·검증 대기였고 R01은 위의 이력·효과·조회 분리만 반영한 부분 구현이었다. 당시 남았던 전투 연출과 생명 상태 표시 분리는 아래 R01 후속 작업에서 반영했다. 실제 충돌 포화/모델 교체·중첩 취소·원격 위치 작성·지연 보정 및 GC/CPU 측정은 계속 대기다.

#### C09·C10·O06·O08·O10 — HUD·입력·채팅 생명주기

- 변경 파일: `PlayerHUD.cs`, `GameInputController.cs`, `InputModeRules.cs`, `FollowCamera.cs`, `UI/SkillUI.cs`, `BattleChatNetwork.cs`, `BattleChatUI.cs`, `KillAnnouncementUI.cs`, `Managers/BattleStateMachine.cs`.
- HUD 재활성화 때 구독을 복구하고 현재 HP/실드/정체성/스킬/Overflow 스냅샷을 반영한다. 비활성화와 대상 교체에서는 이전 구독을 해제한다.
- 중앙 입력 모드 계산과 프레임별 ESC/Enter 소비를 도입했다. 채팅·메뉴·사망·결과·관전 상태에서 커서와 이동/공격 입력을 결정하며, 결과 화면은 이미 채팅에서 사용한 Enter를 재시작으로 다시 소비하지 않는다. 관전 중에는 입력 제한과 카메라 추적을 분리해 대상을 계속 따라간다.
- 채팅 핸들러는 네트워크 생명주기에서 등록/해제하며 매 프레임 재등록하지 않는다. 같은 프레임의 메시지 레이아웃 변경은 `LateUpdate` 한 번으로 모은다.
- SkillUI의 정적 설정·계층 정리는 초기화 경로로 이동했다. 선택 텍스트 같은 선택적 참조가 없는 프리팹을 매번 재탐색하지 않으며 상태/문자열 변경 때만 동적 표시를 바꾼다.
- 표시 전용 채팅/처치 알림 fallback Canvas에 `GraphicRaycaster`를 자동 추가하지 않는다. 실제 채팅 입력이나 기존 상호작용 버튼의 Raycaster를 일괄 제거한 변경은 아니다.
- 남은 검증: HUD 재활성화 10회, 재연결/씬 전환, 채팅 IME·ESC·Enter 중복 소비, 관전/결과 복귀, 선택 텍스트 없는 프리팹, 레이아웃/문자열/핸들러 비용.

#### O02·O07·O09 — 풀·전송 버퍼·연출 수명

- O02 파일: `UI/DamagePopupManager.cs`, `DamagePopup.cs`, `PopupPredictionCache.cs`, `HealthSystem.cs`. 팝업은 `ObjectPool`로 워밍업/재사용하고 활성 집합을 관리한다. 씬 안의 독립된 풀 루트를 사용해 캐릭터 정체성 스케일이 팝업에 전파되지 않도록 했다. 재사용 시 크기·색·위치·연출 상태를 복원하고 비활성/씬 종료 때 반환/정리한다.
- 기존 prediction ID를 사용하는 확정 팝업 중복 기록은 `HashSet`과 만료 `Queue`로 관리한다. 시계 초기화도 처리한다. 이름에 prediction이 남아 있지만 현재 클라이언트 선표시를 의미하지 않는다. 풀 초과·미워밍업 생성과 보관 상한 초과 폐기는 여전히 할당/파괴할 수 있어 풀 용량 안의 반복 비용을 따로 측정해야 한다.
- O07 파일: `Managers/UnityRelayTransport.cs`. 연결 순회/종료 목록, 관리 수신 배열, NativeArray 송수신 버퍼를 재사용한다. 송신은 UTP writer에 복사한 뒤 완료 콜백을 호출하며 수신은 Mirror의 동기 복사 수명에 맞춘다. 클라이언트·서버 공통 `_isPolling`과 `try/finally`로 콜백의 재귀 EarlyUpdate가 공유 버퍼/목록을 덮어쓰지 못하도록 했다. 콜백 중 Shutdown/연결 종료 뒤에는 드라이버·연결 유효성을 확인한다.
- O07 검사 소스: 라이브 Relay 없이 localhost UTP 드라이버로 콜백 재진입/수신 데이터 보존과 콜백 중 Shutdown을 재현하는 `RelayPollingTests`. 작성 및 일반 컴파일과 Unity 실행을 구분한다. 연속 작은/큰 패킷, Reliable 최대 경계와 Windows/WebGL GC·복사 비용 측정은 남아 있다.
- O09 파일: `UI/StatPreviewPulse.cs`, `StatCustomizerController.cs`, `StatSlider.cs`. 라벨별 원래 크기와 연출 상태를 저장하고 UI의 단일 코루틴에서 진행한다. 같은 라벨을 다시 변경하면 기존 상태를 재시작하며 종료/비활성에서 원래 크기를 복구한다. 빠른 드래그·재활성화의 실제 프레임 동작은 검증 대기다.

#### R02·R03 — 프로필 연결과 결과 화면 분리

- R02 파일: `Managers/LocalPlayerProfileBinding.cs`, `GlobalDataManager.cs`, `NetworkProfileRepository.cs`. 저장소의 성공/없음/실패 및 스냅샷 저장 경계를 유지하면서 씬 이벤트·실제 로컬 플레이어 탐색·HUD 연결을 별도 바인딩 컴포넌트로 옮겼다. GlobalDataManager는 적용 요청을 알리고 바인딩이 화면/플레이어 연결을 수행한다. 기존 공개 진입점은 유지한다.
- R03 파일: `UI/BattleResultData.cs`, `BattleResultView.cs`, `Managers/BattleStateMachine.cs`. 결과 값/문자열 계산과 Canvas/TMP 생성·탐색·표시/숨김을 분리했다. 상태 머신은 결과 데이터를 넘기며 기존 serialized 패널/텍스트 참조를 바인딩으로 전달한다. 런타임 fallback 화면도 재사용하고 생성한 UI는 수명 종료 때 정리한다.
- 결과 화면 표시 상태는 `IsResultPanelVisible`로 알리고 커서는 `GameInputController.RefreshCursorState()`에 위임한다. 스폰은 기존 서버 순간이동 경로를 유지한다. R03 합격 기준인 결과 Canvas/텍스트 직접 구성 제거는 반영했으나 경기 네트워크 전이와 스폰 전체를 별도 서비스로 분리한 것은 아니다.
- 남은 검증: 기존 씬/프리팹 직렬화 참조, 로그인/씬 전환/로컬 플레이어 교체, 경기 결과 표시/재시작/이탈과 UI 수명. 결과 문자열과 공동 순위의 순수 계산은 로컬 검사 범위이며 실제 전이는 Unity 실행이 필요하다.

### 후속 통합 검사 — 현재 확인된 결과

| 검사 | 실행/결과 | 판정 범위 |
|---|---|---|
| 순수 소스 연결 CLI 빌드 | `dotnet build Tools/Tests/LogicRegression/LogicRegression.csproj -v:q --configfile Tools/Tests/LogicRegression/NuGet.Config`: 오류 0, 경고 0 | 실제 프로젝트의 순수 로직 소스를 연결한 별도 실행기. Unity 타입/엔진을 대체하는 테스트 프레임워크가 아님 |
| 순수 소스 연결 CLI 실행 | `dotnet Tools/Tests/LogicRegression/bin/Debug/net10.0/LogicRegression.dll`: 54,833 assertions PASS | 용량 1/3/64 링 버퍼 각각 5,000회 임의 연산과 Queue 비교, 공동 순위/입력 순서, 팝업 중복·만료·시계 초기화, 입력 모드/프레임 소비/관전 카메라 추적 규칙. 실제 Unity 생명주기·물리·네트워크는 미포함 |
| 일반 C# 통합 빌드 | 표시 Canvas 정리와 사용되지 않는 `AttackProcessor.PredictHitDamage/PredictSkillHitDamage` 제거까지 반영한 최종 전체 컴파일: 오류 0, 기존 미사용 필드 경고 6개. 명령은 1단계와 동일한 slnx/추가 소스 targets 사용 | C# 컴파일 결과이며 Unity/Mirror 코드 생성·플레이 성공을 뜻하지 않음 |
| 로컬 CloudScript | `node Tools/Tests/NetworkProfileRoomCapacity.test.js`: 두 스크립트 모의 검사 재실행 통과 | 라이브 서비스 요청·배포 및 실제 PlayFab 동시성 검사 없음 |
| 후속 NUnit 소스 | `StatPreviewCacheTests`, `CombatMovementAndQueryTests`, `RoomAndRankingTests`, `HudAndInputLifecycleTests`, `RelayPollingTests` 5개 파일 추가, 각 Unity meta 존재 | 미실행. CLI 54,833 assertions에 이 NUnit 전체를 포함하지 않음. 실제 UTP/물리/MonoBehaviour를 사용하는 검사도 Unity에서 실행해야 함 |
| 공백/메타 검사 | 수정 경로 `git diff --check` 통과, 신규 C#의 meta 존재 확인. 전체 diff에는 기존 FollowCamera의 Offset 행 공백 1개가 남아 있음 | 사용자 Offset 값/기존 변경 보존. 앞선 1단계 전체 diff의 공백 2개와 현재 결과를 구별 |
| Unity 및 실서비스·성능 | 현재도 Battle_PVP Unity 컴파일/Mirror 코드 생성·EditMode 실행·물리·호스트/원격·Windows/WebGL FPS/GC·라이브 백엔드 검증 결과 없음 | 기존 CLI exit 1/결과 파일 없음 및 MCP가 SimpleGame만 가리키는 제약 유지. 실행/측정 합격을 선언하지 않음 |

위 표는 R01 후속 책임 분리 전의 검사 이력이다. 모든 최적화 항목은 코드 경로를 바꾼 상태이며 비용 감소 수치를 확보한 상태가 아니다. R01의 후속 변경과 현재 검증 결과는 아래 기록을 따른다.

### R01 후속 — 전투·생존 상태와 화면 연출 책임 분리

사용자가 남은 로컬 구조 수정의 진행을 승인하여 반영했다. 상태는 `구현 완료·검증 대기`다. 기존 시전 수치·순서, 서버 승인, 직렬화 필드와 공개/네트워크 호출 진입점을 유지하면서 상태 소유자와 화면 처리 경계를 정리했다.

| 소유자 | 소유한 책임과 상태 | 종료·복구 지점 |
|---|---|---|
| `PlayerCombat` | Command/RPC·SyncVar, 공격/시전 코루틴·시퀀스·히트박스·게임 효과 적용 | 거절/전체 취소는 `CancelPredictedSkillAction`, 정상/취소 시전 종료는 `FinishAdvancedCast`를 공유. 사망·부활·비활성에서 `CancelAllCombatActions` |
| `CombatSkillPresentation` | 검의 원래 재질, 스킬 애니메이션 재생·완료 조회, 효과음·확정 적중 연출 | 효과/대상 교체와 Cancel에서 원복, 소유자 파괴 시 Dispose |
| `SkillAuraPresentation` | 오라 객체·런타임 재질·표시 종료 시각·모델 범위 캐시 | 전달받은 시각/실드 값으로 표시, 취소 시 숨김, Dispose에서 생성 자원 파괴 |
| `SkillHudPresenter` | 선택된 스킬 값→표시 단계/이름/남은 시간/채움 값 변환, 알림 주기·동일값 비교 | 입력 snapshot을 갱신하며 계산. 전투·네트워크 컴포넌트를 탐색하지 않음 |
| `HealthSystem` | 권위 있는 HP/실드/생존 상태와 피해 결과, 중복 없는 생존 이벤트 | 기존 서버 부활 조건 검사→스폰 순간이동→Revive 순서 유지. 생존 알림에서 Animator/전투 취소/부활 화면 직접 호출 제거 |
| `PlayerManager` | 이동·CharacterController 게이트·로컬 사망 입력·부활 대기 시각·서버 승인 요청 | 비활성에서 입력 대기 취소, 재활성에서 현재 Health 상태와 기존 대기 시각 복구 |
| `PlayerLifePresentation` / `PlayerModelVisibility` | 죽음/부활 애니메이션·HUD 안내·관전 시점·모델 표시와 원래 enabled 값 | 중복 애니메이션 억제, 비활성/부활/결과 전환에서 안내 종료·모델 복원. CharacterController는 변경하지 않음 |
| `PlayerRespawnCountdown` | 변경 불가능한 부활 안내 마감 시각 | 재표시 시 기존 값을 재사용. `HealthUiLifeRules`와 같은 5초 기준 사용 |
| `HealthDamagePresentation` | 승인된 피해의 색·위치·글자 크기·로컬 피격 HUD·중복 표시 기록 | 기존 접속 수명 초기화에서 기록 제거. Health/RPC는 승인 결과와 로컬 역할만 전달 |

- 변경 파일: `PlayerCombat.cs`, `PlayerManager.cs`, `HealthSystem.cs`; 위 표의 신규 표시/시간 helper 7개와 각 Unity meta. 기존 `HealthUiLifeRules.cs`는 서버 대기 정책 소스로 재사용한다.
- 게임 규칙은 화면 표시에서 변경하지 않는다. 부활 입력은 `PlayerManager`의 countdown 값으로 판단하고 화면 컴포넌트의 활성 여부를 승인 조건으로 사용하지 않는다. 실제 위치와 생존 전환은 계속 서버 승인 후 이루어진다.
- `PlayerManager`의 공통 생존 이벤트는 원격 캐릭터에도 애니메이션만 적용한다. 로컬 HUD·카메라·부활 입력은 로컬 분기에 둔다. 죽음 이벤트와 RPC 중복에는 애니메이션 가드를 사용하며, 재활성화 시 사망/부활을 놓쳤더라도 현재 상태로 표시를 복구한다.
- 모델 숨김은 원래 Renderer/일반 Collider의 enabled 값을 기록해 복구하므로 비활성 공격 콜라이더를 켜지 않는다. CharacterController는 이동 코드의 단일 소유로 유지한다. 부활 표시는 카메라 타깃과 회전 잠금을 함께 복구한다.
- `HealthSystem._animator`는 기존 프리팹 참조를 위한 adapter로 유지한다. `LifeAnimator`가 필요한 순간 자식 Animator를 찾으므로 PlayerManager.Awake가 먼저 실행되어도 참조가 누락되지 않는다. 저장소의 HealthSystem 직렬화 사용처는 PlayerManager가 있는 Player.prefab으로 확인했으며 프리팹 자산 자체는 이번 분리에서 수정하지 않았다.
- 피해 숫자는 계속 서버 승인 후 표시한다. 표시 분리는 피해 승인·HP/실드 계산·점수·반사 정책을 바꾸지 않는다. 로컬 피격자와 공격자가 같을 때 피격 표시를 우선하고 추가 적중 연출을 중복 재생하지 않는 기존 정책도 유지한다.
- `PlayerCombat`과 `PlayerManager`는 네트워크/입력 조정 역할을 계속 가진다. 파일 줄 수 감소나 모든 메서드의 서비스화를 완료 기준으로 사용하지 않는다. 전투 계산은 기존 계산기/피해 결과 경로로, 표시 변환과 자원 복원은 새 helper로 각각 검사할 수 있게 한 변경이다.

#### R01 검사 결과와 남은 검증

- 일반 C# 통합 빌드: `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets` — 오류 0, 기존 미사용 필드 경고 6개. 개발 중 표시 helper namespace 누락 1건을 수정한 뒤 통합 성공했다.
- 정적 대조: PlayerCombat의 Command/ClientRpc/TargetRpc 선언 19개와 직렬화 선언을 보존했고 공개 메서드 삭제가 없다. 이번 수정 경로의 `git diff --check` 통과, 스크립트 meta 누락 0개. 독립 검토에서 발견한 Awake 순서 및 비활성 자식 Animator 탐색 문제는 lazy 바인딩과 `includeInactive: true`로 보정한 뒤 최종 빌드를 통과했다.
- 순수 소스 연결 CLI: 기존 명령으로 빌드 오류 0/경고 0, 실행 **55,030 assertions PASS**. 기존 54,833개에 부활 5초 경계·유한 시간·재표시 시 마감 시각 유지·서버 승인 조건 일치 검사 197개를 추가했다. 실제 MonoBehaviour 비활성/재활성 동작을 이 검사로 검증한 것은 아니다.
- 신규 NUnit 소스: `CombatPresentationTests`(14사례), `PlayerLifePresentationTests`(8사례), `HealthDamagePresentationTests`(8사례). HUD 단계/기한/주기·검 재질 원복·오라 만료/실드/폐기·소유자 취소·모델 복원·관전 카메라·피해 표시 역할·Awake 이전 Animator 바인딩을 검사한다. 기존 `CombatMovementAndQueryTests`의 private 메서드 reflection 호출은 새 표시 소유자 검사로 연결했다. **Unity에서 실행하지 않았으며 통과로 집계하지 않는다.**
- 실제 Unity/Mirror 코드 생성, 사망/부활 반복과 관찰자 화면, 비활성 중 서버 상태 변경, 씬 전환/결과 복귀, 애니메이션 이벤트 타이밍과 프리팹 참조 검증은 남아 있다. 이번 작업은 Unity MCP나 다른 프로젝트 에디터에 명령을 보내지 않았다.
- S11 백엔드 구현·권한 설정/배포와 Windows/WebGL 8인 60 FPS 측정도 남아 있다. R01 코드 반영을 전체 개선 완료로 취급하지 않는다.

### S11 후속 — 방 권한과 클라이언트 직접 쓰기 경로 보강: 참가자 인증 추가 전 이력

문제 상황: 다른 참가자가 방 ID를 알면 호스트 방을 재등록하거나 Relay 정보를 바꾸는 경로, 공개 Title Data의 관리자 키 fallback, registry 읽기 실패 시 클라이언트가 멤버 권한을 얻는 경로가 있었다. 공개 메서드에는 통계·임의 Shared Group 데이터 직접 쓰기도 남아 있었다.

- 변경 파일: `Assets/PlayFabCloudScript/roomRegistry.js`, `combinedCloudScript.js`, `Assets/Player/Script/Managers/PlayFabBattleManager.cs`, 새 `RoomIdentity.cs`와 meta.
- 새 방 ID는 `battle_<호스트 PlayFabId 소문자 hex>_<32자리 nonce>`다. 클라이언트 생성/참가에서 형식을 검사하고 서버 등록·Relay 갱신은 CloudScript가 제공하는 인증된 `currentPlayerId`와 ID의 호스트를 대조한다. 표시 이름이나 클라이언트 호스트 플래그를 신뢰하지 않는다. nonce는 보안 토큰이나 경기 ID가 아니다.
- 실제 Members에 따라 인원을 집계한다. 등록 재시도가 인원을 1로 되돌리지 않고, 중복·비멤버 탈퇴가 다른 사람의 인원수를 줄이지 않는다. 마지막 멤버 삭제로 Shared Group 자체가 사라지는 정상 API 동작을 처리하되 시간 초과·권한·서비스 오류를 성공으로 삼지 않는다. 입퇴장의 트랜잭션 원자성을 보장한 변경은 아니다.
- 예약 그룹/이전 GUID/잘못된 ID의 일반 변경을 거절한다. 이전 목록의 읽기 호환은 유지하지만 해당 방 참가·변경에는 새 클라이언트에서 방 재생성이 필요하다. 관리자 키는 Title Internal Data만 읽으며 combined의 튜토리얼 핸들러 14개를 비활성화했다. 두 파일의 게임 핸들러 9개 계약을 일치시켰다.
- 클라이언트의 registry 생성·멤버 권한 획득·멤버/데이터 직접 쓰기 fallback을 제거했다. 공개 목록 fallback은 읽기만 한다. `UpdateStatistics`와 `UpdateRoomData`는 기존 외부 참조를 깨지 않는 경고 전용 메서드이며 네트워크 쓰기를 수행하지 않는다. Relay 갱신은 현재 호스트에서만 요청하고 CloudScript 성공 후 현재 방의 캐시를 갱신한다.
- [Tools/PlayFab/README.md](Tools/PlayFab/README.md)와 API Deny Statements 조각에 직접 Client 쓰기 5개 차단, 현재 정책/버전 보존, 내부 키 전환, 실제 테스트 절차를 기록했다. **실제 정책이나 CloudScript revision은 배포하지 않았다.** 공개 관리자 키가 있었다면 제거·교체가 필요하고, 기존 registry는 마지막 멤버 제거 시 데이터도 삭제되므로 백업과 서버 재생성/복구 계획이 필요하다. `what.js` 등 기존 샘플을 별도로 배포하면 이 허용 목록을 우회할 수 있으므로 배포 대상은 문서의 두 파일 중 하나다.
- 상태: S11은 계속 `외부 구현·검증 대기`. Mirror 연결과 검증된 PlayFab 계정의 결합, 중도 이탈자 경기 기록 보존, 인증된 호스트 제출, 경기 ID별 멱등·원자적 보상 저장이 남아 있다. 자세한 신뢰 경계는 [NETWORK_PROFILE_BOUNDARIES.md](NETWORK_PROFILE_BOUNDARIES.md)를 따른다.

### O01–O10 검증 준비 — 개발용 8인 프레임 수집과 3회 분석

- 새 파일: `Assets/Player/Script/Diagnostics/PerformanceCaptureRules.cs`, `DevelopmentPerformanceCapture.cs`, `Editor/PerformanceCaptureMenu.cs`, `Editor/Tests/PerformanceCaptureRulesTests.cs`, `Assets/Plugins/WebGL/BattlePvpPerformanceDownload.jslib` 및 Unity meta.
- 수집기는 Editor 또는 Development Build에서 명시적으로 열며 자동 플레이/부하를 만들지 않는다. 1회마다 30초 워밍업 뒤 60초간 연속 Update의 벽시계 간격을 기록한다. 측정 중 진단 GUI를 숨기고 종료 후 조건/통계 JSON과 원본 프레임·조건 probe CSV를 내보낸다. Windows는 persistentDataPath, WebGL은 사용자가 누르는 다운로드 버튼을 사용한다.
- 인원 8명, 전투 상태, 포커스, 해상도/품질/씬, 메뉴·채팅 중단 여부를 관측한다. 정상 사망/부활은 허용한다. 1초 probe 외 참가/퇴장 이벤트에서도 인원 범위를 기록한다. 각 10초 구간의 4명 이상 이동 및 승인 HP 피해는 유휴 측정을 거르는 도구의 보수적 필터이며 기존 게임 규칙이나 대표 전투 부하의 완전한 증명이 아니다. 관찰자의 대표 전투 확인도 필요하다.
- 순수 판정기는 유한 표본/측정 길이/통계 일관성, 조건, 플랫폼, 서로 다른 1–3번 run과 동일 환경을 확인한다. p95 예산은 `1000/60ms`다. Editor·미완료·조건 누락·플랫폼 혼합·중복 run을 플랫폼 합격으로 처리하지 않는다. 단일 run의 예산 충족과 플랫폼 3회 판정을 구별한다.
- 기존 `Tools/Tests/LogicRegression`에 `--analyze-performance run1.json run2.json run3.json`을 추가했다. JSON의 저장된 Verdict를 무시하고 실제 프로젝트 판정 코드로 재계산한다. 합격 exit 0, 조건 미충족/예산 초과 exit 1, 파일/JSON/인자 오류 exit 2다. 입력 파일의 출처를 인증하거나 CSV 원본으로 p95를 다시 계산하는 분석기는 아니다.
- [PERFORMANCE_VERIFICATION.md](PERFORMANCE_VERIFICATION.md)에 두 플랫폼의 실행·조건·저장·분석 및 남은 Profiler 측정을 정리했다. 이번에 실제 성능 수치 파일은 생성하지 않았다. 수집기 실행, WebGL 플러그인 연결/다운로드, 8인 부하, CPU/GPU 구분·GC·전송 비용·변경 전후 개선은 모두 실측 대기다.

### S11·성능 도구 통합 검사 — 2026-09-06

| 검사 | 결과 | 한계 |
|---|---|---|
| 전체 C# 컴파일 | `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets`: 오류 0 / 기존 CS0414 경고 6개 | Unity 컴파일/Mirror 코드 생성·플레이어 빌드·실행을 대신하지 않음 |
| 순수 CLI 빌드·실행 | 기존 명령으로 오류 0 / 경고 0, **55,867 assertions PASS**. 기존 55,030 유지 + 방 ID/성능 판정/분석 입력 837개 | 합성 입력/합성 JSON 검사. 실제 FPS 측정 아님 |
| CloudScript 모의 검사 | 두 파일 모두 소유자 위조·예약/이전 ID·재시도·정원/경합·마지막 멤버 자동 삭제·내부 키·튜토리얼 비활성화 검사 PASS | 실제 PlayFab 정책/서비스 동시성 미검증 |
| JavaScript·정책 파일 | 두 CloudScript 및 WebGL jslib 구문 검사 통과, API Deny JSON 파싱/5개 Resource 확인 | WebGL 연결/실행 및 정책 배포 검증 아님 |
| NUnit | `PerformanceCaptureRulesTests` 16개 사례 소스 추가·C# 컴파일 | Unity 미실행. CLI 검사 수와 합산하지 않음 |

추가한 소스의 Unity meta 존재, 충돌 표시/행 끝 공백 검사와 수정 경로의 `git diff --check`를 통과했다. 독립 코드 검토에서도 이번 변경의 추가 차단 문제는 발견하지 못했다. 이전 사용자 FollowCamera Offset 값/행 공백은 보존하며 전체 작업 트리를 이번 단계 단독 변경으로 간주하지 않는다. 전체 개선 완료 기준과 상태는 [COMPLETION_CRITERIA.md](COMPLETION_CRITERIA.md)를 따른다.

### S11 후속 — 참가자 인증과 경기 기록 보존: 2026-09-07

문제 상황: Mirror 인증기가 비어 있어 PlayFab 로그인 계정과 실제 게임 연결이 연결되지 않았다. 종료 결과는 남은 ScoreSystem 객체만 조회하여 중도 이탈자가 순위/우승/예상 XP 모수에서 사라지고, 해당 상대 이름과 처치 횟수도 잃었다. 같은 계정으로 재접속하면 새 객체의 경기 합계가 0에서 시작했다.

| 테마 | 이번 구현에서 확인할 조건 | 실제 실행으로 남은 완료 조건 |
|---|---|---|
| 참가자 인증 | 클라이언트가 주장한 ID가 아니라 PlayFab이 승인한 계정을 원래 Mirror 연결에 바인딩. 다른 방/계정/challenge·만료·실패·늦은 응답 거절 | 테스트 타이틀의 호스트+원격7명, 위조·중복·끊김·재시도·WebGL 및 Mirror 코드 생성 |
| 경기 기록 | 검증된 계정별 처치·사망·피해·상대 기록 보존. 재접속 합산, 같은 죽음/과거 죽음 중복 차단 | 실제 생성/파괴/씬 전환 순서, 이탈/재접속 중 전투·표시 일치 |
| 종료 결과 | 전체 참가자 스냅샷 한 번 확정, 공동순위/예상 XP 일치, 이후 변경 거절 | 이탈 우승자·종료 직후 접속·관전 대상 소멸·다음 경기 복귀 |

#### 참가자 인증

- 파일: 새 `Managers/RoomNetworkAuthenticator.cs`, `RoomAuthenticationRules.cs`와 meta, 기존 `RoomIdentity.cs`, `BattleNetworkManager.cs`, `PlayFabBattleManager.cs`, CloudScript 두 파일.
- 호스트가 새 연결마다 challenge를 발급하고, 참가자가 자기 PlayFab 세션으로 `ApproveRoomConnection`을 호출한다. 호스트는 자기 세션으로 `VerifyRoomConnection`을 호출한다. CloudScript의 실제 호출자·방 소유자/멤버·InternalData·기한을 검사하며 세션 티켓/비밀키를 Mirror로 전달하지 않는다. 게임 핸들러 허용 목록은 현재 11개다.
- 인증기 자동 연결과 플레이어 생성 직전 authData 검사를 추가했다. 호스트 로컬 연결은 사용자 신뢰 범위에 따라 로컬 로그인/방 소유자 및 실제 LocalConnection 객체를 확인하고 기존 방 등록 순서를 유지한다. 원격7명+호스트1명을 지키며 호스트 씬 로딩 중 자리도 예약한다.
- 30초 기한, 방/연결 객체/실행 세대 검증, 연결당 승인 요청 1개, 검증 후 계정별 예약, 이탈 시 정확한 예약 해제를 적용했다. 계정 문자열은 인증층에서 64자리 hex로 제한한다. 인증 실패/끊김은 로비 busy를 해제한다. 명시적 중복 계정 거절 시 두 번째 연결은 기존 계정의 멤버십을 지우지 않고 로컬 상태만 정리한다.
- 계정당 InternalData 고정 키 한 개의 최신 승인 정책이다. 같은 계정 동시 승인 두 건은 경합할 수 있고, Shared Group의 연결별 임대/원자적 정리나 영구 proof 소비를 구현한 것은 아니다. 거절 응답 유실/장기 지연까지 같은 계정 멤버십 보존을 보장하지 않는다. 새 CloudScript 없이 새 클라이언트만 배포하면 원격 인증이 실패한다.

#### 경기 기록과 결과

- 파일: 새 `Managers/MatchLedger.cs`와 meta, `ScoreSystem.cs`, `BattleStateMachine.cs`, `BattleNetworkManager.cs`.
- Unity 객체를 보관하지 않는 서버 ledger가 계정별 누적 기록과 현재 netId 연결을 소유한다. `ScoreSystem`은 인증 데이터의 방/계정을 확인해 붙고 ledger 합계를 SyncVar에 반영한다. 서버 객체 종료는 연결만 해제한다. 같은 계정의 순차 재접속은 합계·상대 기록을 이어받고 객체의 사망 번호 세대만 새로 시작한다.
- 기존 ScoreSystem 내부의 별도 상대별 Dictionary와 종료 시 살아 있는 객체의 재계산 경로를 제거했다. 킬·데스·상대별 횟수를 한 번에 반영하고, 유효하지 않은 피해 입력과 유한 범위를 벗어나는 누적을 검사한다. 과거 사망 sequence가 늦게 도착하는 재현을 회귀에서 찾아 단조 증가/uint 순환 검사를 추가했다.
- 동시 정원 8명과 경기 누적 참가자를 구별한다. 교체 참가자가 들어와 누적이 8명을 넘어도 이전 참가자를 버리지 않는다. 종료 스냅샷은 읽기 전용 컬렉션/값으로 한 번 확정하고, 진행 중 Begin 재호출로 기록을 지우는 실수도 거절한다.
- 전원 스냅샷으로 `1, 1, 3` 공동 순위, 기존 예상 XP 공식, 이탈한 우승자와 상대 이름을 계산한다. 현재 객체 조회는 결과 RPC 전달과 관전 대상에만 쓴다. 예상 XP는 로그에서도 백엔드 미반영으로 명시한다.
- 호스트 NetworkManager에 최근 결과 한 개를 보존해 씬 재시작 뒤 진단할 수 있게 했다. 다음 결과에서 교체되며 서버 종료 시 비운다. `LocalMatchId`는 로컬 식별자이고 durable outbox/백엔드 경기 ID/영속 저장이 아니다. 재접속 복구 대상은 경기 통계이며 HP·스킬 쿨다운 등 전체 캐릭터 상태 복구를 추가한 것은 아니다.

#### 독립 검토에서 보강한 종료 경계

- 종료된 경기에 뒤늦게 생성되면 결과/관전 RPC를 놓치고 일반 이동 상태로 남는 경로를 확인했다. `MatchEnded`에서는 새 플레이어 생성을 거절하고 호스트의 다음 경기 재시작 후 재참가하도록 처리한다. 진행 중 경기의 정상 late join은 유지한다.
- 우승자가 이미 이탈한 경우 로컬 플레이어를 관전 대상으로 사용한다. 종료 RPC 뒤 우승자가 나가는 경우도 `PlayerLifePresentation.RefreshSpectateTarget`으로 로컬 대상에 복구한다. 호출은 기존 PlayerManager의 로컬 Update에서 결과/메뉴 early return 앞에 연결하며 별도 Update/코루틴/검색을 추가하지 않는다. 승자/순위는 바꾸지 않는다.

정책·서비스 적용 절차와 실제 검사 항목은 [Tools/PlayFab/README.md](Tools/PlayFab/README.md), 인증/기록의 한계는 [NETWORK_PROFILE_BOUNDARIES.md](NETWORK_PROFILE_BOUNDARIES.md)에 정리했다. S11의 남은 구현은 서버 발급 경기 ID/참가자 등록, 인증된 호스트 제출, 영속 제출 대기·재시도, 경기 처리와 보상의 원자적 반영이다. 이번 로컬 코드 변경을 S11 전체 완료로 처리하지 않는다.

#### 최종 검사 — 2026-09-07

| 검사 | 실행 결과 | 판정 범위 |
|---|---|---|
| 전체 C# 빌드 | 기존 slnx/AdditionalUnitySources.targets 명령, 오류 0 / 기존 CS0414 경고 6개 | 인증·ledger·종료/관전 보강의 C# 컴파일. Mirror 코드 생성/플레이어 빌드/엔진 실행 아님 |
| 순수 CLI 빌드·실행 | 오류 0 / 경고 0, **56,044 assertions PASS**. 기존 55,867 유지 + 경기 기록/인증 177개 | 실제 production 소스를 연결. 이탈·재접속·누적10/연결8·readonly snapshot·지연 사망/uint 순환·유한 피해·proof/예약·64/65자리·진행 중 Begin 거절. 합성 입력이며 Unity/네트워크/FPS 미포함 |
| CloudScript | `node Tools/Tests/NetworkProfileRoomCapacity.test.js`: 두 파일 PASS, 두 스크립트 구문/공통 계약 검사 PASS | 기존 권한/정원 검사와 승인/검증·다른 계정/방/challenge·만료·InternalData 오류. 실제 API policy/배포/동시성 미검증 |
| NUnit 소스 | 기존 `NetworkProfileRegressionTests`에 과거 사망 재전달 조건 추가, `PlayerLifePresentationTests`에 관전 대상 파괴/복구/중단 관련 3개 사례 추가 | 작성·C# 컴파일만 수행. Unity 미실행이며 순수 CLI 56,044에 합산하지 않음 |
| 파일/독립 검토 | 신규 C# 3개의 meta, 변경 경로 diff 공백, 새 파일/문서 공백·충돌 표시 검사 통과. 독립 리뷰의 종료/관전 발견 사항 보강 | 사용자 기존 FollowCamera Offset/행 공백 보존. 전체 작업 트리는 이번 단계만의 변경이 아님 |

MCP·Unity·실서비스를 호출하지 않았고 실제 성능 수치/보상 지급도 생성하지 않았다. 기존 8인 두 플랫폼 60 FPS 목표와 각 테마의 실행 검증 대기 상태를 유지한다.

### 입·퇴장 현황과 방 전환 후속 — 2026-09-07

#### 작업별 완료 기준

| 테마 | 코드 완료 기준 | 별도 실행 검증 |
|---|---|---|
| 상단 플레이어 현황 | 실제 클라이언트 명단의 입·퇴장 이벤트로 인원/순위를 갱신. 메타데이터의 이전 인원수로 덮어쓰지 않음. 0명·재활성화·같은 netId 교체 시 중복 행 없음 | 호스트+원격 입·퇴장/재접속, 씬 전환, UI 비활성 중 명단 변경 후 재활성화 |
| 방 전환 비동기 처리 | 취소·A→B 전환·계정 전환 후 A의 응답/Relay 준비가 현재 방·대기 표시·네트워크 시작을 바꾸지 않음. 동일 멤버십 변경 순서를 유지하고 이전 요청의 정리는 원래 인증 문맥 사용 | 실제 PlayFab 응답 지연/유실, Relay 준비 중 취소·전환, 호스트 등록 재시도, 같은 방 재참가 |
| 재접속 전투 상태 | 같은 경기의 HP·사망/부활 기한·공격/스킬 제한·지속 효과를 초기화하지 않음. 연결 중인 인원과 보존된 전투 객체를 구분. 다음 경기/서버 종료 시 정리 | 생존/사망·독/감속/도발·공중 이동·차징/공격 중 이탈과 복귀, 상대 표시와 판정 일치 |

#### 상단 플레이어 현황

- `Battle_waiting`의 `BattleRoomInfoBanner`는 저장된 PlayFab 멤버 수를 표시하고 방 registry 변경만 구독했다. 이 경로에서는 실제 Mirror 입·퇴장과 표시 인원이 즉시 일치하지 않을 수 있었다.
- 배너가 `ScoreSystem.OnScoreUpdated`를 구독해 유효하고 중복되지 않은 `ActiveScores.netId`로 인원을 갱신하도록 바꿨다. 안정된 manager의 점수/입·퇴장 이벤트에서는 웹 요청 없이 명단만 읽는다. 0명도 0으로 표시하고 저장된 멤버 수로 대체하지 않는다. 방 이름/방장 이름은 기존 메타데이터를 유지한다.
- 새 `RoomBannerRequestState`가 최신 요청·방·UI 활성 기간을 확인한다. 비활성화/씬 전환/manager 교체 이후 응답은 적용하지 않으며 실제 구독한 manager에서 이벤트를 해제한다. 재활성화 시 현재 명단을 다시 읽는다.
- 전투 장면의 `RankingUIManager`는 이미 `OnStartClient/OnStopClient → OnScoreUpdated → 목록 재구성`과 `OnEnable` 즉시 갱신 경로가 있어 이번에 runtime 코드를 추가 변경하지 않았다.
- `ConnectedRosterUiTests` 6개 사례를 추가했다. 입·퇴장 2→1→0, 오래된 메타데이터의 인원 8 무시, 늦은 활성/비활성 중 변경/재활성화, 새 netId 재접속, 중복·0·파괴된 객체 제외, 순위 재계산/행 재사용, 응답 세대 검사를 포함한다. 실제 Mirror 연결 없이 생명주기 콜백을 호출하는 NUnit 소스이며 Unity 실행 결과와 구분한다.

#### 방 전환과 멤버십 정리

- `PlayFabBattleManager`의 Join/호스트 등록·재시도/Relay 준비/Relay 코드 갱신/현재 방 조회에 계정·방·요청 세대를 연결했다. 취소·계정 변경·비활성/파괴 시 세대를 무효화하며 이전 응답은 현재 방/화면/네트워크 시작에 반영하지 않는다.
- 새 `RoomFlowGeneration`이 요청의 유효성과 중복 계정 거절 시 보존 범위를 소유하고, `RoomOperationQueue`가 계정+방별 쓰기 순서를 보장한다. 이전 Join/Leave 뒤에 새 Join이 진행되며 다른 방 요청은 독립적이다. 진행 중 요청이 끝난 후 정리 여부를 다시 검사하고, 같은 방 새 요청이 이전 멤버십의 정리 책임을 이어받는다. 보존 범위는 거절된 세대까지여서 다음 정상 재참가의 퇴장을 영구 차단하지 않는다.
- 정리는 요청 시작 시 복사한 PlayFab 인증 문맥으로 실행한다. 새 로그인 계정으로 이전 멤버십을 정리하지 않는다. 매 프레임 계정 ID 정규화를 반복하지 않고 원래 ID가 바뀐 경우에만 계정 변경 처리를 한다.
- Relay 준비는 하나의 큐에서 실행하고 완료 후에도 현재 세대를 검사한다. 새 네트워크 시작 전 이전 Mirror mode/연결 객체/씬 로딩의 종료를 최대 10초 기다린다. `BattleNetworkManager.OnClientDisconnect`는 실제 시작했던 방의 흐름에 종료를 전달하며, 이전 연결의 종료가 새 방을 지우지 않는다.
- PBM의 `_waitSceneName`은 동작에 사용되지 않고 로그에만 남아 있던 필드라 제거했다. 실제 씬 전환은 기존 NetworkManager 설정을 따른다. 기존 Login/Lobby/_Recovery 씬의 사용되지 않는 직렬화 값은 수정하지 않았다.
- 서비스 콜백이 오지 않으면 같은 키/Relay 큐가 기다릴 수 있다. 정리 요청 실패·원래 세션 만료·프로세스 종료 시 멤버십 정리를 보장하지 않으며, 다른 장치의 같은 계정에 대한 원자적 연결 임대를 구현한 것은 아니다. 외부 서비스 정리가 확인되지 않으면 경고를 남긴다.

#### Relay 종료 알림 누락

- 독립 검토에서 `UnityRelayTransport.ClientDisconnect`가 드라이버만 폐기하고 `OnClientDisconnected`를 호출하지 않는 경로를 발견했다. Mirror는 이 알림을 통해 `OnStopClient`, 네트워크 객체/`ActiveScores` 정리와 mode 전환을 수행하므로 자발적 퇴장과 다음 방 접속에 직접 영향을 준다. 서버가 참가자를 끊는 `ServerDisconnect`에도 동일한 알림 누락이 있었다.
- 클라이언트는 시도별 종료 알림을 한 번만 발행하도록 변경했다. 연결 상태와 드라이버 정리를 먼저 완료해 콜백 내부 `Shutdown → ClientDisconnect` 재진입이 알림을 반복하지 않는다. 원격 종료 수신도 같은 정리를 사용한다. 서버는 연결 목록에서 제거한 경우에만 종료를 알리고 polling의 중복 종료도 무시한다.
- 준비되지 않은 Relay/드라이버 초기화 실패는 다음 client polling에서 알린다. Mirror가 `Transport.ClientConnect` 호출 뒤에 연결 객체를 만드는 순서 때문에, 호출 안에서 실패를 알리고 청소한 직후 빈 연결 객체가 다시 생기는 상황을 피한다. 첫 polling 전 취소는 대기 오류도 없앤다.
- 오류 콜백 안에서 즉시 새 시도를 시작해도 이전 오류의 `finally`가 새 시도를 지우지 않도록 연결 시도 번호를 검사한다. 기존 `RelayPollingTests`에 자발적 종료/서버 측 종료의 양쪽 알림, 콜백 재진입, 유휴·반복 종료, 지연 실패 알림, 새 시도, 첫 polling 전 취소, 오류 콜백 안 재시도 관련 4개 사례를 추가했다. 로컬 UTP 및 미준비 transport를 사용하는 검사 소스이며 실제 Unity/Mirror 실행은 아직 하지 않았다.

#### 재접속 전투 상태: 기획 선택 확인 중

현재 코드는 재접속 시 경기 통계만 복원한다. HP/쿨다운 몇 개를 새 객체에 복사하는 것만으로는 충분하지 않다. 독은 공격자의 `PlayerCombat`에서 기존 대상 객체를 참조하며, 공격·이동·부활 UI에도 객체 수명에 연결된 상태가 있다.

연결이 끊긴 몸을 경기 종료까지 유지해 공격받게 할지, 몸을 제거해 단절 중 공격받지 않게 할지 사용자 선택을 요청했다. 이는 재접속 중 전투 규칙의 선택이며 MCP 연결이나 코드 편집 승인이 필요한 것은 아니다. 선택 전에는 HP만 부분 복원해 전체 상태 복구로 표시하지 않는다.

기존 몸 유지 방식을 택하면 Mirror의 `RemovePlayerForConnection(..., KeepActive)`와 `AddPlayerForConnection`으로 서버 객체를 재귀속할 수 있다. 추가로 실제 연결 명단, 공격/스킬 요청 번호 연속성, 저장 프리셋 자동 주입 생략, 서버 부활 기한 표시, 단절 중 공중 이동과 복귀 시 검증 기준, 종료 정리를 함께 보강해야 한다. 이 항목은 아직 구현 완료가 아니다.

#### 이번 후속 검사 결과

| 검사 | 결과와 범위 |
|---|---|
| 전체 C# 빌드 | 기존 slnx/AdditionalUnitySources.targets 명령, 오류 0 / 기존 CS0414 경고 6개. 제한된 실행의 Windows SDK 읽기 오류 후 기존 승인 범위의 빌드로 검사. Unity-custom NUnit에 없는 `NonParallelizable` 속성 제거 및 로그 전용 미사용 필드 정리 후 통과 |
| 순수 CLI | 빌드 오류 0 / 경고 0, 최종 **56,116 assertions PASS**. 기존 56,044 유지 + 실제 room flow/banner helper 및 작업 순서·실패 후 진행 검사 72개 |
| NUnit | `ConnectedRosterUiTests` 6개, `RoomFlowGenerationTests` 5개, `RelayPollingTests` 추가 4개를 작성·컴파일. Unity 실행은 미완료이며 CLI 검사 수에 포함하지 않음 |
| 정적/독립 검토 | 이번 runtime 경로 diff 공백 및 신규/수정 테스트의 공백·충돌 표시 검사 통과. 독립 검토에서 발견한 중복 보존 범위와 Relay 종료 누락을 보강 |

현재 코드로 상단 현황 갱신을 보강했지만 실제 호스트/원격 입·퇴장, 강제 종료, 인증 거절, 재접속, 씬 전환의 화면 결과는 검증 대기다. MCP·Unity 실행·PlayFab 배포를 하지 않았으며 전체 재접속 전투 상태/8인 두 플랫폼 성능 완료로 표시하지 않는다.

### 다음 우선 작업: 호스트 종료 후 남는 방 — 2026-09-07

제보 확인 당시 상태: **버그 확인·미수정**. 아래 내용은 수정 전 원인과 정한 완료 기준이다. 이후 사용자 진행 승인으로 구현한 내용은 다음 `호스트 종료 방 만료 구현`에 기록한다. 실제 서비스 배포는 아직 하지 않았다.

- `PlayFabBattleManager.OnApplicationQuit`는 비동기 `LeaveCurrentRoom`을 요청한다. 종료 시 콜백 실행이나 요청 완료를 보장하지 못하며 프로세스 강제 종료에서는 이 경로 자체가 실행되지 않을 수 있다. 지난 Relay 종료 수정은 실행 중인 클라이언트의 Mirror 정리를 보강한 것으로 이 버그를 해결하지 않는다.
- 두 CloudScript의 `LeaveRoom`은 호출자 멤버만 제거하고 남은 멤버가 0명일 때 registry를 지운다. 호스트 이탈 자체를 방 종료로 처리하지 않으며, 호스트의 탈퇴 요청이 도착하지 않으면 남은 참가자가 모두 탈퇴해도 호스트 멤버가 남을 수 있다.
- `loadActiveRoomInfos`에는 호스트 생존·방 만료 검사가 없고 저장된 양수 인원으로 표시한다. `JoinRoom`에도 호스트가 살아 있는지 확인하는 검사가 없다. 연결 proof의 60초 TTL은 참가자 인증 기한이며 방 수명이 아니다.
- 공개 목록 fallback은 `_knownRooms`도 합치므로 서버 목록에서만 숨기는 수정으로는 충분하지 않다. 목록 캐시/fallback과 입장 승인까지 같은 방 만료 정책을 적용해야 한다.

우선 구현 방향은 인증된 호스트의 주기적 생존 신호, 서버 시간 기준 유효 기한, 호스트의 정상 퇴장 시 명시적 방 종료, 만료 방의 목록 제외/입장·인증 거절, 남은 참가자의 안내/로비 복귀다. heartbeat 간격과 만료 시간은 아직 확정하지 않았다. 오래된 데이터의 실제 삭제는 조회 시 정리 또는 서버 정기 정리 방식과 갱신/정리 경합을 함께 설계해야 하며, 시간 필드만 추가했다고 자동 삭제되는 것은 아니다.

완료 기준은 다음과 같다.

1. 창 닫기·강제 종료·네트워크 단절 후 방이 유효 기한과 목록 갱신 지연 범위 안에서 사라지고 새 참가를 거절한다. 기존 참가자도 연결 종료를 안내받고 로비로 복귀한다.
2. 참가자가 남아 있어도 호스트 정상 퇴장은 방을 닫는다. 호스트가 살아 있고 유효 기한 내 heartbeat가 도착하는 방은 유지한다.
3. 참가자가 호스트 대신 방 수명을 늘리거나 다른 방을 닫을 수 없다. 만료된 방이 늦은 응답/등록 재시도/목록 fallback으로 되살아나지 않는다.
4. 정상 지연·일시 중단/WebGL 백그라운드에서의 정책을 확인하고, 정리 실패·동시 갱신이 다른 유효 방을 삭제하지 않는다. 조회 시 정리와 실제 데이터 삭제 보장 범위를 구분한다.
5. 로컬 시간/서비스 모의 검사와 별도로 수정 CloudScript·클라이언트를 테스트 환경에 함께 적용해 실제 종료/목록/재참가를 검증한다. 로컬 코드 작업은 Unity MCP 없이 가능하지만 서비스 반영과 실검증은 별도다.

이후 남은 우선 작업은 재접속 전투 상태 보존(단절 중 캐릭터 정책 선택 대기), 서비스 응답 미도착 시 대기 종료/복구, 동일 계정의 연결별 멤버십 경합과 경기 결과 영속 제출/보상, 실제 Unity 다중 접속 및 8인 Windows/WebGL 성능 검증이다.

### 호스트 종료 방 만료 구현 — 2026-09-07

상태: **로컬 코드 구현·검사 통과, 서비스 배포/Unity 검증 대기**. 별도 전용 서버나 Unity MCP 연결 없이 로컬 변경을 진행했다.

- 호스트는 등록 승인 후 15초 간격으로 `HeartbeatRoom`을 호출한다. 서버 시간으로 마지막 갱신부터 60초 동안만 방을 유효하게 인정한다. 일시 실패는 5초 뒤 재시도하고, 원래 기한을 넘으면 호스트도 게임 연결을 닫는다. 첫 등록 응답 미도착도 60초를 넘겨 호스트만 계속 실행되지 않도록 제한했다. 긴 일시 중단/WebGL 백그라운드로 기한을 넘긴 경우 방을 새로 만들어야 한다.
- 서버 두 파일에 owner-only heartbeat를 추가해 게임 핸들러는 **12개**다. `GLOBALROOMREGISTRY`의 방 메타데이터와 별도로 `ROOMLEASE_<roomId>`에 만료 시각, `ROOMCLOSED_<roomId>`에 폐쇄 기록을 저장한다. 참가자의 인원 갱신은 이 두 키를 쓰지 않는다. 명시적 폐쇄가 늦은 heartbeat/메타데이터 쓰기로 해제되지 않는다.
- 정상 호스트 퇴장은 참가자가 남아 있어도 먼저 방을 닫고 메타데이터를 제거한다. 멤버 제거가 실패해도 폐쇄 기록이 유지된다. 만료/폐쇄 방은 두 목록 API, 입장, 참가자 승인/호스트 검증, Relay 변경에서 거절하며 같은 방 ID의 재등록으로 초기화할 수 없다. 숫자가 비정상적으로 크거나 서버 시각에서 60초를 초과한 만료 값도 거절한다.
- 실제 방 그룹/만료 기록을 주기적으로 물리 삭제하는 작업은 추가하지 않았다. 만료 방은 서버 조회에서 제외되며 폐쇄/만료 기록을 남겨 지연 요청의 재생성을 막는다. 운영 정리 시에도 관리 API는 유효 방 ID의 폐쇄 기록을 유지한다. 보존 기록의 장기 보관량과 폐기 정책은 별도 운영 작업이다.
- `HostRoomLease`는 서버 응답의 남은 수명을 요청 시작 당시 로컬 단조 시각으로 환산한다. 응답 지연이 로컬 기한을 연장하지 않으며 만료/중단 후 늦은 성공도 이전 방을 복구하지 않는다. 매 프레임 API나 계정 문자열 정규화를 하지 않고 기한/전송 여부만 확인한다.
- 클라이언트 방 목록은 유효 기한을 포함한 CloudScript 응답만 사용한다. `_knownRooms`를 합치거나 빈 결과에 이전 목록을 되살리는 동작, 만료 검사가 없는 공개 SharedGroup fallback을 제거했다. 빈 결과도 캐시에 반영하며 유효 기간이 끝난 캐시는 표시하지 않는다. 조회 응답이 10초 안에 오지 않으면 목록을 비우고 다음 조회를 허용하며, 기한 이후 응답은 `Update`보다 먼저 도착해도 성공으로 적용하지 않는다.
- 목록 자동 갱신은 실제 시간 기준 최대 5초 간격이다. 이전 씬의 60초 설정도 런타임에서 제한한다. 서버 만료 시점과 화면 반영은 같지 않으며 정상 응답 기준 목록 갱신 간격·서비스 지연이 추가된다. 요청 실패 시 오래된 방을 표시하는 대신 다음 조회로 복구한다.
- 기존 Mirror 종료/로비 씬 전환을 유지하고, 종료 안내를 `LastRoomNotice`에 보관해 새 로비 UI에서도 표시한다. 다른 방 시작 시 안내를 초기화한다.

설정/전환 절차는 [Tools/PlayFab/README.md](Tools/PlayFab/README.md)에 기록한다. 새 CloudScript와 새 클라이언트를 함께 적용해야 한다. 이전 방은 유효 기한이 없으므로 새 목록에서 제외된다. Client API 직접 쓰기 차단과 registry 기존 멤버 정리 전제는 유지하며, 이번 로컬 변경이 운영 정책 반영을 대신하지 않는다.

SharedGroup은 원자적 비교 후 쓰기를 제공하지 않는다. 진행 중 요청의 읽기/쓰기와 만료가 겹치는 모든 서비스 실행 순서를 트랜잭션처럼 보장하지 않으며, 종료된 방의 단조로운 폐쇄 기록과 참가자 쓰기 분리로 재등장을 막는다. Relay 준비 SDK 자체가 끝나지 않는 경우, 응답 없는 같은 멤버십 변경 큐, 다른 장치의 동일 계정 접속 경합은 별도 후속 과제다.

#### 방 만료 최종 검증

| 검사 | 실행 결과와 판정 범위 |
|---|---|
| 전체 C# 빌드 | 기존 slnx/AdditionalUnitySources.targets 명령, 오류 0 / 기존 CS0414 경고 6개. 새 runtime와 NUnit 소스 컴파일. Unity/Mirror 코드 생성·실행 검증 아님 |
| 순수 CLI | 빌드 오류 0 / 경고 0, **56,214 assertions PASS**. 기존 56,116 유지 + 실제 `HostRoomLease` 초기 등록/만료/지연 ACK/중복 갱신/실패/비유한 입력 검사 98개 |
| 기존 CloudScript 회귀 | `NetworkProfileRoomCapacity.test.js` 두 스크립트 PASS. 기존 권한·정원·재시도·참가자 proof·관리 기능 유지. 호스트 퇴장 정책이 달라진 부분은 새 계약에 맞춰 검사하고 비회원/마지막 멤버 경계는 유지 |
| 방 만료 Node 회귀 | `RoomLease.test.js`: **26개 시나리오, 564 assertions PASS**. 두 스크립트의 15초 갱신/60초 경계, 강제 종료, 게스트 권한, 비정상 미래 시각, 폐쇄·지연 쓰기, 등록 중 비회원 퇴장 등을 FakeDate/모의 API로 검사 |
| NUnit 소스 | `RoomAvailabilityTests` 9개 작성·컴파일. 빈 서버 목록/캐시 삭제, 응답 역전/중복, 기한, Update 전 늦은 응답, 콜백 격리·재진입, 만료 캐시를 검사하며 Unity 실행은 대기. CLI 수에 포함하지 않음 |

독립 검토에서 응답 기한의 실행 순서 의존성, 초기 등록 응답 누락, 비정상 미래 만료 시각, 등록 중 그룹이 아직 없을 때 비회원이 `LeaveRoom`으로 다른 방을 닫는 경계를 찾아 보강했다. 마지막 경우는 비회원 요청이 목록/lease/폐쇄 상태를 바꾸지 않고 원래 호스트의 등록이 끝나는 교차 실행 검사로 확인했다. 실제 PlayFab 동시성 전체를 증명하는 검사는 아니다.

### 초기 최적화·구조 항목 재대조와 추가 잔여 작업 — 2026-09-07

초기 O01–O10의 링 버퍼·팝업 풀·NonAlloc 조회·스탯 캐시·목록 diff·채팅 등록·전송 버퍼·UI 갱신 통합은 코드에 반영되어 있다. R01–R04의 연출/결과 화면/프로필/스탯 적용 절차 분리도 해당 완료 기준에 맞춰 반영했다. **코드 반영을 실제 성능 향상이나 전체 구조 정리 완료로 간주하지 않는다.** Unity 생명주기/물리/Mirror 실행, 프리팹 참조, 8인 Windows/WebGL의 60 FPS 및 전후 비용 비교는 여전히 미완료다.

재대조에서 아래 후속 항목을 구체적으로 확인했다. 이번에는 유령방 수정에 집중했으며 이 항목들의 runtime 코드는 변경하지 않았다.

| 우선순위 | 추가 작업 | 현재 근거와 완료 기준 |
|---|---|---|
| P2 | 경기 시계 오차 | `BattleStateMachine.MatchFlowRoutine`의 1초 대기 후 1 차감은 호스트 프레임 지연만큼 경기 길이를 늘린다. 서버 종료 시각을 기준으로 남은 시간을 계산하고 hitch·일시 중단 후에도 종료를 한 번만 처리해야 함 |
| P2 | 로비 UI 탐색/구독 | `LobbyUIManager.CoAutoDiscovery`의 반복 전체 객체 검색, 최초 로컬 플레이어 1회 구독과 현재 플레이어에서의 구독 해제가 남아 있다. 명시적 참조/등록과 구독 원본 보관, 플레이어 교체 시 재바인딩으로 탐색·구독 누수 제거 |
| P2/P3 | 빈 스탯 프리셋과 HUD 결합 | `StatCustomizerController.CoInitialSyncWithDB`는 합계가 0인 정상 프리셋을 미로드로 취급해 반복 대기하며, 대상 탐색/`PlayerHUD.Bind`까지 소유한다. 로드 완료와 값 0을 구분하고 기존 바인딩 책임으로 연결을 옮겨야 함 |
| P3 | 남은 전투 실행 책임 | `PlayerCombat`에는 시전·쿨다운·잠금·독 stack 갱신이 함께 남아 있다. 네트워크 API를 유지하며 개별 실행 상태/효과를 점진적으로 분리하고 중단·만료·재접속 회귀 유지 |
| P3 | 경기 입력/스폰 및 오래된 경로 | `BattleStateMachine`의 결과 Canvas 분리는 반영했지만 입력·스폰 배정은 남아 있다. 호출되지 않는 이전 순간이동 RPC/부활 stub를 확인·정리하고 입력/스폰 책임은 별도로 분리할 수 있음 |

백엔드/재접속 잔여 항목과 별도로 위 실제 코드 문제를 다음 작업 목록에 유지한다. 단순히 파일 줄 수가 크다는 이유로 한 번에 분해하거나 성능 수치를 추정하지 않는다.

### 남은 초기 개선 항목 일괄 수정 — 2026-09-07

사용자가 남은 작업을 추가 질문 없이 일괄 진행하도록 승인했다. 위 목록의 `미수정`/`선택 대기`는 당시 상태이며, 아래가 후속 상태다. 호스트 신뢰·최대 8명·Windows/WebGL 60 FPS 기준은 유지한다. 소스 변경과 일반 C# 검증을 Unity 실행 또는 서비스 배포 완료와 구분한다.

| 테마 | 완료 기준 | 반영 내용 |
|---|---|---|
| T01 경기 시계 | 호스트 hitch 후 실제 경과 시간이 차감되고 종료가 한 번만 발생 | `MatchClock`이 절대 서버 종료 시각을 소유한다. `BattleStateMachine`은 `NetworkTime.time`으로 남은 표시 초를 계산한다. 매초 대기 객체 생성/누적 차감을 제거하고 서버 종료 시 루틴을 정리한다 |
| T02 로비 탐색·구독 | 유휴 전체 객체 검색 없음, 이전 구독 원본 해제, 플레이어/방 서비스 교체 반영 | `StatManager.LocalChanged`/`PlayFabBattleManager.InstanceChanged`로 교체를 전달한다. `LobbyUIManager`가 실제 구독한 원본을 보관하며 반복 탐색 코루틴을 제거한다. 기존 직렬화 참조와 씬 진입 시 제한된 fallback 탐색은 유지한다 |
| T03 프로필·스탯 UI | 정상 0 프리셋을 로드 완료로 인식, HUD 연결과 편집 분리, 적용 응답 누락에서 복구 | `GlobalDataManager`가 로드 상태를 먼저 바꾸고 이벤트를 보낸다. `StatCustomizerController`는 프로필/프리셋/로컬 플레이어 이벤트를 사용한다. `LocalPlayerProfileBinding`이 HUD 원본 교체/해제를 소유하고 서버 확정 스탯 자동 재주입을 막는다 |
| T04 전투 실행 책임 | 중단 시 임시 동작을 정리하면서 서버 쿨다운 유지, 독 만료·중첩을 독립 검사 | `CombatSkillExecution`, `CombatActionLocks`, `PoisonStackCollection`, `CombatRequestSequences`로 시전·활성/쿨다운 전이, 예측 잠금, 독 스택, 요청 번호 규칙을 분리했다. Mirror API/직렬화 필드는 `PlayerCombat`에 남긴다 |
| T05 경기 입력·스폰 | 입력 소비가 한 경로이며 등록된 시작점으로 서버만 배치 | Enter 처리는 기존 `GameInputController`에서 경기의 재시작 요청으로 전달한다. 서버는 인증된 현재 방 플레이어만 재시작 요청을 받는다. `BattleSpawnPlacement`가 Mirror 시작점/서버 spawned 플레이어를 정렬해 배치한다. 참조가 없는 이전 순간이동 RPC·부활 stub 및 사용되지 않는 BNM 씬 설정 필드를 제거했다 |
| T06 응답 없는 서비스 | 화면 대기가 끝나고 늦은 응답이 다음 요청을 오염하지 않으며 미확정 쓰기가 겹치지 않음 | 방 변경에 15초 기한/계정·방별 미확정 요청 격리를 추가했다. Relay 준비는 취소와 30초 제한을 적용하고 결과를 지역 변수에 보관하다 최신 세대일 때만 transport에 반영한다. SDK 공유 초기화는 중복 실행하지 않는다 |
| T07 재접속 전투 상태 | 기존 몸/HP·스탯·효과·독·쿨다운·사망 번호 유지, 접속 인원 표시와 몸 보존 분리 | 아래 정책과 수명 처리를 적용했다. 실제 Mirror 재귀속/호스트·참가자·관찰자 확인은 실행 대기다 |

#### 재접속 정책 및 상태 수명

- 재접속 중 피해 회피를 막기 위해 **경기 종료까지 캐릭터를 남겨 공격받게 하고 인증된 같은 계정은 같은 몸으로 복귀**하도록 정했다. 질문을 반복하지 말라는 승인과 기존 공정성 기준에 따른 구현 선택이다. 정상 퇴장과 비정상 단절 모두 경기 중에는 같은 정책을 사용한다. 호스트 종료 후 호스트 자체를 복구하거나 자동 재접속하는 기능은 아니다.
- `BattleNetworkManager`는 최초 스탯 승인을 마친 참가자만 `RemovePlayerForConnection(..., KeepActive)`로 보존한다. 아직 초기화하지 않은 연결, 경기 외 퇴장, 서버 종료/씬 전환은 기존 파괴 경로를 사용한다. 기존 인스턴스가 남으므로 HP/실드, 독의 대상 참조, 시전 코루틴, 공격자와 피격자의 기록을 새 객체로 부분 복사하지 않는다.
- 서버는 인증된 동일 방/계정만 `AddPlayerForConnection`으로 재귀속한다. 단절 캐릭터도 해당 경기의 8인 자리를 예약한다. 새 계정으로 반복 입·퇴장하여 대상 객체가 무제한 늘어날 수 없다. 경기 중 만석에서 이탈자의 자리는 본인 재접속용이며 다른 계정의 새 참가가 거절될 수 있다.
- `ScoreSystem`의 연결 여부 SyncVar와 `MatchLedger.SetConnectionState`를 추가했다. 상단 현황/실시간 목록에는 실제 접속한 플레이어만 남고, 단절된 몸은 계속 피해/독/처치 기록의 대상이 된다. 같은 netId 재귀속 시 사망 처리 번호를 초기화하지 않는다. 최종 결과는 단절 여부와 기록을 고정한 뒤 보존 캐릭터를 정리한다.
- `PlayerManager`는 단절 중 서버가 중력·승인된 강제 이동·도발 이동을 처리한다. 도발 이동에도 기존 이동 잠금을 적용한다. 복귀 시 이동 epoch와 검증 위치를 갱신하며, 효과를 지우는 일반 순간이동 함수를 사용하지 않는다. `ServerMovementValidator.Rebase`는 공중에서 마지막 접지 높이·시각을 보존하므로 재접속으로 점프 높이·체공 시간 제한을 초기화할 수 없다. 남은 이동 배율/잠금은 원래 서버 만료 시각으로 새 소유자에게 전달한다. 서버 시전 잠금은 전투 상태에서 복원하고 이전 클라이언트 예측 잠금은 복사하지 않는다.
- 활의 끝나지 않은 차징/예약은 단절 시 취소하고 기존 서버 발사 쿨다운은 유지한다. 단절 중 새로 적용된 도발·해제·대상 이동을 서버 전투 처리에도 반영한다. 공격/스킬 요청 번호는 서버 상태에서 이어서 시작해 새 소유자의 첫 요청이 과거 번호로 거절되거나 관전자에서 누락되는 것을 막는다.
- `HealthSystem`의 서버 부활 허용 시각을 동기화한다. 새 클라이언트의 부활 안내는 서버의 남은 시간으로 생성하고, 이미 지난 5초를 재접속 때문에 다시 기다리지 않는다. 저장 프리셋 자동 주입은 `HasServerStats`일 때 생략해 유지 중인 서버 스탯을 덮지 않는다.

#### 외부 구현/검증으로 남는 경계

이 변경은 PlayFab/Relay SDK 내부 요청 자체를 취소하는 보장이 아니다. 응답이 없는 미확정 방 쓰기는 같은 키에서 격리하고 다른 방 작업을 허용한다. 실제 응답이 영원히 오지 않거나 서비스가 실패의 적용 여부를 보장하지 않으면 해당 키의 안전한 재사용을 로컬 코드로 보장할 수 없다.

호스트 생존 신호는 별도 정책이다. `HeartbeatRoom`은 lease 키만 갱신하고 멤버십/폐쇄 기록을 변경하지 않으므로 15초 대기 실패 뒤 기존 5초 간격으로 재시도를 허용한다. 늦은 ACK는 로컬 수명을 다시 적용하지 않는다. 이 구분이 없으면 한 번의 일시 통신 오류가 멤버십 격리를 만들어 살아 있는 호스트도 60초 뒤 닫히므로 최종 검토에서 분리했다. Join/Register/Leave/Relay 코드 변경은 기존 격리를 유지한다. 동시 heartbeat의 SharedGroup 쓰기를 원자적인 최대 만료 시각 갱신으로 보장하는 변경은 아니다.

초기 S11의 서버 발급 경기 ID·영속 제출/outbox·원자적 중복 보상, 다른 장치의 동일 계정에 대한 연결별 멤버십 소유권/원자적 fencing, 운영 Client API 정책·기존 registry 멤버 이전은 외부 구현/설정이 필요하다. SharedGroup의 account 단위 멤버 제거를 challenge 필드 추가만으로 원자적 연결 임대라고 표시하지 않는다. 이번 턴에 PlayFab 배포/정책 변경이나 새 백엔드 서비스를 실행하지 않았다.

Unity/Mirror 코드 생성·NUnit 실행·실제 재접속/단절·프리팹 및 씬 확인, 8인 Windows/WebGL의 프레임 p95 16.67ms 이하와 최적화 전후 GC/CPU/전송 비용 측정은 별도 검증 대기다. 아래 최종 검사 결과에 실행 범위를 구분해 기록한다.

#### 추가 적용·프로필 요청 복구

- `StatManager.RequestApplyStats`에 10초 실제 시간 기한을 추가했다. 응답 콜백에서도 기한을 확인하며, 비활성/로컬 소유권 종료/연결 종료/파괴 시 대기를 실패로 완료한다. 요청 ID·콜백·타이머를 먼저 비운 뒤 결과를 알리므로 콜백 안 새 요청과 늦은 과거 응답이 섞이지 않는다. 서버 SyncVar는 계속 권위 상태를 반영한다.
- `NetworkProfileRepository`의 요청 수명은 실제로 연결된 순수 `ProfileRequestQueue`로 분리했다. 프로필 읽기는 15초와 요청 ID/계정 세대를 검사하고, 같은 계정의 옛 중복 응답이 다음 로드를 완료하지 못한다. 정상 빈 프로필과 실패를 계속 구분한다.
- 저장은 요청 시 직렬화한 문서/복사한 payload로 직렬 실행한다. 15초 동안 응답이 없거나 네트워크 오류 등 적용 여부가 불명확하면 현재/대기 호출자에게 실패를 알리고, 같은 계정의 실제 쓰기는 확인될 때까지 격리한다. 같은 repository에서 계정을 바꿨다 돌아와도 격리를 유지한다. SDK 어댑터는 확인 가능한 요청 거절과 HTTP/연결 오류를 구분하고, 확인된 거절 뒤의 정상 재시도는 유지한다.
- 계정 전환/비활성 시 로드·저장 콜백을 실패로 완료하며 개별 콜백 예외가 다른 완료 통지를 막지 않는다. 비활성/파괴된 `PlayFabBattleManager`의 새 프로필 진입을 거절하여 정리 콜백 재진입으로 새 무한 대기를 만들지 않는다.
- 스킬 복귀는 시전이 끝난 뒤에도 남은 애니메이션/입력 기한을 별도로 보존한다. 승인된 입력 source와 클라이언트 예측 source를 분리해 거절/취소가 서버 잠금을 지우지 않는다. 첫 reliable Spawn/RPC보다 unreliable 시간 동기화가 늦는 경우에는 메시지의 서버 배치 시각을 복원 시각의 하한으로 사용한다. 부활 5초/애니메이션이 처음부터 재시작되는 경계를 보강했다.
- 독은 마지막 tick 후 만료되었더라도 새 적중 시 만료 시각을 다시 검사하여 1중첩부터 시작한다. 공격은 시퀀스·문맥·서버 중복 방지 등록 및 원격 승인 요청을 애니메이션 첫 프레임 이벤트보다 먼저 준비한다.

#### 일괄 수정 최종 검사 결과

| 검사 | 결과 및 범위 |
|---|---|
| 전체 C# | `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets` 최종 오류 **0** / 기존 CS0414 경고 **4개**. Unity import/Mirror 코드 생성·실행을 대신하지 않음 |
| 순수 CLI | `dotnet build Tools/Tests/LogicRegression/LogicRegression.csproj -v:q --configfile Tools/Tests/LogicRegression/NuGet.Config` 오류 0/경고 0. 실행 결과 **56,379 assertions PASS** = 이전 56,214 유지 + 이번 165. MatchClock/retained ledger/부활 시각, 전투 실행/독/요청 번호, 방 요청 기한·생존 신호 재시도, 프로필 큐의 실제 production source를 링크하여 검사 |
| CloudScript | `NetworkProfileRoomCapacity.test.js`의 두 파일 권한·정원·인증 회귀 PASS, `RoomLease.test.js`의 **26개 시나리오/564 assertions PASS**. 이번 턴에는 CloudScript 내용 추가 변경·외부 배포 없음 |
| 새 NUnit | `RetainedPlayerStateTests` 6 + `LocalProfileUiBindingTests` 12 + `CombatExecutionStateTests` 19 + `RoomServiceTimeoutTests` 5 + `ProfileRequestLifecycleTests` 15 = **57개 사례 작성·컴파일**. 기존 전투 생명주기 3개와 프로필 세션 초기화 검사도 보강. 실제 Unity 실행은 미완료, CLI assertions에 포함하지 않음 |
| 정적/독립 검토 | 변경 runtime의 `git diff --check` PASS, 신규 소스/.meta 존재 및 공백·충돌 표시 검사 PASS. Battle 씬의 GameInputController 활성 참조와 제거 함수의 serialized 호출 참조 0건 확인. 검토에서 단절 도발의 이동 잠금 누락, 첫 시간 메시지 역전, 미확정 저장의 격리, 생존 신호 재시도, 공중 재접속의 이동 제한 보존을 보강 |

남아 있는 CS0414는 FollowCamera `_moveSmoothTime`/`_rotSmoothSpeed`, WebGL 조건 컴파일의 UnityRelayTransport `_connectionType`, DamagePopup `_moveYSpeed`다. 사용자 FollowCamera 값과 기존 공백은 수정하지 않았다. 불필요해진 StatCustomizer 초기화 플래그와 BNM 씬 설정을 제거해 기존 6개에서 4개로 줄었다.

**최종 상태: 초기 후속 T01–T07 및 로컬 응답 복구 구현·로컬 검사 완료, Unity/서비스/성능 검증 대기.** 테스트 타이틀 배포/운영 설정, S11 영속 경쟁 기록·원자적 보상과 여러 장치 간 멤버십 원자성은 외부 구현·검증 대기다. 전체 프로젝트 개선이 검증 완료됐거나 두 플랫폼에서 60 FPS를 달성했다고 선언하지 않는다.

### 초기 기준 재점검 및 신규 개선 검토 — 2026-09-07

사용자의 후속 요청에 따라 초기 기준과 실제 호출·프리팹 경로를 다시 대조했다. 다음 6곳의 누락을 추가로 수정했다. 앞 절의 완료는 당시 로컬 구현·검사 범위이며 이번 발견을 이미 검증된 것으로 소급하지 않는다. 상세 신규 목록은 [FOLLOW_UP_REVIEW.md](C:/Github/Battle_PVP/FOLLOW_UP_REVIEW.md)에 우선순위·재현 조건·근거·완료 기준과 함께 기록했다.

| 항목 | 파일과 변경 | 보존할 정상 경계 |
|---|---|---|
| Q01 · S06/C05 서버 입력 제한 | `PlayerManager`, `ServerMovementValidator`, 새 `MovementControlHistory`로 서버 이동 효과·공격/앉기 속도·Move/Jump/Crouch 잠금 이력을 연결. 앉기 거절 시 owner 상태 원복, 원격 emote도 서버 잠금/종료 수명 생성 | 기존 최대 0.5초 지연 창, 잠금 직전 이동의 1회 허용, 진행 중 비행/착지/경사·승인 강제 이동·재접속. 이력은 고정 64칸이며 패킷 수가 이동 예산을 늘리지 않음 |
| Q02 · T07 재접속 이동 시간 | 강제 이동의 남은 시간을 계산할 때 reliable 배치 시각을 NetworkTime의 하한으로 사용 | 첫 시간 동기화가 늦어도 남은 0.1초가 서버 가동 시간으로 늘지 않음. 만료된 효과는 재시작하지 않음 |
| Q03 · S09 손상 데이터 구분 | `NetworkProfileRepository`에서 존재하지만 잘못된 legacy 값·일부 스탯만 있는 묶음·잘못된 플래그/인덱스·V1 누락 필드 거절 | 정상 빈 계정, 완전한 legacy 4키와 프리셋/0값/V1 문서 유지. 손상 데이터는 자동 0으로 저장하지 않고 실패·복구 대상으로 남김 |
| Q04 · T06 상위 완료 통지 | `GlobalDataManager`가 요청별 대기자 분리 후 상태 commit, 개별 이벤트·완료 callback 예외 격리, reset/disable 실패 완료, 세션/통지 버전 적용 | 재진입 새 로드·동기 성공·계정 전환을 이전 완료가 덮지 않음. 동일 요청 중복 응답과 한 소비자의 예외가 다른 대기자에 영향을 주지 않음 |
| Q05 · T03 로비 0 프리셋 | `LobbyPlayerActivator`의 합계 0 검사 대신 `HasLoadedPlayerStats`로 실제 적용 여부 판단 | 미로드에는 scene stats 보존, 로드 완료 빈 슬롯은 지정된 캐릭터의 스탯/정체성 초기화, HUD 바인딩 유지 |
| Q06 · S05 부위 이력 | `ServerPoseHistory`가 승인 root 위치·회전을 저장하고 collider bounds를 같은 기준으로 보수적으로 변환. 주기 원격 hold와 실제 로컬 pose를 구분 | 동일 시각은 전체 슬롯 교체, 링 순환·지연 승인 뒤 hold 보정, host 실제 기록 보존, reset·비활성 collider 제외. 실제 Transform/Collider를 이동하지 않으며 과거 전체 뼈 애니메이션 rollback 구현은 아님 |

#### 재점검 최종 검사

| 검사 | 실행 결과와 범위 |
|---|---|
| 전체 C# | 기존 slnx + `AdditionalUnitySources.targets` 명령으로 최종 **오류 0 / 기존 CS0414 경고 4개**. 마지막 잠금 전 이동 구간 보강까지 포함. Unity import/Mirror 코드 생성·실행 대체 아님 |
| 순수 CLI | 기존 csproj 빌드 오류 0/경고 0. **56,429 assertions PASS** = 직전 56,379 + 이번 50. `MovementPermissionRegression`이 production 이동 이력/비행/복원 시각을 링크하여 검사. 실제 프레임 성능 측정 아님 |
| 새 NUnit | 이동 제한 8 + 프로필 데이터 24 + 상위 완료 통지 7 + 부위 이력 10 + 기존 로비 UI 검사 추가 3 = **52개 사례 작성·컴파일**. Unity 실행 미완료, CLI 수에 합산하지 않음. 기존 정상 legacy fixture도 보강 |
| 독립/정적 검토 | 실제 로비/화살/순위/위젯 프리팹 연결 확인, 부위 이력 시간·소유자 경계를 독립 검토 후 반영. 변경 경로 공백/충돌 표시, 신규 `.meta`, 리뷰 파일 링크 검사 |
| 외부 작업 | Unity/MCP·PlayFab 배포/정책·CloudScript 변경 없음. 이전 CloudScript 통과 수는 당시 실행 이력이며 이번에 재실행한 결과로 제시하지 않음 |

신규 N01–N11은 별도 수정 대기다. 인증 전 채팅/전송 제한, 접속 중 강제 이동 실행 보장, 화살 장애물, 로그인 경합을 P1로 두고, 방 UI 예외/서비스 교체의 저장 수명/문자열 서식, 위젯 복원·처치 알림 풀·추가 책임 분리 순서로 제안했다. 기존 S11 및 실제 Unity/8인 Windows/WebGL 검증 대기는 유지한다. **이번에 보강한 6곳의 로컬 구현·검사와 신규 11개 제안의 수정 완료를 혼동하지 않는다.**

### 후속 P1 수정 N01–N04 — 2026-09-07

사용자의 다음 수정 진행 요청에 따라 신규 검토 목록의 P1 네 항목을 처리한다. 위 절의 N01–N11 대기 표시는 발견 당시 이력이다. N05–N11은 이번 범위에 포함하지 않는다.

| 항목 | 변경과 적용 정책 | 완료 기준 / 필요한 실행 검증 |
|---|---|---|
| N01 채팅 권한·빈도 | `BattleChatNetwork`는 Mirror 인증 및 현재 방 인증 정보, 실제 연결·spawn·avatar 소유권·접속 상태를 확인한 뒤 방송한다. 이름은 서버 `ScoreSystem`만 사용한다. `ChatRateLimiter`의 연결별 bucket은 연속 3개, 초당 1개 충전, 초과 요청 무시다. 연결/서버 종료 시 삭제하고 handler 재등록으로 충전하지 않는다. | 인증 전/다른 방/과거 연결/미생성 avatar 요청 전파 0회, 위조 이름 무시, 정상 채팅과 재접속 유지. 실제 Mirror handler·8인 채팅 실행 대기 |
| N02 서버 강제 이동 | kick/roll 구간은 `ServerForcedMotion`의 서버 시각과 `PlayerManager`의 CharacterController가 이동·벽/경사 충돌·중력을 처리한다. owner 좌표 제출은 구간 동안 거절하고 크기·시각·세대를 검사한 방향/점프 입력만 받는다. 종료 reliable 위치와 새 세대로 일반 이동을 복원한다. | owner RPC 무시·제자리/역행 좌표로 넉백 취소 불가. 지연/단절·복귀/효과 교체·벽·경사·공중 복원 확인. 실제 물리·네트워크 실행 대기 |
| N03 화살 장애물 | 실제 BoxCollider 모양으로 시작 겹침과 이동 구간 BoxCast를 서버에서 검사한다. 가장 가까운 유효 충돌에서 종료하며 동일 거리면 고체를 우선한다. 발사자/비활성 collider/장식 trigger/무시한 layer·collider 쌍을 제외하고 부위·더미 피해 규칙을 유지한다. 재사용 query는 포화 시 재조회한다. | 벽 뒤 피해 0회, 노출 대상 피해 1회, 얇은 벽·낮은 FPS·시작 겹침·다수 collider에서 첫 충돌 유지. 실제 프리팹 물리 실행 대기 |
| N04 인증 요청 수명 | 로그인/회원가입 SDK 요청마다 별도 인증 context를 사용하고 최신 승인 로그인만 공유 세션에 복사한다. 요청 ID·응답/프로필 기한·UI 활성 수명으로 중복/늦은 완료·중복 씬 전환을 막는다. 회원가입 응답의 인증 context는 채택하지 않는다. 현재 세션의 일시 프로필 실패는 로그인 성공과 로비 재시도 계약을 유지한다. | A/B 역전·등록/로그인 교차·중복 클릭·실패/timeout 재시도·화면 종료에서 SDK 계정·닉네임·프로필·성공 이벤트 일치. SDK 실통신·씬 실행 대기 |

넉백의 중복 효과 정책은 기존처럼 최신 효과로 교체한다. 교체 전에 이전 효과의 미소비 시간을 처리하며, 단절/재접속으로 종료 시각이나 강제 이동량을 다시 만들지 않는다. 이 구간에는 소유자도 서버 충돌 결과를 표시하므로 지연 환경의 조작감은 실제 다중 접속으로 확인해야 한다. N07의 TMP 사용자 문자열 서식 문제는 이번 채팅 권한 수정과 별도로 남아 있다.

#### P1 통합 검증

| 검사 | 결과와 범위 |
|---|---|
| 전체 C# | `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets` 최종 **오류 0 / 기존 CS0414 경고 4개**. 예비 빌드에서 채팅 NUnit의 Mirror internal handler 접근 3곳을 reflection으로 수정한 뒤 통과 |
| 순수 CLI | 기존 csproj 빌드 오류 0/경고 0. 실행 **56,632 assertions PASS** = 직전 56,429 + 이번 203. production `ChatRateLimiter`·`ServerForcedMotion`·`AuthenticationAttemptState`와 기존 helper를 직접 링크. 실제 FPS/물리/SDK 실행 검사 아님 |
| 새 NUnit | `ChatAuthorityTests` 15 + `ServerForcedMotionTests` 9 + `BowProjectileCollisionTests` 10 + `AuthenticationFlowTests` 14 = **48개 사례 작성·컴파일**. 등록된 Mirror 인증 wrapper, 연결/이름/전송 예산, 실제 CharacterController와 화살 프리팹의 충돌, SDK 어댑터의 응답 역전·UI 수명 경계를 검사하도록 작성. **Unity 실행 미완료**, CLI assertions에 합산하지 않음 |
| 독립/정적 검토 | 정상 host·retained 재접속의 채팅 허용 경로, owner 좌표 차단·서버 강제 이동·인증 context 채택을 교차 검토. 변경 파일 공백·충돌 표시, 새 C#의 `.meta`, 문서 참조 검사 |
| 외부 작업 | 이번에 Unity/MCP·PlayFab/Relay 실통신·CloudScript 내용·외부 배포/정책은 변경하거나 실행하지 않음. 이전 CloudScript 검사 수를 이번 실행 결과로 재사용하지 않음 |

독립 검토에서 강제 이동 중 순간이동의 서버 검증 원점이 옛 위치로 덮이는 순서를 수정했다. 실제 이동 시간이 0인 프레임에는 점프 입력을 소비하지 않아 새 점프 높이 허용량이 지워지지 않게 했다. 효과 교체/재접속 때 jump sequence와 입력 세대를 함께 초기화하고, owner의 서버 이동 표시 수명은 종료 reliable 응답까지 유지한다. 인증은 프로필 초기화 전에 기대 세션 버전을 고정하며 현재 활성 인스턴스까지 검사해 reset 재진입과 인스턴스 교체를 거절한다.

**N01–N04 상태: 코드 반영·로컬 빌드/순수 로직 검사 완료, Unity·실제 SDK·네트워크 실행 검증 대기.** N05·N06의 방 이벤트 예외/서비스 교체 시 저장 수명부터 이어서 처리한다. N07–N11 및 기존 S11 외부 구현·Windows/WebGL 8인 성능 검증도 남아 있다. 성능 향상량이나 두 플랫폼 60 FPS 달성을 이번 결과로 주장하지 않는다.

### 후속 P2 서비스 경계 N05·N06 — 2026-09-07

사용자의 진행 요청에 따라 방 화면 알림이 서비스 작업을 중단하는 문제와, 서비스 교체로 실제 저장 요청의 보호가 사라지는 문제를 수정했다. 위 P1 절의 N05·N06 대기 표시는 당시 이력이며 최신 상태는 아래를 따른다.

| 항목 | 완료 기준 | 적용 내용과 파일 |
|---|---|---|
| N05 방 알림 예외·재진입 | 한 구독자가 예외를 던져도 나머지 구독자와 생성/참가/등록/탈퇴가 완료된다. UI 예외로 성공한 요청을 실패·재시도·불필요한 탈퇴로 바꾸지 않는다. 알림 중 새 방이 생기면 옛 상태를 적용하지 않는다. 종료 중 새 방 작업을 시작하지 않는다. | `PlayFabBattleManager`의 방·목록·인스턴스 이벤트와 공개 방 콜백을 구독자별로 격리. `OnRoomJoined`의 내부 Relay 구독을 제거하고 서비스에서 명시적으로 연결 시작. 목록은 소비자마다 복사본 전달. 실패 원인을 탈퇴 상태 전이에 포함하고 알림 세대로 오래된 busy/종료 통지 차단. 이전 네트워크 정리를 cleanup 알림 전에 완료하고 Create의 상태 commit 전 현재 flow 재확인. disable/destroy/quit 차단과 재활성 수명 연결 |
| N06 실제 저장의 수명 | 같은 실행 중 PBM/repository 교체·계정 전환·timeout 뒤에도 미확정 저장 A가 확인될 때까지 같은 title/account의 B 전송은 0회. 다른 계정/title은 진행한다. 확인된 성공/거절 뒤 재시도 가능하며 옛 중복 응답이 새 저장 소유권을 해제하지 않는다. | 새 순수 `ProfileWriteCoordinator`가 실제 요청의 계정 키와 작은 lease만 소유. production `NetworkProfileRepository`가 공유 coordinator 사용, `ProfileRequestQueue`는 로컬 대기자/콜백과 실제 write 소유권 분리. 기존 테스트 생성자는 독립 상태를 유지하고 공유 coordinator를 명시 주입 가능. 계정 키는 변경 시에만 조합하고, 다른 인스턴스가 소유권을 얻었을 때 옛 대기 목록은 재귀 없이 실패 처리 |

독립 검토에서 실패 알림 중 새 방 시작, cleanup 알림 중 Create 재진입, 이전 연결 정리로 새 연결 준비가 취소되는 순서, 비활성·파괴 중 구독자의 새 작업 요청을 추가 보강했다. 저장 검토는 실제 기본 repository 생성 경로와 계정/title 구분, 미확정 결과 보존, 확인된 응답의 참조 동일성 및 4,096개 대기 요청의 반복 정리를 확인했다.

#### N05·N06 검사 결과

| 검사 | 결과 및 한계 |
|---|---|
| 전체 C# | 기존 slnx + `AdditionalUnitySources.targets` 명령으로 최종 **오류 0 / 기존 CS0414 경고 4개**. 예비 빌드에서 NUnit의 Mirror singleton setter 접근 2곳을 reflection으로 수정 후 통과 |
| 순수 CLI | 기존 csproj 빌드 오류 0/경고 0, 실행 **56,665 assertions PASS** = 직전 56,632 + 이번 33. `ProfileWriteLifetimeRegression`이 실제 coordinator/queue 소스를 링크하여 교체·중복 응답·timeout·타계정·대기 목록 경계를 검사 |
| 신규 NUnit | `RoomObserverIsolationTests` **16사례** + `ProfileWriteOwnershipTests` **8사례** = **24개 작성·컴파일**. 실제 PBM 진입/상태/SDK 어댑터를 가짜 응답과 대기 중 Relay 큐로 검사하도록 작성. **Unity 실행 미완료**, SDK/Relay 실통신과 CLI assertion 수에 포함하지 않음 |
| 정적/독립 검토 | PBM 변경 경로 `git diff --check`, 새 source/회귀의 공백·충돌 표시·`.meta`, 문서 링크 검사. N05와 N06을 별도 검토자가 대조 |
| 외부 작업 | Unity/MCP·CloudScript·PlayFab 정책/배포·실제 부하 실행 없음. 이전 Node 통과 수는 재실행한 것으로 집계하지 않음 |

N06 보호 범위는 **같은 managed domain 안에서의 인스턴스 교체**다. 확인되지 않은 실제 요청은 로컬 15초 timeout으로 해제하지 않는다. 응답이 끝내 확인되지 않으면 해당 계정의 저장 격리는 유지된다. 프로세스 재시작·Unity domain reload·다른 장치까지 보호하는 영속 조정자나 원자적 백엔드를 구현한 것은 아니다.

**당시 상태: N01–N06 코드 반영·로컬 검사 완료, Unity/SDK/네트워크 실행 검증 대기.** 당시 남은 N07·N08은 아래 후속에서 반영했다. 측정에 따른 N09 처치알림 최적화, N10 전투/N11 방 관리 추가 분리와 외부 S11 영속 경기 결과·재시도·원자적 보상, PlayFab 배포/정책/데이터 이전, 다중 장치 멤버십, 8인 Windows/WebGL 성능 검증은 남아 있다.

### 후속 표시 정확성 N07·N08 — 2026-09-07

| 항목 | 완료 기준 | 적용 내용과 파일 |
|---|---|---|
| N07 사용자 문자열 | 닉네임·방 이름·채팅의 태그, no-parse 종료 태그, 개행/제어문자, 리터럴 Unicode escape가 TMP 서식이나 추가 행으로 해석되지 않는다. 정상 한국어와 길이 경계의 완전한 surrogate pair, 기존 개발자 서식, 원본 데이터·입력값은 유지된다. | 순수 `UserDisplayText`가 표시용 한 줄 정리/길이 제한/이스케이프를 담당하고 `UserTextPresentation`이 TMP 옵션과 텍스트를 함께 설정. `RankingEntryUI`, `BattleChatUI`, `RoomListItem`, `BattleRoomInfoBanner`, `CharacterInfoController`, `KillAnnouncementItemUI`, `KillAnnouncementUI`의 사용자 표시 연결. `BattleResultText.ForRichText`가 결과의 지역 복사본 이름만 처리하고 `BattleResultView`의 필드/요약/동적 패널에 적용 |
| N08 위젯 저체력 효과 | 비활성 중 회복·피해·최대 HP 변경 뒤 다음 HP 이벤트 없이 정확한 pulse와 overflow를 복원한다. 최초 활성·원본 교체·파괴 후 연결·반복 활성에서도 구독은 한 번만 유지된다. | `UIIdentityGlitchBinder.PullCurrentHpFromReader`에서 HP/최대 HP를 함께 읽고 기존 HP 이벤트 처리로 비율·material 값을 갱신. 읽기 원본 부재/최대 HP 0 이하는 기본 pulse 및 overflow 0. Unity 원본의 파괴 여부를 확인한 뒤 interface 캐시 갱신 |

N07 표시 길이는 닉네임 **64**, 방 이름 **80**, 채팅 본문 **120 UTF-16 단위**다. 기존 채팅 전송의 발신자 이름 제한 **24**는 유지한다. 방 이름 80은 표시 비용을 제한하기 위한 UI 기준이며 저장/조회 키를 변경하지 않는다. 공동 우승 문자열은 8명 이름과 `, ` 구분자를 고려해 **526**까지 보존한다. 실제 제어문자(C0/C1·CR/LF/TAB·U+2028/U+2029)는 공백으로 표시한다. 로그/결과의 개발자 개행은 사용자 필드를 정리한 뒤 조합하여 유지한다. Unicode 전체의 시각적 유사성이나 입력창 미리보기의 별도 서식 문제까지 해결했다고 주장하지 않는다.

설치된 TMP 소스에서 `parseCtrlCharacters=false`여도 `\u`·`\U`가 해석되는 것을 확인했다. 일반 출력은 rich text를 끄고 사용자 backslash를 두 배로 만들며 제어문자 파싱은 켜서 한 번만 소비한다. 개발자 서식을 쓰는 결과 화면은 사용자 `<` 각각을 `<noparse><</noparse>`로 분리하여 입력의 `</noparse>`·`<style>`·`<br>`도 완전한 태그로 전달하지 않는다. 로그인 안내·스킬 이름 등 개발자 문자열의 서식은 유지하고, 입력창/IME·네트워크 메시지 형식·저장된 프로필·계정 및 방 ID는 변경하지 않았다.

최종 연결 경로 점검에서 `MatchLedger.NormalizeName`과 `BattleChatNetwork.Sanitize`가 UI에 도착하기 전에 UTF-16 surrogate pair를 반으로 자를 수 있음을 확인해 함께 보강했다. 기존 trim/CR·LF 처리와 길이 제한은 그대로 두고, 경계가 정상 high/low surrogate 사이인 경우 한 단위 덜 자른다. 서버 코드에 UI helper 의존성을 추가하지 않았다.

#### N07·N08 검사 결과

| 검사 | 실행 결과 및 범위 |
|---|---|
| 전체 C# | `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets` **오류 0 / 기존 CS0414 경고 4개**. 새 runtime 및 Editor 테스트를 포함한 일반 C# 컴파일이며 Unity import/Mirror weaving 실행이 아님 |
| 순수 CLI | `dotnet build Tools/Tests/LogicRegression/LogicRegression.csproj -v:q --configfile Tools/Tests/LogicRegression/NuGet.Config` **오류 0/경고 0**. `dotnet Tools/Tests/LogicRegression/bin/Debug/net10.0/LogicRegression.dll` 실행 **56,727 assertions PASS** = 직전 56,665 + 이번 62. 실제 `UserDisplayText`·`BattleResultData`·`MatchLedger`를 링크하여 제어문자/길이/이스케이프/원본 불변/공동 우승 보존 확인 |
| 새 NUnit | `UserTextPresentationTests` **22사례**(10개 입력×plain/rich + 결과 필드/동적 패널), `UserTextUiIntegrationTests` **8사례**(실제 UI 진입과 캐시/콜백/입력/아이콘 보존), `IdentityGlitchLifecycleTests` **14사례**(실제 셰이더 material·HP/최대 HP·반복 활성/원본 변경), `ChatAuthorityTests` 추가 **1사례**(이름/본문 전송 경계). 합계 **45개 작성·컴파일**, **Unity 실행 미완료**이며 CLI assertion 수에 합산하지 않음 |
| 독립/정적 검토 | 설치 TMP의 두 파싱 단계와 출력 사용처, 위젯 원본/구독/HP 계산을 별도 검토자가 대조. 변경 파일 공백·충돌 표시와 새 Unity `.meta` 및 문서 참조 확인 |
| 외부 작업 | Unity/MCP·실제 네트워크/8인 부하·CloudScript 내용·PlayFab 배포/설정은 실행하거나 변경하지 않음 |

**N07·N08 직후 상태: N01–N08 코드 반영·로컬 검사 완료, Unity 실행 검증 대기.** 당시 남은 N09–N11의 후속 처리는 아래에 기록했다. S11 외부 구현·배포/정책 및 8인 Windows/WebGL 60 FPS 실측은 별도로 남아 있다. 당시 UI 변경의 FPS 개선량은 측정하지 않았다.

### 사용자 재현 Mirror Weaver 오류 우선 수정 — 2026-09-07

사용자가 Unity에서 `CmdUpdateStats cannot have optional parameters` 오류를 확인했다. `StatManager.CmdUpdateStats(StatContainer, uint requestId = 0)`의 기본값을 제거하고, 응답 추적이 필요 없는 기존 호출에서 `0`을 명시하도록 바꿨다. 추적 요청의 ID, 서버 스탯 검증, TargetRpc 완료 응답과 네트워크 인자 형식은 유지한다.

일반 C# 빌드는 이 Mirror 규칙을 검사하지 못했다. 따라서 `Tools/Tests/MirrorWeaverCheck`를 추가해 프로젝트에 설치된 `ILPostProcessorHook`으로 최신 런타임 어셈블리를 메모리에서 직접 처리한다. 전체 `Assembly-CSharp`의 Weaver 오류 0과 생성 직렬화 코드/Command dispatch를 확인하고, 메모리 복사본에 옛 기본값을 되살리면 같은 오류가 재현되는지도 검사한다. 원본 DLL/PDB는 덮어쓰지 않는다. `Tools/.gitignore`에서 수동 검사 프로젝트 `.csproj`는 추적 가능하게 하고 생성 `bin`/`obj`는 제외한다. 실행 방법과 범위는 [README](C:/Github/Battle_PVP/Tools/Tests/MirrorWeaverCheck/README.md)에 기록했다.

우선 수정 직후 전체 C#은 **오류 0 / 기존 CS0414 경고 4개**, 검사 도구 빌드는 **오류 0/경고 0**, 실제 설치 Weaver의 정상 처리와 오류 재현 대조는 **모두 통과**했다. 선언 61개도 별도 읽기 검토했다. `CmdRequestRestart(NetworkConnectionToClient sender = null)`은 Mirror가 허용하는 발신 연결 주입 예외라 유지한다. Unity 전체 import·다른 IL postprocessor·플랫폼 빌드·RPC 실통신의 실행 결과는 아니다.

### N09 수명 보강·계측 및 N10·N11 책임 분리 — 2026-09-07

| 항목 | 이번 완료 기준 | 반영 내용과 범위 |
|---|---|---|
| N09 처치 알림 수명 | 비활성 UI 요청으로 알림을 생성하지 않는다. 숨김/파괴 시 자신이 생성한 알림과 만료 코루틴을 정리하고 외부 컨테이너의 다른 자식은 보존한다. 재활성 시 옛 알림이 돌아오지 않는다. | `KillAnnouncementUI`에 활성 검사·소유 항목 `HashSet`·disable/destroy 정리 추가. 프리팹 OnEnable 중 소유 UI가 비활성화되는 경우도 회수. Editor의 명시 정리는 즉시 삭제, Play Mode는 비활성 후 기존 지연 삭제 사용. 개발용 Show/CreateItem/Release ProfilerMarker 추가. **풀·상한·만료 목록 최적화와 성능 완료 판정은 실측 대기** |
| N10 스킬 판정 책임 | 선택과 서버 허용이 같은 직업/슬롯 규칙을 사용한다. 승인 시점의 시전 계획으로 즉시/시작/종료/발차기 창 적용을 결정하고, 실패 계획은 효과를 적용하지 않는다. 기존 취소·쿨다운·독·강제 이동·재접속 동작은 유지한다. | `CombatSkillRules`/`AdvancedSkillPlan`으로 선택·허용·시전 적용 판정, `CombatPresetPlan` 계열로 왕복 프리셋·주력 스탯·HP/보호막·보너스·무기 전환 수치 계산을 추출. `PlayerCombat`이 결과를 실제 Unity/Mirror 상태에 적용. 프리셋 복귀 기억값은 서버 적용 성공 후 commit. `JobSkillKind` 선언 위치만 이동하고 namespace/정수값/SO 직렬화는 유지 |
| N11 방 상태·캐시 소유 | 방 목록의 이름/방장/인원/코드가 서로 다른 사전에 남지 않는다. 삭제·퇴장·Relay 갱신 후 오래된 목록 응답이 옛 값을 복원하지 않는다. 목록 TTL/lease와 현재 참가 세션을 구분하고, 늦은 응답에서도 필요한 멤버십 정리를 유지한다. | PBM의 `_knownRooms`/`_knownRoomMasters`/`_knownRoomCounts`/`_knownRoomRelayJoinCodes` 제거, `_lastLoadedRoomInfos` 하나로 목록 소유. `RoomListSnapshotState`가 3초 TTL와 mutation revision, `RoomSessionState`가 정리/Relay 예약과 같은 방 책임 전달 결정을 소유. 기존 flow 세대·lease·순서 큐·미확정 gate 재사용 |

N09의 실제 경로는 Battle 씬의 `KillAnouncement.prefab`과 10초 표시 시간이다. 프리팹·씬은 바꾸지 않았다. 기존 프레임 수집기만으로 CPU/GC 감소를 입증할 수 없으므로 [PERFORMANCE_VERIFICATION.md](C:/Github/Battle_PVP/PERFORMANCE_VERIFICATION.md)에 실제 프리팹·Profiler 구간과 비교 절차를 추가했다. 표시 상한이나 풀을 측정 없이 새로 적용하지 않았다.

N10은 스킬의 판정과 결과 계산을 분리한 변경이다. 새 효과 메커니즘의 실제 Unity 적용, ScriptableObject 매핑, 코루틴·SyncVar·RPC 전달은 여전히 `PlayerCombat`의 책임이다. 전체 전투를 별도 서비스로 옮겼다고 보지 않는다. N11도 SDK 호출·Unity 연결 시작/정지·화면 통지는 PBM에 남기고, 결정과 상태 작성 주체를 명시한 범위다. N06 프로필 저장 경계는 변경하지 않았다.

검토 중 N11의 두 경계를 추가 보강했다. 같은 방 신규 요청으로 책임을 전달해도 이전 flow가 아주 늦은 SDK 확인 뒤 정리를 재개할 가능성을 지우지 않는다. 실제 정리 전송 여부는 현재 ticket·보존 cutoff·기존 gate가 결정한다. 또 목록 변경 뒤 조회가 실패/10초 초과한 경우에는 revision과 무관하게 빈 목록을 확정하여 미확인 방을 숨긴다. 정상이나 오래된 성공 응답만 현재의 유효 목록을 반환하며 그 응답으로 TTL을 연장하지 않는다.

#### N09–N11 통합 검사 결과

| 검사 | 결과와 범위 |
|---|---|
| 전체 C# | 기존 slnx + `AdditionalUnitySources.targets` 명령 **오류 0 / 기존 CS0414 경고 4개**. 새 runtime·Editor 테스트 포함 |
| Mirror 코드 생성 | `dotnet Tools/Tests/MirrorWeaverCheck/bin/Debug/net10.0/MirrorWeaverCheck.dll` **통과**. 모든 변경을 포함한 최신 `Assembly-CSharp`의 실제 설치 Weaver 처리·생성 코드 확인, 옛 선택적 매개변수 오류 재현 대조 통과 |
| 순수 CLI | 기존 csproj 빌드 **오류 0/경고 0**, `dotnet Tools/Tests/LogicRegression/bin/Debug/net10.0/LogicRegression.dll` 실행 **56,982 assertions PASS** = 직전 56,727 + 이번 255. production 스킬 계획과 방 세션/목록 상태를 기존 소스들과 직접 링크 |
| 새 NUnit | 처치 알림 수명 **8** + 스킬 계획/실제 PC 어댑터 **15** + 방 세션 **14** + 목록 mutation **15** = **52사례 작성·컴파일**. 기존 방 테스트의 private 상태 접근도 새 소유자에 맞춤. **Unity 실행 미완료**이며 CLI 검사 수에 합산하지 않음 |
| 독립 검토 | N11의 같은 방 재요청/늦은 확인과 timeout·목록 변경 경합, N09 실제 프리팹 수명, N10 실패 계획과 실제 선택/적용 연결을 검토. 비동기 회귀의 완료 대기는 기한을 두어 실패 시 테스트가 무한 대기하지 않게 함 |
| 한계 | N09 NUnit은 실제 메서드/프리팹을 사용하되 EditMode 수명 콜백과 만료 IEnumerator를 명시 진행하도록 작성했다. 자동 PlayerLoop·10초 경과·플레이어 실행 통과가 아니다. Unity/MCP·실제 SDK/Relay·플랫폼 실행·외부 배포와 부하 측정 없음 |

**최신 상태:** 사용자 재현 Weaver 오류 수정 및 실제 코드 생성 검사 통과. N10·N11 판정/상태 소유 분리는 코드·로컬 검사 완료, N09는 수명 보강·계측 반영 및 최적화 실측 대기다. Unity 전체 import·NUnit/실제 다중 접속, 8인 Windows/WebGL 60 FPS와 CPU/GC 검증, S11 영속 결과/원자적 보상 및 PlayFab 배포·정책·데이터 이전은 별도로 남아 있다.

### 사용자 재현 프로필 생성자 예외 우선 수정 — 2026-09-07

사용자 로그에서 `PlayFabBattleManager` 필드 초기화 → `NetworkProfileRepository` → `ProfileRequestQueue` 생성자의 계정 조회 → `PlayFabSettings.staticSettings.TitleId` → `Resources.LoadAll` 경로를 확인했다. Unity가 MonoBehaviour를 생성하는 로딩 스레드에서 리소스를 읽어 생성이 중단되었고, 이후 `Update`의 `_profileRepository.Tick()`에서 null 접근이 보고됐다. 일반 C# 컴파일과 Mirror 코드 생성은 이 실행 시점의 제약을 검증하지 못한다.

완료 기준은 **생성자가 SDK·계정·시계·콜백 delegate를 실행하지 않을 것**, **첫 Load/Save/Tick/ResetSession 시점의 계정을 사용할 것**, **최초 바인딩을 세션 변경 실패로 처리하지 않을 것**, **기존 계정 전환·timeout·미확정 저장 격리를 유지할 것**이다. 최종 실행 합격에는 Unity에서 Play 종료 후 재진입 및 로그인/프로필 요청 시 같은 예외와 후속 null 오류가 없는지도 포함한다.

`ProfileRequestQueue` 생성자에서 계정 조회를 제거하고 첫 실행 작업의 `EnsureAccount`에서 계정을 바인딩하도록 바꿨다. `PlayFabBattleManager`의 readonly 저장소는 순수 관리 상태만 생성하므로 유지하고 이 계약을 주석에 명시했다. 저장소를 Awake마다 새로 만들거나 Update의 null 접근을 무시하도록 바꾸지 않아 비활성 객체·재활성·기존 저장 요청 수명을 유지한다. SDK 자체, 계정 키 구성 및 공유 `ProfileWriteCoordinator`는 변경하지 않았다.

| 검사 | 결과와 범위 |
|---|---|
| 오류 원인 재현 | 수정 전 실제 queue 소스를 링크한 CLI 검사에서 생성 중 외부 delegate 접근을 금지하자 queue 생성자에서 실패. 수정 후 같은 검사 통과 |
| 순수 CLI | 기존 명령 빌드 **오류 0/경고 0**, 실행 **57,012 assertions PASS** = 직전 56,982 + 이번 30. 첫 Load/Save/Tick/Reset, 현재 계정 선택, 불필요한 요청/실패 없음과 계정별 저장 lease 보호 확인. 기존 계정 전환·응답 지연/중복·재진입 회귀도 통과 |
| 전체 C# | 기존 slnx + AdditionalUnitySources.targets 명령 **오류 0 / 기존 CS0414 경고 4개**. 새 Editor 테스트 포함 |
| Mirror 코드 생성 | 최신 Assembly-CSharp의 실제 설치 ILPostProcessorHook 처리 및 옛 선택적 매개변수 오류 재현 대조 **통과** |
| 새 NUnit | 생성 시 delegate 호출 0/첫 Load·Save **2사례**, 실제 production repository의 worker thread 생성 **1사례**, 합계 **3개 작성·컴파일**, **Unity 실행 미완료**. worker 검사는 SDK 캐시가 이미 준비된 환경에서 옛 오류를 항상 검출하지 못하므로 delegate 0회 검사를 주회귀로 사용 |
| 독립 초기화 감사 | 소유 런타임 코드의 다른 필드/정적 초기화 경로를 검토했으며 같은 종류의 추가 결함은 발견하지 못함. Shader/Animator ID 및 ProfilerMarker는 설치 Unity DLL의 스레드 안전 속성을 확인 |

`CrashReporter::GetInsightsSignedUrlAsync should only be called from the main thread`는 생성자 예외 보고 과정의 후속 증상일 가능성이 있지만, 별도 스택과 Unity 재실행 결과가 없어 해결됐다고 확정하지 않는다. 현재 상태는 **생성자 원인 수정·로컬 검사 완료 / Unity 재현 확인 대기**다. 이번 작업에서 Unity/MCP·실제 SDK 요청·외부 서비스 배포는 실행하지 않았다.

### 사용자 재현 로그인 중단·기기 정보 인증 예외 수정 — 2026-09-07

N04에서 요청별 `AuthenticationContext`를 분리하면서 정적 `PlayFabClientAPI` 로그인/회원가입 호출을 유지한 조합이 원인이었다. 설치 SDK의 `PlayFabUnityHttp.OnResponse`는 요청 context 갱신 → 자동 `ReportDeviceInfo` → 앱 성공 콜백 순서로 실행한다. 정적 호출은 요청 container에 `instanceApi`가 없어, 자동 기기 정보 요청이 아직 승인되지 않은 전역 `staticPlayer`를 사용한다. 전역 세션이 비어 있으면 `Must be logged in` 예외로 앱 성공 콜백에 도달하지 못한다. 다른 계정이 로그인된 상태라면 그 계정으로 기기 정보가 전송될 수도 있다.

`PlayFabAuthManager`의 기본 로그인·회원가입 전송을 각각 `new PlayFabClientInstanceAPI(request.AuthenticationContext)`로 바꿨다. SDK가 요청 container에 인스턴스를 보관하고, 응답 처리와 기기 정보 후속 요청이 같은 인증 정보를 사용한다. 성공 콜백의 최신 요청 확인 후에만 전역 계정을 채택하는 기존 규칙과 회원가입 세션 미채택은 유지한다. SDK 소스·서비스 설정·기기 정보 수집 설정은 수정하지 않았다.

완료 기준은 로그인·회원가입 각각에 대해 **전역 세션 없음/다른 계정 로그인 상태에서도 기기 정보 요청이 원래 요청의 context와 ticket를 사용할 것**, **자동 후속 요청 뒤 앱 성공 콜백이 도달할 것**, **응답 역전 시 서로 다른 요청의 세션과 전역 계정을 덮지 않을 것**이다. SDK의 전역 화면 사용 시간 수집까지 요청별 상태로 분리했다는 의미는 아니다. 최종 합격에는 실제 Unity 로그인 후 로비 진입·프로필 조회 확인이 필요하다.

`AuthenticationSdkIntegrationTests`에 실제 production 전송 delegate와 SDK serializer/`PlayFabUnityHttp.OnResponse`를 연결한 **6개 NUnit 사례**를 추가했다. 로그인/회원가입 × 전역 세션 없음/다른 계정 선택의 정상 4사례는 두 요청을 역순으로 완료하며 context·instance·합성 인증 헤더와 기기 정보 요청 → 앱 콜백 순서를 검사한다. 옛 정적 호출의 `NotLoggedIn` 로그 및 앱 콜백 누락을 확인하도록 설계한 대조 사례도 로그인/회원가입 각 1개다. HTTP 전송은 메모리에서 요청을 캡처하는 fake transport로 막고, 프로젝트 설정 asset 대신 임시 설정 객체를 사용한다. 바꾼 SDK 전역 상태와 플러그인은 검사 종료 시 복원한다.

| 검사 | 결과와 범위 |
|---|---|
| 전체 C# | slnx + AdditionalUnitySources.targets로 인증 코드 재컴파일 **오류 0 / 기존 CS0414 경고 4개**. 이후 새 NUnit 포함 최종 증분 빌드 **오류 0/경고 0**. 기존 경고를 제거한 것은 아님 |
| 기존 순수 CLI | **57,012 assertions PASS** 재확인. 인증 시도 상태·프로필을 포함한 기존 순수 소스 회귀이며 이번 SDK 응답 경로의 실행 검증은 아님 |
| 새 SDK 통합 NUnit | **6개 작성·컴파일 완료 / Unity 실행 미완료**. 앱의 최신 계정 선택 검사는 기존 AuthenticationFlowTests, 이번 사례는 기존 검사가 우회하던 SDK 자동 후속 요청 경계를 대상으로 함 |
| 독립 SDK 검토 | 설치 SDK의 instance constructor → 요청 container 보관 → 응답 context 갱신 → instance ReportDeviceInfo → 앱 callback 순서를 대조. 로그인·회원가입 모두 같은 request context를 넘기는 최소 변경 확인 |
| 실행 한계 | MCP 인스턴스 목록은 다른 프로젝트 SimpleGame만 연결된 상태여서 해당 에디터를 조작하지 않음. Battle_PVP의 Unity 실행·실제 로그인/회원가입·서비스 호출/배포는 하지 않음 |

현재 상태는 **로그인 중단 원인 수정·C# 컴파일 완료 / 실제 Unity 로그인 검증 대기**다. 실제 확인은 Play 종료 및 재컴파일 후 로그인 재시도 → 로비 진입 → 프로필 조회까지 진행하고, 같은 예외가 없는지 확인해야 한다.

### 방 입장 시 체력 초기화·피해 팝업 파괴 순서 오류 수정 — 2026-09-07

사용자가 방 입장 후 두 오류를 재현했다. `HealthSystem.OnValidate`가 Mirror의 `NetworkIdentity.Awake` 바인딩 전에 `isServer`를 읽어 null 예외가 발생했다. 별도로 씬이 먼저 파괴한 `DamagePopup`의 관리 참조가 풀에 남아, 매니저 `OnDestroy` → `ObjectPool.Clear`의 파괴 callback이 이미 없는 `gameObject`를 읽었다.

이번 합격 조건은 **미바인딩 체력/부활 요청이 예외와 상태 변경 없이 거절될 것**, **검증 콜백은 사망·UI·코루틴을 직접 실행하지 않고 준비된 실제 플레이 객체의 메인 스레드에서 처리될 것**, **서버/클라이언트/오프라인 권한과 정상 사망·부활은 유지될 것**이다. 팝업은 **매니저와 자식 중 무엇이 먼저 파괴되든 정리가 끝나고**, **비활성화 시 활성 집합을 반드시 비우며**, **없어진 풀 항목을 다시 사용하지 않고 재활성 후 정상 표시할 것**을 기준으로 한다.

- `HealthSystem`: `netIdentity`가 없는 동안 체력 변경과 부활 요청을 거절한다. `OnValidate`는 원자적으로 대기 표시만 남기고, Editor의 실제 플레이 객체가 준비된 뒤 Update에서 사망 판정·HP 이벤트·overflow 처리를 수행한다. 인스펙터의 실시간 체력 변경은 다음 정상 프레임에 반영한다.
- `DamagePopupManager`/`DamagePopup`: 풀 반납/파괴 callback에 Unity의 파괴 객체 검사를 넣었다. 매니저가 활성 원소를 직접 제거하므로 파괴된 객체나 이미 반납된 상태 때문에 종료 반복이 멈추지 않는다. 팝업 자체의 비활성/파괴를 통지하고, 보관된 파괴 참조는 제한된 횟수 안에서 건너뛴다. root가 없어졌으면 다음 정상 생성에서 같은 씬의 단위 스케일 root를 복구한다. 활성화 callback 중 소유자가 종료되는 경우에도 표시를 계속하지 않는다. 기존 기본 prewarm 32·보관 상한 256과 프리팹은 유지한다.
- `StatManager`/`StatBalanceConfig`: 체력·UI로 연결되는 검증 이벤트 경로도 지연 처리한다. 밸런스 asset은 검증 중 revision과 대기 표시만 갱신해 편집 미리보기 캐시는 최신값을 읽을 수 있게 하고, 실제 알림은 준비된 플레이 객체의 Update에서 전달한다. 명시적인 `NotifyChanged`의 메인 스레드 즉시 알림은 유지한다. 스탯 검증도 변경 표시만 남긴다. 초기화 전 Mirror 속성 조회를 방어하며, 실제 NetworkIdentity가 없는 오프라인 표시 대상과 바인딩 대기 중인 네트워크 객체는 구분한다.

| 검사 | 결과와 범위 |
|---|---|
| 전체 C# | 기존 slnx + AdditionalUnitySources.targets 명령으로 최종 **오류 0 / 기존 CS0414 경고 4개**. 현재 Unity NUnit이 지원하지 않는 신규 테스트의 NonParallelizable 속성은 제거한 뒤 검사 |
| Mirror 코드 생성 | 최신 전체 Assembly-CSharp의 실제 설치 ILPostProcessorHook 처리 및 옛 CmdUpdateStats 선택적 매개변수 오류 재현 대조 **통과** |
| 새 체력 NUnit | **12개 작성·컴파일**. 미바인딩 offline/server/client/host의 체력·부활 무변경, 실제 Mirror 바인딩 후 역할별 권한, 사망·부활, OnValidate의 즉시 부작용 없음과 EditMode 대기 유지. 기존 HealthUiLifeAndDamageTests의 정상 fixture도 실제 Mirror 바인딩을 명시하도록 수정 |
| 새 팝업 NUnit | **8개 작성·컴파일**. 실제 Damage_Popup 프리팹으로 root 먼저 파괴/중복 정리, 보관 객체·컴포넌트 외부 파괴 후 재사용, stale active handle·이미 returned 상태, GO/컴포넌트 재활성 10회, world root 복구 검사 |
| 새 스탯 NUnit | **9개 작성·컴파일**. 검증 중 resource 미로드/이벤트 없음, 준비 단계의 묶음 처리, worker 검증(5초 제한), revision 기반 미리보기, 명시 NotifyChanged, 재진입, 바인딩 복구, NetworkIdentity 없는 offline 표시·카메라 경로 |
| 실행 한계 | 합계 **29개 NUnit은 Unity 실행 미완료**. 팝업 검사는 실제 handler를 명시 호출하는 EditMode 경계이고 자동 씬 unload/프레임 진행 증명이 아님. live Inspector 변경의 다음 Update 적용과 실제 방 입·퇴장, 서버/클라이언트 플레이는 확인 대기. 이번 수정의 FPS/GC 개선을 측정했다고 주장하지 않음 |

새 테스트·meta와 변경 파일의 공백/충돌 표시를 확인했다. SDK/Mirror 패키지·씬·프리팹과 외부 배포는 변경하지 않았다. 현재 상태는 **제보된 두 원인 및 연결된 스탯 검증 경로 수정·컴파일/Weaver 확인 완료 / Unity 실행 검증 대기**다. 실제 합격은 방 입장→퇴장→재입장과 Play 종료를 반복하며 두 예외가 없고, 피해 표시·사망·부활이 정상인 것을 확인해야 한다.

### 로비·대기실 설정 클릭 시 커서 사라짐 수정 — 2026-09-07

`GameInputController`가 `Battle_waiting`도 커서를 잠그는 전투 씬으로 분류했다. Windows는 매 프레임, WebGL은 마우스 클릭 시 잠금을 요청했다. 또한 실제 `Lobby` 씬에는 이 컨트롤러가 없는데 이전 전투 컨트롤러가 종료되면서 커서 잠금을 해제하지 않았다. 씬 참조를 대조했으며 별도의 커서 잠금 코드가 있는 Crusader 카메라는 Demo 씬에만 사용되어 수정 대상에서 제외했다.

완료 기준은 **Lobby와 Battle_waiting에서 설정 열기/닫기·좌우 클릭·ESC·포커스 복귀 후 커서가 보이고 잠기지 않을 것**, **전투 종료→로비 복귀에서도 잠금이 남지 않을 것**, **Battle의 정상 플레이 잠금과 메뉴·채팅·관전·결과 화면의 해제 정책을 유지할 것**이다. Windows와 WebGL 모두 적용하며 실제 플랫폼 실행까지 완료돼야 최종 합격이다.

- `InputModeRules.CanLockCursor`에서 정확히 `Battle` 씬의 `Gameplay` 모드만 잠금을 허용한다. `GameInputController`의 Windows 자동 잠금과 WebGL 클릭 잠금이 같은 판정을 사용한다.
- 현재 소유자인 컨트롤러의 `OnDisable`에서 잠금을 풀고 커서를 표시한다. 새 컨트롤러가 소유권을 가져간 뒤 이전 객체가 종료될 때는 새 상태를 덮지 않는다.
- `LobbyUIManager.RefreshVisibility`의 로비/대기실 분기에서 커서 상태를 갱신한다. 이 경로는 UI 활성화와 씬 로드 시에도 실행되며, 입력 컨트롤러가 없거나 비활성인 일반 로비에서도 잠금을 해제한다. 기존 플레이 입력·일시정지·카메라 판정과 씬/프리팹은 유지한다.

| 검사 | 결과와 범위 |
|---|---|
| 커서 정책 CLI | 실제 production `InputModeRules`를 링크해 8가지 씬 입력 × 5가지 모드, **40 assertions PASS**. 실제 마우스/OS 커서/브라우저 실행 검사는 아님 |
| 기존 CLI 회귀 | 기존 LogicRegression 빌드 **오류 0/경고 0**, 실행 **57,052 assertions PASS** = 직전 57,012 + 이번 40 |
| 전체 C# | 기존 slnx + AdditionalUnitySources.targets 명령 **오류 0 / 기존 CS0414 경고 4개** |
| 독립 코드 검토 | 실제 씬의 커서 작성자 참조, 두 플랫폼의 공통 잠금 판정, 소유자 종료와 입력 매니저 없는 로비 복귀 경로를 확인 |
| 실행 대기 | Unity에서 로비/대기실의 설정·좌우 클릭·ESC·포커스 복귀 및 Battle→Lobby→재입장, Windows/WebGL 전투 잠금·채팅·결과 동작은 아직 실행하지 않음 |

현재 상태는 **원인 수정·정책 회귀 검사·전체 C# 컴파일 완료 / 실제 플랫폼 커서 동작 검증 대기**다.

### 에디터 활성 상태와 무관한 UI 시작 상태 초기화 — 2026-09-07

사용자가 에디터에서 켜 둔 스탯 창 등으로 Play를 시작하면 열린 상태가 유지되는 문제를 보고했다. Login/Lobby/Battle_waiting/Battle과 Lobby_UI/UI_Root/Player의 실제 참조를 대조했다. 초기 닫힘이 빠진 창 외에 대기실 입장/늦은 플레이어 바인딩이 스탯 창을 자동으로 여는 경로, 직렬화된 로그인 성공 문구, 소유 스크립트 없는 화면 왜곡 이미지도 확인했다.

완료 기준은 다음과 같다.

1. 에디터에서 각 대상 UI를 활성화해 저장했더라도 처음 실행하거나 새 씬으로 진입하면 기본 닫힘/빈 표시로 시작한다.
2. 스탯·캐릭터 정보·방 목록/설정은 사용자의 첫 클릭부터 정상적으로 열리고 닫힌다. 창을 연 뒤 데이터 바인딩이나 UI 재활성화 때문에 임의로 다시 닫히지 않는다.
3. 사망·로딩·카운트다운·피해·스킬·결과·로그인 상태는 실제 요청이 오면 표시한다. 첫 Awake 전에 전달된 HUD 상태나 첫 프레임 전에 표시한 피해를 뒤늦은 초기화가 지우지 않는다.
4. 상시 HUD·채팅 기록·플레이어 현황·버튼은 유지한다. 실제 로딩 중인 경우처럼 게임 상태가 표시를 요구하면 그 상태를 적용한다.

| 대상 | 반영 내용 |
|---|---|
| 스탯 창·방 목록·방 생성 설정 | LobbyUIManager의 Awake와 씬 로드에서 닫는다. 늦게 생성된 customizer도 처음 발견하면 닫으며, 대기실의 자동 열기를 제거했다. 이미 찾은 창은 재조회만으로 닫지 않는다 |
| 캐릭터 정보 | CharacterInfoController에서 최초 기본 닫힘을 적용한 뒤 버튼 토글을 받는다 |
| 사망·로딩·카운트다운·피해 표시 | PlayerHudView가 초기 숨김/빈 문구를 한 번 적용하고 실제 표시 요청을 반영한다. 늦은 Start에서 피해 표시를 지우던 경로를 제거했다 |
| 스킬 미리보기 | 첫 실제 상태 전에는 숨김으로 시작하고, 이미 수신한 스킬 상태를 Awake가 덮지 않도록 한다 |
| 결과 창 | BattleStateMachine의 채택된 인스턴스 Awake에서 기존 결과 View의 Hide를 호출한다. 실제 경기 종료의 Show 경로는 유지한다 |
| 로그인 상태·이메일 입력 | PlayFabLoginUI의 Awake에서 편집용 성공 문구를 지우고 숨긴다. 기본 비활성인 이메일 입력도 닫는다. 상태 메시지는 실제 요청/검증 결과가 발생하면 다시 표시한다 |
| Screen_Distortion | UI_Root에 UiInitialVisibility를 연결해 해당 화면 효과만 부모의 Awake에서 숨긴다. 자식의 첫 활성화 때 스스로 다시 닫는 방식이나 매 프레임 강제 숨김은 사용하지 않는다 |

채팅의 입력 취소 초기화, floating message의 alpha 0 초기화, 피해 팝업/킬피드의 생성·회수 경로는 기존 처리를 확인했다. 별도 오디오/옵션/일시정지 창은 대상 씬에서 발견되지 않았다. 비활성 HUD 장식과 개발용 피해 테스트 버튼은 팝업처럼 일괄 숨기지 않는다. 스탯 컨트롤러는 Canvas_Customizer의 부모인 Stat_Setting에 있으므로 창을 닫아도 데이터 구독과 별도 floating message 표시가 유지된다.

| 검사 | 결과와 범위 |
|---|---|
| 전체 C# | 기존 slnx + AdditionalUnitySources.targets 명령 **오류 0 / 기존 CS0414 경고 4개**. 새 초기화 컴포넌트와 Editor 검사 소스 포함 |
| 신규 NUnit | TemporaryHudInitializationTests **6사례 작성·컴파일**. 편집 중 켠 상세 창/사망/로딩/카운트다운 초기값, 피해 예시 문구 정리, Awake 전후 실제 HUD 상태 및 스킬 cooldown/명시 hidden 유지. **Unity 실행 미완료** |
| 프리팹 연결 | UI_Root의 기존 값 변경 없이 컴포넌트·참조 **15줄 추가**. 새 script GUID, component fileID, 활성 부모와 Screen_Distortion 대상 연결·고유성 검사 통과 |
| 독립 검토 | 네 씬과 관련 프리팹의 임시 창/상시 UI를 대조하고, customizer의 첫 발견·첫 클릭·늦은 플레이어 바인딩·재활성 경로를 확인. 변경 경로 diff 공백 검사 통과 |
| 실행 대기 | 에디터에서 대상들을 켠 채 각 씬 Play → 초기 닫힘 → 버튼으로 다시 열기, 실제 로딩/사망/스킬/결과 표시, 씬 복귀와 Windows/WebGL 실행은 확인 대기 |

현재 상태는 **초기 UI 정책 반영·전체 C# 컴파일 완료 / Unity 실제 실행 검증 대기**다. 이번 NUnit은 명시적으로 생명주기 메서드를 호출하는 EditMode 검사 소스이며 자동 PlayerLoop나 실제 플랫폼 검증 통과로 집계하지 않는다. 순수 CLI 판정 소스는 바뀌지 않아 기존 57,052 검사 결과를 이번 UI 실행 결과로 사용하지 않았다.

### G01–G11 플레이 흐름 문제 수정 — 2026-09-07

추가 검토에서 기록한 11개 문제를 수정했다. 각 항목의 재현 조건과 완료 기준은 [FOLLOW_UP_REVIEW.md](C:/Github/Battle_PVP/FOLLOW_UP_REVIEW.md)의 13–14절을 따른다. 기존 작업 트리 변경을 보존했고 CloudScript/외부 서비스 배포와 실제 게임 실행은 수행하지 않았다.

| 범위 | 변경과 이유 |
|---|---|
| G01 전송 | UnityRelayTransport와 ReliableSendBacklog에 BeginSend 포화 대기·순서 유지·크기/시간 상한을 적용. Mirror가 원본 배치 배열을 재사용하므로 대기할 때만 복사한다. 성공한 일반 전송은 대기 배열을 만들지 않는다. reliable EndSend 실패는 부분 전송 가능성 때문에 전체 배치를 재시도하지 않고 연결을 종료한다. 양쪽 reliable window 128은 최대 60 KB 패킷의 분할 수보다 크게 잡은 용량 설정이며 실측 최적값은 아니다 |
| G02 도발 | PlayerCombat의 서버 도발을 연결 유무와 무관하게 실행. PlayerManager는 도발 시작·종료 시 강제 이동 epoch를 전환하며 owner 위치 요청을 거절하고 서버 충돌 위치를 전송한다. 도발 중 회전/보행 애니메이션·점프 제한도 서버 규칙을 따른다. 넉백과 겹칠 때 매 충돌 단계마다 정지 거리를 다시 계산한다. 활은 BowAttackController가 서버 시계로 최소 차징 후 발사하고 종료 시 남은 동작을 정리한다. 참가자의 도발용 근접 요청 플래그를 Command에서 제거했다 |
| G03 콤보 | ServerComboSequence에 서버가 허용하는 순서를 둔다. 초기/취소 후 index 0, 공격 중 80% 이후 다음 타격, 정상 종료 직후 서버가 부여한 다음 단계에만 0.35초 지연 연결을 허용한다. 새로운 공격·취소는 이전 연결 권한을 제거한다. 타격 계수·애니메이션 데이터는 유지했다 |
| G04 로컬 설정 창 | StatCustomizerController의 중복 오브젝트 파괴를 제거하고 로컬 플레이어 또는 오프라인 StatManager.Local만 창 소유권을 얻는다. 플레이어 없는 독립 로비 UI는 사용할 수 있다. LobbyUIManager는 소유자 변경 이벤트를 받아 실제 Canvas_Customizer만 교체하며 임의의 원격 Canvas를 검색하지 않는다. 적용 버튼에서도 현재 로컬 소유권을 확인한다 |
| G05 경기 준비 | BattleStateMachine의 고정 1.5초 대기를 시작 명단별 ready/스탯 승인 대기로 교체했다. 최대 15초, 그 전에 연결 종료한 사람은 대기에서 제외한다. HealthSystem은 승인/ready 전 피해를 거절해 중도 참가자도 보호한다. StatManager의 기존 최초 승인 기한 10초는 유지한다 |
| G06 Relay 생존 | PlayFabBattleManager는 Relay가 Established가 된 뒤 방 등록을 시작하고 최대 15초 준비 기한을 적용한다. driver 없음/AllocationInvalid는 호스트 이탈 경로로 전환하며 Established에서만 heartbeat를 허용한다. 참가자 쪽 할당 무효화도 transport 오류와 disconnect로 전달한다. 기존 호스트 프로세스 종료 TTL과 함께 작동한다 |
| G07–G09 UI/입력 | 승인된 부활 뒤 스탯 Canvas를 닫고 입력 정책을 갱신한다. Canvas만 닫아 진행 중인 스탯 저장/승인 요청은 취소하지 않는다. GameInputController는 명시적 채팅 상태뿐 아니라 현재 TMP/legacy 입력창 포커스, 열린 방/스탯 패널도 반영한다. 채팅을 닫은 프레임까지 텍스트 입력을 소비해 RespawnRoutine이 같은 Space를 사용하지 않게 한다. 기존 FollowCamera의 입력 차단 조건을 통해 시점 조작도 막으므로 카메라의 사용자 설정값은 수정하지 않았다 |
| G10 방 매니저 교체 | RoomServiceLifetime이 세대, 직렬 요청 큐, 응답 미확정 gate, 현재 RoomSessionState를 소유하며 모든 production 매니저가 같은 수명을 사용한다. 옛 인스턴스의 종료는 자기 세션만 무효화할 수 있다. 늦은 정리는 새 세션과 비교해 취소하거나 정리 책임을 넘기며 다른 계정·다른 방은 독립 처리한다 |
| G11 아이콘 | Player.prefab의 _overflowEffect만 0으로 바꿔 스탯 미리보기 아이콘을 체력 표시 코드가 끄지 않게 했다. 별도 overflow Image가 없는 프리팹이므로 새로운 장식을 생성하지 않는다. 체력 수치/바의 기존 표시와 customizer의 아이콘 참조는 유지한다 |

통신 변경은 같은 수정 버전으로 빌드한 호스트와 참가자를 사용해야 한다. G01의 부분 전송 실패 시 연결 종료는 고의적인 처리이며, 실제 8인 전투에서 불필요하게 발생하는지 확인해야 한다. 중도 참가의 정책·스탯 예산·공격 계수·도발 지속 시간·부활 기한은 변경하지 않았다.

검증 결과:

- `dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets`: 전체 C# 오류 0. 기존 CS0414 4개(카메라 2개, Relay connectionType, DamagePopup moveYSpeed)를 유지하며 새 경고 없음.
- LogicRegression 빌드 및 실행: **59,709 assertions PASS**, 직전 57,052개 대비 **2,657개 추가**. 실제 production 대기열의 복사/순서/상한/만료/해제, 콤보 단계/취소/지연 경계, shared room 수명의 실행 직렬화/미확정 응답/늦은 정리/계정 전환을 검사했다. 이것은 네트워크 전송·FPS 실측 결과가 아니다.
- `dotnet Tools/Tests/MirrorWeaverCheck/bin/Debug/net10.0/MirrorWeaverCheck.dll`: 설치된 ILPostProcessorHook의 전체 Assembly-CSharp 처리 통과. 옛 선택적 Command 매개변수를 복원한 메모리 대조는 원래 Weaver 오류를 정상 검출했다.
- Unity NUnit 신규 **12사례 작성·컴파일**: IPC 메모리 전송의 reliable 창 포화/오류 콜백 재진입 2, 로컬 설정 창 생성 순서·교체·부활·텍스트 소비·프리팹 참조 6, 미승인 플레이어 피해 거절 2, 도발+강제 이동의 소유자 도주/점프 입력·벽·정지 거리 2. 기존 room observer 검사는 공유 수명을 테스트별로 격리했다. **Unity에서 이 사례들을 실행하지 않았다.**

현재 상태는 **G01–G11 구현 완료·로컬 컴파일/CLI/Weaver 확인 / Unity 생명주기·실제 연결·플랫폼 실행 검증 대기**다. 호스트 1+원격 2의 기본 회귀 후 호스트 포함 8인의 Windows/WebGL 부하와 60 FPS 기준을 별도로 확인해야 한다. 기존 S11 외부 구현·검증과 N09 성능 실측 대기도 그대로 남는다.

### Unity 실행 검증 복구 및 OnValidate 후속 — 2026-09-25

- SkillUI/BgmManager의 OnValidate에서 Unity 객체 접근을 직접 수행하지 않고 EditorValidationQueue에 관리 작업을 예약한다. 에디터 update에서 한 번 처리하며, 파괴된 객체를 무시하고 실행 중 스킬 HUD 상태/BGM 페이드를 보존한다.
- 잘못된 길이의 GUID 때문에 로드되지 않던 테스트 스크립트 5개의 meta를 복구했다. 기존 씬/프리팹 참조는 없었다. 기존 테스트 65개가 실제 실행 대상에 추가됐다.
- EditMode에서 자동 호출되지 않는 Awake/OnEnable/OnDisable, Mirror NetworkIdentity 바인딩을 테스트에서 명시한다. 테스트 종료 시 static 이벤트 구독도 정리한다. 프로덕션 PlayFabBattleManager의 DontDestroyOnLoad는 실제 Play 모드에서만 호출한다.
- 테스트의 유효하지 않은 roomId, 교체된 RoomFlow 생성자, unsaved 테스트 씬 처리, UTP 포화 입력을 실제 계약에 맞췄다. 포화 검사는 기본 window 32를 ACK 없이 채운 뒤 다음 송신 실패·정리·동기 종료 재진입을 확인한다.
- 로딩 스레드 OnValidate 예약·에디터 갱신·예약 후 객체 파괴를 실행하는 UnityTest 3개를 추가했다.

검증: 첫 실제 EditMode 425개 중 338 통과/87 실패 → 복구 후 **493/493 통과**, 실패/건너뜀 0. 순수 CLI **59,709 assertions PASS**. CloudScript 권한/정원 및 임대 **26시나리오·564 assertions PASS**. Windows/WebGL 실제 Development 빌드 오류 0(기존 경고 3/4). 오프라인 4개 씬 Play 로딩 예외 0, 빌드 씬/게임 프리팹 863개 오브젝트 Missing Script 0.

[전체 실행 기록과 JSON 증거](VALIDATION_AND_REMODEL_2026-09-25.md)를 따르며, 실제 서비스/8인 접속·플랫폼 60 FPS·S11 외부 구현은 완료로 처리하지 않는다. 빌드 후 원래 Login 씬/비실행/WebGL 상태를 복구했다. 시각 리모델링은 Assets 밖의 [HTML 시안](DesignPreview/index.html)만 작성했으며 사용자 승인 후 반영한다.
