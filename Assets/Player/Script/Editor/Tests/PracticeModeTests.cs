using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BattlePvp.Combat;
using BattlePvp.Networking;
using BattlePvp.Stats;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BattlePvp.EditorTests
{
    public sealed class PracticeModeTests
    {
        [Test]
        public void RandomBuildsRemainValidAndCoverEveryJobAndSkillPool()
        {
            var saved = Random.state;
            try
            {
                Random.InitState(1092026);
                var jobs = new HashSet<int>();
                var skills = new HashSet<int>();
                for (int i = 0; i < 200; i++)
                {
                    var stats = PracticeBot.RandomStats();
                    Assert.That(StatValidation.IsCompletePreset(stats), Is.True);
                    var identity = new IdentityCalculator().ResolveIdentity(stats, out _);
                    jobs.Add(BattlePvp.UI.JobGuideContent.IndexOf(identity));
                    var choices = PracticeBot.RandomSkills();
                    Assert.That(SkillLoadout.Validate(choices), Is.True);
                    foreach (int kind in choices) skills.Add(kind);
                }
                Assert.That(jobs.Count, Is.EqualTo(6));
                foreach (var row in SkillGameData.Instance.Pools) Assert.That(skills, Does.Contain(row.Kind));
            }
            finally { Random.state = saved; }
        }

        [Test]
        public void EditableBotDraftsStayIndependentAndUseLegalSkillPairs()
        {
            var saved = Random.state;
            try
            {
                Random.InitState(10926);
                var characters = new HashSet<string>();
                for (int i = 0; i < 100; i++)
                {
                    var draft = PracticeBotSetup.Randomized();
                    Assert.That(draft.IsValid, Is.True); characters.Add(draft.CharacterId);
                    var copy = draft.Copy();
                    copy.Skills[0] = -1;
                    Assert.That(copy.IsValid, Is.False);
                    Assert.That(draft.IsValid, Is.True, "Editing a snapshot must not edit the lobby draft.");
                }
                foreach (var character in BattlePvp.Characters.CharacterCatalog.Instance.Characters)
                    Assert.That(characters, Does.Contain(character.Id));
                var bot = PracticeBotSetup.Randomized();
                for (int job = 0; job < BattlePvp.UI.JobGuideContent.Count; job++)
                {
                    bot.SetJob(job);
                    Assert.That(bot.Job, Is.EqualTo(job));
                    foreach (var skill in PracticeBotSetup.SkillOptions(job))
                    {
                        Assert.That(bot.SetSkill(0, skill), Is.True);
                        Assert.That(bot.SetSkill(1, skill), Is.True);
                        Assert.That(bot.IsValid, Is.True, "Choosing an occupied skill swaps slots rather than duplicating it.");
                        Assert.That(bot.Skills[job * 2], Is.Not.EqualTo(bot.Skills[job * 2 + 1]));
                    }
                }
                Assert.That(bot.SetSkill(0, JobSkillKind.Hook), Is.False, "A polymath cannot equip a strength-only skill.");
                for (byte map = 0; map < BattleMapSelection.MapCount; map++)
                    Assert.That(BattlePvp.UI.BattleMapPreviews.Get(map), Is.Not.Null);
            }
            finally { Random.state = saved; }
        }

        [UnityTest]
        public IEnumerator ServerBotsCastWithCooldownsAndCompleteKnifeTrapAndBowActions()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync("Lobby");
            var manager = (BattleNetworkManager)NetworkManager.singleton;
            Assert.That(manager.StartPractice(0, 0), Is.True);
            float deadline = Time.realtimeSinceStartup + 25;
            while (BattleStateMachine.Instance == null || BattleStateMachine.Instance.CurrentState != BattleState.InBattle)
            { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }

            var cry = SpawnSkillBot(manager, new StatContainer { STR = new StatSlot { Invested = 30 } }, 0, JobSkillKind.WarCry);
            var knife = SpawnSkillBot(manager, new StatContainer { AGI = new StatSlot { Invested = 30 } }, 2, JobSkillKind.Knife);
            var trap = SpawnSkillBot(manager, new StatContainer { STR = new StatSlot { Invested = 18 }, CON = new StatSlot { Invested = 4 },
                AGI = new StatSlot { Invested = 4 }, DEF = new StatSlot { Invested = 4 } }, 4, JobSkillKind.Trap);
            var bow = SpawnSkillBot(manager, BattleNetworkManager.DefaultPracticeStats(), 5, JobSkillKind.PolymathWeaponSwap);
            // Keep trap placement on an unobstructed patch selected by the game's own footprint rules.
            var pool = BattleSpawnPoints.ForScene(SceneManager.GetActiveScene());
            bool floor = false;
            foreach (var point in pool.Points)
            {
                trap.GetComponent<PlayerManager>().ServerTeleport(point, Quaternion.identity);
                Physics.SyncTransforms();
                if (trap.GetComponent<ExpandedSkillController>().TryGetTrapPlacement(out _)) { floor = true; break; }
            }
            Assert.That(floor, Is.True);
            var aim = new Vector3(0, .2f, 1).normalized;
            deadline = Time.realtimeSinceStartup + 5;
            while (!cry.CanBeginExpandedSkill || BattleStateMachine.Instance.IsLoading)
            { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "New actors must finish spawn/loading before combat."); yield return null; }
            foreach (var bot in new[] { cry, knife, trap, bow })
                Assert.That(bot.ServerPracticeUseSkill(0, aim), Is.True,
                    bot.name + " ready=" + bot.CanBeginExpandedSkill + " phase=" + bot.GetSkillHudState(0).Phase +
                    " choices=" + string.Join(",", bot.GetComponent<SkillLoadout>().Snapshot()));
            Assert.That(cry.ServerPracticeUseSkill(0, aim), Is.False, "A repeated cast must respect cooldown.");
            Assert.That(NetworkClient.localPlayer.GetComponent<PlayerCombat>().ServerPracticeUseSkill(0, aim), Is.False);
            var knifeSkills = knife.GetComponent<ExpandedSkillController>();
            var trapSkills = trap.GetComponent<ExpandedSkillController>();
            var continuation = typeof(ExpandedSkillController).GetMethod("TickPracticeAction", BindingFlags.Instance | BindingFlags.NonPublic);
            continuation.Invoke(knifeSkills, new object[] { aim, true });
            continuation.Invoke(trapSkills, new object[] { aim, true });
            Assert.That(knifeSkills.Read(JobSkillKind.Knife).Charges, Is.EqualTo(2), "Readying a knife must be followed by a real throw.");
            Assert.That(trapSkills.ChargeState(JobSkillKind.Trap).Charges, Is.EqualTo(2));
            bool arrowSeen = false;
            float until = Time.time + 7;
            while (Time.time < until)
            {
                bow.ServerPracticeBowAttack(aim, true);
                arrowSeen |= Object.FindObjectsByType<BowArrowProjectile>(FindObjectsSortMode.None).Length > 0;
                if (arrowSeen && trapSkills.Traps.Count > 0) break;
                yield return null;
            }
            Assert.That(trapSkills.Traps.Count, Is.EqualTo(1), "Trap preparation must finish placement.");
            Assert.That(arrowSeen, Is.True, "A server-owned bot must charge and release an actual arrow.");
            Assert.That(cry.ServerPracticeUseSkill(0, aim), Is.False, "Cooldown must survive other actors' actions.");
            cry.GetComponent<HealthSystem>().ApplyDamage(100000, DamageSource.Fixed, cry.transform.position);
            Assert.That(cry.ServerPracticeUseSkill(0, aim), Is.False, "Dead actors cannot cast.");
        }

        private static PlayerCombat SpawnSkillBot(BattleNetworkManager manager, StatContainer stats, int job, JobSkillKind kind)
        {
            Assert.That(BattleSpawnPoints.ForScene(SceneManager.GetActiveScene()).TryTake(null, null, out var pose), Is.True);
            var actor = Object.Instantiate(manager.playerPrefab, pose.position, pose.rotation);
            actor.name = "Skill test " + kind;
            actor.AddComponent<PracticeBot>().enabled = false;
            NetworkServer.Spawn(actor);
            var appearance = actor.GetComponent<BattlePvp.Characters.PlayerAppearance>();
            var selectAppearance = typeof(BattlePvp.Characters.PlayerAppearance).GetMethod("TrySelectOnServer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(selectAppearance.Invoke(appearance, new object[] { BattlePvp.Characters.CharacterCatalog.DefaultId, true, null }), Is.True);
            Assert.That(actor.GetComponent<StatManager>().TryApplyServerPreset(stats), Is.True);
            var choices = SkillLoadout.Defaults();
            choices[job * 2] = (int)kind;
            if (choices[job * 2 + 1] == (int)kind)
                choices[job * 2 + 1] = SkillLoadout.Defaults()[job * 2];
            var initialize = typeof(SkillLoadout).GetMethod("InitializeServerChoices", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(initialize.Invoke(actor.GetComponent<SkillLoadout>(), new object[] { choices }), Is.True);
            Assert.That(initialize.Invoke(actor.GetComponent<SkillLoadout>(), new object[] { choices }), Is.False, "Cannot reroll to reset skill state.");
            return actor.GetComponent<PlayerCombat>();
        }

        [UnityTest]
        public IEnumerator PointerClicksOpenConfigureAndStartPracticeAboveLobbyChat()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync("Lobby");
            yield return null;
            var previousMouse = Mouse.current;
            var background = InputSystem.settings.backgroundBehavior;
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                // An automated editor run need not own OS focus; use the real UI input module and raycasters.
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                var ui = BattlePvp.UI.PracticeModePanel.Instance;
                Assert.That(ui, Is.Not.Null);
                yield return Click(mouse, ui.transform.Find("Practice"));
                Assert.That(BattlePvp.UI.PracticeModePanel.IsOpen, Is.True);
                var dialog = ui.transform.Find("Practice overlay/Practice setup");
                var count = dialog.Find("AI count").GetComponent<TMPro.TMP_InputField>();
                int before = int.Parse(count.text);
                yield return Click(mouse, dialog.Find("More"));
                Assert.That(int.Parse(count.text), Is.EqualTo(before + 1));
                yield return Click(mouse, dialog.Find("Less"));
                Assert.That(int.Parse(count.text), Is.EqualTo(before));
                string map = dialog.Find("Map/Label").GetComponent<TMPro.TMP_Text>().text;
                var preview = dialog.Find("Map preview").GetComponent<UnityEngine.UI.RawImage>();
                Assert.That(preview.texture, Is.Not.Null);
                var oldPreview = preview.texture;
                yield return Click(mouse, dialog.Find("Map"));
                Assert.That(dialog.Find("Map/Label").GetComponent<TMPro.TMP_Text>().text, Is.Not.EqualTo(map));
                Assert.That(preview.texture, Is.Not.Null.And.Not.EqualTo(oldPreview));

                yield return Click(mouse, dialog.Find("AI roster/Content/AI 01"));
                var editor = dialog.Find("AI editor");
                var options = dialog.Find("Selection panel/Options/Content");
                yield return Click(mouse, editor.Find("Character"));
                yield return Click(mouse, options.Find("brute"));
                yield return Click(mouse, editor.Find("Weapon"));
                yield return Click(mouse, options.Find("Axe"));
                yield return Click(mouse, editor.Find("Job"));
                yield return Click(mouse, options.Find("Job 2"));
                yield return Click(mouse, editor.Find("Skill 0"));
                yield return Click(mouse, options.Find("Knife"));
                yield return Click(mouse, editor.Find("Skill 1"));
                yield return Click(mouse, options.Find("MonostatAgiPoison"));
                var edited = ui.SnapshotBots()[0];
                Assert.That(edited.CharacterId, Is.EqualTo("brute"));
                Assert.That(edited.Weapon, Is.EqualTo(MeleeWeaponKind.Axe));
                Assert.That(edited.Job, Is.EqualTo(2));
                Assert.That(edited.Skills[4], Is.EqualTo((int)JobSkillKind.Knife));
                Assert.That(edited.Skills[5], Is.EqualTo((int)JobSkillKind.MonostatAgiPoison));
                yield return Click(mouse, dialog.Find("Cancel"));
                Assert.That(BattlePvp.UI.PracticeModePanel.IsOpen, Is.False);
                yield return Click(mouse, ui.transform.Find("Practice"));
                Assert.That(ui.SnapshotBots()[0].Skills, Is.EqualTo(edited.Skills), "Closing setup must preserve edits.");
                var drafts = ui.SnapshotBots();
                var invalid = drafts[0].Copy(); invalid.CharacterId = "not-a-character";
                var manager = (BattleNetworkManager)NetworkManager.singleton;
                Assert.That(manager.StartPractice(new[] { invalid }, 0), Is.False);
                Assert.That(NetworkServer.active, Is.False, "Invalid bot drafts must not partially start a session.");
                yield return Click(mouse, dialog.Find("Start practice"));
                float deadline = Time.realtimeSinceStartup + 60;
                while (BattleStateMachine.Instance == null || BattleStateMachine.Instance.CurrentState != BattleState.InBattle)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Pointer-clicked Start must enter battle.");
                    yield return null;
                }
                var spawned = Object.FindObjectsByType<PracticeBot>(FindObjectsSortMode.None);
                Assert.That(spawned.Length, Is.EqualTo(before));
                foreach (var bot in spawned)
                {
                    int index = int.Parse(bot.name.Substring(bot.name.Length - 2)) - 1;
                    var expected = drafts[index];
                    Assert.That(bot.GetComponent<BattlePvp.Characters.PlayerAppearance>().SelectedId, Is.EqualTo(expected.CharacterId));
                    Assert.That(bot.GetComponent<WeaponLoadout>().Selected, Is.EqualTo(expected.Weapon));
                    Assert.That(bot.GetComponent<SkillLoadout>().Snapshot(), Is.EqualTo(expected.Skills));
                    Assert.That(BattlePvp.UI.JobGuideContent.IndexOf(bot.GetComponent<StatManager>().CurrentIdentity), Is.EqualTo(expected.Job));
                }
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                if (previousMouse != null) previousMouse.MakeCurrent();
                InputSystem.settings.backgroundBehavior = background;
            }
        }

        private static IEnumerator Click(Mouse mouse, Transform button, [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0)
        {
            Assert.That(button, Is.Not.Null, "Missing click target at test line " + callerLine);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.EqualTo(button.gameObject), button.name + " is covered by " + hits[0].gameObject.name);
            foreach (ushort buttons in new ushort[] { 0, 1, 0 })
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = buttons });
                InputSystem.Update();
                // EditMode coroutines can resume between player-loop updates. Keep each
                // pointer state through real frames so the live EventSystem sees it once.
                int frame = Time.frameCount;
                while (Time.frameCount < frame + 2) yield return null;
            }
        }

        [Test]
        public void SpawnSeparationRejectsOverlapsAndAcceptsExactlyTwoUnits()
        {
            var taken = new[] { Vector3.zero, new Vector3(8, 0, 0) };
            Assert.That(BattleSpawnPoints.IsSeparated(Vector3.zero, taken), Is.False);
            Assert.That(BattleSpawnPoints.IsSeparated(new Vector3(1.999f, 0, 0), taken), Is.False);
            Assert.That(BattleSpawnPoints.IsSeparated(new Vector3(2, 0, 0), taken), Is.True);
            Assert.That(StatValidation.IsCompletePreset(BattleNetworkManager.DefaultPracticeStats()), Is.True);
        }

        [UnityTest]
        public IEnumerator AllMapsSupportLocalCombatUniqueRespawnsAndRepeatedSessions()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync("Lobby");
            var report = new List<string>();
            for (byte map = 0; map < BattleMapSelection.MapCount; map++)
            {
                int botCount = map == 0 ? 15 : map == 1 ? 0 : 3;
                var manager = (BattleNetworkManager)NetworkManager.singleton;
                Assert.That(manager.IsPractice, Is.False, "A new Lobby manager must restore online room authentication.");
                Assert.That(manager.authenticator, Is.TypeOf<RoomNetworkAuthenticator>());
                Assert.That(manager.transport, Is.Not.TypeOf<PracticeTransport>());
                Assert.That(manager.StartPractice(-1, map), Is.False);
                Assert.That(manager.StartPractice(16, map), Is.False);
                Assert.That(manager.StartPractice(1, 3), Is.False);
                Assert.That(manager.StartPractice(botCount, map), Is.True);
                float deadline = Time.realtimeSinceStartup + 25;
                while (BattleStateMachine.Instance == null || BattleStateMachine.Instance.CurrentState != BattleState.InBattle)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Practice should reach playable battle without login or Relay.");
                    yield return null;
                }
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Battle"));
                Assert.That(BattleMapSelection.Instance.Selected, Is.EqualTo(map));
                Assert.That(Transport.active, Is.TypeOf<PracticeTransport>());
                Assert.That(NetworkServer.connections.Count, Is.EqualTo(1));
                Assert.That(BattlePvp.UI.PracticeModePanel.Instance, Is.Not.Null, "Practice exit must be available.");
                var bots = Object.FindObjectsByType<PracticeBot>(FindObjectsSortMode.None);
                Assert.That(bots.Length, Is.EqualTo(botCount));
                var players = Object.FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
                Assert.That(players.Length, Is.EqualTo(botCount + 1));
                var positions = new List<Vector3>();
                foreach (var player in players)
                {
                    Assert.That(player.GetComponent<StatManager>().HasServerCombatStats, Is.True);
                    Assert.That(BattleSpawnPoints.IsSeparated(player.transform.position, positions), Is.True, "Initial spawn overlap.");
                    positions.Add(player.transform.position);
                }
                var pool = BattleSpawnPoints.ForScene(SceneManager.GetActiveScene());
                Assert.That(pool.Points.Count, Is.GreaterThan(30), "Generate points throughout the map, not only eight starts.");
                float spacing = float.MaxValue;
                for (int i = 0; i < pool.Points.Count; i++)
                    for (int j = i + 1; j < pool.Points.Count; j++)
                        spacing = Mathf.Min(spacing, Vector3.Distance(pool.Points[i], pool.Points[j]));
                Assert.That(spacing, Is.GreaterThanOrEqualTo(2f));
                Assert.That(pool.TryTake(null, pool.Points, out _), Is.False, "A full pool must not wrap to an occupied point.");
                report.Add($"{BattleMapSelection.MapName(map)}: 후보 {pool.Points.Count}, 최소 간격 {spacing:F4}, AI {botCount}명");

                if (map == 0)
                {
                    var weapons = new HashSet<MeleeWeaponKind>();
                    var jobs = new HashSet<int>();
                    var usedSkills = new HashSet<int>();
                    foreach (var bot in bots)
                    {
                        Assert.That(SkillLoadout.Validate(bot.GetComponent<SkillLoadout>().Snapshot()), Is.True);
                        weapons.Add(bot.GetComponent<WeaponLoadout>().Selected);
                        jobs.Add(BattlePvp.UI.JobGuideContent.IndexOf(bot.GetComponent<StatManager>().CurrentIdentity));
                    }
                    Assert.That(weapons.Count, Is.GreaterThan(1));
                    Assert.That(jobs.Count, Is.GreaterThan(1));
                    int kills = BattlePvp.Managers.GlobalDataManager.Instance != null ? BattlePvp.Managers.GlobalDataManager.Instance.CumulativeKills : 0;
                    int deaths = BattlePvp.Managers.GlobalDataManager.Instance != null ? BattlePvp.Managers.GlobalDataManager.Instance.CumulativeDeaths : 0;
                    float until = Time.time + 18;
                    while (Time.time < until && (TotalDamage() <= 0 || usedSkills.Count < 3))
                    {
                        foreach (var bot in bots)
                        {
                            var combat = bot.GetComponent<PlayerCombat>();
                            var loadout = bot.GetComponent<SkillLoadout>();
                            for (int slot = 0; slot < 2; slot++)
                                if (loadout.Select(slot, out var kind) && combat.GetSkillHudState(slot).Phase != BattlePvp.UI.SkillHudPhase.Ready)
                                    usedSkills.Add((int)kind);
                        }
                        yield return null;
                    }
                    Assert.That(TotalDamage(), Is.GreaterThan(0), "Real bot steering and melee must damage another player.");
                    Assert.That(usedSkills.Count, Is.GreaterThanOrEqualTo(3), "Autonomous bots must actually use several assigned skills.");
                    report.Add($"AI 무기 {weapons.Count}종 / 직업 {jobs.Count}종 / 실제 사용 스킬 {usedSkills.Count}종");
                    var botHealth = bots[0].GetComponent<HealthSystem>();
                    botHealth.ApplyDamage(100000, DamageSource.Fixed, botHealth.transform.position);
                    Assert.That(botHealth.IsDead, Is.True);
                    until = Time.time + 8;
                    while (botHealth.IsDead && Time.time < until) yield return null;
                    Assert.That(botHealth.IsDead, Is.False, "AI must automatically revive after the normal delay.");

                    // Freeze steering only: use the real death timers and server revive requests for a same-tick wave.
                    foreach (var bot in bots) { bot.enabled = false; bot.GetComponent<PlayerManager>().ServerSetPracticeSteering(Vector3.zero, bot.transform.forward); }
                    foreach (var player in players)
                        player.GetComponent<HealthSystem>().ApplyDamage(100000, DamageSource.Fixed, player.transform.position);
                    until = Time.time + 5.2f;
                    while (Time.time < until) yield return null;
                    positions.Clear();
                    foreach (var player in players)
                    {
                        var health = player.GetComponent<HealthSystem>();
                        health.RequestRevive();
                        Assert.That(health.IsDead, Is.False, player.name + " could not revive.");
                        Assert.That(BattleSpawnPoints.IsSeparated(player.transform.position, positions), Is.True, "Same-tick respawns overlap.");
                        positions.Add(player.transform.position);
                    }
                    if (BattlePvp.Managers.GlobalDataManager.Instance != null)
                    {
                        Assert.That(BattlePvp.Managers.GlobalDataManager.Instance.CumulativeKills, Is.EqualTo(kills));
                        Assert.That(BattlePvp.Managers.GlobalDataManager.Instance.CumulativeDeaths, Is.EqualTo(deaths));
                    }
                    report.Add("실제 AI 타격 / 자동 부활 / 16명 같은 프레임 부활 / 계정 전적 유지: 통과");
                }
                if (map == 2)
                {
                    // Exercise the normal results/J return path as well as the in-match exit button.
                    typeof(BattleStateMachine).GetMethod("EndMatchOnServer", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(BattleStateMachine.Instance, null);
                    Assert.That(BattleStateMachine.Instance.LastCompletedMatch.Participants.Count, Is.EqualTo(botCount + 1));
                    manager.ReturnToLobbyAfterMatch();
                }
                else manager.StopPractice();
                deadline = Time.realtimeSinceStartup + 20;
                while (SceneManager.GetActiveScene().name != "Lobby" || NetworkServer.active || NetworkClient.active)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Practice exit must return to Lobby.");
                    yield return null;
                }
                yield return null;
                Assert.That(Object.FindObjectsByType<PracticeBot>(FindObjectsSortMode.None), Is.Empty);
                Assert.That(Object.FindObjectsByType<BattleSpawnPoints>(FindObjectsSortMode.None), Is.Empty);
            }
            // The shared allocator also serves ordinary room admission and additive post-match returns.
            // Isolate the waiting geometry before reproducing its additive-return world offset.
            yield return SceneManager.LoadSceneAsync(BattleNetworkManager.WaitingScene);
            var waiting = SceneManager.GetSceneByName(BattleNetworkManager.WaitingScene);
            foreach (var root in waiting.GetRootGameObjects()) root.transform.position += BattleNetworkManager.ReturnWaitingOffset;
            var waitingPool = BattleSpawnPoints.ForScene(waiting);
            Assert.That(waitingPool, Is.Not.Null);
            var roomReservations = new List<Vector3>();
            for (int i = 0; i < BattleNetworkManager.PlayerCapacity; i++)
            {
                Assert.That(waitingPool.TryTake(null, roomReservations, out var pose), Is.True);
                Assert.That(pose.position.y, Is.LessThan(-4000f), "Use this waiting scene, not the original arena's navigation.");
                Assert.That(BattleSpawnPoints.IsSeparated(pose.position, roomReservations), Is.True);
                roomReservations.Add(pose.position);
            }
            report.Add($"복귀 대기실: 후보 {waitingPool.Points.Count}, 8명 중복 없는 배정 통과");
            System.IO.Directory.CreateDirectory("Reports/Practice");
            System.IO.File.WriteAllLines("Reports/Practice/runtime-verification.txt", report);
        }

        [UnityTest]
        public IEnumerator BotsSeparateStrafeAndDeathMenusEnforceLoadoutRules()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync("Lobby");
            var manager = (BattleNetworkManager)NetworkManager.singleton;
            var setup = new PracticeBotSetup { CharacterId = "default", Weapon = MeleeWeaponKind.Sword,
                Stats = new StatContainer { CON = new StatSlot { Invested = 30 } }, Skills = SkillLoadout.Defaults() };
            Assert.That(manager.StartPractice(new[] { setup, setup.Copy() }, 0), Is.True);
            float deadline = Time.realtimeSinceStartup + 30;
            while (BattleStateMachine.Instance == null || BattleStateMachine.Instance.CurrentState != BattleState.InBattle)
            { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            var bots = Object.FindObjectsByType<PracticeBot>(FindObjectsSortMode.None);
            Assert.That(Vector3.Distance(bots[0].transform.position, bots[1].transform.position), Is.GreaterThanOrEqualTo(2));
            var pool = BattleSpawnPoints.ForScene(SceneManager.GetActiveScene());
            Vector3 shared = pool.Points[pool.Points.Count / 2];
            foreach (var bot in bots) bot.GetComponent<PlayerManager>().ServerTeleport(shared, Quaternion.identity);
            var previous = new[] { shared, shared };
            float swingMovement = 0, maxSeparation = 0;
            float until = Time.time + 14;
            while (Time.time < until)
            {
                for (int i = 0; i < bots.Length; i++)
                {
                    Vector3 now = bots[i].transform.position;
                    if (bots[i].GetComponent<PlayerCombat>().IsAttackActive)
                        swingMovement += Vector3.ProjectOnPlane(now - previous[i], Vector3.up).magnitude;
                    previous[i] = now;
                }
                maxSeparation = Mathf.Max(maxSeparation, Vector3.Distance(bots[0].transform.position, bots[1].transform.position));
                yield return null;
            }
            Assert.That(maxSeparation, Is.GreaterThan(1), "Overlapping bots must steer apart without waiting for a teleport.");
            Assert.That(swingMovement, Is.GreaterThan(.5f), "Bots must move while their real attack animation is active.");
            Assert.That(TotalDamage(), Is.GreaterThan(0), "Footwork must preserve effective attacks.");
            foreach (var bot in bots) { bot.enabled = false; bot.GetComponent<PlayerManager>().ServerSetPracticeSteering(Vector3.zero, bot.transform.forward); }

            var local = NetworkClient.localPlayer.gameObject;
            var stats = local.GetComponent<StatManager>();
            var health = local.GetComponent<HealthSystem>();
            var weapon = local.GetComponent<WeaponLoadout>();
            var skills = local.GetComponent<SkillLoadout>();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var select = typeof(WeaponLoadout).GetMethod("TrySelect", flags);
            var setSkills = typeof(SkillLoadout).GetMethod("TrySetChoices", flags);
            Assert.That(stats.TryApplyServerPreset(new StatContainer { STR = new StatSlot { Invested = 30 } }), Is.True);
            yield return null;
            AssertMenu(false, false);
            typeof(WeaponLoadout).GetField("_nextRequest", flags).SetValue(weapon, 0d);
            Assert.That(select.Invoke(weapon, new object[] { MeleeWeaponKind.Axe, false }), Is.False);
            Assert.That(setSkills.Invoke(skills, new object[] { SkillLoadout.Defaults() }), Is.False);
            stats.TryApplyServerPreset(BattleNetworkManager.DefaultPracticeStats());
            yield return null;
            AssertMenu(false, true);
            Assert.That(select.Invoke(weapon, new object[] { MeleeWeaponKind.Axe, false }), Is.True);
            health.ApplyDamage(100000, DamageSource.Fixed, local.transform.position);
            yield return null;
            AssertMenu(true, true);
            typeof(WeaponLoadout).GetField("_nextRequest", flags).SetValue(weapon, 0d);
            Assert.That(select.Invoke(weapon, new object[] { MeleeWeaponKind.Greatsword, false }), Is.True);
            var choices = SkillLoadout.Defaults();
            int first = choices[10]; choices[10] = choices[11]; choices[11] = first;
            Assert.That(setSkills.Invoke(skills, new object[] { choices }), Is.True);
            Assert.That(typeof(BattlePvp.UI.JobGuidePanel).GetField("_local", flags).GetValue(BattlePvp.UI.JobGuidePanel.Instance), Is.EqualTo(stats), "The surviving guide must edit the local player.");
            BattlePvp.UI.JobGuidePanel.Instance.Open();
            Assert.That(BattlePvp.UI.JobGuidePanel.IsOpen, Is.True);
            until = Time.time + 5.2f;
            while (Time.time < until) yield return null;
            health.RequestRevive();
            yield return null;
            yield return null;
            Assert.That(health.IsDead, Is.False);
            Assert.That(BattlePvp.UI.JobGuidePanel.IsOpen, Is.False);
            AssertMenu(false, true);
            Assert.That(weapon.Selected, Is.EqualTo(MeleeWeaponKind.Greatsword));
            Assert.That(skills.Choices[10], Is.EqualTo(choices[10]));
            Assert.That(setSkills.Invoke(skills, new object[] { SkillLoadout.Defaults() }), Is.False);
            System.IO.Directory.CreateDirectory("Reports/Practice");
            System.IO.File.WriteAllText("Reports/Practice/footwork-verification.txt", $"Separation {maxSeparation:F2}; movement during attacks {swingMovement:F2}; loadout/death/revive menu checks passed.");
        }

        private static void AssertMenu(bool dead, bool polymath)
        {
            var layout = Object.FindFirstObjectByType<BattlePvp.UI.TopMenuLayout>();
            Assert.That(layout, Is.Not.Null);
            typeof(BattlePvp.UI.TopMenuLayout).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layout, null);
            var entries = (IEnumerable)typeof(BattlePvp.UI.TopMenuLayout).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layout);
            int visible = 0;
            var seen = new HashSet<int>();
            foreach (var entry in entries)
            {
                var t = entry.GetType();
                var button = (UnityEngine.UI.Button)t.GetField("Button").GetValue(entry);
                var owner = (NetworkIdentity)t.GetField("Owner").GetValue(entry);
                int column = (int)t.GetField("Column").GetValue(entry);
                if (button == null || owner != null && !owner.isLocalPlayer) continue;
                bool show = column >= 2 ? column != 3 || dead : column == -1 ? dead || polymath : column == 1 && dead;
                Assert.That(button.gameObject.activeSelf, Is.EqualTo(show), "Column " + column);
                if (!show || !button.gameObject.activeInHierarchy) continue;
                Assert.That(seen.Add(column), Is.True, "One visible local entry per column.");
                Assert.That(((RectTransform)button.transform).anchoredPosition.x, Is.EqualTo(-82f - 128f * visible++));
            }
            Assert.That(visible, Is.EqualTo(dead ? 5 : polymath ? 3 : 2));
        }

        private static float TotalDamage()
        {
            float damage = 0;
            foreach (var score in ScoreSystem.ActiveScores) damage += score.MatchDamageDealt;
            return damage;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (NetworkManager.singleton != null && NetworkServer.active) NetworkManager.singleton.StopHost();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
