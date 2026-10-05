# 네트워크 상태 표시 위치 수정 — 2026-10-05

## 확인 결과

- 사용자 제보: RTT 약 100ms / Jitter 약 50ms, 방 정보 배너가 네트워크 표시를 가림.
- 수정 전 에디터 호스트 조회: `route=relay`, `protocol=udp`, `region=asia-northeast1`, 직접 참가자 0명 / Relay 참가자 1명. 웹 빌드는 WSS를 사용하므로 웹→Relay는 WSS, 에디터 호스트→Relay는 UDP다. 이 조회는 지터 원인을 특정하거나 장기 성능을 측정한 것이 아니다.
- 현재 WebGL 빌드에는 WebRTC 직접 연결이 구현되지 않았다. 앞서 언급한 WebRTC 지연 목표와 현재 Relay 빌드의 실제 지연을 구분한다.

## 변경

- 공용 `LobbyUIManager.EnsureLatencyText()`에서 우측 상단 오프셋을 `(-4, -114)`에서 `(-28, -160)`으로 변경했다.
- 표시 영역을 `190×62`에서 `240×76`으로 늘렸다. 방 정보 배너 아래에 여백을 확보하고 RTT·Jitter·접속 경로 3줄을 수용한다.
- 지연 계산식과 네트워크 연결 방식은 변경하지 않았다.

## 검증

- 재생 중인 에디터에서 새 배치를 적용해 캡처했다. 화면 1920×1080에서 배너와 네트워크 표시 간 세로 간격 30px, 사각형 겹침 false를 확인했다.
- 3줄 문자열의 TMP 필요 크기는 약 148.65×57.26으로 새 영역 240×76 안에 들어간다.
- 전후 화면: `Reports/Validation/2026-10-05/latency-before.png`, `latency-after.png`.
- 에디터 재생을 종료하고 Refresh/Compile 후 컴파일 오류 0개를 확인했다. 실제 표시 위치 변경이므로 구현을 반복하는 별도 단위 테스트는 추가하지 않았다.

## 배포

수정된 웹/Windows 빌드 결과를 아래에 기록한다. 기존 ZIP은 보존한다.

- Windows: `build-ee5e1cd1a0`, 19.87초, 오류 0 / 기존 미사용 필드 경고 3개.
- 실행 파일: `Builds/Windows-2026-10-05-HudFix/Battle_PvP.exe`.
- ZIP: `Builds/Battle_PvP-Windows-2026-10-05-HudFix.zip` (109,243,035 bytes, 파일 233개). 압축 내 파일명·크기 일치와 전체 CRC 검증 완료.
- Windows 빌드/압축 검증 기록: `Reports/Builds/windows-hud-fix-2026-10-05.json`.
- WebGL: `build-2242a7ce9c`, 443.53초, 오류 0 / 기존 미사용 필드 경고 4개.
- 웹 출력: `Builds/WebGL-2026-10-05-HudFix`.
- 웹 ZIP: `Builds/Battle_PvP-WebGL-2026-10-05-HudFix.zip` (80,019,642 bytes, 파일 19개). 루트 `index.html`, 압축 내 파일명·크기 일치와 전체 CRC 검증 완료.
- 웹 빌드/압축 검증 기록: `Reports/Builds/webgl-hud-fix-2026-10-05.json`.
- 수정본 로컬 주소: `http://127.0.0.1:8771/` (이 PC에서만 접근 가능). 기존 8770 서버와 기존 빌드를 보존하고 수정본 전용 포트를 사용한다.
- 수정본을 브라우저에서 실행해 시작 화면과 클릭 후 로그인 화면을 확인했다. 확인 시점 브라우저 오류 로그 0개. 새 빌드의 로그인 이후 멀티플레이 및 RTT/Jitter 재측정은 진행하지 않았다.
- Unity 에디터 빌드 대상은 `StandaloneWindows64`로 되돌렸다. 위치 수정은 웹/Windows 공용 코드이며 네트워크 성능 개선을 의미하지 않는다.
