# S11 서버 설정 반영 준비

이 폴더는 검토할 정책 조각을 제공한다. 실제 PlayFab 설정 조회·변경·배포는 수행하지 않았다. 개발자 비밀 키나 관리자 키를 게임 코드/이 폴더에 저장하지 않는다.

## 2026-10-05 참가자 인증 진단 추가

후속 직접 UDP 우선 연결: `RegisterRoomToRegistry`에 선택 인자 `directEndpoint`(공인 IPv4:UDP 포트)를 추가했다. `roomInfo`의 해당 값을 JoinRoom/목록에서 유지해 인터넷 참가자가 직접 접속을 먼저 시도하도록 한다. 기존 `relayJoinCode`의 의미/형식은 유지하며, 구버전 요청은 직접 후보 없이 정상 처리한다. 호스트만 등록할 수 있고 참가 인증은 그대로다. `node Tools/Tests/RoomDirectEndpoint.test.js`로 양쪽 배포 파일을 검사했다. **사용자가 직접 배포하기로 했으며 Live 반영은 아직 미확인이다.** 배포 대상은 `Assets/PlayFabCloudScript/combinedCloudScript.js`이고, 코드 반영 후 새 방을 생성해야 새 공인 후보가 게시된다.

`Assets/PlayFabCloudScript/combinedCloudScript.js` (또는 동일 게임 핸들러의 `roomRegistry.js`)에 JoinRoom / ApproveRoomConnection / VerifyRoomConnection의 실패 코드 로그를 추가했다. `ROOM_FAILURE:<고정 코드>`만 기록하며 계정·방·암호·challenge·서버 예외 본문은 기록하지 않는다. 성공 응답과 실패 throw 계약은 유지한다. **이 변경은 아직 배포하지 않았다.** 기존 Live revision 15의 불투명 JavascriptException은 새 호스트가 최대 3회 같은 증명을 읽어 확인한다. 서버에 진단 코드를 배포하면 영구 거절과 일시적인 증명 읽기 실패를 구분할 수 있다. 승인 없는 입장 허용이나 개발자 비밀키 추가는 필요 없다.

로컬 검증: `node Tools/Tests/RoomFailureDiagnostics.test.js`. 상세 실측과 한계는 [핑·접속 작업 기록](../../PING_FIX_PROGRESS_2026-10-05.md)에 저장했다.

## 반영 대상

