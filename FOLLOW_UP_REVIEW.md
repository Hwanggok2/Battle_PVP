# 초기 개선 재점검과 신규 개선 목록

검토일: 2026-09-07. 기준: 신뢰하는 호스트, 최대 8명, Windows/WebGL 각각 60 FPS. 사용자의 요청에 따라 초기 기준에 직접 해당하는 누락은 수정하고, 새로 발견한 별도 문제와 추가 구조·성능 개선은 아래에 정리한다.

검토 시작 시 자체 런타임 C# 113개(생성 입력 코드·Editor 검사 제외)를 목록화하고 전투/이동/피해, 인증/방/Relay/프로필, 로비/스탯/HUD/입력/채팅, 경기 기록과 진단 도구의 연결 경로를 검토했다. 실제 사용 여부가 중요한 후보는 씬·프리팹과 연결했다. 전체 코드에 결함이 더 없다는 증명이나 Unity 실행 결과가 아니며, 아래 항목은 현재 소스에서 근거를 확인한 결과다. 외부 SDK 전체의 품질 검토는 포함하지 않는다.

## 1. 초기 기준에서 다시 열린 항목

| 항목 | 관련 기준 | 발견한 누락 | 이번 처리 |
|---|---|---|---|
| Q01 서버 이동 제한 | S06/C05 | 로컬 입력의 이동·점프·앉기 잠금과 앉기 속도가 서버 좌표/앉기 요청 검사에 일부 빠짐 | 서버 상태 이력과 이동 검증 연결, 공중에서 시작된 잠금·정상 강제 이동·늦은 패킷의 경계 보강 |
| Q02 재접속 강제 이동 시간 | T07 | 첫 시간 동기화 전 `NetworkTime.time`이 0이면 짧게 남은 강제 이동이 서버 가동 시간만큼 늘어남 | reliable 메시지의 서버 배치 시각을 복원 시각의 하한으로 사용 |
| Q03 옛 프로필 파싱 | S09 | 존재하지만 잘못된 옛 스탯을 0으로 읽거나 일부 키만 있는 데이터를 빈 계정으로 취급 | 정상 빈 계정과 완전한 옛 형식은 유지하고 손상·부분 데이터는 실패로 반환 |
| Q04 상위 로드 완료 통지 | T06/S09 | GDM 세션 초기화가 대기 콜백을 지우고, 이벤트/콜백 예외가 뒤의 완료 통지를 막음 | 요청별 대기자 분리, 상태 반영과 통지 순서 정리, 세션 전환·예외·재진입 격리 |
| Q05 로비 빈 슬롯 적용 | T03 | 완성 프리셋에서 빈 슬롯으로 바꾸면 UI 저장값은 0이지만 로비 캐릭터에는 이전 스탯이 남음 | 스탯 합계 대신 `HasLoadedPlayerStats`로 판단하여 정상 0도 실제 대상에 적용 |
| Q06 근접 부위 이력 좌표 | S05 | 승인된 root 위치와 호스트 화면에서 보간 중인 collider bounds가 같은 기록에 섞이고 주기 기록은 다시 화면 위치를 저장 | 원격 승인 위치·회전 보관, 부위 bounds의 기준 좌표 통일, 주기 기록의 화면 위치 역행 방지 |

위 변경의 최종 컴파일·회귀 결과는 이 문서 마지막 검증 절과 `CODE_IMPROVEMENTS.md`의 재점검 절에 기록한다. 소스 반영을 Unity/네트워크 실행 통과로 표시하지 않는다. Q06은 현재 신체의 축정렬 bounds를 승인 root 기준으로 보수적으로 옮긴다. 과거 Animator/뼈 전체를 되감거나 정밀 collider 모양을 복원하는 구현은 아니다.

## 2. 신규 문제 — 발견 내용과 후속 처리 이력

아래 근거·재현 조건은 **수정 전 발견 내용**으로 보존하며, 최신 반영·검증 상태는 문서 마지막의 후속 검증 절을 따른다. 코드 반영과 Unity·성능 실행 검증을 구분한다.

P1은 참가자 조작 또는 핵심 전투·계정 동작에 직접 영향을 주므로 먼저 처리한다. P2는 기능·표시 오류다. P3는 부가 연출, 측정 후 적용할 최적화 또는 추가 구조 정리다. 같은 현상의 여러 호출 지점을 별도 문제로 부풀리지 않는다.

### N01 · P1 · 인증 전 채팅과 전송 빈도 제한 누락

- 근거: [BattleChatNetwork.cs](C:/Github/Battle_PVP/Assets/Player/Script/UI/BattleChatNetwork.cs:91)의 서버 등록은 `requireAuthentication=false`다. `OnServerChatMessage`는 인증·현재 플레이어 확인 없이 전체 reliable 채널로 전송한다. identity가 없는 연결에서는 제출한 발신자 이름도 fallback으로 사용한다. 연결별 빈도 제한은 없다.
- 재현 조건과 영향: Mirror 인증이 끝나기 전에 해당 메시지를 보내면 채팅에 들어갈 수 있다. 승인된 참가자도 메시지를 빠르게 반복하여 모든 참가자의 전송·문자열·레이아웃 비용을 늘릴 수 있다. 클라이언트 UI의 전송 제한만으로는 막을 수 없다.
- 개선: 인증된 현재 방 참가자만 허용하고 표시 이름은 서버의 연결 정보로 결정한다. `Dictionary<connectionId, 전송 예산>`에 작은 token bucket 또는 고정 시간 창을 두고 연결 종료 시 삭제한다.
- 완료 기준: 인증 전/다른 방/identity 없는 요청은 전파 0회. 허용 빈도 안의 정상 채팅·재접속은 유지하며 초과 요청은 제한된다. 원격 참가자가 이름 필드를 바꿔도 서버 발신자와 일치한다.

### N02 · P1 · 접속 중인 클라이언트가 넉백 이동을 무시할 수 있음

- 근거: [PlayerCombat.cs](C:/Github/Battle_PVP/Assets/Player/Script/PlayerCombat.cs:1762)의 넉백은 서버에 추가 이동 허용량을 기록한 뒤 RPC의 `MoveBySkill`로 실제 위치를 바꾼다. `ServerMovementValidator.TryAccept`의 강제 이동은 허용 거리에서 차감하는 방식이므로 제자리 패킷도 승인된다. 서버가 직접 캐릭터를 움직이는 경로는 단절된 캐릭터에 한정된다.
- 재현 조건과 영향: 피격 클라이언트가 강제 이동 RPC의 위치 변경을 생략하고 제자리 좌표를 계속 제출하면 피해는 받아도 밀리지 않을 수 있다. Q01의 과도한 이동/입력 잠금 방어와 별개로, 서버가 요구한 이동의 실행 보장이 필요하다.
- 개선: 접속 중에도 강제 이동의 시작·방향·종료와 충돌 결과를 서버가 소유하도록 한다. 소유자는 예측 표시를 하고 서버 결과에 맞춰 보정한다. 임의 최소 이동 거리만 강요하면 벽에 막힌 정상 플레이어를 오인하므로 충돌과 함께 설계한다.
- 완료 기준: RPC 무시/역행 좌표로 넉백을 취소할 수 없다. 벽·경사·복수 효과·단절/복귀와 정상 지연에서도 과도한 보정 또는 벽 관통이 없다.

### N03 · P1 · 화살이 환경 충돌을 무시함

