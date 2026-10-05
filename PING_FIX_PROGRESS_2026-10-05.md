# 방 접속 정체 보완 및 실제 Relay 지연 검증

## 배포용 Windows 재빌드 — 2026-10-05

- 사용자 빌드 요청에 따라 현재 소스를 전체 Refresh/Compile 후 Windows64 Mono 일반 빌드로 다시 생성했다. 게임 코드 및 네트워크 정책은 추가 변경하지 않았다.
- 작업 `build-d95588dc51`: 성공, 5.19초(증분), 241.37MB, 오류 0 / 경고 0. Login·Lobby·Battle_waiting·Battle 4개 씬, Development 해제, 진단 전용 빌드 심볼 없음.
- 실행 파일: `Builds/Windows-2026-10-05-Release/Battle_PvP.exe`.
- 전달용 ZIP: `Builds/Battle_PvP-Windows-2026-10-05.zip` (110,900,468 bytes). 실행 안내 및 선택적 진단 실행 파일을 포함하며, 232개 파일의 경로·크기 일치와 압축 스트림 읽기를 확인했다. 압축을 모두 푼 뒤 EXE를 실행한다.
- 빌드·압축 검증 기록과 SHA256: `Reports/Builds/windows-release-2026-10-05.json`.
- CloudScript는 사용자 직접 배포 방침을 유지하며 Live 반영 여부는 확인하지 않았다. 이번 재빌드는 외부 회선에서의 실제 친구 참가 검증을 포함하지 않는다.

## 최신 변경 — 직접 UDP 우선, 실패 시 Relay

사용자 추가 지시에 따라 같은 PC/LAN으로 직접 연결 적용 범위를 제한하지 않는다. 아래 이전 단계 기록보다 이 절의 연결 정책이 우선한다.

- Windows 참가자는 유효한 직접 UDP 후보를 먼저 사용한다. LAN 탐색은 빠른 후보를 찾는 보조 경로이며, LAN 후보가 없어도 방의 공인 IPv4 UDP 주소로 직접 접속한다. 직접 후보가 있으면 Relay JoinAllocation API를 먼저 호출하지 않는다.
- 각 직접 후보는 1.5초 안에 UTP 연결이 성립해야 한다. 로컬 후보 실패 시 공인 후보가 있으면 시도하고, 모두 실패하면 그때 Relay 참가 데이터를 준비한다. 동일 Mirror 참가 시도 안에서 전환하며 방 멤버십을 정리하거나 임의로 인증을 생략하지 않는다. 전체 Transport 연결 기한 20초는 유지한다.
- 호스트는 직접 UDP 수신과 기존 Relay 수신을 함께 열어 서로 다른 경로의 참가자를 받을 수 있다. 호스트 방 생성은 백업 Relay 할당 및 기존 방 등록 서비스를 계속 사용하므로, Relay 서비스 자체 장애까지 제거한 오프라인 방 기능은 아니다.
- 공인 주소는 실제 게임 UDP 소켓에서 STUN Binding으로 확인한다. 별도 소켓의 포트를 게임 포트라고 추정하지 않는다. 현재 서버는 `stun.l.google.com:19302`, DNS 대기 1.5초, 주소 수집 최대 2.5초, 최초 요청 3회, 확인된 매핑 유지 요청 15초 간격이다. 응답 출처·트랜잭션·길이를 검사하고 STUN 메시지는 Mirror에 넘기지 않는다. 게임 데이터/계정/로그를 STUN 서버에 보내지 않는다.
- STUN은 주소 확인 수단이다. **NAT 홀펀칭 신호 교환, UPnP/공유기 포트 설정 자동화는 이번 구현에 포함하지 않았다.** 외부 수신을 차단하는 NAT에서는 직접 시도가 실패하고 Relay로 연결된다. IPv6 직접 연결과 WebGL raw UDP는 적용하지 않았다. WebGL은 기존 Relay WSS 경로를 유지하며 빌드·실행하지 않았다.
- `combinedCloudScript.js`와 `roomRegistry.js`의 소유자 권한 방 등록에 선택 항목 `directEndpoint`를 추가했다. 공인 IPv4와 1024~65535 포트만 허용하며 기존 참가 인증·비밀번호·강퇴·정원 검사와 Relay 코드 형식은 유지한다. 구버전 요청은 해당 항목 없이 정상 처리된다. **실제 배포는 사용자가 직접 진행하기로 했으며 아직 배포 완료를 확인하지 않았다.** 배포 전에는 인터넷 참가자에게 공인 후보가 전달되지 않는다.
- HUD에 실제 연결 경로 `Direct UDP` / `Relay <지역>` / 혼합을 표시한다. RTT 계산식, Ping 주기 및 통계 평활화는 변경하지 않았다.

