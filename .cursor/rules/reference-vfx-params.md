# 🎨 Reference: VFX & Shader Parameters (v1.0)
코드와 쉐이더 간의 데이터 바인딩을 위한 표준 파라미터 규격입니다.

## 1. 글로벌 쉐이더 프로퍼티 (Global Shader Properties)
- `_GlitchAmount (Float)`: 0.0 ~ 1.0. 몰빵 임계점 도달 및 스왑 시 노이즈 강도.
- `_StatColor (Color)`: 현재 아이덴티티의 주력 스탯 색상 (STR: Red, AGI: Green, CON: Yellow, DEF: Blue).
- `_EmissionPulse (Float)`: 글리치 아우라의 깜빡임 속도.

## 2. 전략가 전용 (Strategist Special)
- `_OverlapPercent (Float)`: 0.0 ~ 1.0. 최대 체력을 초과한 HP의 비율 (UI 및 캐릭터 글리치 연출용).
- `_ReassembleProgress (Float)`: 0.0 ~ 1.0. 1.5초 재조립 애니메이션 진행도.

## 3. 팔방미인 전용 (Polymath Special)
- `_MirrorActive (Int/Bool)`: 거울 모드 활성화 여부 (반전 효과 트리거).

## 4. 최적화 원칙
- 모든 파라미터는 `Shader.PropertyToID()`를 통해 캐싱하여 접근할 것.
- `MaterialPropertyBlock`을 사용하여 인스턴스별 개별 연출 최적화.