`ApiPolicy.room-and-statistics-deny.json`은 Client API 5개의 직접 쓰기를 막는 추가 Statements다. 현재 정책 전체를 대체하는 파일이나 완성된 UpdatePolicy 요청이 아니다. 로그인·프로필 프리셋 저장·공개 목록 읽기·ExecuteCloudScript와 Server API는 이 조각의 차단 대상이 아니다. API별 정책 형식은 [공식 API Access Policy](https://learn.microsoft.com/en-us/xbox/playfab/api-references/api-access-policy)를 따른다.

1. 테스트 타이틀에서 현재 `ApiPolicy`를 GetPolicy로 읽어 백업하고 최신 PolicyVersion을 확인한다. 이미 같은 Deny가 있으면 중복 추가하지 않는다.
2. 기존 Statements를 보존하며 해당 조각의 필요한 Deny만 추가한다. Admin UpdatePolicy 사용 시 `OverwritePolicy: false`와 읽은 최신 PolicyVersion을 사용하고 응답 Warnings 및 재조회 결과를 확인한다. [공식 UpdatePolicy 계약](https://learn.microsoft.com/en-us/rest/api/playfab/admin/authentication/update-policy?view=playfab-rest)
3. 수정한 `combinedCloudScript.js` 또는 동일 게임 핸들러를 가진 `roomRegistry.js`를 테스트 revision에 반영한다. 새 클라이언트와 함께 검증한다. 기존 `what.js` 튜토리얼 파일을 별도로 배포하지 않는다. 서버 권한 샘플 호출이 다시 활성화되면 게임 핸들러 허용 목록을 우회할 수 있다.
4. `RoomAdminKey`는 Title Internal Data에만 설정한다. 기존 공개 Title Data에 키가 있었다면 공개 항목 제거와 새 키 발급이 필요하다. 새 코드에서는 공개 키만 설정된 관리자 기능이 거절된다.
5. 기존 `GLOBALROOMREGISTRY`의 Data·공개 권한·Members를 서버 권한으로 조회해 먼저 백업한다. 목록 조회에는 사용자 멤버십이 필요 없다. **Shared Group은 마지막 멤버 제거 시 데이터까지 삭제되므로 멤버를 일괄 제거해서는 안 된다.** 기존 방 종료 후 서버에서 멤버 없는 registry를 재생성하고 공개 데이터를 복구하는 절차 등 데이터 보존 계획을 먼저 확정해야 한다. 이 마이그레이션을 자동 실행하는 코드는 없다. 마지막 멤버 삭제 동작은 프로젝트에 설치된 `PlayFabServerAPI.RemoveSharedGroupMembers` 계약에서도 확인했다.
6. 새 방은 `battle_<호스트 PlayFabId 소문자 hex>_<32자리 nonce>` 형식이다. 이전 GUID 방은 목록 읽기만 호환되고 변경 요청은 거절되므로 호스트가 새 클라이언트에서 방을 다시 생성해야 한다. 새 빌드와 CloudScript 전환 시 진행 중 방의 종료 시점을 함께 정한다.

## 테스트 타이틀의 실제 합격 검사

- 로그인·프리셋 저장·방 생성/참가/탈퇴/공개 목록 조회는 정상 수행된다.
- Client 통계/SharedGroup 생성·데이터 쓰기·멤버 추가/삭제의 직접 요청 5개는 거절된다.
- 참가자가 다른 호스트의 roomId로 재등록/Relay 변경을 시도하면 거절되고 기존 값이 유지된다.
- 전역 registry 및 이전 GUID roomId 변경 요청이 거절되고 registry 멤버가 증가하지 않는다.
- 실제 멤버의 중복 탈퇴/비멤버 탈퇴로 다른 참가자의 인원수가 감소하지 않는다.
- Internal 관리자 키 누락·잘못된 키는 거절되고 정상 관리 요청은 허용된다.
- 같은 호스트의 등록 응답 유실 후 재시도는 기존 참가자를 제거하거나 인원을 1로 되돌리지 않는다.

로컬 Node 모의 검사는 핸들러 흐름의 증거다. 라이브 API policy, 실제 Shared Group 동시성이나 경쟁 기록 보상 트랜잭션의 증거로 사용하지 않는다. Shared Group 멤버는 자체 소유자 권한 구분이 없으므로 클라이언트 직접 변경 차단이 필요하다. [공식 Shared Group 권한 설명](https://learn.microsoft.com/en-us/xbox/playfab/community/associations/groups/using-shared-group-data)

## 별도 구현이 남은 S11 계약

현재 방 ID는 호스트 변경 요청의 이름공간이며 경기 ID나 그 자체로 Mirror 참가자 인증 증명이 아니다. Mirror 인증과 중도 이탈자 메모리 기록의 로컬 구현은 추가했으나 아래 실제 검사는 대기다. 서버 발급 경기 ID, 확정 결과의 영속 제출/재시도, 같은 트랜잭션의 보상 반영은 아직 구현하지 않았다. Shared Group과 통계 쓰기를 순서대로 호출하는 것으로 중복 방지·부분 실패 복구를 완료 처리하지 않는다.

## 참가자 인증 추가분의 배포·실행 검사

참가자 인증 추가 당시 게임 핸들러는 11개였고, 이후 아래 방 만료용 `HeartbeatRoom`을 추가해 현재는 **12개**다. `ApproveRoomConnection`, `VerifyRoomConnection`이 없는 revision에서는 원격 클라이언트가 인증에 실패한다. 기존 Client 직접 쓰기 차단과 함께 테스트 타이틀에서 새 클라이언트/CloudScript를 검증한 후 전환한다. 개발자 비밀키를 Unity 설정에 추가할 필요가 없는 프로토콜이며, 기존 공개 관리자 키를 재사용하는 인증도 아니다.

| 검사 | 합격 조건 |
|---|---|
| 호스트 및 원격 7명 | 각 PlayFab 계정이 해당 Mirror 연결에 묶이고 플레이어 8명. 호스트 시작/씬 로딩에서도 교착이나 9번째 플레이어 생성 없음 |
| 잘못된 계정/방/challenge | 자기 세션으로 다른 계정을 주장해도 승인 주체는 자기 계정. 잘못된 증명으로 플레이어 생성 불가 |
| 인증 전 명령·Ready/AddPlayer | 미인증 연결은 스폰/게임 명령을 실행하지 못함 |
| 만료/서비스 오류/끊김 | 30초 호스트 인증 기한, 만료 proof, InternalData 읽기/쓰기 실패는 인증 실패. 로비의 대기 상태가 해제되고 정상 재시도 가능 |
| 중복·재접속 | 검증된 같은 계정 두 연결은 동시에 플레이하지 못함. 기존 연결 종료 뒤 순차 재접속하면 해당 경기 기록 복구 |
| 응답 역전 | 방 전환/서버 재시작/끊김 뒤 이전 성공 콜백을 새 연결에 적용하지 않음 |
| 이탈한 우승자/상대 | 이탈해도 전원 순위·예상 XP·상대 이름/횟수 유지. 연결된 대상에게만 결과 전달 |
| 종료·새 경기 | 확정 결과 변경 거절, 새 경기 점수 초기화, 최근 결과 진단 보존. 호스트 종료 뒤 영속 복구는 제공하지 않음 |
| 종료 직후 접속·관전 이탈 | 종료된 경기의 신규 스폰은 거절하고 다음 경기 재시작 후 참가. 관전 중 우승자가 나가면 로컬 시점으로 복구하고 결과는 유지 |

계정별 최신 InternalData proof 한 개 정책과 Shared Group의 연결별 멤버십 임대 부재 때문에 같은 계정의 동시 인증 경합·거절 응답 유실까지 원자적으로 처리하지 않는다. 상세 흐름과 제한은 [NETWORK_PROFILE_BOUNDARIES.md](../../NETWORK_PROFILE_BOUNDARIES.md)에 기록했다. 실제 8인/Windows/WebGL 인증 성공이나 배포 완료를 로컬 Node/.NET 검사로 대체하지 않는다.

## 호스트 생존 신호와 방 만료 추가분

이 변경도 **테스트/운영 타이틀에 아직 배포하지 않았다.** 새 클라이언트와 다음 내용을 함께 반영해야 한다.

- 게임 핸들러 12개에 owner-only `HeartbeatRoom({roomId})`이 포함된다. `RegisterRoomToRegistry`와 heartbeat 응답의 최상위 `roomId`, `serverNow`, `leaseExpiresAt`을 새 클라이언트가 검사한다. 목록의 각 roomInfo에도 `serverNow`와 `leaseExpiresAt`이 포함된다. 시각 단위는 서버 Unix milliseconds다.
- 서버 임대는 60초, 클라이언트 heartbeat는 15초 간격이며 실패 시 5초 뒤 재시도한다. 첫 등록 승인도 60초 안에 받아야 한다. 긴 WebGL 백그라운드/일시 중단으로 만료되면 호스트 연결을 끝내고 새 방을 만들어야 한다. 실제 플랫폼의 지연/중단 동작은 테스트 타이틀에서 확인한다.
- `GLOBALROOMREGISTRY`에는 `<roomId>` 메타데이터와 `ROOMLEASE_<roomId>`, `ROOMCLOSED_<roomId>` 상태 키를 별도로 둔다. 이들은 목록의 방 이름이 아니다. 비어 있는 room 그룹이 자동 삭제되어도 registry의 만료/폐쇄 기록은 유지해야 한다. 앞서 명시한 registry 기존 멤버 정리/백업·재생성 계획과 직접 Client 쓰기 차단은 계속 필요하다.
- 유효 기한이 없는 이전 방은 새 목록/입장에서 제외된다. 기존 세션 종료 후 두 CloudScript 중 배포 대상을 새 revision에 올리고 새 클라이언트를 함께 전환한다. 과거 revision으로 되돌릴 때에는 그 revision이 폐쇄 상태를 무시할 수 있으므로 진행 중 방과 남은 메타데이터를 함께 관리한다.
- 클라이언트 공개 SharedGroup fallback과 오래된 로컬 목록의 재사용 경로를 제거했다. 서버 조회가 실패하면 빈 목록을 반환하고 최대 5초 간격으로 다시 조회한다. 목록 요청은 10초를 넘으면 무효화하며 서버가 빈 목록을 반환한 경우도 캐시를 비운다.
- 만료 데이터의 물리 삭제/정기 청소는 이 구현에 포함하지 않는다. 조회 시 만료된 방을 제외하고 입장/인증을 거절한다. 관리자 삭제/전체 목록 정리는 메타데이터를 제거하되 유효 방 ID의 폐쇄 기록과 lease 키를 보존한다. 장기 보존 기록의 용량·아카이브 정책은 별도 작업으로, 기록을 임의로 지우면 이전 요청/ID의 재생성 방어가 사라진다.

실제 합격 검사는 호스트 1인/게스트가 남은 방 각각에서 창 닫기·강제 종료·네트워크 차단을 실행하고, 마지막 서버 갱신 후 60초 만료 및 다음 목록 갱신에서 제외/입장 거절을 확인하는 것이다. 호스트 정상 퇴장은 참가자가 남아 있어도 즉시 폐쇄하며, 참가자 퇴장은 유효 호스트 방을 닫지 않아야 한다. 만료/폐쇄 뒤 늦은 heartbeat·등록 재시도·게스트 정보 쓰기, API 오류와 원래 로그인 세션 만료, 재접속 시 안내/로비 복귀도 확인한다.

로컬 회귀는 `node Tools/Tests/NetworkProfileRoomCapacity.test.js`와 `node Tools/Tests/RoomLease.test.js`로 실행한다. FakeDate와 모의 Server API로 시간·권한·쓰기 교차 순서를 검사하며 실제 PlayFab의 원자성·지연·배포를 증명하지 않는다. SharedGroup의 read/write는 트랜잭션이 아니며 서버 조회/갱신 사이 만료나 다른 장치의 동일 계정 접속 경쟁은 별도로 검증해야 한다.
