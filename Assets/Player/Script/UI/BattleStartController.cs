using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Mirror;

namespace BattlePvp.UI
{
    [RequireComponent(typeof(Button))]
    public class BattleStartController : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("카운트다운을 표시할 텍스트 컴포넌트입니다. 할당하지 않으면 자식에서 자동으로 찾습니다.")]
        [SerializeField] private Text _countdownText;

        [Header("Settings")]
        [Tooltip("카운트다운 시간 (초)")]
        [SerializeField] private float _countdownDuration = 5f;
        [Tooltip("이동할 씬의 이름")]
        [SerializeField] private string _battleSceneName = "Battle";

        private Button _button;
        private bool _isCountingDown = false;
        private bool _isSceneTransitioning = false;
        private string _originalText = "Start";

        private void Awake()
        {
            _button = GetComponent<Button>();
            if (_button != null)
            {
                _button.onClick.AddListener(OnStartButtonClick);
            }
            
            // Text가 할당되지 않았다면 자식 오브젝트에서 찾기 시도
            if (_countdownText == null)
            {
                _countdownText = GetComponentInChildren<Text>();
            }

            if (_countdownText != null)
            {
                _originalText = _countdownText.text;
            }
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnStartButtonClick);
            }
        }

        private void OnStartButtonClick()
        {
            if (_isCountingDown) return;

            // Mirror 환경 체크: 네트워크가 활성화되어 있다면 호스트(서버)인지 확인
            if (NetworkManager.singleton != null && NetworkClient.active)
            {
                if (!NetworkServer.active)
                {
                    Debug.LogWarning("[BattleStartController] 오직 방장(Host)만 게임을 시작할 수 있습니다.");
                    if (_countdownText != null)
                    {
                        _countdownText.text = "Host Only";
                        StartCoroutine(CoResetTextAfterDelay(2f));
                    }
                    return;
                }
            }

            StartCoroutine(CoStartCountdown());
        }

        private IEnumerator CoStartCountdown()
        {
            _isCountingDown = true;
            if (_button != null) _button.interactable = false;

            float remainingTime = _countdownDuration;

            while (remainingTime > 0)
            {
                if (_countdownText != null)
                {
                    _countdownText.text = Mathf.CeilToInt(remainingTime).ToString();
                }

                yield return new WaitForSeconds(1f);
                remainingTime -= 1f;
            }

            if (_countdownText != null)
            {
                _countdownText.text = "Starting...";
            }

            // 이미 목표 씬이거나 전환 중이라면 중복 실행 방지
            if (_isSceneTransitioning || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == _battleSceneName)
            {
                _isCountingDown = false;
                yield break;
            }

            _isSceneTransitioning = true;

            // 씬 전환 처리
            if (NetworkManager.singleton != null && NetworkServer.active)
            {
                NetworkManager.singleton.ServerChangeScene(_battleSceneName);
            }
            else
            {
                SceneManager.LoadScene(_battleSceneName);
            }

            _isCountingDown = false;
        }

        private IEnumerator CoResetTextAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_countdownText != null && !_isCountingDown)
            {
                _countdownText.text = _originalText;
            }
        }
    }
}