### 게임 루프와 성능

- 수신 처리 직후 큐에 만들어진 모든 채널의 응답을 먼저 flush한다. Ping만 특별 처리하지 않는다. 프레임 끝의 기존 flush도 유지하고 이미 전송한 메시지를 중복 전송하지 않는다. 이를 위해 Mirror `NetworkConnection`의 기존 배치 배출 부분만 `FlushBatches()`로 노출했다.
- 네이티브 방에서는 Update/입력/수신 기회를 목표 120Hz로 늘리고 `OnDemandRendering`으로 렌더링 목표는 설정된 60 또는 30FPS를 유지한다. 방 종료 시 기존 프레임 설정으로 복귀한다. Mirror 상태 전송 설정 및 고정 물리 주기(프로젝트 기본 0.02초)는 변경하지 않았다. 실제 8인 전투에서 CPU 여유와 60FPS 유지 여부는 별도 검증 대상이다.
- 빈 UDP 소켓을 매 프레임 읽으며 WouldBlock 예외를 생성하던 경로를 발견해 nonblocking Poll로 교체했다. 초기 공인 주소 확장 버전 `direct-first-final` 호스트의 GC0 18회 기록은 이 수정 전 값이다. 직접 소켓 송신 실패도 별도로 집계한다.

### 검증 기록

- Unity EditMode **78/78 통과**, 실패/건너뛰기 0. 작업 `197e32e22314460fa291db15af80b91d`, 약 4.76초. 실제 로컬 소켓으로 STUN이 게임 포트를 사용하는지, STUN이 게임 참가자로 오인되지 않는지, 두 수신 Driver의 50KB 신뢰성 메시지·연결 ID·퇴장 분리, 채널 순서·중복 flush 방지, 직접 후보의 Relay API 선호출 금지, 렌더 예산/물리 주기 보존, 기존 연결/인증 회귀를 검사했다.
- Node: RoomDirectEndpoint, RoomFailureDiagnostics, NetworkProfileRoomCapacity, RoomAdmission, RoomLease(564 assertions/26 scenarios), SharedGroupCreation(18 scenarios)이 두 배포 파일에 통과했다. 이 검사는 Live revision 배포 확인을 대신하지 않는다.
- `auto-direct-early120`: Windows 두 프로세스 60초, 초기 10초 제외. 호스트와 참가자 원시 RTT p50/p95 약 8.33ms, 최대 각각 8.56/8.82ms. 이전 기본 Relay 비교는 참가자 p50 83.33ms / p95 150ms였다. 서로 다른 시간대/할당이며 전투 부하는 없다.
- `direct-first-final`: 인터넷 후보 전달 및 Relay 지연 준비 코드가 포함된 Windows 재측정 60초. 직접 연결 성공, 실제 게임 소켓의 공인 후보 조회 성공, 클라이언트 Relay 참가 없이 연결. 호스트/참가자 p50/p95 약 8.33ms, 최대 8.70/8.65ms. 이 측정은 빈 소켓 Poll 수정 이전이다.
- `public-udp-first`: LAN 탐색을 끄고 공인 후보로 먼저 시도했다. 이 환경의 공인 주소 경유 직접 연결은 실패했고 **그 후 Relay 참가·연결·왕복·정상 종료 성공**. Tokyo Relay 참가자 p50 83.33ms / p95 141.68ms. Relay 경로의 10ms 목표는 여전히 미달이다. 같은 PC에서 공인 주소를 경유한 검사이며 서로 다른 인터넷 회선 간 직접 연결 성공을 증명하지 않는다.
- 진단 전용 씬은 일반 게임의 PlayFab 로그인·방 인증을 대체한 테스트 fixture다. 실제 8인 전투/계정 인증을 완료했다고 해석하지 않는다. 일반 Windows 빌드에서는 테스트 fixture가 제외되는지 별도로 확인한다.