- 근거: [BowArrowProjectile.cs](C:/Github/Battle_PVP/Assets/Player/Script/BowArrowProjectile.cs:55)의 `TryHit`는 상대 `HealthSystem`/`StatManager`가 없으면 종료한다. [실제 화살 프리팹](<C:/Github/Battle_PVP/Assets/Prefabs/ArrowProjectile Variant.prefab:131>)은 trigger collider와 kinematic Rigidbody를 사용한다. 환경 충돌에서 화살을 끝내는 코드가 없다.
- 재현 조건과 영향: 벽 뒤에 플레이어를 놓고 쏘면 벽 충돌을 무시한 뒤 뒤쪽 플레이어에 피해를 줄 수 있는 코드 경로다. 실제 물리 실행은 아직 하지 않았다.
- 개선: 서버에서 환경 장애물과 플레이어 적중을 구분하고 첫 유효 고체 충돌에서 화살을 종료한다. 빠른 화살의 터널링은 이전 위치→현재 위치의 sweep 검사 필요성을 함께 확인한다. 발사자 collider와 통과해야 하는 trigger는 명시적으로 제외한다.
- 완료 기준: 벽 뒤 대상 피해 0, 노출된 대상 정상 피해 1회, 발사자·장식 trigger 제외, 얇은 벽/낮은 FPS에서 첫 유효 충돌 유지.

### N04 · P1 · 로그인·회원가입 요청의 세션 경합

- 근거: [PlayFabAuthManager.cs](C:/Github/Battle_PVP/Assets/Player/Script/PlayFabAuthManager.cs:46)의 회원가입과 `Login`은 공유 정적 SDK를 호출한다. [PlayFabLoginUI.cs](C:/Github/Battle_PVP/Assets/Player/Script/UI/PlayFabLoginUI.cs:83)에서 중복 요청을 막는 진행 상태가 없다. 성공 콜백마다 닉네임·프로필 세션과 씬 전환 이벤트를 갱신한다.
- 재현 조건과 영향: A 로그인 다음 B 로그인/회원가입을 보낸 뒤 A 응답이 늦게 도착하면 최근 사용자 의도와 다른 세션·닉네임·화면이 적용될 수 있다. SDK 내부의 공유 인증 문맥 변경은 앱의 마지막 이벤트만 무시하는 것으로 해결되지 않는다.
- 개선: 인증 작업을 한 경로로 직렬화하고 요청별 인증 문맥/결과를 검증한 뒤 현재 세션으로 채택한다. UI에도 진행·실패·재시도 수명을 연결한다. 기한이 지났다는 이유만으로 적용 미확정 정적 로그인을 겹치게 하지 않는다.
- 완료 기준: A/B 응답 역전·중복 클릭·회원가입/로그인 교차·화면 종료·실패 후 재시도에서 SDK 계정, 닉네임, 프로필, 성공 이벤트가 동일한 승인 요청을 가리킨다.

### N05 · P2 · 화면 이벤트 예외가 방 생성·참가를 중단함

- 근거: [PlayFabBattleManager.cs](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:401)의 방 생성은 `OnRoomRegistryChanged` 뒤에 Relay 시작으로 이어진다. 참가 경로의 같은 이벤트는 SDK 성공 처리의 try 안에 있어, UI 구독자 예외가 참가 실패 catch와 방 정리를 실행할 수 있다. `SetRoomFlowState`도 multicast 이벤트를 직접 호출한다.
- 재현 조건과 영향: 이벤트 구독자 하나가 예외를 던지면 서버 가입이 성공했어도 방을 떠나거나 Relay 시작 전 상태에 머물 수 있다. Q04에서 보강한 GDM 프로필 완료 경계와는 다른 방 서비스 경로다.
- 개선/완료 기준: 서비스 상태 전이와 화면 통지를 분리하고 구독자별 예외를 격리한다. 예외를 던지는 구독자와 정상 구독자를 함께 붙여도 생성/참가/탈퇴가 올바르게 완료되고 불필요한 멤버십 정리가 발생하지 않아야 한다.

### N06 · P2 · 서비스 인스턴스 교체 시 미확정 저장 격리가 사라짐

- 근거: [PlayFabBattleManager.cs](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:114)의 repository와 [ProfileRequestQueue.cs](C:/Github/Battle_PVP/Assets/Player/Script/Managers/ProfileRequestQueue.cs:20)의 실제 진행 중 저장은 인스턴스별이다. 동일 repository의 세션 초기화는 격리를 유지하지만 새 manager/repository는 이전 쓰기를 알 수 없다.
- 재현 조건과 영향: A 저장의 적용 여부가 미확정인 상태에서 PBM을 파괴·재생성하고 같은 계정으로 B를 저장하면 두 쓰기가 겹칠 수 있다. 같은 프로세스 내부의 서비스 수명 문제이며, 기존에 남겨 둔 여러 장치/프로세스 재시작의 외부 원자성 문제와 구분한다.
- 개선/완료 기준: 미확정 실제 요청을 소유한 조정자의 수명을 UI/MonoBehaviour 교체보다 길게 둔다. manager 교체 뒤에도 같은 계정은 A 확인 전 B를 보내지 않고, 다른 계정은 진행하며, A의 확인된 완료 뒤 정상 재시도가 가능해야 한다.

### N07 · P2 · 닉네임과 채팅의 TMP 서식 태그 해석

- 근거: [MatchLedger.cs](C:/Github/Battle_PVP/Assets/Player/Script/Managers/MatchLedger.cs:227)의 이름 정리는 공백/길이만 제한한다. [RankingEntryUI.cs](C:/Github/Battle_PVP/Assets/Player/Script/UI/RankingEntryUI.cs:19)는 이름을 직접 text에 넣고, [RankingEntry.prefab](C:/Github/Battle_PVP/Assets/Prefabs/RankingEntry.prefab:349)의 Name TMP는 rich text가 켜져 있다. 채팅의 `Sanitize`도 개행/길이만 다루며 결과 화면·처치 알림도 문자열을 직접 표시한다.
- 재현 조건과 영향: `<size=500%>X</size>` 같은 이름·메시지가 그대로 표시되는 대신 크기/색/줄바꿈 서식으로 해석되어 순위·결과·채팅 레이아웃을 바꿀 수 있다. 웹 스크립트 실행 문제가 아니라 게임 UI 입력 경계 문제다.
- 개선: 일반 사용자 문자열을 표시하는 TMP는 rich text를 끄거나, 개발자가 의도한 서식과 사용자 값을 분리하고 사용자 값은 안전하게 문자 그대로 표시한다. 단순 `<noparse>` 감싸기는 종료 태그를 포함한 입력도 고려해야 한다.
- 완료 기준: 서식 태그·개행·Unicode 이름·최대 길이를 모든 표시 경로에서 확인한다. 입력 때문에 다른 행의 서식/크기/배치가 바뀌지 않으며 정상 한국어 이름은 유지된다.

### N08 · P3 · 정체성 위젯 재활성 시 저체력 효과가 이전 값에 머묾

- 근거: [UIIdentityGlitchBinder.cs](C:/Github/Battle_PVP/Assets/Player/Script/UI/UIIdentityGlitchBinder.cs:360)의 초기 동기화는 초과 HP만 복원한다. `_hpPercent`는 `OnHpChanged`에서만 갱신되며 `ApplyAll`의 pulse 계산에 사용된다.
- 재현 조건과 영향: 위젯 비활성 중 HP 변경 후 켜면 다음 HP 이벤트까지 이전 저체력 효과를 보여준다. 기본 HP 바와 별개인 부가 연출 오류다.
- 개선/완료 기준: 재활성 시 현재 HP·최대 HP로 비율과 overflow를 함께 복원한다. 저체력→비활성→회복→활성, 그 반대, 최대 HP 변경, HP 이벤트 없는 재활성에서 즉시 올바른 pulse를 표시한다.

