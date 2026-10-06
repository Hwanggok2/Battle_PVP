using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public static class BattleActionPrompt
    {
        public const string Respawn = "J를 눌러 부활하기";
        public const string ReturnToLobby = "J를 눌러 로비로 돌아가기";
        public static string RespawnStatus(int seconds) => seconds > 0
            ? $"부활까지 {seconds}초 남았습니다."
            : "부활할 준비가 되었습니다.";

        public static void Center(TMP_Text text, Vector2 size)
        {
            if (text == null) return;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            Style(text);
        }

        public static void Style(TMP_Text text)
        {
            if (text == null) return;
            text.color = Color.white;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            Transform parent = rect.parent;
            if (parent == null) return;
            string name = text.name + " Backplate";
            Transform existing = parent.Find(name);
            Image image = existing != null ? existing.GetComponent<Image>() : null;
            if (image == null)
            {
                var background = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                background.transform.SetParent(parent, false);
                background.GetComponent<LayoutElement>().ignoreLayout = true;
                image = background.GetComponent<Image>();
            }
            RectTransform backdrop = image.rectTransform;
            backdrop.anchorMin = rect.anchorMin;
            backdrop.anchorMax = rect.anchorMax;
            backdrop.pivot = rect.pivot;
            backdrop.anchoredPosition = rect.anchoredPosition;
            backdrop.sizeDelta = rect.sizeDelta + new Vector2(32f, 18f);
            int textIndex = rect.GetSiblingIndex();
            if (backdrop.GetSiblingIndex() < textIndex) textIndex--;
            backdrop.SetSiblingIndex(textIndex);
            image.color = new Color(0f, 0f, 0f, .8f);
            image.raycastTarget = false;
        }
    }
}