실제 서버 배포, 새 일반 Windows 빌드와 에디터 간 참가 인증, 다른 회선의 직접 연결 성공률 및 8인 전투 성능은 남은 검증이다. 전 지역/전 회선의 10ms를 보장하거나 Relay 경로가 해결됐다고 보고하지 않는다.

### 최종 재측정 및 판정

- 후속 일반 실행 `direct-render-windowed`에서 양쪽 Mirror RTT p50/p95가 16.67ms로 나타났다. 프레임 위상/실행 방식에 따른 차이가 있으므로 첫 배치 실행의 최대 9ms 미만 기록만으로 안정적 10ms 달성을 주장하지 않는다.
- 최종 보완은 프레임 시작 수신 후 조기 flush에 더해, 게임 Update 중 도착한 패킷도 최종 전송 전에 한 번 더 수신·flush한다. 모든 게임 메시지에 적용하며 Ping만 우선하지 않는다. UTP 핸들러와 Mirror 인증/순서는 그대로 사용한다.
- `direct-wall-clock`: 일반 네이티브 실행 두 개, 45초 중 처음 10초 제외. 추가 Echo가 생성된 시점부터 같은 메시지가 돌아온 시점까지 `Time.realtimeSinceStartupAsDouble`로 측정했다. 참가자 **실제 경과 RTT p50 8.336ms / p95 16.645ms / 최대 16.858ms**. Mirror RTT는 호스트 p50/p95 8.33/8.33ms, 참가자 8.33/16.67ms다. 프레임 시계의 양자화와 실제 경과 시간을 구분한다. 최종 코드의 로컬 지연은 크게 줄었지만 p95 10ms 이하는 미달이다.
- 최종 직접 소켓 송신 실패·UTP 큐 포화·송신 오류·백로그는 0. 최종 유효 구간 GC0 증가는 호스트 2회/참가자 0회였다. 전투/VFX 부하가 없는 시험이며 실제 회선 손실 0을 의미하지 않는다.
- 숨김 진단 실행에서 렌더 완료 콜백 계수는 0이었다. 따라서 `renderFrameInterval=2`, 루프 목표 120Hz 설정만 확인됐고, **실제 화면 60FPS 유지의 실측 성공으로 보고하지 않는다.** 파일명 `direct-render60`은 시험 의도이며 성공 판정을 뜻하지 않는다. 실제 게임/8인 화면 성능은 남은 검증이다.
- 최종 Unity 회귀 작업 `cd4aca295a6944f5a3d7a3c6539a5c28`: **78/78 통과**, 약 4.76초, 실패/건너뛰기 0. 이후 게임 소스 수정 없이 Windows 빌드를 진행했다.

### 최종 Windows 빌드 및 전환 검사

