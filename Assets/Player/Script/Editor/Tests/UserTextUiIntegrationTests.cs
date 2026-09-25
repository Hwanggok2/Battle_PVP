using System;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Managers;
using BattlePvp.Networking;
using BattlePvp.Stats;
using BattlePvp.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.EditorTests
{
    public sealed class UserTextUiIntegrationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Name = "<size=500%>홍길동</size>\\u0041\n끝";
        private const string PlainName = "<size=500%>홍길동</size>\\\\u0041 끝";
        private const string RoomName = "<br>한국방\\n\r호스트\u2028A";
        private const string PlainRoomName = "<br>한국방\\\\n 호스트 A";
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            for (int i = _assets.Count - 1; i >= 0; i--)
                if (_assets[i] != null) UnityEngine.Object.DestroyImmediate(_assets[i]);
            _assets.Clear();
        }

        [Test]
        public void RankingSetDataUsesPlainDisplayWhileKeepingRawNameAndNumericCaches()
        {
            GameObject root = NewObject("Ranking fixture");
            RankingEntryUI entry = root.AddComponent<RankingEntryUI>();
            TextMeshProUGUI rank = Label("Rank", root.transform);
            TextMeshProUGUI name = Label("Name", root.transform);
            TextMeshProUGUI score = Label("Score", root.transform);
            TextMeshProUGUI deaths = Label("Deaths", root.transform);
            Set(entry, "rankText", rank);
            Set(entry, "nameText", name);
            Set(entry, "scoreText", score);
            Set(entry, "deathText", deaths);

            entry.SetData(2, Name, 17, 4);
            AssertPlain(name, PlainName);
            Assert.That(rank.text, Is.EqualTo("2"));
            Assert.That(score.text, Is.EqualTo("17"));
            Assert.That(deaths.text, Is.EqualTo("4"));
            Assert.That(Get<string>(entry, "_playerName"), Is.EqualTo(Name));
            entry.SetData(2, Name, 18, 4);
            AssertPlain(name, PlainName);
            Assert.That(score.text, Is.EqualTo("18"));
            Assert.That(Get<string>(entry, "_playerName"), Is.EqualTo(Name));
        }

        [Test]
        public void RoomSetInfoEscapesOnlyLabelsAndPreservesSelectionAndDeletionIds()
        {
            const string roomId = "battle_abc123_11111111111111111111111111111111";
            FieldInfo selection = typeof(RoomListItem).GetField("_selectedItem", BindingFlags.Static | BindingFlags.NonPublic);
            object previousSelection = selection.GetValue(null);
            selection.SetValue(null, null);
            try
            {
                GameObject root = NewObject("Room row fixture");
                root.AddComponent<Image>();
                RoomListItem item = root.AddComponent<RoomListItem>();
                TextMeshProUGUI room = Label("Room", root.transform);
                TextMeshProUGUI master = Label("Master", root.transform);
                TextMeshProUGUI count = Label("Count", root.transform);
                Button delete = NewObject("Delete", root.transform).AddComponent<Button>();
                Set(item, "_roomNameText", room);
                Set(item, "_masterNameText", master);
                Set(item, "_playerCountText", count);
                Set(item, "_deleteButton", delete);
                string selected = null, deleted = null;

                item.SetInfo(roomId, RoomName, Name, 7, id => selected = id, id => deleted = id, true);
                AssertPlain(room, PlainRoomName);
                AssertPlain(master, "Master : " + PlainName);
                Assert.That(count.text, Is.EqualTo("Player : 7"));
                Assert.That(Get<string>(item, "_roomName"), Is.EqualTo(RoomName));
                Assert.That(Get<string>(item, "_masterName"), Is.EqualTo(Name));
                Invoke(item, "OnItemClicked");
                delete.onClick.Invoke();
                Assert.That(selected, Is.EqualTo(roomId));
                Assert.That(deleted, Is.EqualTo(roomId));
                Assert.That(item.IsSelected, Is.True);
                Assert.That(delete.gameObject.activeSelf, Is.True);

                item.SetInfo(roomId, RoomName, Name, 8, id => selected = id, null, false);
                AssertPlain(room, PlainRoomName);
                Assert.That(count.text, Is.EqualTo("Player : 8"));
                Assert.That(item.IsSelected, Is.True);
                Assert.That(delete.gameObject.activeSelf, Is.False);
            }
            finally { selection.SetValue(null, previousSelection); }
        }

        [Test]
        public void KillItemSetDataUsesPlainNamesAndPreservesUnspecifiedIconState()
        {
            GameObject root = NewObject("Kill item fixture");
            KillAnnouncementItemUI item = root.AddComponent<KillAnnouncementItemUI>();
            TextMeshProUGUI killer = Label("Killer", root.transform);
            TextMeshProUGUI victim = Label("Victim", root.transform);
            Image icon = NewObject("Icon", root.transform).AddComponent<Image>();
            Sprite original = NewSprite();
            Sprite replacement = NewSprite();
            icon.sprite = original;
            icon.enabled = false;
            icon.color = Color.cyan;
            Set(item, "_killerNameText", killer);
            Set(item, "_victimNameText", victim);
            Set(item, "_killIcon", icon);

            item.SetData(Name, "<sprite=4>적\\n\t이름");
            AssertPlain(killer, PlainName);
            AssertPlain(victim, "<sprite=4>적\\\\n 이름");
            Assert.That(icon.sprite, Is.SameAs(original));
            Assert.That(icon.enabled, Is.False);
            Assert.That(icon.color, Is.EqualTo(Color.cyan));
            item.SetData(null, "\r\n\t", replacement);
            AssertPlain(killer, "Unknown");
            AssertPlain(victim, "Unknown");
            Assert.That(icon.sprite, Is.SameAs(replacement));
            Assert.That(icon.enabled, Is.False);
            Assert.That(icon.color, Is.EqualTo(Color.cyan));
        }

        [Test]
        public void KillFeedFallbackCreateItemUsesPlainCombinedNames()
        {
            GameObject root = NewObject("Kill feed fallback fixture");
            KillAnnouncementUI feed = root.AddComponent<KillAnnouncementUI>();
            GameObject item = (GameObject)Invoke(feed, "CreateItem", (RectTransform)root.transform, Name, "<b>적</b>\\n");
            AssertPlain(item.GetComponent<TextMeshProUGUI>(), PlainName + "  >  <b>적</b>\\\\n");
            Assert.That(item.transform.parent, Is.SameAs(root.transform));
            Assert.That(item.GetComponent<TextMeshProUGUI>().raycastTarget, Is.False);
        }

        [Test]
        public void KillFeedGenericTmpPrefabUsesPlainNamesWithoutChangingItsTemplate()
        {
            GameObject root = NewObject("Kill feed prefab fixture");
            KillAnnouncementUI feed = root.AddComponent<KillAnnouncementUI>();
            GameObject prefab = NewObject("Plain TMP template");
            TextMeshProUGUI template = Label("Template name", prefab.transform);
            template.text = "<b>원본 프리팹</b>\\n";
            template.raycastTarget = true;
            Set(feed, "_itemPrefab", prefab);

            GameObject item = (GameObject)Invoke(feed, "CreateItem", (RectTransform)root.transform, Name, "<b>적</b>\\n");
            TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>(true);
            AssertPlain(text, PlainName + "  >  <b>적</b>\\\\n");
            Assert.That(text.raycastTarget, Is.False);
            Assert.That(template.text, Is.EqualTo("<b>원본 프리팹</b>\\n"));
            Assert.That(template.richText, Is.True);
            Assert.That(template.parseCtrlCharacters, Is.False);
            Assert.That(template.raycastTarget, Is.True);
        }

        [Test]
        public void ChatMessageCommitKeepsMessagesOnSeparateLinesAndDoesNotRewriteTheInput()
        {
            GameObject root = NewObject("Chat fixture");
            BattleChatUI chat = root.AddComponent<BattleChatUI>();
            TextMeshProUGUI log = Label("Log", root.transform);
            ScrollRect scroll = NewObject("Scroll", root.transform).AddComponent<ScrollRect>();
            TMP_InputField input = NewObject("Input", root.transform).AddComponent<TMP_InputField>();
            input.SetTextWithoutNotify("편집 중 <i>draft</i>\\n");
            input.richText = true;
            Set(chat, "_logText", log);
            Set(chat, "_scrollRect", scroll);
            Set(chat, "_input", input);
            Set(chat, "_maxLines", 2);
            const string message = "첫째\r\n둘째\t셋째\u2028넷째\\n<size=9>끝</size>";
            Invoke(chat, "AddMessage", Name, message, 1d);
            Invoke(chat, "AddMessage", "두번째", "후속 메시지", 2d);
            Queue<string> cache = Get<Queue<string>>(chat, "_lines");
            CollectionAssert.AreEqual(new[]
            {
                "[<size=500%>홍길동</size>\\u0041 끝] 첫째  둘째 셋째 넷째\\n<size=9>끝</size>",
                "[두번째] 후속 메시지"
            }, cache.ToArray());
            // Commit actual log text without rebuilding every Canvas in the editor's open scenes.
            Set(chat, "_scrollRect", null);
            Invoke(chat, "LateUpdate");
            AssertPlain(log, "[" + PlainName + "] 첫째  둘째 셋째 넷째\\\\n<size=9>끝</size>\n[두번째] 후속 메시지");
            Assert.That(log.text.Split('\n').Length, Is.EqualTo(2));
            Assert.That(input.text, Is.EqualTo("편집 중 <i>draft</i>\\n"));
            Assert.That(input.richText, Is.True);
            Assert.That(Get<bool>(chat, "_logDirty"), Is.False);

            Set(chat, "_scrollRect", scroll);
            Invoke(chat, "AddMessage", "세번째", "새 메시지", 3d);
            Set(chat, "_scrollRect", null);
            Invoke(chat, "LateUpdate");
            AssertPlain(log, "[두번째] 후속 메시지\n[세번째] 새 메시지");
            Assert.That(cache.Count, Is.EqualTo(2));
        }

        [Test]
        public void RoomBannerMetadataUsesPlainNamesWhileKeepingLiveOccupancySeparate()
        {
            var previousScores = new List<ScoreSystem>(ScoreSystem.ActiveScores);
            ScoreSystem.ActiveScores.Clear();
            try
            {
                GameObject root = NewObject("Room banner fixture");
                BattleRoomInfoBanner banner = root.AddComponent<BattleRoomInfoBanner>();
                TextMeshProUGUI room = Label("Room", root.transform);
                TextMeshProUGUI master = Label("Master", root.transform);
                TextMeshProUGUI count = Label("Count", root.transform);
                Set(banner, "_roomNameText", room);
                Set(banner, "_masterNameText", master);
                Set(banner, "_playerCountText", count);
                var metadata = new PlayFabBattleManager.RoomInfo(RoomName, Name, 8);

                Invoke(banner, "SetInfo", metadata);
                AssertPlain(room, "Room: " + PlainRoomName);
                AssertPlain(master, "Master: " + PlainName);
                Assert.That(count.text, Is.EqualTo("Players: 0"));
                Assert.That(metadata.RoomName, Is.EqualTo(RoomName));
                Assert.That(metadata.MasterName, Is.EqualTo(Name));
            }
            finally
            {
                ScoreSystem.ActiveScores.Clear();
                ScoreSystem.ActiveScores.AddRange(previousScores);
            }
        }

        [Test]
        public void CharacterInfoNicknameUsesPlainDisplayWithoutRewritingProfileData()
        {
            FieldInfo instance = typeof(GlobalDataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo quitting = typeof(GlobalDataManager).GetField("_applicationIsQuitting", BindingFlags.Static | BindingFlags.NonPublic);
            object previousInstance = instance.GetValue(null), previousQuitting = quitting.GetValue(null);
            CharacterInfoController info = null;
            try
            {
                GlobalDataManager profile = NewObject("Profile fixture").AddComponent<GlobalDataManager>();
                Set(profile, "_playerNickname", Name);
                instance.SetValue(null, profile);
                quitting.SetValue(null, false);
                GameObject root = NewObject("Character info fixture");
                info = root.AddComponent<CharacterInfoController>();
                StatManager stats = NewObject("Inactive stats fixture").AddComponent<StatManager>();
                Set(info, "_statManager", stats);
                TextMeshProUGUI nickname = Label("Nickname", root.transform);
                Set(info, "_loginIdText", nickname);
                foreach (string field in new[] { "_atkText", "_defRateText", "_maxHpText", "_peneText", "_regenText", "_moveSpdText", "_atkSpdText" })
                    Set(info, field, Label(field, root.transform));

                info.UpdateStatsDisplay();
                AssertPlain(nickname, PlainName);
                Assert.That(profile.PlayerNickname, Is.EqualTo(Name));
            }
            finally
            {
                // UpdateStatsDisplay binds the injected StatManager even while this fixture stays inactive.
                if (info != null) Invoke(info, "OnDisable");
                instance.SetValue(null, previousInstance);
                quitting.SetValue(null, previousQuitting);
            }
        }

        private GameObject NewObject(string name, Transform parent = null)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.SetActive(false);
            if (parent != null) result.transform.SetParent(parent, false);
            _objects.Add(result);
            return result;
        }

        private TextMeshProUGUI Label(string name, Transform parent)
        {
            TextMeshProUGUI label = NewObject(name, parent).AddComponent<TextMeshProUGUI>();
            label.richText = true;
            label.parseCtrlCharacters = false;
            return label;
        }

        private Sprite NewSprite()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.zero);
            _assets.Add(texture);
            _assets.Add(sprite);
            return sprite;
        }

        private static void AssertPlain(TMP_Text text, string expected)
        {
            Assert.That(text.text, Is.EqualTo(expected));
            Assert.That(text.richText, Is.False);
            Assert.That(text.parseCtrlCharacters, Is.True);
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        private static object Invoke(object target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, Private, null, Array.ConvertAll(arguments, argument => argument.GetType()), null).Invoke(target, arguments);
    }
}
