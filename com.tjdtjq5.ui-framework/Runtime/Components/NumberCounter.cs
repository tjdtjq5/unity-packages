using LitMotion;
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
        private MotionHandle _handle;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        public void SetValue(float target)
        {
            _handle.TryCancel();
            _handle = LMotion.Create(_currentValue, target, _duration)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(x =>
                {
                    _currentValue = x;
                    UpdateText();
                });
        }

        public void SetValueImmediate(float value)
        {
            _handle.TryCancel();
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
            _handle.TryCancel();
        }
    }
}
