using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    // Existing scene references remain serialized on BattleStateMachine and are passed here once.
    public sealed class BattleResultBindings
    {
        public GameObject Panel;
        public TMP_Text Nickname, Rank, DamageTaken, DamageDealt, Winner, MostKilledBy, MostKilled;
        public TMP_Text RestartPrompt, Summary;
    }

    public sealed class BattleResultView
    {
        private readonly BattleResultBindings _bindings;
        private readonly BattleResultLabels _labels;
        private GameObject _runtimePanel;
        private GameObject _ownedCanvas;
        private TMP_Text _runtimeText;
        private bool _resolvedReferences;

        public BattleResultView(BattleResultBindings bindings, BattleResultLabels labels)
        {
            _bindings = bindings;
            _labels = labels;
        }

        public void Hide()
        {
            if (_bindings.Panel != null) _bindings.Panel.SetActive(false);
            if (_runtimePanel != null) _runtimePanel.SetActive(false);
        }

        public void Dispose()
        {
            if (_ownedCanvas != null) Object.Destroy(_ownedCanvas);
            else if (_runtimePanel != null) Object.Destroy(_runtimePanel);
            _ownedCanvas = _runtimePanel = null;
            _runtimeText = null;
        }

        public void Show(PersonalBattleResult result)
        {
            result = BattleResultText.ForRichText(result);
            string message = BattleResultText.Build(result, _labels);
            if (_bindings.Panel != null)
            {
                ResolveReferences();
                SetText(_bindings.Nickname, _labels.NicknamePrefix + result.PlayerName);
                SetText(_bindings.Rank, _labels.RankPrefix + result.Rank);
                SetText(_bindings.DamageTaken, _labels.DamageTakenPrefix + BattleResultText.FormatDamage(result.DamageTaken));
                SetText(_bindings.DamageDealt, _labels.DamageDealtPrefix + BattleResultText.FormatDamage(result.DamageDealt));
                SetText(_bindings.Winner, _labels.WinnerPrefix + result.WinnerName);
                SetText(_bindings.MostKilledBy, _labels.MostKilledByPrefix + BattleResultText.FormatOpponent(result.MostKilledBy, result.MostKilledByCount));
                SetText(_bindings.MostKilled, _labels.MostKilledPrefix + BattleResultText.FormatOpponent(result.MostKilled, result.MostKilledCount));
                SetText(_bindings.RestartPrompt, _labels.RestartPrompt);
                SetText(_bindings.Summary, message);
                _bindings.Panel.SetActive(true);
                return;
            }
            EnsureRuntimePanel();
            SetText(_runtimeText, message);
            _runtimePanel.SetActive(true);
        }

        private void EnsureRuntimePanel()
        {
            if (_runtimePanel != null) return;
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                _ownedCanvas = new GameObject("ResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = _ownedCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            _runtimePanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _runtimePanel.transform.SetParent(canvas.transform, false);
            RectTransform rect = _runtimePanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image background = _runtimePanel.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.72f);
            background.raycastTarget = true;

            var textObject = new GameObject("ResultText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(_runtimePanel.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(760f, 220f);
            _runtimeText = textObject.GetComponent<TextMeshProUGUI>();
            _runtimeText.alignment = TextAlignmentOptions.Center;
            _runtimeText.fontSize = 34;
            _runtimeText.color = Color.white;
            _runtimeText.raycastTarget = false;
        }

        private void ResolveReferences()
        {
            if (_resolvedReferences) return;
            _resolvedReferences = true;
            TMP_Text[] texts = _bindings.Panel.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (text == null) continue;
                string name = text.name.ToLowerInvariant();
                if (_bindings.Nickname == null && (name.Contains("nickname") || name.Contains("playername")))
                    _bindings.Nickname = text;
                else if (_bindings.Rank == null && name.Contains("rank")) _bindings.Rank = text;
                else if (_bindings.DamageTaken == null && (name.Contains("takedamage") || name.Contains("damage taken") || name.Contains("receiveddamage")))
                    _bindings.DamageTaken = text;
                else if (_bindings.DamageDealt == null && (name.Contains("hitdamage") || name.Contains("damage dealt") || name.Contains("dealtdamage")))
                    _bindings.DamageDealt = text;
                else if (_bindings.Winner == null && name.Contains("winner")) _bindings.Winner = text;
                else if (_bindings.MostKilledBy == null && (name.Contains("manydie") || name.Contains("killedby") || name.Contains("killed_by") || name.Contains("killed by") || name.Contains("defeatedby") || name.Contains("defeated_by") || name.Contains("defeated by")))
                    _bindings.MostKilledBy = text;
                else if (_bindings.MostKilled == null && (name.Contains("manykill") || name.Contains("mostkilled") || name.Contains("most_killed") || name.Contains("most killed") || name.Contains("mostdefeated") || name.Contains("most_defeated") || name.Contains("most defeated") || name.Contains("defeated")))
                    _bindings.MostKilled = text;
                else if (_bindings.RestartPrompt == null && (name.Contains("restart") || name.Contains("prompt")))
                    _bindings.RestartPrompt = text;
            }
            if (_bindings.Summary == null && _bindings.Nickname == null && _bindings.Rank == null &&
                _bindings.DamageTaken == null && _bindings.DamageDealt == null && _bindings.Winner == null &&
                _bindings.MostKilledBy == null && _bindings.MostKilled == null && texts.Length > 0)
                _bindings.Summary = texts[0];
        }

        private static void SetText(TMP_Text text, string value)
        {
            UserTextPresentation.SetRich(text, value);
        }
    }
}
