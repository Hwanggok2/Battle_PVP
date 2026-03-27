using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>
    /// 데미지 팝업 프리팹을 생성하는 매니저입니다.
    /// </summary>
    public class DamagePopupManager : MonoBehaviour
    {
        public static DamagePopupManager Instance { get; private set; }
 
        [SerializeField] private DamagePopup _popupPrefab;
 
        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(gameObject);
        }
 
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void CreatePopup(Vector3 position, float damage, bool isCritical = false)
        {
            if (_popupPrefab == null)
            {
                Debug.LogError("[DamagePopupManager] Popup Prefab이 할당되지 않았습니다!");
                return;
            }

            // [수정] 랜덤 오프셋을 최소화하고 위쪽(Y축)으로만 살짝 띄웁니다.
            Vector3 spawnPos = position + new Vector3(0, 0.5f, 0); 
            DamagePopup popup = Instantiate(_popupPrefab, spawnPos, Quaternion.identity);
            popup.Setup(damage, isCritical);

            Debug.Log($"[DamagePopupManager] Popup spawned at {spawnPos} with damage {damage}");
        }
    }
}
