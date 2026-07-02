using UnityEngine;

public sealed class SkillAnimationEventRelay : MonoBehaviour
{
    [SerializeField] private PlayerCombat _playerCombat;

    private PlayerCombat Combat
    {
        get
        {
            if (_playerCombat == null)
                _playerCombat = GetComponentInParent<PlayerCombat>();

            return _playerCombat;
        }
    }

    private void Awake()
    {
        if (_playerCombat == null)
            _playerCombat = GetComponentInParent<PlayerCombat>();
    }

    public void Initialize(PlayerCombat playerCombat)
    {
        _playerCombat = playerCombat;
    }

    public void EnableKickHitBox()
    {
        Combat?.EnableKickHitBox();
    }

    public void DisableKickHitBox()
    {
        Combat?.DisableKickHitBox();
    }

    public void EnableSkillHitBox()
    {
        Combat?.EnableKickHitBox();
    }

    public void DisableSkillHitBox()
    {
        Combat?.DisableKickHitBox();
    }

    public void OnSkillHitWindow()
    {
        Combat?.EnableKickHitBox();
    }
}
