using System;

namespace BattlePvp.Stats
{
    /// <summary>
    /// Identity 변경 상태를 외부 시스템(UI/VFX/HUD)에 전달하기 위한 추상 소스 인터페이스.
    /// </summary>
    public interface IIdentitySource
    {
        /// <summary>
        /// 현재 판정된 Identity.
        /// </summary>
        Identity CurrentIdentity { get; }

        /// <summary>
        /// Identity 변경 이벤트.
        /// </summary>
        event Action<Identity> IdentityChanged;
    }
}

