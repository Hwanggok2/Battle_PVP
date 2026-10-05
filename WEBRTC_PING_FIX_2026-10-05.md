# 웹 핑 개선: UDP 직접 연결

## 원인과 변경 범위

- 기존 Windows 직접 UDP 경로는 유지한다. 웹 클라이언트에는 직접 연결 경로가 없어 같은 PC에서도 Tokyo Relay/WSS를 경유했다.
- 웹과 Windows 사이에 WebRTC 데이터 채널을 추가한다. STUN으로 직접 UDP 연결을 시도하며, 오디오·영상 스트리밍은 사용하지 않는다.
- 기존 Relay 연결에서 참가자 인증을 마친 뒤 연결 정보만 교환한다. 별도 시그널링 서버와 CloudScript 변경은 필요하지 않다.
- 실제 ICE 후보의 전송 방식이 UDP인 경우에만 게임 데이터를 전환한다. HUD에 `WebRTC UDP`를 표시한다. RTT 계산이나 표기 수치를 낮추는 보정은 하지 않는다.

## 메시지 전환과 호환성

- 이동용 채널은 재전송하지 않고, 공격·스킬 등 신뢰성 메시지는 순서를 보장하는 별도 채널로 전송한다.
- 이전 Relay 메시지 뒤에 전환 표식을 보낸다. UDP 메시지가 먼저 도착하면 표식을 받을 때까지 보관해 순서 역전을 막는다.
- 신뢰성 전송 대기열은 1 MiB/1,024개/10초, 브라우저 수신 대기열은 1 MiB/1,024개로 제한한다.
- 첫 동시 접속 부하 시험에서 기존 UTP의 `EndSend(-5)`가 발생했다. 분할 패킷 버스트를 수용하도록 신뢰성 창을 128→256, 드라이버 송수신 큐를 각 2,048개로 설정했다. 부분 전송 후 무조건 재시도해 명령이 중복될 수 있는 동작은 추가하지 않았다. 지속적인 혼잡에서는 대기열 제한 또는 전송 오류로 연결이 종료될 수 있다.
- 연결 협상이 12초 안에 끝나지 않거나 UDP가 차단되면 기존 Relay를 유지한다.
- 이미 직접 경로로 전환된 뒤 연결이 끊기면 방 연결을 종료한다. 전달 여부를 알 수 없는 공격 명령을 Relay로 재전송하는 무손실 재접속 기능은 이번 변경에 포함하지 않는다.
- 인증 응답 구조는 유지하고 별도 지원 안내를 추가했다. 이전 클라이언트는 안내를 무시하고, 이전 호스트는 안내하지 않으므로 기존 Relay 연결을 유지한다.
- SDP·ICE 인증값·Relay 참가 코드는 진단 로그에 기록하지 않는다.

## 검증