## 3. 추가 최적화·구조 제안 — 측정 또는 별도 구현 후 판정

### N09 · P3 · 처치 알림의 생성/파괴·수명 관리

- 근거: [KillAnnouncementUI.cs](C:/Github/Battle_PVP/Assets/Player/Script/UI/KillAnnouncementUI.cs:108)는 처치마다 프리팹/텍스트 객체를 생성하고 개별 코루틴·대기 객체를 만든 뒤 파괴한다. 레이아웃 설정과 raycast 대상 검색도 매 알림에 반복한다. 피해 숫자 팝업에 적용한 풀과는 별도의 경로다.
- 개선: 실제 8인 연속 처치에서 비용을 먼저 측정한다. 비용이 유의미하면 작은 `Stack` 재사용 풀과 활성 항목의 만료 목록을 사용하고, 정적 레이아웃/참조는 생성 때 한 번 설정한다. 비활성/재활성·씬 종료 시 만료 항목 정리도 명시한다.
- 완료 기준: 워밍업 후 설정 용량 안에서 Instantiate/Destroy 0회, 동시 알림 수 제한, 정확한 만료와 재사용 초기화. 전후 GC/CPU를 같은 부하에서 비교한다. 현재 FPS 개선량은 측정하지 않았다.

### N10 · P3 · 전투 스킬 실행과 네트워크 어댑터의 추가 분리

- 근거: [PlayerCombat.cs](C:/Github/Battle_PVP/Assets/Player/Script/PlayerCombat.cs:1304)의 스킬 선택, `CoAdvancedSkillCast`, `ApplyAdvancedSkill`, 프리셋/무기 보너스, 이동 효과, 스킬 데이터 해석이 여전히 한 클래스의 상태와 코루틴을 공유한다. 앞서 분리한 실행 기한·잠금·독 helper는 사용 중이며 불필요한 변경이 아니었다.
- 개선: 새 스킬 추가 시 여러 선택/적용/복원 분기를 함께 수정해야 하는 영역부터 스킬 실행과 효과 적용 책임을 단계적으로 옮긴다. `PlayerCombat`에는 Mirror 요청 검증·SyncVar 전달을 두고, 실행 부분은 명시적 입력/결과로 검사한다. 클래스 줄 수를 줄이려고 빈 wrapper나 스킬마다 범용 계층을 만드는 방식은 피한다.
- 완료 기준: 스킬 하나의 변경이 관련 없는 네트워크/소유자 복구 경로까지 요구하지 않는다. 기존 취소·쿨다운·독·재접속 회귀 유지, 서버/소유자/관찰자 표시 확인. 이 추가 분리는 초기 R01/T04의 이미 반영한 부분을 미구현으로 되돌린다는 뜻이 아니다.

### N11 · P3 · 방 상태 조율과 캐시의 중복 소유 정리

- 근거: [PlayFabBattleManager.cs](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:69)는 방 이름·방장·인원·Relay 코드·통합 RoomInfo의 여러 dictionary를 별도로 보유한다. `RoomFlow` 세대/취소, 멤버십 정리, Relay 준비, 생존 신호, 목록 캐시와 SDK 호출도 이 클래스가 조율한다. 기존 helper 분리 뒤에도 상태를 같이 갱신해야 할 지점이 많다.
- 개선: 중복 map은 실제 갱신 경로를 대조해 `Dictionary<string, RoomInfo>`를 중심으로 통합한다. 방 세션의 상태 전이/정리 결정과 SDK·Unity 실행 어댑터를 분리하고 상태 작성 주체를 하나로 정한다. 8인 목록에 새 트리/복잡한 인덱스가 필요한 상황은 아니다.
- 완료 기준: 같은 방의 이름/정원/코드가 서로 다른 캐시에 남지 않는다. Join/Leave/취소/응답 역전/heartbeat 예외를 씬 없이 검사할 수 있고 기존 정상 흐름과 오류 통지를 유지한다.

## 4. 신규 결함으로 집계하지 않은 항목

- 초기 S11의 서버 발급 경기 ID·영속 제출/재시도·원자적 중복 보상, 여러 장치의 동일 계정 멤버십, Client API 정책과 registry 이전은 **기존 외부 구현·설정 잔여 작업**이다.
- Unity import/Mirror 코드 생성·실제 NUnit 실행·다중 접속·Windows/WebGL 성능과 GC/전송 비용은 **기존 실행 검증 잔여 작업**이다. 이번 정적 검토와 일반 C# 빌드로 대체하지 않는다.
- `UIIdentityGlitchBinder`의 원격 플레이어 fallback 오연결: 실제 `UI_Root`는 `Player`의 자식이며 부모 StatManager가 우선 선택되어 현재 프리팹 경로에서는 성립하지 않는다.
- 프로젝트의 `runInBackground: 0`만으로 호스트가 Alt-Tab 시 정지한다고 판단하지 않는다. 실제 NetworkManager 프리팹은 `runInBackground: 1`이고 Mirror가 실행 때 이를 적용한다.
- `AttackProcessor`의 타격 로그는 `_logHits` 조건 안에 있어 무조건적인 매 타격 로그로 집계하지 않는다.
- 8명짜리 명단의 단순 순회/정렬이나 단독 파일 줄 수는 결함의 근거가 아니다. 이 규모에서는 소유권·정확성·할당 빈도부터 개선한다.

## 5. 권장 작업 순서와 검증

1. Q01–Q06의 통합 컴파일·순수 로직 회귀는 완료했고 Unity 다중 접속 검증은 대기다.
2. N01–N04의 요청 권한, 강제 이동, 투사체 장애물, 로그인 경합은 코드 반영·로컬 검사를 완료했다. 아래 최신 결과를 따른다.
3. N05·N06 서비스 완료/수명 경계와 N07·N08 표시 입력/재활성 정확성의 코드를 반영했다. 각 단계의 검증 결과는 아래를 따른다.
4. N09 수명 보강과 계측을 먼저 반영하고 실제 8인 비용에 따라 풀 적용을 판단한다. N10·N11의 판정/상태 소유권 분리 결과는 마지막 절에서 갱신한다.
5. 기존 외부 백엔드·배포와 Windows/WebGL 성능 검증은 별도 완료 기준으로 진행한다.

Q01–Q06 단계의 일반 C# 빌드는 **오류 0 / 기존 CS0414 경고 4개**였다. 순수 CLI 빌드는 오류 0/경고 0, 실행은 **56,429 assertions PASS**(직전 56,379 유지 + 당시 50)였다. 실제 production의 이동 상태 이력·잠금 전 이동 구간·비행 제한·복원 시간 규칙을 링크해 검사했다.

이번에 추가한 NUnit은 **52개 사례 작성·컴파일**이다: `MovementControlAuthorityTests` 8, `ProfileDataIntegrityTests` 24, `GlobalProfileCompletionTests` 7, `ServerPoseConsistencyTests` 10, 기존 `LocalProfileUiBindingTests`에 추가 3. 정상 legacy fixture도 수정했다. **Unity에서 실행하지 않았으며 CLI 검사 수에 포함하지 않는다.**

S05 독립 검토에서 동일 timestamp 슬롯 중복, 링 순환 뒤 선택 불일치, 늦은 승인 패킷 이후의 원격 hold 기록, 호스트 로컬 실제 기록과 hold 구분을 추가 보강했다. 승인된 root와 body를 같은 슬롯에서 교체하며 현재 콜라이더나 표시 Transform을 직접 이동하지 않는다.

