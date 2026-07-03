# Lobby Errors

## Login 후 Lobby 진입 직후 저장된 스텟이 적용되지 않음

### 증상
- 로그인 직후 Lobby 씬에 들어오면 이전에 저장한 스텟이 아직 적용되지 않은 상태로 인식된다.
- Stat 설정 창을 한 번 열기 전까지 방 만들기/참여 시 "모든 스텟을 투자하십시오" 알림이 뜬다.

### 원인
- 로그인 성공 이벤트가 PlayFab 스텟 로드 완료보다 먼저 발생할 수 있었다.
- Lobby 방 만들기/참여 검증에서 저장 데이터가 아니라 `StatCustomizerController.GetRemainPoints()`를 함께 검사하고 있었다.
- `GetRemainPoints()`는 Stat 설정 UI의 슬라이더 상태를 기준으로 하므로, Stat 설정 창이 비활성화된 상태에서는 저장된 스텟과 다른 결과를 낼 수 있다.

### 해결 방법
- `PlayFabAuthManager`에서 PlayFab 스텟 로드가 끝난 뒤 로그인 성공 이벤트를 발생시키도록 변경한다.
- `GlobalDataManager`에 플레이어 스텟 로드 완료 여부를 저장하는 플래그를 둔다.
- 씬 전환 시 `GlobalDataManager`가 아직 스텟을 불러오지 않았다면 PlayFab에서 자동으로 다시 불러오도록 한다.
- Lobby의 방 만들기/참여 검증에서는 `StatCustomizerController.GetRemainPoints()`를 사용하지 않는다.
- 방 만들기/참여 가능 여부는 `GlobalDataManager`에 저장된 스텟과 전략가 전환 프리셋만 기준으로 판단한다.

### 관련 파일
- `Assets/Player/Script/PlayFabAuthManager.cs`
- `Assets/Player/Script/Managers/GlobalDataManager.cs`
- `Assets/Player/Script/UI/LobbyUIManager.cs`
