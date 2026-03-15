using DG.Tweening;
using UnityEngine;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// UI 흔들림 효과. RectTransform.anchoredPosition 기반.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UIShake : MonoBehaviour
    {
        [SerializeField] private float _defaultStrength = 10f;
        [SerializeField] private float _defaultDuration = 0.3f;
        [SerializeField] private int _vibrato = 10;

        private RectTransform _rect;
        private Vector2 _originalPosition;
        private Tweener _tweener;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _originalPosition = _rect.anchoredPosition;
        }

        public void Shake()
        {
            Shake(_defaultStrength, _defaultDuration);
        }

        public void Shake(float strength, float duration)
        {
            Stop();
            _rect.anchoredPosition = _originalPosition;
            _tweener = _rect.DOShakeAnchorPos(duration, strength, _vibrato, snapping: true)
                .SetUpdate(true)
                .OnComplete(() => _rect.anchoredPosition = _originalPosition);
        }

        public void Stop()
        {
            _tweener?.Kill();
            _tweener = null;
            if (_rect != null)
                _rect.anchoredPosition = _originalPosition;
        }

        private void OnDisable()
        {
            Stop();
        }
    }
}