- 실행 파일: `Builds/Windows-2026-10-05-DirectUDP/Battle_PvP.exe`. 선택적 진단 실행은 같은 폴더의 `Start-Network-Diagnostics.cmd`다.
- 작업 `build-0335c9eaf0`: **성공**, 26.27초, 241.37MB, 오류 0. Unity 6000.3.15f1 / Windows64 Mono / Development 해제 / Login·Lobby·Battle_waiting·Battle 4개 씬. 경고 3개는 기존 `FollowCamera._rotSmoothSpeed`, `FollowCamera._moveSmoothTime`, `DamagePopup._moveYSpeed` 미사용 경고다.
- 실제 배포 DLL에서 직접 주소·STUN 소켓·네트워크 타이밍·방 인증 코드와 Mirror 조기 flush 포함, 테스트용 `RelayNetworkProbe` 제외를 확인했다. 확인 결과는 `Reports/PingDiagnosis/windows-direct-udp-build.json`에 저장했다.
- `fallback-final`: 직접 후보를 실패하도록 구성한 최종 네이티브 검사에서 양쪽 모두 Tokyo Relay로 전환한 뒤 연결·왕복 통신·종료에 성공했다. 직접 시도 실패 이후 Relay 준비가 시작됐으며 준비부터 연결까지 약 7.35초였다. 일반 빌드와 동시에 실행한 기능 검사이므로 이 실행의 RTT를 성능 기준으로 사용하지 않는다.
- CloudScript는 사용자가 `Assets/PlayFabCloudScript/combinedCloudScript.js`를 최신 Live revision으로 직접 배포한다. 배포 후 업데이트된 에디터/호스트에서 **새 방을 생성**하고 이번 Windows 빌드로 참가해야 공인 직접 주소 전달을 확인할 수 있다. 실제 Live 배포와 일반 게임의 반복 참가·다른 회선 직접 연결·8인 60FPS 검증은 아직 완료하지 않았다.
- 기존 Windows 빌드는 보존했으며 웹 빌드/웹 테스트는 하지 않았다. 에디터는 Login 씬의 재생 종료 상태로 정리했다.