Q01–Q06 단계에서 코드·문서 공백/충돌 표시, 새 Unity `.meta`, 검토 문서의 파일 링크를 확인했다. 당시 CloudScript·PlayFab 배포/설정과 Unity 실행은 변경·실행하지 않았다. 신규 N01–N11은 그 시점에는 수정 대기였고 아래 후속 작업으로 N01–N04 상태를 갱신한다.

## 6. 후속 P1 수정 결과 — 2026-09-07

| 항목 | 반영 내용 | 현재 상태 |
|---|---|---|
| N01 | 인증된 현재 연결·방·spawn·소유 avatar·접속 여부 확인, 서버 이름만 사용, 연결별 연속 3회/초당 1회 충전, 종료 시 예산 정리 | 코드 및 로컬 검사 완료 / 실제 Mirror 채팅 검증 대기 |
| N02 | 강제 이동을 서버 CharacterController로 실행, 구간 중 owner 위치 패킷 차단, 제한된 입력만 접수, reliable 최종 위치·새 세대로 복원, 효과 교체·단절 시 서버 기한 유지 | 코드 및 로컬 검사 완료 / 벽·경사·공중·지연·재접속 실행 대기 |
| N03 | 실제 화살 BoxCollider로 최초 겹침·이동 구간 sweep, 가장 가까운 고체/피해 대상 선택, 발사자·장식 trigger 제외, 피해/종료 1회 | 코드 및 로컬 검사 완료 / 프리팹 물리 실행 대기 |
| N04 | 요청별 SDK 인증 context, 최신 로그인만 채택, 요청·프로필 버전·현재 인스턴스 검사, UI 중복/취소/기한/씬 이동 수명 연결 | 코드 및 로컬 검사 완료 / 실제 SDK·씬 검증 대기 |

일반 C# **오류 0 / 기존 CS0414 경고 4개**, 순수 CLI 빌드 오류 0/경고 0 및 **56,632 assertions PASS**(이전 56,429 + 이번 203)다. 신규 NUnit은 채팅 15·서버 강제 이동 9·화살 물리 10·인증/UI 14로 **48개 사례 작성·컴파일만 완료**했다. Unity에서는 실행하지 않았고 CLI 검사 수에 포함하지 않는다.

