using UnityEngine;

[CreateAssetMenu(fileName = "NewAttack", menuName = "Combat/AttackData")]
public class AttackData : ScriptableObject
{
    [Header("Crosshair crossing (calibrated from the attack clip)")]
    [Range(0f, 1f)] public float aimCrossingPhase = .6f;
    [Tooltip("Blade point relative to the Spine pivot, expressed in the Spine parent's coordinates.")]
    public Vector3 aimBladePoint;
    public Vector3 aimBladeBase;
    public Vector3 aimBladeTip;
    public string animationName;      // 아아아아아
    public float comboWindowStart;    // �޺� �Է��� �ޱ� �����ϴ� ���� (0~1)
    public float comboWindowEnd;      // �޺� �Է��� �����Ǵ� ���� (0~1)
    public float damage;              // ���ݷ� ����ġ
}
