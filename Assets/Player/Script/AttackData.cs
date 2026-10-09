using UnityEngine;

[CreateAssetMenu(fileName = "NewAttack", menuName = "Combat/AttackData")]
public class AttackData : ScriptableObject
{
    [Header("Crosshair crossing (calibrated from the attack clip)")]
    [Range(0f, 1f)] public float aimCrossingPhase = .6f;
    [Tooltip("Blade point relative to the Spine pivot; in player coordinates when aimInRootSpace is enabled, otherwise in Spine-parent coordinates.")]
    public Vector3 aimBladePoint;
    public Vector3 aimBladeBase;
    public Vector3 aimBladeTip;
    [Tooltip("Keep the strike reference in player coordinates while the authored hips turn.")]
    public bool aimInRootSpace;
    [System.Serializable] public sealed class VisualAim
    {
        public Avatar avatar;
        public float crossingPhase;
        public Vector3 bladeBase, bladeTip;
        public BattlePvp.Combat.MeleeMotionSample[] samples;
    }
    [HideInInspector] public VisualAim[] visualAim;
    public VisualAim FindVisualAim(Avatar avatar)
    {
        if (avatar != null && visualAim != null)
            foreach (var aim in visualAim) if (aim.avatar == avatar) return aim;
        return null;
    }
    public float CrossingPhase(Avatar avatar) => FindVisualAim(avatar)?.crossingPhase ?? aimCrossingPhase;
    [Range(0f, 180f)] public float maxAimCalibration = 180f;
    [HideInInspector] public BattlePvp.Combat.MeleeMotionSample[] motionSamples;
    public string animationName;      // 아아아아아
    public float comboWindowStart;    // �޺� �Է��� �ޱ� �����ϴ� ���� (0~1)
    public float comboWindowEnd;      // �޺� �Է��� �����Ǵ� ���� (0~1)
    public float damage;              // ���ݷ� ����ġ
}