독립 검토로 넉백 중 순간이동의 검증 위치, 이동 시간 0인 프레임의 점프 소비, reliable 상태보다 먼저 도착한 SyncVar, 프로필 reset 재진입/활성 인스턴스 교체 경계를 보강했다. Q02의 owner 코루틴 재시작 경로는 서버 단일 강제 이동 시각으로 대체했다. 변경 이유·명령·보존 정책은 [CODE_IMPROVEMENTS.md](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 `후속 P1 수정 N01–N04`에 기록했다.

P1 직후 예정한 순서는 N05·N06 → N07·N08 → 측정/필요 범위에 따른 N09–N11이었으며, N05·N06은 아래 후속에서 반영했다. 기존 S11 외부 백엔드/배포·정책과 Unity/Mirror·8인 Windows/WebGL 성능 검증은 별도로 남아 있다. P1 단계에서 CloudScript·PlayFab 배포/설정이나 Unity 실행은 진행하지 않았다.

## 7. N05·N06 수정 이력 — 2026-09-07

N05는 방 이벤트·콜백을 소비자별로 격리하고 연결 시작을 UI 이벤트에서 분리했다. 실패/종료 상태를 한 번에 통지하며 알림 중 새 flow가 시작되면 옛 상태를 버린다. cleanup 전에 이전 연결을 정리하고 새 방 commit 직전 현재 요청을 확인한다. 비활성/파괴/종료 중 Create·Join을 거절한다. N06은 production repository의 계정/title별 실제 write lease를 공유 coordinator로 옮겨, 매니저 교체·timeout 뒤에도 확인 전 중복 전송을 막는다.

검증: 전체 C# **오류 0 / 기존 경고 4개**, 순수 CLI **56,665 assertions PASS**(직전 +33). 새 NUnit **24개(방 알림 16 + 저장 수명 8)는 작성·컴파일만 완료**했으며 Unity에서는 실행하지 않았다. 실제 SDK·Relay·물리·8인 성능을 검증한 수치가 아니다. 상세 변경·실행 명령은 [CODE_IMPROVEMENTS.md](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 `후속 P2 서비스 경계 N05·N06`을 따른다.

N06은 같은 managed domain 안의 서비스 교체를 보호한다. domain reload·프로세스 재시작·다른 장치까지의 쓰기 원자성은 이 로컬 변경의 완료 범위가 아니다. N05·N06 단계에 CloudScript 내용·외부 배포/설정·Unity 실행은 진행하지 않았다.

## 8. N07·N08 수정 이력 — 2026-09-07

| 항목 | 반영 내용 | 완료 조건과 남은 확인 |
|---|---|---|
| N07 사용자 문자열 | 순위·채팅·방 목록/상단 정보·캐릭터 정보·처치 알림에 일반 텍스트 표시 적용. 결과 화면은 개발자 서식을 유지하고 사용자 이름만 이스케이프. 사용자 제어문자는 공백으로 표시하고 닉네임 64/방 이름 80/채팅 120 UTF-16 단위로 제한. 공동 우승 목록은 8명·구분자까지 526 단위 보존 | 태그·종료 태그·리터럴 Unicode escape로 서식/행을 삽입하지 못하고 정상 한국어·완전한 surrogate pair는 보존. 저장 데이터·방 ID·통계·입력/IME 값은 유지. 실제 TMP 표시와 Windows/WebGL 시각 확인은 대기 |
| N08 위젯 체력 복원 | 활성/원본 재연결 시 현재 HP와 최대 HP를 함께 읽어 저체력 pulse와 overflow 갱신. 읽기 원본이 없거나 최대 HP가 0 이하면 기본 pulse/overflow 적용. 파괴된 Unity 원본의 interface 캐시 정리 | 비활성 중 회복·피해·최대 HP 변경을 이벤트 없이 즉시 반영하고 구독이 중복되지 않는다. 실제 Shader material·활성 수명 테스트 실행은 대기 |

설치된 TMP는 `parseCtrlCharacters=false`여도 `\u`·`\U`를 해석한다. 일반 출력은 rich text를 끄고 사용자 backslash를 두 배로 만든 뒤 제어문자 파싱을 켜서 한 번만 소비하게 한다. 결과 화면은 사용자 `<` 각각을 별도 no-parse 토큰으로 표시하여 사용자가 넣은 종료 태그도 문자 그대로 남긴다. 입력창의 실제 문자열을 출력용 escape로 바꾸지 않는다.

UI 앞단의 경기 기록 이름과 채팅 전송 문자열도 길이 경계에서 surrogate pair를 나누지 않도록 보강했다. 기존 채팅 발신자 24/본문 120, 경기 기록 이름 64 제한과 네트워크 형식·프로필 원본은 유지한다.

전체 C#은 **오류 0 / 기존 CS0414 경고 4개**다. 순수 CLI는 **56,727 assertions PASS**(직전 56,665 + 이번 62), 빌드는 오류 0/경고 0이다. 새 NUnit은 TMP 렌더/결과 22 + 실제 UI 연결 8 + 위젯 수명 14 + 채팅 전송 문자 경계 1 = **45개 작성·컴파일**했으며 **Unity에서는 실행하지 않았다**. 실제 TMP 화면·Shader material·네트워크·성능 검증을 이 수치에 포함하지 않는다. N07 입력 경계와 N08 재활성 경로는 독립 읽기 검토도 마쳤다.

| N07·N08 직후 남았던 작업 | 다음 완료 조건 |
|---|---|
| N09 · P3 처치 알림 | 8인 부하 비용 측정 후 필요한 재사용 풀·만료 목록·동시 개수 제한과 전후 GC/CPU 비교 |
| N10 · P3 전투 책임 | 스킬 선택/시전/효과 적용과 Mirror 전달 책임 추가 분리, 기존 취소·독·쿨다운·강제 이동·재접속 회귀 유지 |
| N11 · P3 방 책임 | 중복 방 캐시 일관성 및 상태 전이/정리 결정과 SDK·Unity 실행의 추가 분리 |
| S11 등 외부 작업 | 서버 발급 경기 ID·영속 결과 제출/재시도·원자적 보상, 다중 장치 멤버십, PlayFab 스크립트 배포·API 정책·기존 데이터 이전 |
| Unity/성능 | import·Mirror weaving·NUnit 실제 실행, 기본 다중 접속 후 host+7명 전투/지연/단절 검증. Windows/WebGL 각각 30초 워밍업 후 60초×3회, p95≤16.67ms와 CPU/GPU/GC·전송량 비교 |

N07·N08 단계에 CloudScript 내용·외부 배포/설정·Unity/MCP 실행은 진행하지 않았다. 상세 변경과 명령은 [CODE_IMPROVEMENTS.md](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 `후속 표시 정확성 N07·N08`을 따른다.

## 9. Weaver 오류 복구 및 N09–N11 최신 상태 — 2026-09-07

사용자가 재현한 `CmdUpdateStats cannot have optional parameters`는 선언의 기본값 제거와 명시적 `0` 전달로 수정했다. 일반 C# 빌드의 검증 누락을 줄이도록 [설치 Weaver 로컬 검사](C:/Github/Battle_PVP/Tools/Tests/MirrorWeaverCheck/README.md)를 추가했다. 최신 전체 런타임 어셈블리의 코드 생성과 옛 오류의 메모리 재현 대조가 모두 통과했다.

| 항목 | 이번 반영 | 현재 상태 |
|---|---|---|
| N09 | 비활성 알림 생성 거절, 소유 항목/만료 코루틴 정리, 외부 컨테이너 보존·재활성/프리팹 재진입 보강. 개발용 Show/CreateItem/Release 계측 | 수명 코드·컴파일 반영 / 실제 동작·8인 CPU/GC 측정 및 필요 시 풀·상한·만료 목록 적용 대기 |
| N10 | 직업별 선택·서버 허용·시전 적용 계획과 프리셋/보너스 계산을 순수 코드로 분리. PC는 실제 효과·코루틴·Mirror 전달 담당. 실패한 프리셋 적용은 복귀 기억값을 바꾸지 않음 | 코드·로컬 검사 완료 / 취소·독·쿨다운·재접속과 서버/소유자/관찰자 실행 확인 대기 |
| N11 | 방 목록 map 5개→1개, 목록 TTL/revision과 세션 정리/Relay 예약 소유 분리. 삭제·퇴장·Relay 변경과 늦은 응답 충돌, timeout의 미확인 방 숨김, 같은 방 재요청 후 늦은 ACK 정리 경계 보강 | 코드·로컬 검사 완료 / 실제 SDK·Relay·다중 접속 확인 대기 |

전체 C# **오류 0 / 기존 경고 4개**, 실제 설치 Mirror 코드 생성 검사 **통과**, 순수 CLI **56,982 assertions PASS**(직전 +255)다. 새 NUnit **52사례(8+15+14+15)는 작성·컴파일**했으며 실제 Unity 실행은 하지 않았다. N09 수명 검사는 EditMode의 명시적 콜백/IEnumerator 경계이며 시간 경과 증명이 아니다. 구체적인 범위·명령은 [CODE_IMPROVEMENTS.md](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 마지막 절을 따른다.

남은 작업은 다음과 같다.

- N09의 실제 8인 비용 측정 후 재사용 풀·동시 상한·단일 만료 목록의 필요성을 판단하고 전후 CPU/GC를 비교한다. [측정 절차](C:/Github/Battle_PVP/PERFORMANCE_VERIFICATION.md)에 실제 프리팹과 계측 구간을 기록했다.
- Unity 전체 import/플랫폼 빌드·NUnit 실제 실행, 호스트+원격 2명의 기본 기능, 이후 호스트+7명의 지연/단절/재접속과 Windows/WebGL 각각 60 FPS 기준을 검증한다.
- S11 서버 발급 경기 ID·영속 결과 제출/재시도·원자적 보상, 다중 장치 멤버십, PlayFab 스크립트 배포·API 정책·기존 데이터 이전을 진행한다.

N10·N11의 관련 Unity/SDK 실행 어댑터가 기존 클래스에 남는 것은 위 분리 범위에 따른다. 모든 연관 코드를 옮기거나 전체 기능 검증까지 완료했다고 표시하지 않는다. 이번에 CloudScript 내용과 외부 배포/설정은 변경하지 않았다.

## 10. 사용자 재현 초기화 예외 수정 — 2026-09-07

PBM의 필드 생성 중 프로필 queue가 계정 delegate를 즉시 실행하여 PlayFab 설정의 `Resources.LoadAll`이 Unity 로딩 스레드에서 호출됐다. queue 생성은 순수 관리 상태 준비만 수행하고, 최초 실행 작업에서 현재 계정을 읽도록 수정했다. 기존 readonly 저장소·비활성 객체 계약·미확정 저장 coordinator는 유지한다.

수정 전 생성자 접근 금지 회귀가 실패하고 수정 후 통과했다. 전체 C# 오류 0/기존 경고 4개, CLI **57,012 assertions PASS**, 실제 설치 Weaver 및 오류 재현 대조 통과. 새 NUnit **3개는 작성·컴파일**했다. 같은 종류의 다른 소유 코드 초기화 결함은 감사에서 발견하지 못했지만, Unity 실제 재실행과 CrashReporter 메시지 해소는 확인 대기다. 위 N09 실측·Unity 다중 접속·S11 외부 구현 대기는 유지한다. 구체적인 조건은 [변경 기록](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 마지막 절을 따른다.

## 11. 사용자 재현 로그인 SDK 예외 수정 — 2026-09-07

N04의 개별 인증 context가 SDK 자동 기기 정보 전송에도 전달되도록 로그인·회원가입을 요청별 `PlayFabClientInstanceAPI` 호출로 바꿨다. 정적 API의 `instanceApi=null` 경로에서 전역 세션을 읽어 성공 콜백 전에 예외가 나던 원인을 제거했다. 최신 로그인만 전역 계정을 채택하는 기존 처리는 유지한다. 코드·C# 컴파일은 반영했고 실제 Unity 로그인/로비 진입 검증은 대기다. SDK 응답 처리 경계를 포함한 검사와 구체적인 범위는 [변경 기록](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 마지막 절을 따른다.

## 12. 방 입장 시 초기화·팝업 정리 오류 수정 — 2026-09-07

HealthSystem의 미바인딩 Mirror 속성 접근과 DamagePopupManager의 파괴 객체 풀 정리를 수정했다. 직접 연결된 StatManager/StatBalanceConfig의 검증 이벤트도 준비된 main-thread 실행으로 옮겼다. 전체 C# 오류 0/기존 경고 4개, 실제 설치 Weaver 검사 통과. 새 체력 12·팝업 8·스탯 9 = **29개 NUnit은 작성·컴파일**, Unity 실행은 대기다. 원인·합격 조건·한계는 [변경 기록](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 마지막 절을 따른다.

좁은 OnValidate 감사에서 별도 후속 후보도 확인했다. `SkillUI.OnValidate`는 기본 overlay가 없을 때 Texture2D/Sprite 생성과 UI 변경까지 수행하고, `BgmManager.OnValidate`는 AudioSource 설정을 직접 변경한다. 이번 제보 스택에는 없는 경로이며 현재 사용자 환경의 예외 재현은 확인하지 않았다. 두 경로의 편집 미리보기와 메인 스레드 실행 시점 정리는 후속 점검 대상으로 남겼다.

## 13. 플레이 흐름 추가 검토 — 2026-09-07

최근 커서·UI 시작 상태 수정까지 반영된 작업 트리를 대상으로 전투/생존, 방/Relay, UI/프리팹을 병렬 검토했다. 아래 11개는 소스의 분기·호출자·실제 직렬화 참조를 교차 확인한 추가 문제다. **Unity 플레이·변조 클라이언트·실제 Relay 부하로 재현한 결과는 아니다.** 재현 조건은 다음 실행 검증에서 사용할 절차이며 발생 빈도나 성능 수치를 측정하지 않았다. 게임 코드·프리팹·서비스는 변경하지 않고 이 검토 기록만 추가했다.

기존 OnValidate 후보, S11 외부 백엔드/배포·원자성, N09 성능 실측 대기는 새 발견에 중복 집계하지 않았다. P1은 핵심 통신·공정성·기능 유실을 우선 수정할 항목, P2는 특정 플레이/연결 순서에서 발생하는 후속 항목이다.

| ID | 우선순위 | 추가 문제 | 핵심 조건 |
|---|---|---|---|
| G01 | P1 | Reliable 메시지 전송 실패를 복구하지 않아 중요 상태가 누락됨 | 전송 큐/미확인 패킷 창 포화 |
| G02 | P1 | 접속 중 원격 참가자가 도발 이동·강제 공격을 무시할 수 있음 | 소유 클라이언트가 로컬 도발 처리를 생략 |
| G03 | P1 | 콤보 첫 타격을 건너뛴 요청을 서버가 승인함 | 공격 대기 상태에서 후속 공격 index 요청 |
| G04 | P1 | 로컬 설정 창이 원격 플레이어의 생성·퇴장 순서에 종속됨 | 원격 customizer가 먼저 Awake 실행 |
| G05 | P2 | 준비가 끝나지 않은 참가자도 경기 중 공격받을 수 있음 | 최초 스탯 승인이 경기 시작의 고정 1.5초보다 늦음 |
| G06 | P2 | Relay가 죽었는데도 호스트 방의 생존 신호가 계속됨 | PlayFab HTTP는 정상, Relay 할당만 무효화 |
| G07 | P2 | 사망 중 연 스탯 창이 부활 뒤 남고 커서는 잠김 | 스탯 창을 연 채 Space 부활 |
| G08 | P2 | 채팅의 띄어쓰기가 부활 요청으로 처리됨 | 부활 대기 종료 후 네이티브 채팅에서 Space 입력 |
| G09 | P2 | 일반 UI 입력이 이동·카메라 입력과 분리되지 않음 | 방 이름 입력, 로비/대기실 스탯 슬라이더 조작 |
| G10 | P2 | 옛 방 매니저의 정리가 새 매니저의 가입을 지울 수 있음 | 매니저 교체 뒤 같은 계정·방의 늦은 Join 응답 |
| G11 | P2 | 초과 체력 표시가 스탯 미리보기 아이콘을 숨김 | 정상 HP 바인딩 후 스탯 창 최초 열기 |

### G01 — Reliable 전송 실패의 유실 처리

- 근거: [UnityRelayTransport.Send](C:/Github/Battle_PVP/Assets/Player/Script/Managers/UnityRelayTransport.cs:590)는 BeginSend/EndSend 실패 시 오류 이벤트만 호출한다. 설치된 [ReliableSequencedPipelineStage](C:/Github/Battle_PVP/Library/PackageCache/com.unity.transport@ed7eca02732f/Runtime/Pipelines/ReliableSequencedPipelineStage.cs:137)는 창 포화로 재전송 저장에 실패하면 NetworkSendQueueFull을 반환한다. [Mirror NetworkConnection.Update](C:/Github/Battle_PVP/Assets/Mirror/Core/NetworkConnection.cs:155)는 이미 소비한 배치를 다시 보내지 않으며, 기본 NetworkManager의 오류 콜백은 연결 종료를 보장하지 않는다.
- 조건/영향: RTT가 높거나 한 번에 많은 reliable 데이터를 보낼 때 승인·스폰·Command/RPC 메시지가 사라져도 연결은 계속 유지될 수 있다. UTP의 정상적인 재전송이 처리하는 것은 전송 창에 이미 저장된 패킷이다.
- 개선/완료 기준: 임시 포화는 크기·순서를 제한한 대기열로 처리하고 복구 불가능한 오류는 명확한 연결 실패로 전환한다. 포화 후 메시지가 순서대로 전달되거나 연결 실패로 판정돼야 하며, 성공처럼 진행하면서 메시지만 버리면 안 된다.

### G02 — 도발의 서버 실행 누락

- 근거: [SetTauntedBy](C:/Github/Battle_PVP/Assets/Player/Script/PlayerCombat.cs:2649)는 대상 ID와 만료 시각만 기록한다. [서버 도발 실행](C:/Github/Battle_PVP/Assets/Player/Script/PlayerCombat.cs:2658)은 연결이 남아 있는 원격 참가자를 제외하고, 이동/강제 공격은 소유자 UpdateLocalTauntControl에 의존한다. [서버 이동 제한 기록](C:/Github/Battle_PVP/Assets/Player/Script/PlayerManager.cs:1521)에는 도발 방향·점프 제한이 포함되지 않는다.
- 조건/영향: 변조 참가자가 로컬 도발 처리만 생략하면 정상 속도 범위 안에서 도주/점프하고 강제 공격을 보내지 않을 수 있다. 이미 보강한 넉백의 서버 실행과 별개다. 정상 활 사용자도 로컬 도발이 요청하는 근접 공격이 CmdStartAttack의 활 장착 검사에서 거절되는 host/remote 차이가 있다.
- 개선/완료 기준: 서버가 접속 중 참가자의 도발 이동 방향·제한·공격 결정을 소유한다. 도발 상태에서 소유자 구현을 생략해도 효과가 유지되고, 근접/활·호스트/원격·단절/복귀에서 같은 규칙을 적용해야 한다.

### G03 — 공격 시작 시 콤보 순서 검증 누락

- 근거: [CmdStartAttack](C:/Github/Battle_PVP/Assets/Player/Script/PlayerCombat.cs:713)의 index 순서 검사는 isAttacking인 경우에만 실행된다. 대기 상태에서는 유효 범위 내 어느 index도 시작 가능하다. 실제 [Player 콤보 연결](C:/Github/Battle_PVP/Assets/Prefabs/Player.prefab:2927)은 Atk_1.damage=1과 Atk_2.damage=1.3을 사용하며 [피해 계산](C:/Github/Battle_PVP/Assets/Player/Script/AttackProcessor.cs:115)에 선택된 계수가 곱해진다.
- 조건/영향: 참가자가 공격 대기 상태마다 후속 타격 index를 직접 요청하면 선행 1타 없이 강한 타격부터 사용할 수 있다. 1.3배는 해당 타격 계수이며 DPS 증가량을 측정한 값이 아니다.
- 개선/완료 기준: 다음 콤보 index와 연결 기한을 서버가 결정한다. 대기/종료/취소 후에는 0만 허용하고 정상 0→1→2 연결은 유지한다. 요청 sequence가 새 값이어도 잘못된 콤보 단계는 거절해야 한다.

### G04 — 설정 창 singleton의 플레이어 소유권 누락

- 근거: [StatCustomizerController.Awake](C:/Github/Battle_PVP/Assets/Player/Script/UI/StatCustomizerController.cs:80)는 최초 인스턴스를 선택하고 이후 인스턴스의 Stat_Setting 오브젝트를 파괴한다. Player의 UI_Root에는 이 부모가 활성 상태로 포함된다. [PlayerHUD 원격 숨김](C:/Github/Battle_PVP/Assets/Player/Script/PlayerHUD.cs:569)은 HP slider의 Canvas_HUD만 닫으며 형제 Stat_Setting은 제외된다. [LobbyUI 검색](C:/Github/Battle_PVP/Assets/Player/Script/UI/LobbyUIManager.cs:269)도 로컬 소유자 대신 첫 Canvas_Customizer를 선택한다.
- 조건/영향: 원격 UI가 먼저 생성되면 로컬 설정 UI가 중복으로 파괴된다. 선택된 원격 객체가 이후 제거되면 남은 설정 창을 찾지 못할 수 있다. 실제 Mirror 스폰 순서별 발생 여부는 실행 확인이 필요하다.
- 개선/완료 기준: 로컬 플레이어 또는 명시적인 오프라인 UI만 전역 바인딩을 소유하게 한다. 원격 선행/후행 스폰과 해당 원격 퇴장 후에도 설정 창이 유지되고, 표시·적용 대상이 로컬 플레이어와 일치해야 한다.

### G05 — 고정 지연으로 시작해 미준비 참가자가 피격됨

- 근거: [MatchFlowRoutine](C:/Github/Battle_PVP/Assets/Player/Script/Managers/BattleStateMachine.cs:141)은 1.5초 후 스폰·체력 처리와 InBattle 전이를 진행한다. [최초 스탯 승인 기한](C:/Github/Battle_PVP/Assets/Player/Script/Stats/StatManager.cs:309)은 별도로 10초다. 이동은 미승인 시 거절되지만 피해 경로는 공격자의 승인만 검사하고 [피해자 승인 여부](C:/Github/Battle_PVP/Assets/Player/Script/HealthSystem.cs:312)는 확인하지 않는다.
- 조건/영향: 원격 객체는 생성됐지만 초기 스탯 패킷이 늦으면, 상대는 경기 중인데 해당 참가자는 움직이지 못한 채 기본 스탯으로 공격받을 수 있다. 아직 로딩 중인 사람을 포함한 전체 준비 완료를 기다리는 코드도 없다.
- 개선/완료 기준: 시작 명단별 ready와 스탯 승인을 기한 안에서 확인하고, 미준비/중도 참가자는 승인 전 피격도 막는다. 느린 로드와 승인 지연에서 불리한 무방비 시간이 없어야 하며 이탈자는 대기를 무기한 막지 않아야 한다.

### G06 — Relay 실패와 방 생존 판정의 분리

- 근거: [PollServer](C:/Github/Battle_PVP/Assets/Player/Script/Managers/UnityRelayTransport.cs:488)는 driver 존재만 확인한다. 설치 UTP의 AllocationInvalid는 새 할당/driver 생성이 필요한 상태인데 게임 코드는 GetRelayConnectionStatus를 읽지 않는다. [호스트 heartbeat 조건](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:196)은 NetworkServer.active다.
- 조건/영향: 호스트 프로세스와 PlayFab HTTP는 살아 있지만 Relay가 실패하면 참가 불가능한 방이 계속 갱신될 수 있다. ServerStart의 Bind/Listen 실패도 오류 통지만 하므로 등록 단계에 실제 서버 준비 확인이 필요하다. 이전 프로세스 강제 종료의 TTL 수정과 다른 경계다.
- 개선/완료 기준: Relay 준비/종료/할당 무효화를 호스트 상태·방 등록/heartbeat와 연결한다. Relay만 실패한 상황에서 방을 계속 정상으로 공지하지 않고 종료 또는 재생성 경로로 전환해야 한다.

### G07 — 부활 시 열린 스탯 창과 입력 모드 불일치

- 근거: [OnLocalPlayerRevived](C:/Github/Battle_PVP/Assets/Player/Script/UI/LobbyUIManager.cs:529)는 RefreshVisibility만 호출한다. 살아난 경우에는 버튼을 숨기고 열린 Canvas_Customizer는 닫지 않는다. [HandleRevived](C:/Github/Battle_PVP/Assets/Player/Script/PlayerManager.cs:1783)는 플레이 입력을 복구해 Battle의 커서를 다시 잠근다.
- 조건/영향: 사망 중 스탯 창을 열어 둔 채 Space로 부활하면 창이 화면을 덮는데 커서는 잠기고 설정 토글 버튼도 사라진다. 초기 진입 시 닫기 수정으로 해결되는 상태 전이는 아니다.
- 개선/완료 기준: 승인된 부활에서 사망 중 편집 창을 정리하고 커서/입력 정책을 함께 전환한다. 열기→부활, 저장 요청 중 부활, 부활 거절의 세 경우에 창과 조작 상태가 일치해야 한다.

### G08 — 채팅 Space가 부활 입력으로 사용됨

- 근거: [RespawnRoutine](C:/Github/Battle_PVP/Assets/Player/Script/PlayerManager.cs:1914)은 스페이스와 부활 기한만 검사하며 IsTextInputActive/입력창 포커스를 확인하지 않는다. [서버 부활 판정](C:/Github/Battle_PVP/Assets/Player/Script/HealthSystem.cs:724)은 정상적인 기한/생존/경기 상태를 검사하므로 이 의도치 않은 요청을 구분하지 못한다.
- 조건/영향: Windows 등 네이티브 채팅에서 사망 후 대기 기한이 지나면 띄어쓰기만 해도 부활할 수 있다. WebGL의 별도 DOM 입력은 키 전파 경로가 달라 같은 현상으로 단정하지 않는다.
- 개선/완료 기준: UI가 소비한 입력을 부활 요청에서 제외한다. 채팅 중 Space는 문자 입력만 처리하고 채팅 종료 후 명시적인 Space는 정상 부활시켜야 한다.

### G09 — 일반 설정/텍스트 입력의 게임 입력 차단 누락

- 근거: 텍스트 입력 상태를 설정하는 실제 사용처는 [BattleChatUI](C:/Github/Battle_PVP/Assets/Player/Script/UI/BattleChatUI.cs:215)와 채팅 크기 조절뿐이다. Lobby_UI의 방 이름 TMP_InputField에는 onSelect/onDeselect 연결이 없다. [이동 Update](C:/Github/Battle_PVP/Assets/Player/Script/PlayerManager.cs:750)는 이 전역 상태에 의존하며, [FollowCamera](C:/Github/Battle_PVP/Assets/Player/Script/FollowCamera.cs:58)는 별도 창 상태 없이 마우스 delta를 읽는다.
- 조건/영향: 방 이름에 WASD/Space를 입력할 때 플레이어 이동/점프가 함께 실행될 수 있다. 로비/대기실에서 커서가 보여도 스탯 슬라이더 조작 중 카메라가 같이 회전하는 경로도 남는다.
- 개선/완료 기준: 일반 입력창 포커스와 열린 설정 창이 필요한 입력을 소유하도록 중앙 정책에 연결한다. 이름 입력 중 이동/점프가 없고, 설정 조작 중 시점이 흔들리지 않으며, UI 종료 후 입력이 정상 복구돼야 한다.

### G10 — 매니저 교체를 넘지 못하는 방 정리 보호

- 근거: [방 세대·큐·응답 gate](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:81)는 인스턴스별이다. 옛 Join 응답은 [stale cleanup](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:375)을 예약하고, [ScheduleRoomCleanup](C:/Github/Battle_PVP/Assets/Player/Script/Managers/PlayFabBattleManager.cs:1052)은 새 인스턴스의 현재 방 상태를 모르는 옛 flow 판정으로 Leave를 전송할 수 있다.
- 조건/영향: A의 Join 대기→A 교체→B가 같은 계정·방 Join 완료→A 응답 도착 순서에서는 A의 정리가 B의 멤버십을 삭제할 수 있다. 같은 인스턴스에서 처리한 N11 및 프로필 쓰기에 한정된 N06과 다른 경계다.
- 개선/완료 기준: 같은 계정·방의 요청/미확정 상태와 정리 소유권을 인스턴스 교체에서도 공유한다. 위 순서와 계정 전환을 검사해 새 가입을 취소하지 않되 실제로 버려진 가입은 정리해야 한다.

### G11 — overflow와 스탯 미리보기 아이콘의 잘못된 참조 공유

- 근거: [Player.prefab의 _overflowEffect](C:/Github/Battle_PVP/Assets/Prefabs/Player.prefab:453)는 stripped 참조를 거쳐 UI_Root의 customizer `_identityIcon`과 같은 Image를 가리킨다. [SetOverflow](C:/Github/Battle_PVP/Assets/Player/Script/UI/PlayerHudView.cs:224)는 정상 체력 snapshot에서 이 아이콘을 끄고, [ResolveIdentityPreviewReferences](C:/Github/Battle_PVP/Assets/Player/Script/UI/StatCustomizerController.cs:524)는 미리보기 갱신 시 다시 켠다.
- 조건/영향: 정상 HP로 바인딩한 뒤 스탯 창을 열면 아이콘이 사라져 있고, 슬라이더를 움직이면 돌아오는 상태가 생길 수 있다. 체력과 프리셋 편집 두 코드가 같은 오브젝트의 표시 여부를 경쟁한다.
- 개선/완료 기준: 초과 체력 전용 표시와 미리보기 참조를 분리하거나 잘못된 연결을 제거한다. 체력/초과 체력 변화와 무관하게 스탯 창 첫 열기의 아이콘이 정상이며, 별도 overflow 표시는 해당 상태만 따라야 한다.

검토 당시 권장 순서는 G01–G04의 통신/공정성/소유권 문제 → G05·G06의 준비/연결 상태 → G07–G09의 입력·부활 UI → G10·G11의 인스턴스 교체와 표시 참조였다. 아래 14절에 후속 수정 상태를 기록한다. 이전 단계의 C# 컴파일 및 CLI 검사 통과를 이 문제들의 해결 증거로 사용하지 않는다.

## 14. G01–G11 수정 반영 — 2026-09-07

사용자의 수정 요청에 따라 11개 항목의 로컬 코드·프리팹을 수정했다. 상태는 모두 **구현 완료·Unity/실제 연결 검증 대기**다. 실제 플랫폼 플레이나 Relay 부하를 실행한 것으로 간주하지 않는다.

| ID | 반영한 처리 | 실행 시 확인할 완료 기준 |
|---|---|---|
| G01 | BeginSend 큐 포화는 순서대로 복사 보관하며 연결별 1 MiB/1,024개/최대 10초로 제한. 부분 fragment 제출 가능성이 있는 reliable EndSend 실패는 연결 종료. 60 KB 분할을 수용하도록 reliable window를 128로 설정 | 포화 복구 후 순서 유지 또는 명확한 연결 실패, 조용한 누락·중복 없음. 큰 패킷·8인 burst와 오류 콜백의 동기 종료 확인 |
| G02 | 접속 중/단절/호스트 모두 서버가 도발 이동·회전·정지 거리·점프 제한과 근접/활 공격 실행을 결정. 기존 강제 이동 epoch/RPC와 충돌 처리 재사용. 원격 위치/활 조작 요청이 도발을 대체하지 못함 | 소유자 도발 구현을 생략해도 이동·공격 유지, 벽과 정지 거리 준수, 활·넉백 중첩·사망·복귀 시 정상 종료 |
| G03 | 서버가 처음에는 0, 공격 중에는 80% 진행 이후 다음 index만 허용. 정상 완료로 얻은 다음 단계에만 0.35초 지연 허용, 취소/새 공격은 그 권한 폐기 | 후속 타격부터 시작 불가, 정상 0→1→2와 지연 연결 유지, 종료 기한/취소 뒤에는 0부터 재시작 |
| G04 | 원격 중복 UI 파괴 제거. 로컬 NetworkIdentity 또는 명시적 오프라인 대상이 설정 창 소유. LobbyUI는 소유자 변경 이벤트로 그 자식 Canvas만 바인딩 | 원격 선행/후행 생성·퇴장과 로컬 교체에서 로컬 창·적용 대상 유지 |
| G05 | 시작 명단의 scene ready와 스탯 승인을 최대 15초까지 기다림. 이탈자는 제외하고 기한 초과는 연결 종료. 피해자는 스탯 승인/scene ready 전 피해 거절 | 느린 로드·스탯 지연·중도 참가에서 무방비 피격 없음. 정상 승인 뒤 경기 참가·피격 유지 |
| G06 | Relay Established를 확인한 뒤 방 등록. 실패/15초 준비 초과는 종료. AllocationInvalid 또는 driver 종료 시 방 이탈, Established 상태에서만 heartbeat | HTTP가 정상이어도 Relay만 실패하면 정상 방 광고·갱신 중단. 초기 Bind/Listen 실패도 방 등록 불가 |
| G07 | 승인된 부활과 살아 있는 전투 UI 갱신에서 customizer Canvas 닫기, 입력 정책 즉시 갱신 | 사망 중 열기→부활 뒤 창/커서 일치. 저장 확인 수명 보존, 거절된 부활은 편집 상태 유지 |
| G08 | 부활 Space는 채팅·일반 입력창 포커스·해당 프레임의 텍스트 종료 입력이 없을 때만 요청 | Windows 채팅 띄어쓰기와 같은 프레임 종료로 부활하지 않음. 채팅 종료 후 Space는 정상 동작 |
| G09 | 중앙 입력 상태가 TMP/legacy 입력창 포커스와 열린 방/스탯 패널을 반영. 창 조작 중 선택을 강제 해제하지 않음 | 방 이름 WASD/Space는 문자 입력만, 슬라이더 중 이동/카메라 조작 없음, 닫은 뒤 정상 복구. Windows/WebGL 각각 확인 |
| G10 | 세대·멤버십 큐·미확정 응답 gate·현재 정리 소유권을 RoomServiceLifetime으로 매니저 교체 시에도 공유 | A 요청 대기→A 종료→B 동일 방 진입→A 늦은 응답이 B 가입을 지우지 않음. 계정 전환·실제 방 포기는 정리 |
| G11 | Player.prefab의 잘못된 overflow→customizer icon 연결 해제. 미리보기 아이콘 유지 | 정상/초과 HP 변경 후 스탯 창 첫 열기에서 아이콘 유지. 기존 체력 표시 유지 |

로컬 검증: 순수 CLI **59,709 assertions PASS**(이번 변경으로 2,657개 추가). 전체 C# 오류 0/기존 경고 4개, 설치 Mirror ILPostProcessorHook 검사 통과. 새 Unity NUnit 12사례는 작성·컴파일했으며 실행하지 않았다. 최종 명령·세부 정책은 [CODE_IMPROVEMENTS.md](C:/Github/Battle_PVP/CODE_IMPROVEMENTS.md)의 `G01–G11 플레이 흐름 문제 수정` 절에 기록한다.
