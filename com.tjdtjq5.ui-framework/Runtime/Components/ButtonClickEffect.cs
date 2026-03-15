using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 버튼 스케일 펀치 효과. IPointerDown/Up 기반.
    /// </summary>
    public class ButtonClickEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float _pressedScale = 0.9f;
        [SerializeField] private float _pressDuration = 0.08f;
        [SerializeField] private float _releaseDuration = 0.1f;

        private Vector3 _originalScale;
        private Tweener _tweener;

        private void Awake()
        {
            _originalScale = transform.localScale;

            if (_originalScale == Vector3.zero)
                _originalScale = Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _tweener?.Kill();
            _tweener = transform
                .DOScale(_originalScale * _pressedScale, _pressDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _tweener?.Kill();
            _tweener = transform
                .DOScale(_originalScale, _releaseDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        private void OnDisable()
        {
            _tweener?.Kill();
            _tweener = null;
            transform.localScale = _originalScale;
        }
    }
}
