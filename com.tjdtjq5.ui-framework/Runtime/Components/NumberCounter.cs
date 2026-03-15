using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 숫자 카운터 애니메이션. TMP_Text에 부착하여 사용.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class NumberCounter : MonoBehaviour
    {
        [SerializeField] private float _duration = 0.5f;
        [SerializeField] private string _format = "N0";
        [SerializeField] private string _prefix;
        [SerializeField] private string _suffix;

        private TMP_Text _text;
        private float _currentValue;
        private Tweener _tweener;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        public void SetValue(float target)
        {
            _tweener?.Kill();
            _tweener = DOTween.To(() => _currentValue, x =>
                {
                    _currentValue = x;
                    UpdateText();
                }, target, _duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        public void SetValueImmediate(float value)
        {
            _tweener?.Kill();
            _currentValue = value;
            UpdateText();
        }

        private void UpdateText()
        {
            if (_text != null)
                _text.text = $"{_prefix}{_currentValue.ToString(_format)}{_suffix}";
        }

        private void OnDisable()
        {
            _tweener?.Kill();
            _tweener = null;
        }
    }
}