- Unity EditMode 최종 검증: `RoomRtcSessionTests`, `RelayPollingTests`, `RoomDirectTransportTests`, `RelayReliableFailureTests`, `RoomObserverIsolationTests` **46/46 통과**. 작업 ID: `d3edf77073f5486db8e620ce0144b4f5`.
- 브라우저 브리지와 포인터 잠금 테스트: `Tools/Tests/WebRtcBridge.test.js` 4개, `Tools/Tests/WebGlPointerLock.test.js` 8개, **12/12 통과**.
- Windows·WebGL 정식 게임 빌드 모두 성공. WebGL 시작 화면과 브라우저 오류 없음까지 확인했다. 실제 게임 방의 8인 전투 검증은 별도다.
- 초기 Windows 실측: Relay P50 83.35ms/P95 149.98ms. 직접 경로의 짧은 구간은 7.67ms/P95 8.30ms였으나, 동시 접속한 웹 참가자의 Relay 전송 오류로 테스트 전체가 종료됐다. 이 값은 최종 성공 검증으로 취급하지 않는다.
- 버퍼 수정 후 Windows ↔ Windows 재검증 성공: Relay P50 **83.38ms**, P95 **124.81ms** → WebRTC UDP P50 **8.19ms**, P95 **8.29ms**, 최대 **13.82ms**. 직접 구간 191개 표본, 18KB 신뢰성 메시지 480회 왕복, 순서 오류 0. 보고서: `Reports/PingDiagnosis/webrtc-native-stress/client/client-result.json`.
- Windows 호스트 ↔ WebGL 직접 UDP 지연 시험 성공: 380개 표본, P50/P95 **17ms**, 최대 **25ms**, 웹 렌더링 약 **60.6 FPS**. 연결 직후 UDP로 전환하고 10초 이후만 집계했다. 보고서: `Reports/PingDiagnosis/webrtc-web-clean/client-result.json`.
- 이 시험은 그래픽 부하를 제외한 테스트 전용 씬으로, 같은 PC에서 진행했다. Windows 입력·네트워크 루프는 120Hz, 스냅샷 전송은 60Hz이며 웹은 60 FPS에서 측정했다. 실제 8인 전투의 프레임 성능이나 외부 인터넷의 RTT를 보장하는 수치는 아니다.

## 배포 파일

- Windows: `Builds/Battle_PvP-Windows-2026-10-05-WebRTC.zip`. 압축 전체를 풀고 `Battle_PvP.exe`를 실행한다. 네이티브 `webrtc.dll` 및 라이선스 고지를 포함하며 배포 제외용 디버그 폴더는 압축에서 제외했다.
- WebGL: `Builds/Battle_PvP-WebGL-2026-10-05-WebRTC.zip`. 현재 `http://127.0.0.1:8771/`에서 새 빌드를 제공한다. 기존 탭은 새로고침해야 한다.
- Windows 빌드 작업 `build-b1e43dac9a`: 성공, 오류 0/경고 7, 173초.
- WebGL 빌드 작업 `build-e62925f28b`: 성공, 오류 0/경고 4, 736초.
- 호스트와 참가자 모두 새 빌드를 사용한다. 이번 WebRTC 추가에 따른 CloudScript 재배포는 필요하지 않다.
- ZIP 무결성과 테스트 전용 코드·설정 제외 여부를 검사했다. 빌드·압축 검증 기록은 `Reports/Builds/`에 저장한다.

## 남은 검증과 제한

- 웹에서 Relay/WSS로 18KB 메시지를 초당 10회 지속 전송하는 별도 부하 시험은 버퍼 확대 후에도 `EndSend(-5)`로 실패했다. 해당 오류는 해결 완료로 표시하지 않는다. 위 웹 RTT 시험은 이 대형 메시지 부하를 끈 상태이며, 웹의 대용량 신뢰성 전송 스트레스 통과를 의미하지 않는다.
- Windows 간 18KB 양방향 순서 검증과 웹 지연 측정은 구분한다. 다른 네트워크/NAT 환경, 8인 실제 전투는 추가 실기 검증 대상이다.
- 정상 클라이언트 종료 시 WebRTC 종료 이벤트가 Relay의 연결 종료보다 먼저 도착하면 호스트에 `webrtc_active_connection_lost` 진단이 남을 수 있다. 해당 참가자만 정리하며 다른 참가자를 종료하지 않는다. 테스트 전용 호스트는 오류를 포착하면 전체 시험을 종료하므로 클라이언트 성공 결과와 호스트 종료 결과를 함께 해석한다.
- 최종 게임 빌드에는 테스트 씬·자동 접속·테스트 인증 우회·검증 버튼을 넣지 않는다. 호스트와 참가자가 모두 새 버전을 사용해야 직접 연결을 협상할 수 있다.

## 참고

- [Unity 공식 WebRTC 데이터 채널](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/datachannel.html)
- [MDN RTCDataChannel](https://developer.mozilla.org/en-US/docs/Web/API/RTCDataChannel)
