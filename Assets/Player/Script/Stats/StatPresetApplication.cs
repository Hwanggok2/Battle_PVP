using System;
using BattlePvp.Managers;
using BattlePvp.Networking;

namespace BattlePvp.Stats
{
    /// <summary>Apply the accepted preset before saving it; views only handle the result.</summary>
    public static class StatPresetApplication
    {
        public static void Apply(StatManager target, GlobalDataManager profile, PlayFabBattleManager persistence,
            StatContainer stats, bool strategistTarget, Action<bool, string> completed)
        {
            if (profile == null || !StatValidation.IsValidPreset(stats))
            {
                completed?.Invoke(false, "스탯 데이터 또는 프로필을 확인하십시오.");
                return;
            }
            if (strategistTarget)
            {
                if (!GlobalDataManager.IsCompleteStatPreset(stats) || !GlobalDataManager.IsStrategistPreset(stats))
                {
                    completed?.Invoke(false, "전략가 전환 프리셋에 모든 스탯을 투자하십시오.");
                    return;
                }
                profile.SaveStrategistTargetPreset(stats);
                Save(persistence, completed);
                return;
            }
            if (target == null)
            {
                completed?.Invoke(false, "스탯을 적용할 플레이어를 찾지 못했습니다.");
                return;
            }
            int slotIndex = profile.SelectedStatPresetSlot;
            target.RequestApplyStats(stats, (accepted, error) =>
            {
                if (!accepted)
                {
                    completed?.Invoke(false, error);
                    return;
                }
                profile.SaveStatPresetSlot(slotIndex, stats, selectAfterSave: true, applyToPlayer: false);
                Save(persistence, completed);
            });
        }

        private static void Save(PlayFabBattleManager persistence, Action<bool, string> completed)
        {
            if (persistence == null)
            {
                completed?.Invoke(false, "스탯은 적용되었지만 저장 서비스에 연결할 수 없습니다.");
                return;
            }
            persistence.SavePlayerStatPresetData((saved, error) =>
                completed?.Invoke(saved, saved ? null : "스탯은 적용되었지만 저장하지 못했습니다: " + error));
        }
    }
}