근거: [Unity Transport의 사용자 정의 인터페이스](https://docs.unity.cn/Packages/com.unity.transport%402.0/api/Unity.Networking.Transport.ManagedNetworkInterfaceExtensions.html), [STUN RFC 8489](https://www.rfc-editor.org/rfc/rfc8489.html), [Unity OnDemandRendering](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rendering/ondemandrendering/renderframeinterval). 구현 시 실제 설치된 UTP 소스와 Windows 런타임 검사도 함께 확인했다.

## 이전 단계 결론

Windows의 UDP 연결을 유지했다. 방 연결이 끝나지 않을 때 복구하는 코드와 진단 기능을 반영하고 Windows 빌드를 생성했다. 이후 실제 Windows 참가자의 재참가로 PlayFab 인증·Ready·플레이어 생성 성공을 확인했다. **간헐적 인증 실패의 원인 확정과 핑 급등 전체의 해결 완료를 뜻하지는 않는다.** 사용자 목표는 약 10ms이며 현재 일반 Relay 게임은 미달이다.

실제 외부 Tokyo Relay에 Windows 진단 프로세스 두 개를 연결했다. 캐릭터·스킬·VFX가 없는 상태에서도 약 83~92ms 중앙값과 125~150ms p95가 나왔다. 같은 UTP 전송 코드에서 Relay 경로를 제거한 로컬 직접 연결은 약 16.7ms였다. 120FPS에서도 Relay의 높은 RTT가 유지됐다. 이번 표본의 지연은 전투 효과나 로컬 송신 큐 포화만으로 설명되지 않는다.

이 실험은 인터넷 경로와 Relay 처리의 합을 구분한 것이며, ISP·공유기·Relay 노드 중 어느 구간에서 지연이 생기는지까지 측정한 것은 아니다. 과거 약 10ms였던 실행 조건과의 직접 비교 자료도 없다. 게임 FPS를 120으로 바꾸거나 핑 표시값을 낮추는 변경은 하지 않았다.

## 반영한 수정

- `UnityRelayTransport`: ClientConnect 이후 20초 안에 연결되지 않으면 Timeout과 연결 종료를 한 번 통지한다. 네트워크 EarlyUpdate 외에도 Unity Update에서 기한을 확인한다. 연결 성공/취소 시 기한을 해제한다.
- Driver 또는 연결 객체 생성 실패를 다음 프레임에 실패로 통보한다. 유효하지 않은 객체 상태에서 계속 반환하며 대기하는 경로를 제거했다.
- 오류/종료 콜백 안에서 새 접속이 시작돼도 이전 시도의 정리가 새 시도를 종료하지 않도록 기존 시도 번호 보호를 유지했다.
- `BattleNetworkManager` / `PlayFabBattleManager`: 연결 시간 초과와 연결 초기화 실패를 인증 실패와 구분한 안내로 보존한다. 기존 종료·참가 정리 경로가 버튼을 복구하며 다음 참가에서 안내를 초기화한다. 인증 검증은 유지했다.
- `[RelayConnection]` 로그에 참가 데이터 준비, Driver 생성, Relay 상태 변경, 실제 연결, 인증 시작/성공, 종료 이유 코드를 기록한다. 참가 코드·계정·인증 토큰·SDK 응답 본문은 기록하지 않는다.
- 진단 카운터: 전송/수신 바이트, 송신 큐 포화, Unreliable 송신 실패, 송신 오류, Reliable 백로그 크기/나이, Poll/Flush 소요 시간을 수집할 수 있다.
- `RelayLatencyCapture`: 명시적으로 켜는 120초 수집기. 원시 Pong RTT와 평활화 RTT, 프레임 간격, GC, 포커스, 원격 참가자 수와 전송 지표를 CSV/JSON으로 저장한다. 고정 크기 버퍼를 사용하고 기록 종료 시에만 파일을 쓴다. 일반 실행에서는 수집기를 생성하지 않는다.

이 변경은 연결 실패를 숨기지 않고 복구할 수 있게 한다. 이전 실패에서 유효하지 않은 연결 객체가 실제 원인이었는지는 Windows 내부 상태 기록이 없었으므로 확정하지 않는다. 정상 방 연결 성공을 확인하기 전에는 접속 문제 해결 완료로 처리하지 않는다.

## 테스트와 실측

- Unity EditMode **35/35 통과**: RelayPollingTests, RelayReliableFailureTests, RoomServiceTimeoutTests, RoomObserverIsolationTests. 신규 5개 검사는 무효 연결 정리, 폴링 없이 기한 만료, 콜백 내 재시도 보호, 성공 연결의 이전 기한 무시, 연결 실패 종류별 UI/재참가 복구를 다룬다. 파라미터 테스트를 포함한 신규 실행 건수는 5건이다.
- 작업 ID `4c794f42df994e15b7400bff048f3ec5`, 실패/건너뛰기 0. 이후 변경은 진단용 프로그램과 watchdog 메서드의 명시적 `new` 지정이며, 최종 Windows 컴파일에서도 오류가 없었다.
- 실제 네이티브 진단은 정상 네트워크 접근 권한으로 실행했다. 샌드박스 내부의 첫 서비스 초기화 실패(`native-run1`)와 검증기 listen 설정을 수정하기 전 직접 연결 실패(`loopback-60`)는 아래 성능 표에서 제외한다.
- 외부 Relay 60FPS, UTP 직접 연결 60FPS, 외부 Relay 120FPS에서 각각 연결·왕복 통신·정상 종료를 확인했다. 이 검증기는 인증을 대체하는 테스트 전용 씬을 사용하므로 **PlayFab 방 등록/참가 인증의 종단 검증은 아니다.** 최종 일반 Windows DLL에 이 검증기 타입이 포함되지 않은 것을 확인했다.

각 조건은 약 60초 기록에서 처음 10초를 제외한 단일 실행이다. 모든 조건의 Mirror send rate는 60Hz다. 아래는 참가자 수치이며 호스트 수치와 원본은 각 폴더에 있다.

| 조건 | 원시 RTT p50 | p95 | p99 | 최대 | 프레임 간격 p95 |
|---|---:|---:|---:|---:|---:|
| Tokyo Relay / 60FPS | 83.33ms | 150.00ms | 166.69ms | 200.00ms | 16.72ms |
| UTP 로컬 직접 연결 / 60FPS | 16.67ms | 16.67ms | 16.94ms | 17.04ms | 16.80ms |
| Tokyo Relay / 120FPS | 91.67ms | 125.13ms | 158.48ms | 183.38ms | 8.47ms |

- 모든 유효 구간에서 로컬 송신 큐 포화·Unreliable 송신 실패·송신 오류·Reliable 백로그는 0이었다. 이는 인터넷에서 패킷 손실이 없다는 뜻이 아니다.
- GC0 증가 횟수는 60FPS Relay 참가자에서 1회, 나머지 유효 조건에서 0회였다. 프레임 급등 없이도 Relay RTT 분포가 높았다.
- 서로 다른 할당/시간대의 단일 실행 비교이므로 120FPS 변경의 효과를 정확한 개선 비율로 해석하지 않는다. 관측된 RTT 전체를 프레임 속도 문제로 설명할 수 없다는 근거로 사용한다.
- CSV의 원시 RTT도 Mirror가 프레임 시계로 측정한 게임 왕복 시간이다. 소켓/회선만의 RTT는 아니다. 호스트 원시 Pong은 원격 연결들을 합산한다. 현 검증에서는 참가자 1명이다.
- 진단 이벤트 구독에는 메시지 boxing 비용이 있을 수 있다. 이 때문에 일반 배포 실행에서 자동 활성화하지 않는다.

자료:

- `Reports/PingDiagnosis/native-run2/analysis.json`: 실제 Relay 60FPS.
- `Reports/PingDiagnosis/loopback-60-fixed/analysis.json`: 동일 UTP 코드의 직접 연결 대조군.
- `Reports/PingDiagnosis/relay-120/analysis.json`: Relay 120FPS 비교.
- `Tools/Analyze-RelayCapture.ps1`: 원본 CSV 재분석 도구. 예: `./Tools/Analyze-RelayCapture.ps1 -Directory Reports/PingDiagnosis/native-run2`.

## Windows 수정 빌드

- `Builds/Windows-2026-10-05-NetworkFix/Battle_PvP.exe`
- Unity 6000.3.15f1, Windows64 Mono, Development 해제, Login/Lobby/Battle_waiting/Battle 4개 씬.
- 작업 ID `build-43e80719d0`: 성공, 약 28.43초, 241.35MB, 오류 0. 경고 3개는 기존 FollowCamera 두 필드와 DamagePopup 한 필드의 미사용 경고다.
- 실제 빌드 DLL 확인: 연결 기한 처리 포함, 선택적 수집기 포함, 테스트 전용 RelayNetworkProbe 미포함.
- 기존 Windows 실행 파일은 덮어쓰지 않았다. 웹 빌드/웹 테스트는 하지 않았다.

진단 실행은 같은 폴더의 `Start-Network-Diagnostics.cmd`를 사용한다. 실행 파일에 `-battleNetworkCapture`를 전달하며 첫 게임 네트워크 연결 시도부터 기록한다. Windows 결과는 게임의 `Application.persistentDataPath/NetworkDiagnostics`에 생성된다. 진단 없이 실행하려면 EXE를 직접 실행한다.

에디터에서는 Play 중 `Tools > Battle PVP > Capture Relay Latency (120 seconds)`를 사용한다. 이번 검증용 에디터 수집기는 `Reports/PingDiagnosis/live`에 기록하도록 준비했다. 일반 실행에서는 아무 단축키나 HUD를 추가하지 않았다.

## 남은 확인

1. 일반 Windows 빌드로 에디터의 `네트워크 점검` 방에 참가하여 PlayFab 인증과 실제 플레이어 생성까지 확인했다. 간헐적 실패 보완 코드의 실제 반복 입장 검증은 별도로 남는다.
2. 실패하면 새 `[RelayConnection]` 기록으로 Relay 바인딩/연결 성립/인증 중 어느 단계인지 확인한다. 20초 제한은 실패 복구이며 서버에 접속하지 못하는 원인 자체의 해결을 대신하지 않는다.
3. 실제 게임에서 안정된 프레임 상태에도 Relay RTT가 높으면, 같은 시간대 QoS와 여러 Relay 할당의 경로를 비교한다. 별도 PC/네트워크 대조와 반복 측정으로 PC/공유기/외부 경로를 더 구분해야 한다.
4. 전투 중에만 추가 지연이 관측될 때 해당 구간의 VFX/물리/GC/전송량을 프로파일링하고 수정한다. 이번 최소 씬 결과만으로 관련 게임 코드를 임의로 제거하지 않는다.
5. 원인에 맞는 수정 이후 호스트 포함 8인 60FPS/RTT 반복 검증을 진행한다. 이번 2프로세스 최소 씬 검증은 이를 대체하지 않는다.

## 후속: 참가자 인증 실패와 재참가 성공

- 실패한 Windows 시도에서 Relay UDP 연결 성립 뒤 `room_authentication_started`까지 도달했다. 호스트 기록은 `authentication_verify_script_failed`였고 참가자는 거절됐다. 접속 실패와 참가자 검증 실패는 다른 단계다.
- 실제 배포 revision 15의 읽기 전용 검증을 확인했다. 실패 응답은 `Error.Error=JavascriptException`, `Error.Message=JavascriptException`, 빈 StackTrace/로그여서 세부 사유를 복원할 수 없었다. 다른 유효한 호스트 자기 계정 승인→검증은 성공했다.
- 사용자가 같은 Windows 빌드에서 재참가한 결과 revision 15의 Verify가 성공했다. 호스트와 참가자는 서로 다른 계정이며, 참가자는 room 멤버이고 Mirror authenticated/ready/player identity가 모두 확인됐다. 이 성공은 새 인증 보완 코드를 적용하기 전이다. 따라서 보완 코드 때문에 성공했다고 보고하지 않는다.
- 예외 발생의 근본 원인은 미확정이다. 증명 저장 직후 읽기 지연 가능성은 있지만 증거 없이 확정하지 않는다. 동일 배포에서 재시도 성공했으므로 인증 함수 미배포나 UDP 불통으로 설명하지 않는다.

### 수정

- 기존 불투명 `JavascriptException`을 `script_exception`으로 구분한다. **읽기 전용 Verify만** 최초 포함 최대 3회, 0.35/0.70초 간격으로 재검증한다. 기존 30초 인증 기한, 동일 연결/challenge/방/서버 세대 검사, 정상 응답 필드 일치 검사는 유지한다. Join/Approve 쓰기를 불명확한 결과로 자동 반복하지 않는다.
- 확인된 비밀번호 오류·강퇴·멤버십 누락·잘못된 증명·닫힌 방 등은 재시도하지 않는다. 성공 응답 필드 불일치도 즉시 거절한다. 구버전의 불투명 영구 오류는 최대 두 번 더 읽고 거절하며 무한 대기나 검증 생략은 없다.
- 호스트 진단에는 고정 오류 코드, revision, API 요청 수, 시도 번호만 남긴다. 계정/방/challenge/토큰/SDK 본문을 로그에 복사하지 않는다.
- `roomRegistry.js`, `combinedCloudScript.js`의 Join/Approve/Verify에 고정 `ROOM_FAILURE:<code>` 진단을 추가했다. 기존 성공 응답 및 실패 throw 계약은 유지한다. 이를 배포하면 불투명 예외와 함께 전달된 안전한 코드로 사유를 구분할 수 있다. **이 CloudScript 수정은 아직 서버에 배포하지 않았다.** 클라이언트는 코드 없는 revision 15와도 호환된다.

### 검증

- Unity EditMode 최종 **56/56 통과**, 실패/건너뛰기 0, 3.32초. 작업 `99045287a8b547a3a85b0ec97a737922`. WindowsPlayerRegressionTests 포함 5개 테스트 클래스. 고정 코드 분류·영구 오류 재시도 차단·불투명 예외 횟수 제한·임의 서버 정보 로그 배제 추가 검사 포함.
- Node: RoomFailureDiagnostics, NetworkProfileRoomCapacity, RoomAdmission, RoomLease, SharedGroupCreation 모두 두 배포용 파일에 통과. 정상 인증·잘못된 challenge·멤버십 누락·서비스 예외와 기존 throw 계약 확인. 실제 서버 배포 검사와 구분한다.
- 실제 대기실 호스트 120초 참고 수집: `Reports/PingDiagnosis/live-authenticated`. 원시 RTT p50 100.90ms / p95 150.31ms / p99 195.86ms. 로컬 큐 포화/드롭/백로그 0. **수집 중 MCP execute_code로 상태를 확인했으므로 에디터 내 코드 컴파일/실행 비용이 섞였다.** 최대 RTT 1212ms와 최대 프레임 1146ms를 게임 자체의 결함으로 확정하지 않는다. 참가자 동시 수집도 없어 이 기록만으로 지터 원인이나 8인 성능을 판정하지 않는다.

### 10ms 목표를 위한 추가 대조

- 직접 UDP, 120FPS / 서버 전송 60Hz: 호스트 p50/p95 8.33ms, 참가자 p50/p95 16.67ms. 양쪽 모두 약 10ms를 만족하지 않았다. `Reports/PingDiagnosis/loopback-120/analysis.json`.
- 후속 직접 UDP, 120FPS / 서버 전송 120Hz도 양쪽 p50/p95 16.67ms였다. 호스트/참가자 프레임 p95는 각각 8.40/8.40ms, 큐/드롭/백로그 0이다. `Reports/PingDiagnosis/loopback-120-tick120/analysis.json`. 이전 60Hz 전송 실험과는 다른 실행이므로 주기 상향 때문에 호스트 RTT가 악화했다고 단정하지 않는다. 프로세스 프레임 위상도 바뀐 조건이다.
- 다음 단계는 직접 UDP 경로에 더해 양쪽 수신→배치→flush의 처리 대기를 추적하고 전체 게임 메시지의 지연을 줄이는 것이다. 단순히 120Hz로 올리면 10ms가 된다는 가정은 실측에서 성립하지 않았다. 일반 게임의 FPS/전송 주기/접속 경로는 아직 바꾸지 않았다. [수정 목표와 순서](PING_DIAGNOSIS_PLAN_2026-10-05.md)의 10ms 기준을 따른다.

### 후속 Windows 빌드

- 실행 파일: `Builds/Windows-2026-10-05-AuthFix/Battle_PvP.exe`. 진단 실행은 같은 폴더의 `Start-Network-Diagnostics.cmd`.
- 첫 빌드 `build-66e0a8e193` 성공 후 미컴파일 에디터 변경 경고를 확인하여 전체 Refresh/Compile 뒤 다시 빌드했다. 최종 `build-59024232c5`: 성공, 4.03초(증분), 241.35MB, 오류/경고 0. 이전 Windows 빌드는 보존했다.
- 빌드 DLL에서 불투명 예외 재시도와 안전한 진단 메서드 포함, 테스트 전용 RelayNetworkProbe 미포함을 확인했다. 정상 게임 4개 씬, Windows64 Mono, Development 해제. 웹 빌드/테스트 없음.
- 에디터는 수집 후 재생 종료 상태다. 새 코드 적용 후 실제 반복 참가와 CloudScript 진단 배포 검증은 남아 있다. 위 사용자 재참가 성공은 이전 NetworkFix 실행 파일에서 관측한 결과다.
