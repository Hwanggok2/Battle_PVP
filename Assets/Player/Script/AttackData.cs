using UnityEngine;

[CreateAssetMenu(fileName = "NewAttack", menuName = "Combat/AttackData")]
public class AttackData : ScriptableObject
{
    public string animationName;      // 재생할 애니메이션 이름
    public float comboWindowStart;    // 콤보 입력을 받기 시작하는 시점 (0~1)
    public float comboWindowEnd;      // 콤보 입력이 마감되는 시점 (0~1)
    public float damage;              // 공격력 가중치
}