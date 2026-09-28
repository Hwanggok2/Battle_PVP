using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Lobby
{
    /// <summary>Offline targets use prefab instances so Mirror does not disable them as unspawned scene identities.</summary>
    public sealed class LobbyTrainingRange : MonoBehaviour
    {
        [SerializeField] private GameObject _dummyPrefab;
        private void Start()
        {
            if (SceneManager.GetActiveScene().name != "Lobby" || _dummyPrefab == null ||
                NetworkServer.active || NetworkClient.active) return;
            for (int i = 0; i < 3; i++)
            {
                var dummy = Instantiate(_dummyPrefab, transform);
                dummy.name = "Lobby Training Dummy " + (i + 1);
                dummy.transform.localPosition = new Vector3(-4 + i * 4, -.03f, 2);
                dummy.transform.localRotation = Quaternion.identity;
            }
        }
    }
}
