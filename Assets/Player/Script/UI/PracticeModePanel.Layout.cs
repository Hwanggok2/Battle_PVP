using BattlePvp.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    public sealed partial class PracticeModePanel
    {
        private void BuildLayout()
        {
            if (gameObject.scene.name == "Battle")
            {
                var manager = (BattleNetworkManager)NetworkManager.singleton;
                var exit = Button("Exit practice", transform, "연습 종료", new Vector2(160, 44), Vector2.zero);
                Anchor(exit.transform, new Vector2(112, -104), new Vector2(0, 1));
                exit.onClick.AddListener(manager.StopPractice);
                var label = Text("Practice label", transform, $"연습모드 · AI {manager.PracticeBotCount}명", new Vector2(300, 34), Vector2.zero, 20);
                Anchor(label.transform, new Vector2(196, -152), new Vector2(0, 1));
                return;
            }
            var open = Button("Practice", transform, "AI 연습모드", new Vector2(240, 64), Vector2.zero);
            Anchor(open.transform, new Vector2(176, 240), Vector2.zero);
            var outline = open.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.15f, .8f, .87f); outline.effectDistance = new Vector2(1, -1);
            open.GetComponentInChildren<TMP_Text>().fontStyle = FontStyles.Bold;
            open.onClick.AddListener(Open);
            var overlay = RoomUiElements.Rect("Practice overlay", transform, Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.sizeDelta = Vector2.zero;
            var modalCanvas = overlay.gameObject.AddComponent<Canvas>();
            modalCanvas.overrideSorting = true; modalCanvas.sortingOrder = 220;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            overlay.gameObject.AddComponent<Image>().color = new Color(0, .015f, .025f, .8f);
            _overlay = overlay.gameObject;
            _dialog = RoomUiElements.Rect("Practice setup", overlay, new Vector2(1400, 820), Vector2.zero);
            _dialog.gameObject.AddComponent<Image>().color = new Color(.025f, .055f, .075f, .98f);
            _dialog.gameObject.AddComponent<ExpandedPanelLayout>();
            Text("Title", _dialog, "연습 전술 단말", new Vector2(650, 48), new Vector2(-345, 354), 32);
            Button("Close", _dialog, "닫기 · Esc", new Vector2(126, 44), new Vector2(608, 354)).onClick.AddListener(Close);
            Text("Map heading", _dialog, "전장 미리보기", new Vector2(530, 34), new Vector2(-405, 294), 22);
            _mapPreview = RoomUiElements.Rect("Map preview", _dialog, new Vector2(530, 251), new Vector2(-405, 145)).gameObject.AddComponent<RawImage>();
            _mapPreview.raycastTarget = false;
            var map = Button("Map", _dialog, "", new Vector2(410, 48), new Vector2(-405, -12));
            _mapLabel = map.GetComponentInChildren<TMP_Text>(); map.onClick.AddListener(() => ChangeMap(1));
            Button("Previous map", _dialog, "<", new Vector2(50, 48), new Vector2(-645, -12)).onClick.AddListener(() => ChangeMap(-1));
            Button("Next map", _dialog, ">", new Vector2(50, 48), new Vector2(-165, -12)).onClick.AddListener(() => ChangeMap(1));
            Text("Count label", _dialog, $"AI 수 · 최대 {BattleNetworkManager.MaxPracticeBots}명", new Vector2(530, 32), new Vector2(-405, -81), 22);
            _count = RoomUiElements.Input("AI count", _dialog, BattleResultTheme.SharedFont, "AI 수", new Vector2(140, 48), new Vector2(-465, -135));
            _count.contentType = TMP_InputField.ContentType.IntegerNumber; _count.characterLimit = 2;
            _count.onEndEdit.AddListener(_ => ReadCount());
            Button("Less", _dialog, "-", new Vector2(54, 48), new Vector2(-570, -135)).onClick.AddListener(() => AdjustCount(-1));
            Button("More", _dialog, "AI 추가 +", new Vector2(195, 48), new Vector2(-280, -135)).onClick.AddListener(() => AdjustCount(1));
            Text("Description", _dialog, "AI 추가 시 캐릭터·무기·직업·스킬을 무작위로 배정합니다.\n오른쪽 목록에서 AI를 골라 구성을 바꿔보세요.\n\n3분 자유 전투 · 사망 후 부활 가능\nAI 0명으로 시작하면 혼자 전장을 둘러볼 수 있습니다.",
                new Vector2(530, 140), new Vector2(-405, -251), 18);

            _rosterTitle = Text("Roster title", _dialog, "AI 참가자", new Vector2(240, 34), new Vector2(5, 294), 22);
            _roster = ScrollContent("AI roster", _dialog, new Vector2(240, 562), new Vector2(5, -14), out _rosterScroll);
            var editor = RoomUiElements.Rect("AI editor", _dialog, new Vector2(520, 626), new Vector2(420, 0));
            _editor = editor.gameObject;
            _editorTitle = Text("AI name", editor, "", new Vector2(520, 36), new Vector2(0, 293), 26);
            _character = Selector("Character", editor, 225, ShowCharacters);
            _weapon = Selector("Weapon", editor, 151, ShowWeapons);
            _job = Selector("Job", editor, 77, ShowJobs);
            _stats = Text("Stats", editor, "", new Vector2(510, 30), new Vector2(0, 34), 16);
            for (int slot = 0; slot < 2; slot++)
            {
                int selectedSlot = slot;
                _skills[slot] = Selector("Skill " + slot, editor, -27 - slot * 112, () => ShowSkills(selectedSlot));
                _skillDetails[slot] = Text("Skill detail " + slot, editor, "", new Vector2(506, 45), new Vector2(0, -79 - slot * 112), 16);
                _skillDetails[slot].overflowMode = TextOverflowModes.Ellipsis;
            }
            Button("Randomize AI", editor, "이 AI 무작위 변경", new Vector2(248, 48), new Vector2(-136, -267)).onClick.AddListener(() =>
            { if (Selected != null) { _bots[_selected] = PracticeBotSetup.Randomized(); RefreshBots(); } });
            Button("Remove AI", editor, "이 AI 삭제", new Vector2(248, 48), new Vector2(136, -267)).onClick.AddListener(RemoveSelected);
            _empty = Text("No AI", _dialog, "AI를 추가하면\n캐릭터·무기·스킬을 편집할 수 있습니다.", new Vector2(510, 180), new Vector2(420, 45), 24);
            _empty.alignment = TextAlignmentOptions.Center;
            var choices = RoomUiElements.Rect("Selection panel", _dialog, new Vector2(550, 645), new Vector2(420, -6));
            _choicePanel = choices.gameObject;
            choices.gameObject.AddComponent<Image>().color = new Color(.03f, .085f, .115f, 1);
            _choiceTitle = Text("Heading", choices, "", new Vector2(340, 40), new Vector2(-78, 283), 26);
            Button("Back", choices, "뒤로", new Vector2(110, 40), new Vector2(201, 283)).onClick.AddListener(() => _choicePanel.SetActive(false));
            _choices = ScrollContent("Options", choices, new Vector2(520, 530), new Vector2(0, -24), out _);
            _choicePanel.SetActive(false);
            Button("Cancel", _dialog, "취소", new Vector2(195, 52), new Vector2(-571, -362)).onClick.AddListener(Close);
            Button("Start practice", _dialog, "연습 시작", new Vector2(520, 52), new Vector2(420, -362)).onClick.AddListener(StartPractice);
            _status = Text("Status", _dialog, "", new Vector2(595, 52), new Vector2(-161, -362), 17);
            _overlay.SetActive(false);
        }
        private Button Selector(string name, Transform parent, float y, UnityEngine.Events.UnityAction action)
        {
            var button = Button(name, parent, "", new Vector2(520, 52), new Vector2(0, y));
            button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
            button.GetComponentInChildren<TMP_Text>().fontSize = 21;
            button.onClick.AddListener(action); return button;
        }
        private static Button Button(string name, Transform parent, string text, Vector2 size, Vector2 pos) =>
            RoomUiElements.Button(name, parent, BattleResultTheme.SharedFont, text, size, pos);
        private static TMP_Text Text(string name, Transform parent, string text, Vector2 size, Vector2 pos, float fontSize = 22) =>
            RoomUiElements.Text(name, parent, BattleResultTheme.SharedFont, text, size, pos, fontSize);
        private static void Anchor(Transform transform, Vector2 position, Vector2 anchor)
        { var rect = (RectTransform)transform; rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; }
        private static RectTransform ScrollContent(string name, Transform parent, Vector2 size, Vector2 pos, out ScrollRect scroll)
        {
            var viewport = RoomUiElements.Rect(name, parent, size, pos);
            viewport.gameObject.AddComponent<Image>().color = new Color(.02f, .045f, .065f, .8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = RoomUiElements.Rect("Content", viewport, Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8; layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.content = content; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            return content;
        }
    }
}
