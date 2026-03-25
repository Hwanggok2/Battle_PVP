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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void CreatePopup(Vector3 position, float damage, bool isCritical = false)
        {
            if (_popupPrefab == null) return;

            // 약간의 랜덤 오프셋 부여
            Vector3 randomOffset = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.5f, 1.0f), Random.Range(-0.3f, 0.3f));
            DamagePopup popup = Instantiate(_popupPrefab, position + randomOffset, Quaternion.identity);
            popup.Setup(damage, isCritical);
        }
    }
}
