using UnityEngine;
using System.Collections.Generic;
using BattlePvp.Combat;
using System.Linq;

namespace BattlePvp.UI
{
    public class RankingUIManager : MonoBehaviour
    {
        [Header("References")]
        public Transform rankingContainer;
        public GameObject rankingEntryPrefab;

        private List<RankingEntryUI> _activeEntries = new List<RankingEntryUI>();

        private void OnEnable()
        {
            ScoreSystem.OnScoreUpdated += UpdateRanking;
            UpdateRanking(null); // Initial update
        }

        private void OnDisable()
        {
            ScoreSystem.OnScoreUpdated -= UpdateRanking;
        }

        private void UpdateRanking(ScoreSystem _ = null)
        {
            if (rankingContainer == null || rankingEntryPrefab == null) return;

            // 정렬: 점수 내림차순, 점수가 같다면 이름 오름차순 (안정적 정렬)
            var sortedScores = ScoreSystem.ActiveScores
                .Where(s => s != null && s.netId != 0 && s.GetComponent("PlayerManager") != null)
                .GroupBy(s => s.netId)
                .Select(g => g.First())
                .OrderByDescending(s => s.CurrentPoints)
                .ThenBy(s => s.PlayerName)
                .ToList();

            // 필요에 따라 UI 엔트리 개수 맞추기
            while (_activeEntries.Count < sortedScores.Count)
            {
                var go = Instantiate(rankingEntryPrefab, rankingContainer);
                var entry = go.GetComponent<RankingEntryUI>();
                if (entry != null)
                {
                    _activeEntries.Add(entry);
                }
                else
                {
                    Debug.LogError("RankingEntryUI component missing on prefab.");
                    break;
                }
            }

            while (_activeEntries.Count > sortedScores.Count)
            {
                var entry = _activeEntries[_activeEntries.Count - 1];
                _activeEntries.RemoveAt(_activeEntries.Count - 1);
                Destroy(entry.gameObject);
            }

            // 데이터 바인딩
            for (int i = 0; i < sortedScores.Count; i++)
            {
                int rank = i + 1;
                _activeEntries[i].SetData(rank, sortedScores[i].PlayerName, sortedScores[i].CurrentPoints, sortedScores[i].CurrentDeaths);
                
                // 순서 보장 (Hierarchy)
                _activeEntries[i].transform.SetSiblingIndex(i);
            }
        }
    }
}
