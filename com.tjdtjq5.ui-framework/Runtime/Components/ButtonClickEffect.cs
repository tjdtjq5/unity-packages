using LitMotion;
using LitMotion.Extensions;
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
        private MotionHandle _handle;

        private void Awake()
        {
            _originalScale = transform.localScale;

            if (_originalScale == Vector3.zero)
                _originalScale = Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _handle.TryCancel();
            _handle = LMotion.Create(transform.localScale, _originalScale * _pressedScale, _pressDuration)
                .WithEase(Ease.InQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToLocalScale(transform);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _handle.TryCancel();
            _handle = LMotion.Create(transform.localScale, _originalScale, _releaseDuration)
                .WithEase(Ease.OutBack)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToLocalScale(transform);
        }

        private void OnDisable()
        {
            _handle.TryCancel();
            transform.localScale = _originalScale;
        }
    }
}
