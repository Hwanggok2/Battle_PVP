# Battle 씬 입장 직후 점프 불가 문제

## 증상

- `Battle_waiting`, `Battle` 씬 입장 직후 점프가 바로 되지 않거나 여러 번 눌러야 동작했다.
- Space 입력은 들어오지만 실제 점프가 실행되지 않았다.
- 공격을 한 번 하면 이후 점프가 정상적으로 동작했다.

## 확인된 원인

입력, 스킬락, 점프락, UI 포커스 문제가 아니었다.

진단 로그에서 다음 상태가 확인됐다.

```text
queued: grounded=False, velocityY=-5~-10, lastGroundedAgo=Infinity
consume blocked: not grounded/coyote expired
```

즉 점프 입력은 정상적으로 큐에 들어갔지만, `CharacterController.isGrounded`가 false라 점프 요청이 소비되지 않았다.

씬의 `NetworkStartPosition`들은 대부분 `y=0`에 배치되어 있다. 반면 Player의 `CharacterController`는 center, height, skin 설정 때문에 씬 입장 직후 바닥에 완전히 붙지 않고 아주 살짝 떠 있는 상태로 시작할 수 있다.

이 상태에서는 `CharacterController.isGrounded`가 false이므로 Space를 눌러도 점프가 실행되지 않는다. 공격 후 버그가 풀린 것처럼 보였던 이유는 공격 자체가 점프를 고친 것이 아니라, 공격하는 동안 중력과 `CharacterController.Move()`가 여러 번 실행되어 캐릭터가 바닥에 내려앉았기 때문이다.

## 수정 방향

- 점프 입력 버퍼는 유지한다.
- 점프 가능 여부를 `CharacterController.isGrounded`에만 의존하지 않는다.
- 캐릭터 발밑 짧은 거리 안에 바닥이 있으면 grounded로 인정한다.
- 씬 입장 또는 이동 처리 시 바닥과 매우 가까우면 컨트롤러를 살짝 아래로 스냅한다.

## 관련 코드

- `Assets/Player/Script/PlayerManager.cs`
  - `_groundSnapDistance`
  - `IsGroundedForJump()`
  - `TryGetGroundDistance(...)`
  - `SnapToGroundIfClose()`
  - `TryConsumeJumpRequest()`
  - `ApplyMovement()`
