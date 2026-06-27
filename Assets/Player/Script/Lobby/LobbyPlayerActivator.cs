using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Lobby
{
    /// <summary>
    /// 로비 씬에서 플레이어 오브젝트가 비활성화되는 현상을 확실하게 방지하는 매니저입니다.
    /// 이 스크립트는 플레이어 본인이 아닌, '항상 활성화되어 있는 매니저 개체'에 부착해야 합니다.
    /// </summary>
    public sealed class LobbyPlayerActivator : MonoBehaviour
    {
        [Header("Target settings")]
        [Tooltip("로비에서 활성화 상태를 유지할 플레이어 오브젝트입니다. 비어 있으면 이름으로 찾습니다.")]
        [SerializeField] private GameObject _targetPlayer;
        [SerializeField] private GameObject _fallbackPlayerPrefab;
        [SerializeField] private string _playerNameInScene = "Player";
        [SerializeField] private Vector3 _fallbackSpawnPosition = Vector3.zero;

        private void Start()
        {
            if (SceneManager.GetActiveScene().name == "Lobby")
            {
                // Mirror 초기화 타임을 벌기 위해 프레임 지연 실행
                StartCoroutine(CoEnsurePlayerActive());
            }
        }

        private IEnumerator CoEnsurePlayerActive()
        {
            // Mirror의 Awake/Start 초기화 완료 시점까지 충분히 대기 (최대 3프레임)
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                TryActivate();
            }

            // 그 이후에도 혹시 꺼질 경우를 대비해 일정 시간마다 체크 (안전장치)
            float elapsed = 0f;
            while (elapsed < 2f)
            {
                yield return new WaitForSeconds(0.5f);
                TryActivate();
                elapsed += 0.5f;
            }
        }

        private void TryActivate()
        {
            if (_targetPlayer == null)
            {
                // 비활성화된 오브젝트도 찾기 위해 Find 대신 Transform 루프나 Resources 사용 고려
                // 여기서는 인스펙터 할당을 권장하지만, fallback으로 이름 기반 탐색 시도
                GameObject found = GameObject.Find(_playerNameInScene);
                if (found != null) _targetPlayer = found;
                else
                {
                    // 비활성 개체는 Find로 못 찾으므로 수동으로 찾기 시도
                    var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var t in allTransforms)
                    {
                        if (t.hideFlags == HideFlags.None && t.name == _playerNameInScene)
                        {
                            _targetPlayer = t.gameObject;
                            break;
                        }
                    }
                }
            }

            if (_targetPlayer != null && !_targetPlayer.activeSelf)
            {
                Debug.Log($"[LobbyPlayerActivator] Force Activating {_targetPlayer.name} in Lobby.");
                _targetPlayer.SetActive(true);
            }
            else if (_targetPlayer == null && _fallbackPlayerPrefab != null)
            {
                _targetPlayer = Instantiate(_fallbackPlayerPrefab, _fallbackSpawnPosition, Quaternion.identity);
                _targetPlayer.name = _playerNameInScene;
                Debug.LogWarning($"[LobbyPlayerActivator] Player was missing in Lobby. Spawned fallback prefab '{_fallbackPlayerPrefab.name}'.");
            }
        }
    }
}
