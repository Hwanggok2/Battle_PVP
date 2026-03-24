using UnityEngine;

[CreateAssetMenu(fileName = "NewAttack", menuName = "Combat/AttackData")]
public class AttackData : ScriptableObject
{
    public string animationName;      // 아아아아아
    public float comboWindowStart;    // �޺� �Է��� �ޱ� �����ϴ� ���� (0~1)
    public float comboWindowEnd;      // �޺� �Է��� �����Ǵ� ���� (0~1)
    public float damage;              // ���ݷ� ����ġ
}