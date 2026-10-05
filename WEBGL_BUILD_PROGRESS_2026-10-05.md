# WebGL 빌드 기록 — 2026-10-05

## 범위와 완료 기준

- 사용자 요청으로 현재 게임의 WebGL 일반 빌드를 생성한다. Login·Lobby·Battle_waiting·Battle 4개 씬을 포함한다.
- 현재 웹 네트워크는 Unity Relay의 WSS 경로다. WebRTC 직접 연결 및 TURN 전환을 새로 구현한 빌드가 아니다. 앞서 제시한 WebRTC 30~60ms는 미검증 목표치이며 이번 빌드의 측정 결과가 아니다.
- 빌드 성공, 브라우저 로딩과 초기 화면 표시, 웹 호스팅용 압축 파일의 필수 파일 포함을 확인한다. 외부 회선의 8인 대전, 핑, 장시간 메모리 누수 검증과 구분한다.
- 기존 Windows 빌드와 프로젝트의 공용 게임 코드는 보존한다. CloudScript는 사용자 직접 배포 방침을 유지한다.

## 빌드 진행

- 첫 작업 `build-843e2da4e0`: Windows 활성 플랫폼에서 WebGL 빌드를 직접 요청하여 `BattleChatUI`의 `WebGLInput` 참조 두 곳에서 CS0103 발생. 웹 플레이어 컴파일 응답 파일에 `UnityEngine.WebGLModule.dll`이 누락된 것을 확인했다.
- 소스/API 변경 없이 활성 플랫폼을 WebGL로 전환하여 모듈을 로드했다. 이후 응답 파일에 WebGLModule 참조가 포함됐고 플레이어 스크립트 컴파일을 통과했다.
- 재빌드 작업: `build-d3842f5f44`. 결과 경로: `Builds/WebGL-2026-10-05`.
- 로컬 확인용 서버는 빌드 디렉터리만 `127.0.0.1:8770`으로 제공한다. 외부 공개 배포를 의미하지 않는다.

## 최종 결과

- `build-d3842f5f44` 성공: Unity 6000.3.15f1 / WebGL IL2CPP / Development 해제 / 823.92초 / 약 76.74MiB / 오류 0 / 경고 4.
- 경고는 기존 미사용 필드 `DamagePopup._moveYSpeed`, `UnityRelayTransport._connectionType`, `FollowCamera._moveSmoothTime`, `FollowCamera._rotSmoothSpeed`다.
- Brotli 압축과 브라우저 압축 해제 fallback을 유지했다. 일반 정적 HTTP 서버에서 `.unityweb` 로딩·압축 해제와 WebAssembly 초기화가 완료됐다.
- 로컬 브라우저에서 움직이는 전장 배경, `BATTLE PVP`와 `Touch to start`, 클릭 후 ID/비밀번호 입력·로그인·회원가입 패널 표시를 확인했다. 이 구간의 브라우저 오류 로그는 0개다. Unity 런타임의 수동 파일 시스템 동기화 API 폐기 예정 경고 1개는 남아 있다.
- 실제 계정 로그인·방 생성/참가·웹/Windows 상호 대전·핑·장시간 메모리 검사는 하지 않았다. WebRTC 전환이나 핑 개선 완료를 의미하지 않는다.
- 전달용 압축: `Builds/Battle_PvP-WebGL-2026-10-05.zip` / 80,023,359 bytes / 19개 파일. `index.html`을 ZIP 루트에 배치하고 각 파일의 경로·크기·압축 스트림 읽기를 검증했다. 로컬 실행 및 호스팅 안내문을 포함했다.
- 빌드·브라우저·ZIP 검사와 SHA256 기록: `Reports/Builds/webgl-release-2026-10-05.json`.
- 결과 확인용 브라우저 탭과 로컬 HTTP 서버를 남겼다. 주소 `http://127.0.0.1:8770/`는 현재 PC에서만 접속 가능하며 친구 공유용 공개 주소가 아니다.
- 에디터 활성 플랫폼은 빌드 후 StandaloneWindows64로 복귀했다. 이 작업을 위해 게임 소스를 수정하지 않았고 기존 Windows ZIP은 변경하지 않았다.
