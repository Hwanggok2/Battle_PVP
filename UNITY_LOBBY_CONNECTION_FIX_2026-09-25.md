# 시작 화면 연출·로비 줌·입장 후 자동 복귀 수정

## 완료 기준

1. 시작/로그인 화면에 실제 전장 에셋을 배치하고 카메라가 끊김 없이 천천히 순회한다. 제목·Touch to start·로그인 UI는 화면에 고정한다.
2. 현재 Input System의 실제 휠 단위(한 칸 = 1)로 로비 확대/축소를 검증한다. UI 위 스크롤과 메뉴/채팅 입력은 카메라 줌에 사용하지 않는다.
3. 자동 복귀의 연결 종료 경로를 확인하고 원인을 수정한다. 입장 초기화와 종료 제한을 실제 Mirror 수명주기로 검증한다. 온라인 재현 여부와 로컬 검사 범위를 구분한다.
4. Unity 에디터 검사만 수행하고 웹 빌드/웹 테스트는 하지 않는다.

## 진행

### 적용 완료

- Login 배경을 실제 `arena` 프리팹으로 바꾸고 `TitleBattleFlythrough`를 추가했다. 75초 주기로 상공을 순회하고 고도를 천천히 바꾼다. `Time.unscaledDeltaTime`을 사용해 로그인 UI 상태와 무관하게 움직이며 제목/로그인 Canvas는 고정한다. 재생성 도구도 같은 배경과 카메라를 적용한다.
- 로비 휠 계산의 `/120`을 제거했다. 설치된 Input System의 `UniformAcrossAllPlatforms` 설정은 실제 휠 한 칸을 1로 전달한다. 이전 검사는 가상 입력에 120을 넣어 이 문제를 놓쳤다. 이제 실제 설정과 같은 ±1로 검사한다.
- 로비 복귀 과정에서 오류 안내를 지우는 문제를 수정했다. 임대 만료 외의 Relay/방 등록 실패 안내도 다음 Lobby 씬에서 유지한다. 새 입장을 시작하면 이전 안내를 지운다.

### 자동 복귀: 원인 미확정

- 정상 스탯으로 Mirror 로컬 연결을 만들고 실제 `OnStartLocalPlayer → CmdUpdateStats → 서버 스탯 수락` 경로를 실행했다. 서버 스탯 초기화가 완료됐고 입장 후 약 96초 시점에도 대기실과 연결이 유지됐다. 따라서 이번 제보를 10초 스탯 타임아웃 문제로 단정할 근거는 없다.
- PlayFab 인증/Relay 실접속을 포함한 자동 복귀는 재현하지 못했다. 실제 호스트/참가자 여부와 재현 시점 확인이 남아 있다. 인증·스탯 검증과 방 임대 만료 보호를 우회하거나 제거하지 않았다.
- 기존 기본 NetworkManager가 무시하던 전송 오류를 콘솔에 남긴다. 스탯 초기화/수락/거절/제한 초과, 인증 거절, Relay 준비·전송 실패, 방 등록·갱신 실패, 임대 만료, 연결 종료 경로를 구분해 기록한다.
- Unity 에디터에서 다음 실제 방 종료가 발생하면 `Reports/NetworkDiagnostics/latest-room-exit.json`을 자동 저장한다. 호스트 여부, 입장 후 경과 시간, 최대 16개 이벤트만 기록한다. 계정/방 ID, 세션 티켓, Relay 참가 코드는 저장하지 않는다. 종료 후 에디터 재생을 멈추거나 콘솔을 지워도 파일은 남는다. 동일 세션의 후속 정리 콜백은 최초 기록을 덮어쓰지 않는다.
- 이 항목은 진단과 안내 개선까지 적용됐으며, **자동 복귀 버그 수정 완료로 간주하지 않는다.**

### 검증

