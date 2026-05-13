using UnityEngine;
using UnityEngine.UI;
using System;

namespace BattlePvp.UI
{
    /// <summary>
    /// 전장(Battle 씬) 맵에 배치되어 모두가 공유하는 글로벌 타이머 UI입니다.
    /// 플레이어 프리팹과 독립적으로 씬 로드 시 초기화됩니다.
    /// </summary>
    public class BattleTimerUI : MonoBehaviour
    {
        public static BattleTimerUI Instance { get; private set; }

        [Header("UI References")]
        [Tooltip("남은 시간을 표시할 텍스트 컴포넌트")]
        [SerializeField] private Text _timerText;
        
        [Tooltip("상태 메시지(Get Ready, Fight 등)를 표시할 텍스트 컴포넌트")]
        [SerializeField] private Text _stateMessageText;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogWarning("[BattleTimerUI] Multiple instances detected. Destroying duplicate.");
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 초 단위 시간을 받아 mm:ss 포맷으로 UI에 출력합니다.
        /// </summary>
        public void UpdateTime(float remainingSeconds)
        {
            if (_timerText != null)
            {
                // 음수 방지
                float displayTime = Mathf.Max(0, remainingSeconds);
                TimeSpan time = TimeSpan.FromSeconds(displayTime);
                _timerText.text = time.ToString(@"mm\:ss");
            }
        }

        /// <summary>
        /// 카운트다운이나 매치 시작 메시지를 출력합니다.
        /// </summary>
        public void UpdateStateMessage(string message, bool active)
        {
            if (_stateMessageText != null)
            {
                _stateMessageText.text = message;
                _stateMessageText.gameObject.SetActive(active);
            }
        }
    }
}
