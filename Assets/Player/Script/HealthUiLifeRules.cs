namespace BattlePvp.Combat
{
    /// <summary>Server-side request policy shared with respawn timing checks.</summary>
    public static class HealthUiLifeRules
    {
        public const float RespawnDelaySeconds = 5f;

        public static bool CanRequestRevive(bool isDead, double now, double reviveAllowedAt,
            float ratio, bool isInBattle)
        {
            return isDead && isInBattle &&
                   double.IsFinite(now) && double.IsFinite(reviveAllowedAt) &&
                   now >= reviveAllowedAt &&
                   float.IsFinite(ratio) && ratio > 0f && ratio <= 1f;
        }
    }
}