- 전체 EditMode **532/532 통과**, 실패·건너뜀 0. 작업 `dfe8df85b8fe4f28b403069ca8da0e38`, 18.23초. 실제 휠 단위 ±1 회귀 검사 2개와 복귀 안내 보존 검사 1개를 추가했다.
- 종료 경로 진단 기록을 보강한 뒤 관련 입력/방 상태/Relay 실패 검사 **44/44 재통과**, 2.29초. 작업 `aec91fc2b1c64d6eb7e392f42718f9ca`; `Reports/LobbyConnectionFix/editmode-tests.json`. 마지막 컴파일 오류 0개, 최종 Login 편집 모드·미저장 씬 변경 없음.
- 시작 화면 실제 실행에서 카메라 위치가 `(14.46, 9.04, -14.77)`에서 `(22.32, 11.93, 4.57)`로 이동했다. 제목·Touch to start를 유지하고 로그인 패널도 정상적으로 열린다. 루프 양 끝 위치 오차 약 0.0000013, 회전 오차 0도.
- 로비 실제 씬에서 휠 1 입력으로 카메라 거리가 6.3095→5.5595로 0.75만큼 변했다. 화면 중앙 UI Raycast 결과 0개. 설정 패널을 열면 같은 휠 입력에도 거리가 유지됐다. `Reports/LobbyConnectionFix/lobby-wheel.json`.
- 캡처: `Reports/LobbyConnectionFix/title-flythrough-a.png`, `title-flythrough-b.png`.
- Windows/WebGL 빌드와 웹 실행은 하지 않았다.

## 후속 확인: 호스트 방 등록 실패

> 이후 revision 13의 실제 실패 응답으로 기존 그룹 재생성 시 오류 1088 미처리를 확인하고 수정했다. 최신 원인·수정·배포 상태는 [PLAYFAB_ROOM_REGISTRATION_FIX_2026-09-25.md](PLAYFAB_ROOM_REGISTRATION_FIX_2026-09-25.md)를 참고한다. 아래는 그 전에 확인한 기록이다.

- 실제 재현 기록 `Reports/NetworkDiagnostics/latest-room-exit.json`에서 호스트 입장 후 1.84초에 Mirror 연결, 1.95초에 서버 스탯 수락을 확인했다. 이후 2.72/3.72/5.47초에 방 등록이 세 번 실패했고 5.47초에 `room_flow_failed`로 종료됐다. 이번 복귀 경로는 스탯 타임아웃이 아니라 `RegisterRoomToRegistry` 실패다.
- 현재 클라이언트는 등록 응답의 `roomId`, `serverNow`, `leaseExpiresAt`을 확인한다. 이전 CloudScript가 적용돼 이 값이 없거나 등록 함수 자체가 실패하면 재시도 후 로비로 돌아간다. 사용자가 PlayFab 스크립트를 갱신하지 않았을 가능성을 제시했으며, 배포 불일치가 우선 확인 대상이다. 실제 Live revision 및 상세 실패 응답을 조회하지 못했으므로 원인 확정이나 수정 완료로 기록하지 않는다.
- 배포 파일은 `Assets/PlayFabCloudScript/combinedCloudScript.js`다. 같은 PlayFab 타이틀의 Classic CloudScript에 새 revision으로 업로드하고 **Live로 배포**해야 한다. 현재 요청은 revision을 별도로 지정하지 않아 Live revision을 사용한다. Unity 프로젝트 파일을 수정하거나 새 revision만 저장하는 것으로는 실행 서버에 적용되지 않는다.
- 배포 후 기존 실패한 방 대신 새 방을 생성하고 최소 90초간 대기실 유지, 참가자 입장, 정상 퇴장을 확인한다. `HeartbeatRoom`도 새 revision에 포함되어야 한다.
- 배포 준비 검증: `node --check Assets/PlayFabCloudScript/combinedCloudScript.js` 통과. `RoomLease.test.js`는 두 CloudScript에 대해 26개 시나리오/564개 검증 통과. `NetworkProfileRoomCapacity.test.js`의 소유권·재시도·정원·참가 인증 검사도 두 파일 모두 통과. 실제 PlayFab 배포 및 온라인 성공 검증은 아직 수행하지 않았다.
- 공식 기준: https://learn.microsoft.com/en-us/rest/api/playfab/client/server-side-cloud-script/execute-cloud-script?view=playfab-rest 및 https://learn.microsoft.com/en-us/gaming/playfab/live-service-management/service-gateway/automation/cloudscript/quickstart
