using UnityEngine;

public class CombatStateBehavior : StateMachineBehaviour
{
    // 해당 상태(공격)에 진입할 때 실행
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 공격 중 이동 불가 설정
        animator.SendMessage("SetMovementLock", true);
    }

    // 해당 상태(공격)를 빠져나갈 때 실행
    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 공격 종료 후 이동 허용 및 콤보 초기화
        animator.SendMessage("OnAttackAnimationEnd");
        animator.SendMessage("SetMovementLock", false);
    }
}