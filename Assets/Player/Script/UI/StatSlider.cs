using System;
using BattlePvp.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BattlePvp.UI
{
    /// <summary>
    /// "50pt 분배기" 전용 슬라이더.
    /// - 입력(투자)은 0..30
    /// - 시각화는 듀얼 Fill: Pure(투자) / Item(보너스)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatSlider : MonoBehaviour
    {
        private const float MaxInvested = 30f;
        private const float MaxItem = 10f;
        private const float MaxVisualTotal = MaxInvested + MaxItem; // 40

        [Header("Identity")]
        [SerializeField] private StatKind _kind;

        [Header("UI")]
        [SerializeField] private Slider _slider;
        [Tooltip("바닥 Fill(순수 투자) 이미지. Image Type=Filled 권장")]
        [SerializeField] private Image _pureFill;
        [Tooltip("상단 Fill(아이템 보너스 포함) 이미지. Image Type=Filled 권장")]
        [SerializeField] private Image _itemFill;
        [SerializeField] private TMP_Text _valueText;

        public StatKind Kind => _kind;
        public float Invested => _slider != null ? _slider.value : 0f;

        public event Action<StatSlider, float> InvestedChanged;

        private float _item;

        private void Awake()
        {
            if (_slider != null)
            {
                _slider.minValue = 0f;
                _slider.maxValue = MaxInvested;
            }
        }

        private void OnEnable()
        {
            if (_slider != null)
                _slider.onValueChanged.AddListener(OnSliderChanged);

            RefreshVisual();
        }

        private void OnDisable()
        {
            if (_slider != null)
                _slider.onValueChanged.RemoveListener(OnSliderChanged);
        }

        public void SetItem(float item)
        {
            _item = Clamp(item, 0f, MaxItem);
            RefreshVisual();
        }

        public void SetInvestedWithoutNotify(float invested)
        {
            if (_slider == null)
                return;

            _slider.SetValueWithoutNotify(Clamp(invested, 0f, MaxInvested));
            RefreshVisual();
        }

        public void SetMaxInvestedInteractable(float max)
        {
            if (_slider == null)
                return;

            float m = Clamp(max, 0f, MaxInvested);
            _slider.maxValue = m;
            if (_slider.value > m)
                _slider.SetValueWithoutNotify(m);

            RefreshVisual();
        }

        private void OnSliderChanged(float value)
        {
            RefreshVisual();
            InvestedChanged?.Invoke(this, value);
        }

        private void RefreshVisual()
        {
            float invested = _slider != null ? _slider.value : 0f;
            float pure01 = Clamp01(invested / MaxVisualTotal);
            float total01 = Clamp01((invested + _item) / MaxVisualTotal);

            if (_pureFill != null) _pureFill.fillAmount = pure01;
            if (_itemFill != null) _itemFill.fillAmount = total01;

            if (_valueText != null)
                _valueText.text = $"{(int)invested} / {(int)MaxInvested}";
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}

