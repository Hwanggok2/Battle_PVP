# 웹 입력 오류 및 핑 제보 확인 — 2026-10-05

## 확인된 사실

- 사용자 첨부 화면에 `NotAllowedError: A user gesture is required to request Pointer Lock` 및 처리되지 않은 Promise 거부가 반복된다. 팝업은 Unity 웹 로더의 오류 처리 경로로 표시된다.
- 기존 `GameInputController.Update()`는 Input System의 클릭을 감지한 프레임에서 `Cursor.lockState = Locked`를 호출한다. 실제 브라우저 이벤트와 Unity 프레임의 실행 시점이 다르며, Unity/Emscripten의 설치된 `library_html5.js`는 `requestPointerLock()` Promise의 실패를 처리하지 않는다.
- 잠금 실패로 표시된 브라우저 모달은 게임/네트워크 처리를 지연시킬 수 있다. 하지만 이 문제만으로 이전의 RTT 약 100ms / Jitter 약 50ms 전체를 설명하거나 해결했다고 주장하지 않는다.
- 현재 웹의 게임 연결은 Relay/WSS다. 직접 UDP는 네이티브 빌드에만 구현되어 있으며, WebRTC 전송은 구현되지 않았다.

## 실시간 확인과 한계

- 에디터 호스트: Tokyo(`asia-northeast1`) / UDP, 목표 120Hz, 순간 프레임 8.407901ms. 이는 한 프레임 관측값이며 장기 성능 결과가 아니다.
- 조회 당시 직접 참가자 0 / Relay 참가자 0. 60초 수집도 Pong 0개로 끝났다. 재접속을 요청했으나 수정 검증을 시작할 때까지 참가자는 없었다.
- 원본 수집: `Reports/PingDiagnosis/web-live-2026-10-05/relay-20261005-083953-741-host*`. 참가자가 없으므로 핑 비교 표본으로 사용하지 않는다.
- 수정본 컴파일/테스트/빌드를 위해 에디터 재생을 종료했다. 이번 실행에서는 동일 조건의 수정 전후 멀티플레이 지연을 비교하지 못했다.

## 수정

- `BattlePvpPointerLock.jslib`: 실제 캔버스의 신뢰된 마우스 `pointerdown` 안에서만 잠금을 요청한다. 활성 입력/문서 표시 상태와 게임 입력 정책을 확인한다.
- 중복 요청, 자동 프레임 재시도, Esc 직후 자동 재잠금을 막는다. Promise 거부와 동기 예외는 요청 경로에서 처리하고, 다음 사용자 클릭으로 복구한다. 전역 오류를 가리는 핸들러는 추가하지 않는다.
- 메뉴/채팅/씬 전환에서는 잠금을 해제한다. 메뉴 전환 후 늦게 완료된 요청도 해제하며 씬 종료 시 이벤트 구독을 제거한다.
- 웹에서 `WebGLInput.stickyCursorLock = false`로 실제 브라우저 상태와 Unity 상태를 맞춘다. 잠기지 않은 FPS 화면에서는 전투/카메라 입력을 막고 클릭으로 조작을 재개하도록 안내한다.
- Windows 잠금 방식과 RTT 계산/평활화/연결 경로는 변경하지 않았다.

## 검증

- Node 테스트 `Tools/Tests/WebGlPointerLock.test.js`: 8개 통과. Promise 거부, 동기 거부, 이벤트 기반 구형 API, Esc, 비활성 문서, 메뉴 전환, 씬 종료 중 지연 완료, 미지원 API를 검증했다.
- Unity EditMode: `CursorRoomMenuTests`, `HudAndInputLifecycleTests`, `CombatInputRegressionTests` 30개 모두 통과. 작업 ID `144623163d564a15a24d29d98979fc24`.
- 실제 내장 브라우저에서 플러그인 원본을 사용하는 격리 페이지로 클릭 3회 / 잠금 거부 3회 / 미처리 오류 0개를 확인했다. 잠금은 해당 브라우저에서 허용되지 않았으므로 허용 후 카메라 조작 성공을 검증했다고 기록하지 않는다.
- 검증 페이지: `Reports/Validation/2026-10-05/pointer-lock/`. 가짜 네트워크 수치나 로그인 우회는 사용하지 않았다.
- 웹 빌드: `build-bf7327e36a`, 성공, 436.52초, 오류 0 / 기존 미사용 필드 경고 4개.
- `Builds/WebGL-2026-10-05-PointerFix`의 압축된 production framework를 풀어 새 포인터 플러그인이 포함됐음을 확인했다. localhost 서버 로그에서도 이 framework와 새 WASM/data 파일의 HTTP 200 응답을 확인했다.
- 새 빌드 시작 화면 → 로그인 화면 전환 정상, 해당 확인 중 브라우저 오류 로그 0개. 로그인 이후 실제 전투 입력 및 왕복 지연은 미검증이다.
- ZIP `Builds/Battle_PvP-WebGL-2026-10-05-PointerFix.zip`: 파일 19개, 80,037,648 bytes, 전체 CRC/파일명/크기 일치 검증 완료. 상세 기록 `Reports/Builds/webgl-pointer-fix-2026-10-05.json`.
- 기존 로컬 URL `http://127.0.0.1:8771/`을 새 빌드로 교체했다. 기존 프리뷰 서버 PID·실행 경로·시작 시간을 확인한 뒤 교체했으며 이전 빌드 폴더/ZIP은 보존했다. 검증용 8772 서버와 임시 브라우저 탭은 종료했다.
- 에디터 대상을 `StandaloneWindows64`로 복구했고 콘솔 오류 0개를 확인했다. 실제 재측정 시 에디터에서 방을 다시 생성해야 한다.

## 다음 실제 접속에서 확인할 사항

1. 최신 웹 빌드에서 대기실 입장 → 클릭 → Esc → 메뉴/채팅 → 조작 복귀를 반복해 팝업 발생 여부를 확인한다.
2. 팝업이 없는 상태로 브라우저를 전경에 유지하고 호스트/참가자의 원시 RTT·프레임·송신 대기열을 함께 수집한다.
3. 프레임 정지/큐 포화가 없어도 Relay 지연이 높으면 회선/Relay 경로의 영향으로 구분한다. 웹 저지연 전송을 바꾸려면 별도 WebRTC 전송·시그널링·NAT 통과·Relay 대체 경로가 필요하다. 현재 빌드가 이를 제공한다고 안내하지 않는다.

## 근거

- [Unity 6000.3 Web 입력 및 커서 잠금](https://docs.unity3d.com/6000.3/Documentation/Manual/webgl-input.html)
- [Unity WebGLInput.stickyCursorLock](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/WebGLInput-stickyCursorLock.html)
- [MDN requestPointerLock 보안 조건 및 Promise](https://developer.mozilla.org/en-US/docs/Web/API/Element/requestPointerLock)
