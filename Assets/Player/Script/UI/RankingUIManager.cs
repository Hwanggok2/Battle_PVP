using System;
using System.Collections.Generic;
using BattlePvp.Combat;
using UnityEngine;

namespace BattlePvp.UI
{
    public class RankingUIManager : MonoBehaviour
    {
        [Header("References")]
        public Transform rankingContainer;
        public GameObject rankingEntryPrefab;

        private readonly List<RankingEntryUI> _activeEntries = new List<RankingEntryUI>();
        private readonly List<ScoreSystem> _sortedScores = new List<ScoreSystem>();
        private readonly HashSet<uint> _seenNetIds = new HashSet<uint>();
        private static readonly Comparison<ScoreSystem> ScoreComparison = ScoreSystem.CompareForDisplay;

        private void OnEnable()
        {
            ScoreSystem.OnScoreUpdated += UpdateRanking;
            UpdateRanking(null);
        }

        private void OnDisable()
        {
            ScoreSystem.OnScoreUpdated -= UpdateRanking;
        }

        private void UpdateRanking(ScoreSystem _ = null)
        {
            if (rankingContainer == null || rankingEntryPrefab == null)
                return;

            BuildSortedScores();
            ResizeEntries(_sortedScores.Count);

            for (int i = 0; i < _sortedScores.Count && i < _activeEntries.Count; i++)
            {
                ScoreSystem score = _sortedScores[i];
                int rank = CompetitionRanking.GetRank(score.CurrentPoints, _sortedScores, ScoreSystem.PointsOf);
                _activeEntries[i].SetData(rank, score.PlayerName, score.CurrentPoints, score.CurrentDeaths);
                if (_activeEntries[i].transform.GetSiblingIndex() != i)
                    _activeEntries[i].transform.SetSiblingIndex(i);
            }
        }

        private void BuildSortedScores()
        {
            _sortedScores.Clear();
            _seenNetIds.Clear();

            for (int i = 0; i < ScoreSystem.ActiveScores.Count; i++)
            {
                ScoreSystem score = ScoreSystem.ActiveScores[i];
                if (score == null || score.netId == 0)
                    continue;

                if (_seenNetIds.Add(score.netId))
                    _sortedScores.Add(score);
            }

            _sortedScores.Sort(ScoreComparison);
        }

        private void ResizeEntries(int targetCount)
        {
            while (_activeEntries.Count < targetCount)
            {
                GameObject go = Instantiate(rankingEntryPrefab, rankingContainer);
                RankingEntryUI entry = go.GetComponent<RankingEntryUI>();
                if (entry != null)
                {
                    _activeEntries.Add(entry);
                    continue;
                }

                Debug.LogError("RankingEntryUI component missing on prefab.");
                Destroy(go);
                break;
            }

            for (int i = 0; i < _activeEntries.Count; i++)
            {
                bool active = i < targetCount;
                if (_activeEntries[i].gameObject.activeSelf != active)
                    _activeEntries[i].gameObject.SetActive(active);
            }
        }
    }
}
