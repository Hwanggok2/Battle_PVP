# Lobby Errors

## Room List ScrollRect Layout Break

### 제목
- 로비 방 목록에서 생성된 방이 X 표시로 보이거나, 폭이 0이 되거나, 잘못된 위치에 표시되는 문제

### 원인
- 서버/PlayFab에서 방 목록을 받아오는 흐름은 정상 동작하고 있었다.
- `LobbyUIManager`가 방 목록 UI를 열고 `RoomListManager.RefreshList()`를 호출하면, `PlayFabBattleManager.GetActiveRoomInfos()`로 받은 방 정보를 기준으로 `_Room` 프리팹이 `Room_List > Viewport > Content` 아래에 생성된다.
- 실제 문제는 데이터가 아니라 ScrollRect 계층의 UI 레이아웃이었다.
- 해상도와 UI 스케일을 조정하는 과정에서 `Viewport`, `Content`, 스크롤바의 RectTransform/Anchor 값이 깨져 `VerticalLayoutGroup`이 자식 방 아이템의 폭을 0으로 계산하거나 화면 밖에 배치했다.
- 기존 `RoomListManager`가 새로고침 과정에서 `VerticalLayoutGroup.enabled = false`로 레이아웃 그룹을 꺼 버리고 있었기 때문에, 인스펙터에서 수동으로 켜도 새로고침하면 다시 꺼졌다.
- 수평 스크롤바도 코드에서 강제로 비활성화되어 인스펙터 배치와 런타임 상태가 서로 어긋날 수 있었다.

### 해결 방법
- `RoomListManager` 새로고침 시 ScrollRect 계층을 정규화하도록 수정했다.
- `Viewport`는 부모 `Room_List` 영역 안에서 stretch 되도록 복구하고, 스크롤바가 차지하는 오른쪽/아래 여백만 offset으로 예약한다.
- `Content`는 상단 기준으로 가로 stretch 되도록 고정해 `VerticalLayoutGroup`이 방 아이템의 폭을 정상 계산하도록 했다.
- `VerticalLayoutGroup`을 런타임에서 끄지 않고 계속 활성화하며, 방 아이템의 가로 폭은 레이아웃 그룹이 담당하도록 정리했다.
- 방 아이템 높이, 간격, 패딩, 스크롤바 두께/마진, 수평 스크롤바 표시 여부를 `RoomListManager` 인스펙터 값으로 조절할 수 있게 했다.
- 임시로 추가했던 최소 폭 보정 및 수동 방 아이템 배치 코드는 제거했다.

### 관련 파일
- `Assets/Player/Script/UI/RoomListManager.cs`

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
