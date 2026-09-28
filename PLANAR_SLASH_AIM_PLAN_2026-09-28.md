# 참격 평면·조준 보정 계획

## 조사와 선택

- Unity VFX 개발자의 [Smooth Sword Trails 구현 설명](https://discussions.unity.com/t/smooth-sword-trails-with-vfx-graph/1642415): 프레임 사이 베지어 곡선 보간과 균일 분할, 또는 미리 만든 궤적 메시를 사용하는 접근을 설명한다. 분할 수만 늘리면 애니메이션 자체의 흔들림은 그대로 남는다.
- 실제 VFX 제작자의 [Venom Slash 제작 과정](https://realtimevfx.com/t/venom-slash-breakdown-of-the-effect-included/18903): 정돈된 링 메시와 알파 마스크로 참격 형태를 만든다.
- Epic의 [Aim Offset 공식 문서](https://dev.epicgames.com/documentation/unreal-engine/aim-offset-in-unreal-engine): 기본 애니메이션 위에 조준 자세를 가산한다. 좌우는 캐릭터 방향으로, 상하는 상체 보정으로 처리할 수 있다.
- Unity [Multi-Aim](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.4/manual/constraints/MultiAimConstraint.html)과 [Override Transform](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.4/manual/constraints/OverrideTransform.html)을 검토했다. 단순히 검을 타깃에 고정하는 IK는 휘두르는 동작을 없앨 수 있으므로, 기존 검 애니메이션을 유지하는 가산 회전으로 진행한다.

현재 프로젝트에 대한 판단: 콤보마다 방향을 정돈한 평면 원호 메시와, 조준 방향에 따른 상체의 가산 회전을 조합한다. 별도 VFX Graph 패키지나 외부 유료 에셋은 추가하지 않는다.

실행 중 검날 양 끝을 샘플링한 결과, 실제 참격은 준비 동작 이후의 짧은 구간에 집중되어 있었다. 따라서 등속 재생 대신 콤보별 진행 곡선을 작성하고, 빠른 구간은 각도 기준으로 추가 분할한다. 교체된 애니메이션을 사용하게 되면 이 곡선도 다시 조정해야 한다.

## 완료 기준

1. 한 참격의 모든 면이 같은 평면에 있고, 프레임 속도·손목 흔들림에 따라 접히거나 지그재그가 되지 않는다.
2. 공격별 하향·상향·횡방향 차이는 유지한다. 검날 길이, 파란색 알파 0.3, 면 간 20% 중복 기준은 유지한다.
3. 공격 시작부터 끝까지 곡선이 진행된다. 이동·시선 변경에는 참격 전체의 기준 좌표계를 적용해 이미 생긴 면이 뒤틀리지 않게 한다.
4. 상하·좌우 조준 시 기존 공격 동작 위에 방향 보정이 적용된다. 로비 궤도 카메라가 캐릭터를 억지로 돌리지 않는다.
5. 로컬·원격·서버가 같은 공격 방향을 사용한다. 공격 판정의 활성 구간과 서버 피해 검증을 유지한다.
6. 종료·취소·사망 후 조준 보정이 누적되거나 자세가 남지 않는다. 이전 공격의 네트워크 갱신이 다음 공격에 적용되지 않는다.
7. Unity MCP로 실제 모델과 콤보, 조준 각도를 확인하고 관련 회귀 테스트와 결과를 별도 기록한다. 웹 빌드로 테스트하지 않는다.

## 확인한 원인

- `BlockAttackVfx`는 매 프레임 실제 검날 양 끝을 그대로 연결해 손목의 방향 전환·깊이 흔들림을 모두 메시로 만든다.
- `PlayerCombat`은 조준 방향을 `MeleeHitBox`에는 전달하지만 검 공격 상체 애니메이션에는 전달하지 않는다.
- 전투의 좌우 몸통 방향은 `PlayerManager`가 카메라 yaw로 맞추지만 pitch에 대응하는 상체 보정이 없다.
