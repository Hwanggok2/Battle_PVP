using System;
using System.Collections.Generic;
using BattlePvp.Characters;
using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>A lobby draft, copied and validated before the server starts the practice session.</summary>
    [Serializable]
    public sealed class PracticeBotSetup
    {
        public string CharacterId = CharacterCatalog.DefaultId;
        public MeleeWeaponKind Weapon;
        public StatContainer Stats;
        public int[] Skills = SkillLoadout.Defaults();
        public int Job => JobGuideContent.IndexOf(new IdentityCalculator().ResolveIdentity(Stats, out _));

        public PracticeBotSetup Copy() => new PracticeBotSetup
        { CharacterId = CharacterId, Weapon = Weapon, Stats = Stats, Skills = Skills == null ? null : (int[])Skills.Clone() };

        public bool IsValid => CharacterCatalog.Instance?.Find(CharacterId) != null &&
            WeaponCatalog.Instance?.Find(Weapon) != null && StatValidation.IsCompletePreset(Stats) &&
            Stats.STR.Item == 0 && Stats.CON.Item == 0 && Stats.AGI.Item == 0 && Stats.DEF.Item == 0 && SkillLoadout.Validate(Skills);

        public static PracticeBotSetup Randomized()
        {
            var characters = CharacterCatalog.Instance.Characters;
            var weapons = WeaponCatalog.Instance.Weapons;
            return new PracticeBotSetup
            {
                CharacterId = characters[UnityEngine.Random.Range(0, characters.Length)].Id,
                Weapon = weapons[UnityEngine.Random.Range(0, weapons.Length)].Kind,
                Stats = PracticeBot.RandomStats(), Skills = PracticeBot.RandomSkills()
            };
        }

        public void SetJob(int job)
        {
            if (job < 0 || job >= JobGuideContent.Count) return;
            Stats = job == 5 ? BattleNetworkManager.DefaultPracticeStats() : new StatContainer
            {
                STR = new StatSlot { Invested = job == 0 ? 30 : job == 4 ? 18 : 0 },
                CON = new StatSlot { Invested = job == 1 ? 30 : job == 4 ? 4 : 0 },
                AGI = new StatSlot { Invested = job == 2 ? 30 : job == 4 ? 4 : 0 },
                DEF = new StatSlot { Invested = job == 3 ? 30 : job == 4 ? 4 : 0 }
            };
        }

        public static List<JobSkillKind> SkillOptions(int job)
        {
            var choices = new List<JobSkillKind>();
            if (SkillGameData.Instance != null)
            {
                foreach (var row in SkillGameData.Instance.Pools)
                    if (row.Job == job && !choices.Contains((JobSkillKind)row.Kind)) choices.Add((JobSkillKind)row.Kind);
            }
            else foreach (JobSkillKind kind in Enum.GetValues(typeof(JobSkillKind)))
                if (CombatSkillRules.Allows(JobGuideContent.IdentityAt(job), kind)) choices.Add(kind);
            return choices;
        }

        public bool SetSkill(int slot, JobSkillKind kind)
        {
            if (slot < 0 || slot > 1 || !SkillLoadout.Validate(Skills) || !SkillOptions(Job).Contains(kind)) return false;
            int at = Job * 2 + slot, other = Job * 2 + 1 - slot;
            if (Skills[other] == (int)kind) Skills[other] = Skills[at];
            Skills[at] = (int)kind;
            return true;
        }
    }
}
