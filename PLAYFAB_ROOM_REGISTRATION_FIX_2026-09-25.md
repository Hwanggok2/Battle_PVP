# 방 생성 후 자동 로비 복귀: CloudScript 그룹 생성 오류

> 2026-09-26: 사용자가 “잘 된다”고 방 생성 정상화를 확인했다. 아래 배포 대기 문구들은 해당 확인 이전의 진행 기록이다.

> 후속 상태: revision 14에서도 실패가 재현됐다. 최초 수정에서 API 오류의 중첩 구조 처리를 놓쳤다. 아래 후속 수정 절을 포함한 최신 파일을 배포해야 하며, 수정 후 온라인 성공은 아직 확인 전이다.

## 완료 기준

- 실제 PlayFab 응답으로 방 등록 실패 지점을 확인한다.
- 기존 registry와 방 그룹을 보존하면서 신규 생성·재등록·동시 생성을 처리한다.
- 실제 오류 형식으로 수정 전 실패와 수정 후 성공을 검증한다.
- 수정본의 Live 배포 후 새 방을 만들고 최소 90초간 대기실 유지 및 heartbeat 갱신을 확인한다. 온라인 검증 전에는 전체 해결 완료로 처리하지 않는다.

## 확인한 원인

- 로그인된 Unity 에디터 세션에서 실제 방 생성을 재현했다. PlayFab **revision 13**이 실행됐으며, 첫 서버 API 호출인 `/Server/CreateSharedGroup`에서 `InvalidSharedGroupId` **1088**을 반환했다. 전체 응답은 `CloudScriptAPIRequestError`였다.
- 별도 읽기 요청으로 `GLOBALROOMREGISTRY`가 이미 존재함을 확인했다. 데이터 12개가 있었으며 내용과 사용자/방 식별자는 진단 결과에 기록하지 않았다.
- 기존 `ensureSharedGroup`는 매번 생성을 시도한 뒤 `SharedGroupAlreadyExists`, `already`, `exists` 등의 메시지만 검사했다. 실제 중복 ID 응답인 1088을 처리하지 못해 등록을 중단하고, 세 번 재시도 후 클라이언트가 로비로 돌아갔다.
- 기존 모의 서버 역시 중복 생성에 `SharedGroupAlreadyExists`를 발생시켜 실제 서비스와 달랐다. 이 값을 실제 1088 오류 형태로 수정하자 기존 코드에서 회귀 검사 실패가 재현됐다.
- 이전에 제시한 배포 누락 가능성만으로는 이번 문제를 설명할 수 없었다. 이번에는 새 배포에서 실행된 API와 오류 코드를 직접 확인했다.

## 수정 내용

- `Assets/PlayFabCloudScript/roomRegistry.js` 및 `combinedCloudScript.js`: 먼저 그룹을 조회해 존재하면 재사용한다. 없는 경우에만 생성한다.
- 조회와 생성 사이 다른 요청이 같은 그룹을 생성해 1088이 발생하면, 다시 조회해 실제 존재를 확인한 뒤 진행한다.
- 권한/서비스 오류는 그대로 실패 처리한다. 1088이 발생했지만 그룹이 존재하지 않는 경우도 성공으로 취급하지 않는다. 기존 registry 데이터와 멤버는 삭제하지 않는다.
- `Tools/Tests/NetworkProfileRoomCapacity.test.js`: 중복 생성 모의 오류를 실제 PlayFab 형식으로 변경했다.
- `Tools/Tests/SharedGroupCreation.test.js`: 신규 생성, 기존 registry 보존, 멤버 유지 재등록, 동시 생성, 잘못된 ID, 조회/생성 장애 및 확인 조회 실패 검사를 추가했다.
- 진단용 임시 런타임 콜백은 제거하고 원래 SDK 호출로 복구했다. 클라이언트 인증/방 만료 검사는 변경하지 않았다.

## 검증 결과

- 수정 전: 실제 중복 오류 형식으로 기존 방 회귀 검사가 실패했다.
- 수정 후: 두 CloudScript 대상 그룹 생성 **14개 시나리오 통과**.
- 기존 방 소유권·정원·재시도·참가 인증 검사: 두 파일 모두 통과.
- 기존 방 만료 검사: **26개 시나리오, 564개 검증 통과**.
- `node --check Assets/PlayFabCloudScript/combinedCloudScript.js` 통과.
- 증거: `Reports/NetworkDiagnostics/registration-response.jsonl`, `registration-api-error.json`, `registration-api-details.json`, `registry-existence.json`. 요청 값과 인증 정보는 제외했다.
- Unity 에디터에서 실제 실패 경로를 검증했다. 웹 빌드/웹 테스트는 수행하지 않았다.

## 남은 적용

1. 이번에 다시 수정한 `Assets/PlayFabCloudScript/combinedCloudScript.js` 전체를 해당 PlayFab 타이틀의 Classic CloudScript 새 revision에 반영하고 **Live로 배포**한다. 기존 revision 13에는 이 수정이 없다.
2. Unity에서 새 방을 만들고 최소 90초간 대기실이 유지되는지 확인한다. heartbeat가 15초 간격으로 성공해야 60초 임대가 계속 연장된다.
3. 참가자 입장과 정상 퇴장을 확인한다.

실제 Live 배포와 수정 후 온라인 성공은 아직 확인하지 않았다. 기존 registry 삭제나 클라이언트 권한 완화는 이 수정의 선행 조건이 아니다.

공식 API 계약: https://learn.microsoft.com/en-us/rest/api/playfab/server/shared-group-data/create-shared-group?view=playfab-rest

## 후속 수정: revision 14의 신규 그룹 조회 실패

- 사용자의 재보고 후 같은 로그인 세션에서 다시 실제 호스트 방 생성을 실행했다.
- 실행된 revision은 **14**였다. 첫 시도는 서버 API 4회, 재시도는 3회 호출 후 실패했다. 기존 registry 조회 및 lease 쓰기는 통과했고, `ensureSharedGroup(roomId)` 내부 `GetSharedGroupData`에서 새 방 그룹이 없다는 1088 오류가 발생했다.
- 응답 스택은 `ensureSharedGroup (133DF7-main.js:830)`과 등록 함수 357행을 가리켰다. 증거 파일은 `Reports/NetworkDiagnostics/registration-response-followup.jsonl`이다.
- 기존 `isMissingSharedGroup`는 `apiErrorInfo.apiError`를 문자열로 가정했다. 중첩 객체의 `error/errorCode`를 읽도록 수정했다. 일반적인 API 장애를 그룹 부재로 취급하지 않으며, 기존 평면 응답 형식도 유지한다.
- 모의 서버의 조회/생성 오류도 중첩 객체 형식으로 바꿨다. 수정 전에는 중첩 오류 판별 검사가 실패했고, 수정 후에는 그룹 생성 **18개 시나리오**, 기존 방 정원·권한·인증 검사, 방 만료 **26개 시나리오/564개 검증**이 통과했다.
- 두 배포 파일에 동일하게 반영했다. 새 Live revision 배포 후 실제 방 등록, heartbeat 연장 및 최소 90초 유지 확인이 남아 있다.
- 현재 Unity 로그인 세션을 유지하고 등록/heartbeat 응답의 revision·오류 코드·임대 시각만 기록하는 임시 추적을 설치했다. 사용자/방 식별자, 세션 티켓, 참가 코드는 기록하지 않는다. 검증이 끝나면 임시 추적을 제거한다.
