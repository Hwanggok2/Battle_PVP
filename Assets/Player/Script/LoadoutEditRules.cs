using BattlePvp.Stats;
using UnityEngine;

namespace BattlePvp.Combat
{
    public static class LoadoutEditRules
    {
        public static bool CanEditSkills(GameObject player) => CanEdit(player, false);
        public static bool CanEditWeapon(GameObject player) => CanEdit(player, true);

        private static bool CanEdit(GameObject player, bool weapon)
        {
            if (player == null) return false;
            string scene = player.scene.name;
            if (scene == "Lobby" || scene == "Battle_waiting") return true;
            if (scene != "Battle") return false;
            if (player.TryGetComponent<HealthSystem>(out var health) && health.IsDead) return true;
            return weapon && player.TryGetComponent<StatManager>(out var stats) &&
                stats.CurrentIdentity.Type == IdentityType.Polymath;
        }
    }
}
