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
        private static readonly Comparison<ScoreSystem> ScoreComparison = CompareScores;

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

            for (int i = 0; i < _sortedScores.Count; i++)
            {
                ScoreSystem score = _sortedScores[i];
                _activeEntries[i].SetData(i + 1, score.PlayerName, score.CurrentPoints, score.CurrentDeaths);
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
                if (score == null || score.netId == 0 || score.GetComponent("PlayerManager") == null)
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

            while (_activeEntries.Count > targetCount)
            {
                RankingEntryUI entry = _activeEntries[_activeEntries.Count - 1];
                _activeEntries.RemoveAt(_activeEntries.Count - 1);
                Destroy(entry.gameObject);
            }
        }

        private static int CompareScores(ScoreSystem left, ScoreSystem right)
        {
            int pointCompare = right.CurrentPoints.CompareTo(left.CurrentPoints);
            if (pointCompare != 0)
                return pointCompare;

            return string.CompareOrdinal(left.PlayerName, right.PlayerName);
        }
    }
}
