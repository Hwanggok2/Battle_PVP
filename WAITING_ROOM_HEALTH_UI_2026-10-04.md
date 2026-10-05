# 대기실 체력 UI 표시

## 완료 기준

- 전투 대기실에서도 기존 체력바와 현재/최대 체력 수치를 표시한다.
- 체력 소모·회복은 기존 로컬 플레이어 체력 이벤트 연결을 사용한다.
- HUD 크기·불투명도 설정을 그대로 적용하며, UI 재생성 후에도 표시 설정을 유지한다.

## 변경 내용

체력 데이터 연결은 이미 존재했지만 `HudVisibilitySettings`가 전투 장면에서만 체력 UI를 표시하고 있었다.

- `Assets/Player/Script/UI/HudVisibilitySettings.cs`: 대기실 표시 옵션을 직렬화하여 프리팹에 저장한다.
- `Assets/Prefabs/UI_Root.prefab`: `Health_Root`의 대기실 표시 옵션을 활성화한다. 플레이어 프리팹은 이 설정을 상속한다.
- `Assets/Remodel/Editor/RemodelUiBuilder.cs`: UI 재생성 시에도 체력 UI의 대기실 표시 옵션을 활성화한다.

## 검증

실행 중이던 Unity 플레이 모드를 종료하고 MCP를 통해 변경 사항을 가져와 컴파일했다. 임시 편집기 씬에 실제 `Player.prefab`을 복제하여 검사하고 검사 후 임시 씬을 제거했다.

- `Battle_waiting`, `Battle`: 체력 UI의 CanvasGroup alpha가 사용자 HUD 불투명도와 일치한다.
- `Lobby`, `Login`: 기존과 같이 체력 UI가 숨겨진다.
- `SetHp`에 100/100 → 70/100 → 90/100을 전달했을 때 텍스트와 체력바 비율(1 → 0.7 → 0.9)이 일치한다.
- Unity 컴파일 완료, 콘솔의 C# 컴파일 오류 0건.
- `git diff --check` 통과.

이번 검증은 편집기 내 프리팹 표시·갱신 검사다. 실제 네트워크 대기실 접속이나 새 Windows 빌드는 실행하지 않았다.
