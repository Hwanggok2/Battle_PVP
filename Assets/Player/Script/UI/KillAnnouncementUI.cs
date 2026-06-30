using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    [DisallowMultipleComponent]
    public sealed class KillAnnouncementUI : MonoBehaviour
    {
        public static KillAnnouncementUI Instance { get; private set; }

        [Header("UI References")]
        [Tooltip("처치 알림들이 쌓일 부모 RectTransform입니다. 위치는 이 오브젝트로 직접 조절하세요.")]
        [SerializeField] private RectTransform _container;
        [Tooltip("처치 알림 한 줄 프리팹입니다. KillAnnouncementItemUI를 붙이면 닉네임/아이콘을 따로 표시할 수 있습니다.")]
        [SerializeField] private GameObject _itemPrefab;
        [SerializeField] private Sprite _defaultKillIcon;

        [Header("Behavior")]
        [SerializeField] private float _duration = 5f;
        [SerializeField] private float _spacing = 4f;
        [SerializeField] private bool _battleSceneOnly = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureContainer();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static void ShowGlobal(string killerName, string victimName)
        {
            if (string.IsNullOrWhiteSpace(killerName) && string.IsNullOrWhiteSpace(victimName))
                return;

            KillAnnouncementUI ui = Instance;
            if (ui == null)
                ui = FindFirstObjectByType<KillAnnouncementUI>(FindObjectsInactive.Include);

            if (ui == null)
                ui = CreateFallbackInstance();

            ui.Show(killerName, victimName);
        }

        public void Show(string killerName, string victimName)
        {
            if (string.IsNullOrWhiteSpace(killerName) && string.IsNullOrWhiteSpace(victimName))
                return;

            if (_battleSceneOnly && SceneManager.GetActiveScene().name != "Battle")
                return;

            RectTransform parent = EnsureContainer();
            if (parent == null)
                return;

            GameObject item = CreateItem(parent, killerName, victimName);
            if (item == null)
                return;

            item.SetActive(true);
            StartCoroutine(CoRemoveAfter(item, _duration));
        }

        private RectTransform EnsureContainer()
        {
            if (_container != null)
                return _container;

            _container = CreateDefaultContainer(transform);
            return _container;
        }

        private GameObject CreateItem(RectTransform parent, string killerName, string victimName)
        {
            GameObject itemObject;
            if (_itemPrefab != null)
            {
                itemObject = Instantiate(_itemPrefab, parent);
            }
            else
            {
                itemObject = new GameObject("KillAnnouncementItem", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                itemObject.transform.SetParent(parent, false);

                RectTransform rect = itemObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(900f, 36f);

                TextMeshProUGUI fallbackText = itemObject.GetComponent<TextMeshProUGUI>();
                fallbackText.alignment = TextAlignmentOptions.Center;
                fallbackText.fontSize = 32f;
                fallbackText.fontStyle = FontStyles.Bold;
                fallbackText.color = Color.white;
                fallbackText.raycastTarget = false;
                fallbackText.text = $"{NormalizeName(killerName)}  >  {NormalizeName(victimName)}";
                return itemObject;
            }

            KillAnnouncementItemUI itemUi = itemObject.GetComponent<KillAnnouncementItemUI>();
            if (itemUi != null)
            {
                itemUi.SetData(killerName, victimName, _defaultKillIcon);
                DisableRaycastTargets(itemObject);
                return itemObject;
            }

            TextMeshProUGUI text = itemObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
            {
                text.text = $"{NormalizeName(killerName)}  >  {NormalizeName(victimName)}";
                DisableRaycastTargets(itemObject);
                return itemObject;
            }

            Destroy(itemObject);
            return null;
        }

        private IEnumerator CoRemoveAfter(GameObject item, float seconds)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, seconds));

            if (item != null)
                Destroy(item);
        }

        private static KillAnnouncementUI CreateFallbackInstance()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("KillAnnouncementCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            GameObject uiObject = new GameObject("KillAnnouncementUI", typeof(RectTransform), typeof(KillAnnouncementUI));
            uiObject.transform.SetParent(canvas.transform, false);
            return uiObject.GetComponent<KillAnnouncementUI>();
        }

        private RectTransform CreateDefaultContainer(Transform parent)
        {
            GameObject containerObject = new GameObject("KillAnnouncementContainer", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            containerObject.transform.SetParent(parent, false);

            RectTransform rect = containerObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -56f);
            rect.sizeDelta = new Vector2(900f, 0f);

            VerticalLayoutGroup layout = containerObject.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = _spacing;

            ContentSizeFitter fitter = containerObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return rect;
        }

        private static string NormalizeName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        }

        private static void DisableRaycastTargets(GameObject root)
        {
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            foreach (Graphic graphic in graphics)
            {
                if (graphic != null)
                    graphic.raycastTarget = false;
            }
        }
    }
}
