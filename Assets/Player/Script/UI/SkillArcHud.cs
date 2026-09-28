using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace BattlePvp.UI
{
    public sealed class SkillArcHud : MonoBehaviour
    {
        [SerializeField] private SkillUI[] _slots;
        [SerializeField] private TMP_Text[] _keys;
        [SerializeField] private Button[] _buttons;
        private PlayerCombat _combat;
        private float _nextRefresh;
        private void OnEnable()
        {
            StatManager.LocalChanged += Bind;
            Bind(StatManager.Local);
            LocalGameSettings.Changed += ApplySettings;
            for (int i = 0; i < _buttons.Length; i++)
            {
                int slot = i;
                _buttons[i].onClick.AddListener(() => { if (_combat != null) _combat.UseSkillSlot(slot, true); });
            }
            ApplySettings();
        }
        private void OnDisable()
        {
            StatManager.LocalChanged -= Bind; LocalGameSettings.Changed -= ApplySettings;
            foreach (var button in _buttons) if (button != null) button.onClick.RemoveAllListeners();
            _combat = null;
        }
        private void Bind(StatManager stats) { _combat = stats != null ? stats.GetComponent<PlayerCombat>() : null; _nextRefresh = 0; }
        private void ApplySettings()
        {
            var data = LocalGameSettings.Current;
            transform.localScale = Vector3.one * data.hudScale;
            var group = GetComponent<CanvasGroup>();
            bool inBattle = IsVisibleScene(SceneManager.GetActiveScene().name);
            if (group != null) { group.alpha = inBattle ? data.hudOpacity : 0; group.blocksRaycasts = inBattle; group.interactable = inBattle; }
            if (_keys.Length > 0) _keys[0].text = data.skill1.ToUpperInvariant();
            if (_keys.Length > 1) _keys[1].text = data.skill2.ToUpperInvariant();
        }
        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .05f;
            ApplySettings();
            if (!IsVisibleScene(SceneManager.GetActiveScene().name)) return;
            int count = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                var state = _combat != null ? _combat.GetSkillHudState(i) : new SkillHudState(false, "", i, 0, SkillHudPhase.Hidden, 0, 0);
                _slots[i].SetState(state);
                _slots[i].SetDescription(_combat != null ? _combat.GetSkillDescription(i) : string.Empty);
                _buttons[i].interactable = state.Visible && state.Phase == SkillHudPhase.Ready;
                count = state.SkillCount;
            }
            if (_slots.Length > 0) ((RectTransform)_slots[0].transform).anchoredPosition = count > 1 ? new Vector2(-182, 68) : new Vector2(-148, 112);
        }
        public static bool IsVisibleScene(string scene) => scene == "Lobby" || scene == "Battle" || scene == "Battle_waiting" || scene == "Battle_wait";
    }

}
