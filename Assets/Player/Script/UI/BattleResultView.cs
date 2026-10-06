using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BattlePvp.Networking;

namespace BattlePvp.UI
{
    public sealed class BattleResultBindings
    {
        public GameObject Panel;
        public TMP_Text Nickname, Rank, DamageTaken, DamageDealt, Winner, MostKilledBy, MostKilled;
        public TMP_Text RestartPrompt, Summary;
    }

    public sealed class BattleResultView
    {
        private readonly BattleResultBindings _bindings;
        private GameObject _canvas;
        private RectTransform _content, _rows;
        private TMP_Text _headline, _subtitle, _rank, _name, _character, _winner, _dealt, _taken, _rival, _nemesis, _kills, _count, _duration;
        private BattleResultRow[] _standings = Array.Empty<BattleResultRow>();
        private uint _localNetId;
        private readonly List<Canvas> _suppressed = new();
        private static readonly Color Cyan = new Color(.32f, .9f, .93f);
        private static readonly Color Gold = new Color(.95f, .84f, .56f);
        private static readonly Color Muted = new Color(.58f, .67f, .76f);

        public BattleResultView(BattleResultBindings bindings, BattleResultLabels labels) { _bindings = bindings; }
        public void SetStandings(BattleResultRow[] rows, uint localNetId, float duration)
        {
            _standings = rows ?? Array.Empty<BattleResultRow>();
            _localNetId = localNetId;
            EnsureLayout();
            _duration.text = TimeSpan.FromSeconds(Mathf.Max(0f, duration)).ToString(@"mm\:ss") + "  ·  MATCH COMPLETE";
        }
        public void Hide()
        {
            if (_bindings.Panel != null) _bindings.Panel.SetActive(false);
            if (_canvas != null) _canvas.SetActive(false);
            foreach (var canvas in _suppressed) if (canvas != null) canvas.enabled = true;
            _suppressed.Clear();
        }
        public void Dispose()
        {
            Hide();
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            _canvas = null;
        }
        public void Show(PersonalBattleResult result)
        {
            EnsureLayout();
            if (!_canvas.activeSelf)
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (canvas.gameObject != _canvas && canvas.enabled && canvas.renderMode != RenderMode.WorldSpace)
                    { _suppressed.Add(canvas); canvas.enabled = false; }
            if (_bindings.Panel != null) _bindings.Panel.SetActive(false);
            bool victory = result.Rank == 1;
            _headline.text = victory ? "VICTORY" : "DEFEATED";
            _headline.color = victory ? Gold : new Color(.95f, .49f, .69f);
            _subtitle.text = victory ? "마지막까지 살아남았습니다." : "전투는 끝났지만, 다음 기회가 있습니다.";
            _rank.text = "# " + result.Rank.ToString("00");
            Plain(_name, result.PlayerName);
            Plain(_winner, "이번 전투의 승자  " + result.WinnerName);
            _dealt.text = Number(result.DamageDealt); _taken.text = Number(result.DamageTaken);
            Plain(_rival, Opponent(result.MostKilled, result.MostKilledCount));
            Plain(_nemesis, Opponent(result.MostKilledBy, result.MostKilledByCount));
            foreach (Transform child in _rows) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            _count.text = _standings.Length.ToString("00") + " PLAYERS";
            _character.text = _standings.Length > 0 ? _standings.Length + "명 중 " + result.Rank + "위" : "나의 전투 결과";
            _kills.text = "전체 처치 기록";
            for (int i = 0; i < _standings.Length; i++)
            {
                var row = _standings[i];
                bool self = row.NetId == _localNetId;
                if (self)
                {
                    Plain(_character, row.CharacterName + " · " + _standings.Length + "명 중 " + result.Rank + "위");
                    _kills.text = "전체 " + row.Kills + "회 처치";
                }
                var panel = Box(_rows, "Rank " + row.Rank, 0, i * 55, 520, 49,
                    self ? new Color(.13f, .22f, .28f, .98f) : new Color(.035f, .075f, .12f, .8f));
                if (self) Box(panel, "Self accent", 0, 0, 3, 49, Cyan);
                Text(panel, "Rank", row.Rank.ToString("00"), 10, 9, 42, 30, 19, self ? Cyan : Muted);
                Plain(Text(panel, "Player", "", 60, 4, 245, 26, 20, Color.white, true), row.PlayerName + (self ? "   YOU" : ""));
                Plain(Text(panel, "Character", "", 60, 28, 245, 17, 12, Muted), row.CharacterName);
                Text(panel, "Kills", row.Kills.ToString(), 318, 9, 65, 30, 19, Color.white);
                Text(panel, "Damage", Number(row.Damage), 390, 9, 112, 30, 19, Muted, false, TextAlignmentOptions.Right);
            }
            _canvas.SetActive(true);
        }
        private void EnsureLayout()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("Battle Result Design", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var scaler = _canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = Box(_canvas.transform, "Arena shade", 0, 0, 1920, 1080, new Color(.02f, .04f, .07f, .82f));
            background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one; background.offsetMin = background.offsetMax = Vector2.zero;
            _content = new GameObject("Result composition", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_canvas.transform, false); _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(.5f, .5f); _content.sizeDelta = new Vector2(1920,1080);
            var mark=Box(_content,"Brand mark",80,66,25,25,Cyan); mark.localEulerAngles=new Vector3(0,0,-12);
            Text(_content,"Brand","BATTLE  PVP",121,55,500,45,30,Cyan,true);
            _duration=Text(_content,"Match time","MATCH COMPLETE",1270,60,540,35,18,Muted,false,TextAlignmentOptions.Right);
            Text(_content,"Eyebrow","—   BATTLE COMPLETE",100,192,600,30,19,Gold,true);
            _headline=Text(_content,"Headline","VICTORY",92,237,810,140,108,Gold,true); _headline.fontStyle |= FontStyles.Italic;
            _subtitle=Text(_content,"Subtitle","",100,375,810,50,29,Color.white);
            _rank=Text(_content,"Personal rank","# 01",100,450,230,130,102,Color.white,true);
            Box(_content,"Rank divider",347,464,2,114,new Color(.3f,.34f,.39f));
            Text(_content,"Result label","YOUR RESULT",380,465,360,25,17,Muted);
            _name=Text(_content,"Player name","",380,500,300,47,32,Color.white,true);
            _character=Text(_content,"Character","",380,554,300,36,20,Muted);
            _winner=Text(_content,"Winner","",100,640,750,40,21,Muted);
            var board=Box(_content,"Final standings",1240,170,580,600,new Color(.055f,.09f,.15f,.94f));
            Box(board,"Cyan rule",0,0,580,3,Cyan);
            Text(board,"Title","최종 순위",30,27,260,36,28,Color.white,true);
            _count=Text(board,"Count","",340,35,207,25,16,Muted,false,TextAlignmentOptions.Right);
            Text(board,"Rank column","순위",40,83,45,24,15,Muted);
            Text(board,"Name column","플레이어",90,83,245,24,15,Muted);
            Text(board,"Kills column","처치",348,83,65,24,15,Muted);
            Text(board,"Damage column","가한 피해",420,83,112,24,15,Muted,false,TextAlignmentOptions.Right);
            _rows=new GameObject("Standings rows",typeof(RectTransform)).GetComponent<RectTransform>();_rows.SetParent(board,false);Place(_rows,30,123,520,440);
            Text(board,"Footnote","처치 점수 기준  ·  BATTLE REPORT / 01",30,568,520,22,15,Muted);
            Box(_content,"Personal rule",100,816,1720,1,new Color(.21f,.26f,.32f));
            Text(_content,"Personal heading","나의 전투 기록  /  PERSONAL RECORD",100,838,900,27,20,Muted);
            _dealt=Stat(100,"가한 피해",Cyan,out _);_taken=Stat(505,"받은 피해",Color.white,out _);
            _rival=Stat(910,"가장 많이 처치한 상대",Color.white,out _kills);
            _nemesis=Stat(1380,"나를 가장 많이 처치한 상대",Color.white,out _);
            _rival.fontSize=_nemesis.fontSize=30;
            var action=Box(_content,"Return prompt",690,626,540,88,new Color(0,0,0,.8f));
            var button=action.gameObject.AddComponent<Button>(); action.GetComponent<Image>().raycastTarget=true;
            button.navigation=new Navigation{mode=Navigation.Mode.None};button.onClick.AddListener(()=>BattleStateMachine.Instance?.RequestRestartFromInput());
            Text(action,"Return action",BattleActionPrompt.ReturnToLobby,15,20,510,48,30,Color.white,true,TextAlignmentOptions.Center);
            Text(_content,"Return detail","같은 방 대기실로 나만 돌아갑니다",690,728,540,30,18,Muted,false,TextAlignmentOptions.Center);
            _canvas.SetActive(false);
        }
        private TMP_Text Stat(float x,string label,Color color,out TMP_Text detail)
        {
            Text(_content,label,label,x,900,400,25,20,Muted);
            var value=Text(_content,label+" Value","—",x,939,400,56,44,color,true);
            detail=Text(_content,label+" Detail","",x,1003,400,26,17,Muted);return value;
        }
        private static string Opponent(string name,int count) => count <= 0 ? "기록 없음" : name + "  " + count + "회";
        private static string Number(float value) => (float.IsFinite(value)?Mathf.Max(0,value):0).ToString("N0");
        private static void Plain(TMP_Text text,string value) => UserTextPresentation.SetPlain(text,value);
        private static RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color)
        {
            var rect=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();rect.SetParent(parent,false);Place(rect,x,y,w,h);
            var image=rect.GetComponent<Image>();image.color=color;image.raycastTarget=false;return rect;
        }
        private static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size,Color color,bool bold=false,TextAlignmentOptions align=TextAlignmentOptions.Left)
        {
            var text=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();text.transform.SetParent(parent,false);Place(text.rectTransform,x,y,w,h);
            text.font=BattleResultTheme.SharedFont;text.fontSize=size;text.color=color;text.raycastTarget=false;text.alignment=align;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;
            text.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;Plain(text,value);return text;
        }
        private static void Place(RectTransform rect,float x,float y,float w,float h)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
    }
}
