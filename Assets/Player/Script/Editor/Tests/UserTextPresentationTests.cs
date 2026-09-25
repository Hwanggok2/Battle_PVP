using System.Collections.Generic;
using System.Reflection;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BattlePvp.EditorTests
{
    public sealed class UserTextPresentationTests
    {
        private static readonly string[] Payloads =
        {
            "<size=500%>Huge</size>",
            "</noparse><color=red>Red</color>",
            "</NOPARSE><b>Bold</b>",
            "<style=Title>Title</style><br>Next",
            "<a href=\"bad\">Link</a><link=bad>Other</link>",
            "<sprite=0><space=999><pos=500>X",
            "<<</b><i>X</i>>",
            @"\n\r\t\v\u000A\U0000000A",
            @"\u003Csize=500%\u003EBig\U0000003Cbr>",
            @"C:\users\name\\<br>\"
        };

        private readonly List<GameObject> _extraObjects = new List<GameObject>();
        private GameObject _root;
        private TMP_FontAsset _font;

        [SetUp]
        public void SetUp()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            Assert.That(_font, Is.Not.Null, "Exercise the installed TMP renderer with an existing project font.");
            _root = new GameObject("User text rendering fixture", typeof(Canvas));
            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject extra in _extraObjects)
                if (extra != null) Object.DestroyImmediate(extra);
            _extraObjects.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [TestCaseSource(nameof(Payloads))]
        public void PlainUserTextRendersLiterallyWithoutChangingAuthoredStyle(string payload)
        {
            TextMeshProUGUI text = CreateText("Plain");
            text.fontStyle = FontStyles.Italic;
            UserTextPresentation.SetPlain(text, payload);

            Assert.That(Render(text), Is.EqualTo(payload));
            Assert.That(text.richText, Is.False);
            Assert.That(text.parseCtrlCharacters, Is.True);
            Assert.That(text.textInfo.lineCount, Is.EqualTo(1));
            Assert.That(text.textInfo.linkCount, Is.Zero);
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                Assert.That(text.textInfo.characterInfo[i].pointSize, Is.EqualTo(24f));
                Assert.That(text.textInfo.characterInfo[i].style, Is.EqualTo(FontStyles.Italic));
                Assert.That(text.textInfo.characterInfo[i].color, Is.EqualTo((Color32)Color.white));
            }
        }

        [TestCaseSource(nameof(Payloads))]
        public void RichResultKeepsDeveloperMarkupAndRendersUserTagsLiterally(string payload)
        {
            TextMeshProUGUI text = CreateText("Rich");
            UserTextPresentation.SetRich(text,
                "<b><color=#00FF00>Label</color></b> " + UserDisplayText.EscapeRich(payload) +
                "<b><color=#0000FF> Tail</color></b>");

            Assert.That(Render(text), Is.EqualTo("Label " + payload + " Tail"));
            Assert.That(text.richText, Is.True);
            Assert.That(text.textInfo.lineCount, Is.EqualTo(1));
            Assert.That(text.textInfo.linkCount, Is.Zero);
            Assert.That(text.textInfo.characterInfo[0].style & FontStyles.Bold, Is.EqualTo(FontStyles.Bold));
            Assert.That(text.textInfo.characterInfo[0].color, Is.EqualTo((Color32)Color.green));
            for (int i = 6; i < 6 + payload.Length; i++)
            {
                Assert.That(text.textInfo.characterInfo[i].pointSize, Is.EqualTo(24f));
                Assert.That(text.textInfo.characterInfo[i].style, Is.EqualTo(FontStyles.Normal));
                Assert.That(text.textInfo.characterInfo[i].color, Is.EqualTo((Color32)Color.white));
            }
            int last = text.textInfo.characterCount - 1;
            Assert.That(text.textInfo.characterInfo[last].style & FontStyles.Bold, Is.EqualTo(FontStyles.Bold));
            Assert.That(text.textInfo.characterInfo[last].color, Is.EqualTo((Color32)Color.blue));
        }

        [Test]
        public void BoundResultSummaryAndFieldsEscapeOnceAndPreserveAllWinners()
        {
            const string name = @"</noparse><b>X</b>\u000A";
            string winners = string.Join(", ", new string[8]
            {
                new string('A', 64), new string('B', 64), new string('C', 64), new string('D', 64),
                new string('E', 64), new string('F', 64), new string('G', 64), new string('H', 64)
            });
            var result = new PersonalBattleResult(name, 1, winners, 12.5f, 50f, "<br>Enemy", 2, "Victim\nNext", 3);
            var bindings = new BattleResultBindings
            {
                Panel = _root, Nickname = CreateText("Nickname"), Winner = CreateText("Winner"),
                MostKilledBy = CreateText("KilledBy"), MostKilled = CreateText("MostKilled"),
                Summary = CreateText("Summary"), RestartPrompt = CreateText("Prompt")
            };
            var view = new BattleResultView(bindings, Labels());
            view.Show(result);
            Assert.That(Render(bindings.Nickname), Is.EqualTo("Name: " + name));
            Assert.That(Render(bindings.Winner), Is.EqualTo("Winners: " + winners));
            Assert.That(Render(bindings.MostKilledBy), Is.EqualTo("By: <br>Enemy (2)"));
            Assert.That(Render(bindings.MostKilled), Is.EqualTo("Against: Victim Next (3)"));
            string summary = Render(bindings.Summary);
            Assert.That(summary, Does.StartWith("Name: " + name + "\nRank: 1\nTaken: 12.5\nDealt: 50"));
            Assert.That(summary, Does.Contain("Winners: " + winners));
            Assert.That(summary, Does.EndWith("Against: Victim Next (3)\n\nRestart"));
            Assert.That(Render(bindings.RestartPrompt), Is.EqualTo("Restart"));
            Assert.That(result.PlayerName, Is.EqualTo(name));
            Assert.That(result.MostKilled, Is.EqualTo("Victim\nNext"), "Display normalization must not mutate result data.");

            view.Hide();
            view.Show(result);
            Assert.That(Render(bindings.Nickname), Is.EqualTo("Name: " + name), "Repeated show must not escape an already escaped copy.");
        }

        [Test]
        public void RuntimeResultFallbackUsesTheSameLiteralDisplayBoundary()
        {
            var view = new BattleResultView(new BattleResultBindings(), Labels());
            view.Show(new PersonalBattleResult("<br>Player", 2, @"Winner\u000A", 1, 2, null, 0, null, 0));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            _extraObjects.Add((GameObject)typeof(BattleResultView).GetField("_runtimePanel", flags).GetValue(view));
            _extraObjects.Add((GameObject)typeof(BattleResultView).GetField("_ownedCanvas", flags).GetValue(view));
            var text = (TMP_Text)typeof(BattleResultView).GetField("_runtimeText", flags).GetValue(view);
            Configure(text);
            string rendered = Render(text);
            Assert.That(rendered, Does.StartWith("Name: <br>Player\nRank: 2"));
            Assert.That(rendered, Does.Contain(@"Winners: Winner\u000A"));
            Assert.That(rendered, Does.EndWith("By: None (0)\nAgainst: None (0)\n\nRestart"));
        }

        private TextMeshProUGUI CreateText(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            Configure(text);
            return text;
        }

        private void Configure(TMP_Text text)
        {
            text.font = _font;
            text.fontSize = 24f;
            text.fontStyle = FontStyles.Normal;
            text.color = Color.white;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(50000f, 4000f);
        }

        private static string Render(TMP_Text text)
        {
            text.ForceMeshUpdate(true, true);
            return text.GetParsedText();
        }

        private static BattleResultLabels Labels() => new BattleResultLabels
        {
            NicknamePrefix = "<b>Name:</b> ", RankPrefix = "Rank: ", DamageTakenPrefix = "Taken: ",
            DamageDealtPrefix = "Dealt: ", WinnerPrefix = "Winners: ",
            MostKilledByPrefix = "By: ", MostKilledPrefix = "Against: ", RestartPrompt = "<color=green>Restart</color>"
        };
    }
}
