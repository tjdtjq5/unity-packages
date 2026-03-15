using System;
using DG.Tweening;
using Tjdtjq5.EditorToolkit;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 색상 구간 설정.
    /// </summary>
    [Serializable]
    public class ColorThreshold
    {
        [Range(0f, 1f)] public float threshold;
        public Color color = Color.white;
    }

    /// <summary>
    /// 프로그레스바. Slider 래핑 + Tween + 색상 구간 전환.
    /// HP바, 경험치바, 로딩바 등에 범용 사용.
    /// </summary>
    public class UIProgressBar : MonoBehaviour
    {
        [SectionHeader("Progress Bar", 0.5f, 0.85f, 0.55f)]
        [Required] [SerializeField] private Slider _slider;
        [SerializeField] private Image _fillImage;

        [BoxGroup("Tween")]
        [SerializeField] private float _tweenDuration = 0.3f;
        [BoxGroup("Tween")]
        [SerializeField] private Ease _tweenEase = Ease.OutQuad;

        [SectionHeader("Color Thresholds", 0.95f, 0.7f, 0.3f)]
        [SerializeField] private ColorThreshold[] _colorThresholds;

        private Tweener _tweener;

        /// <summary>현재 값 (0~1).</summary>
        public float Value => _slider != null ? _slider.value : 0f;

        /// <summary>
        /// 값 설정 (0~1). Tween 적용.
        /// </summary>
        public void SetValue(float value)
        {
            value = Mathf.Clamp01(value);
            _tweener?.Kill();
            _tweener = _slider.DOValue(value, _tweenDuration)
                .SetEase(_tweenEase)
                .SetUpdate(true)
                .OnUpdate(() => ApplyColor(_slider.value));
        }

        /// <summary>
        /// 값 즉시 설정 (Tween 없음).
        /// </summary>
        public void SetValueImmediate(float value)
        {
            value = Mathf.Clamp01(value);
            _tweener?.Kill();
            _slider.value = value;
            ApplyColor(value);
        }

        private void ApplyColor(float value)
        {
            if (_fillImage == null || _colorThresholds == null || _colorThresholds.Length == 0)
                return;

            // threshold 오름차순 정렬 가정: 값 이하인 첫 번째 구간의 색상 적용
            foreach (var ct in _colorThresholds)
            {
                if (value <= ct.threshold)
                {
                    _fillImage.color = ct.color;
                    return;
                }
            }

            // 모든 threshold를 초과하면 마지막 색상
            _fillImage.color = _colorThresholds[^1].color;
        }

        private void OnDisable()
        {
            _tweener?.Kill();
            _tweener = null;
        }
    }
}
