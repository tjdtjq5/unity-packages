using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// Accordion의 펼침/접힘 요소. Toggle 상속 — ToggleGroup의 일원으로 동작.
    /// 펼침/접힘은 LayoutElement.preferredHeight/Width 트윈으로 구현.
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(LayoutElement))]
    public class AccordionElement : Toggle
    {
        [SerializeField] private float _minHeight = 18f;
        [SerializeField] private float _minWidth = 40f;

        public float MinHeight => _minHeight;
        public float MinWidth => _minWidth;

        Accordion _accordion;
        RectTransform _rectTransform;
        LayoutElement _layoutElement;
        MotionHandle _tweenHandle;

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None;
            toggleTransition = ToggleTransition.None;
            _accordion = GetComponentInParent<Accordion>();
            _rectTransform = (RectTransform)transform;
            _layoutElement = GetComponent<LayoutElement>();
            onValueChanged.AddListener(OnValueChangedInternal);
        }

        protected override void OnDestroy()
        {
            _tweenHandle.TryCancel();
            onValueChanged.RemoveListener(OnValueChangedInternal);
            base.OnDestroy();
        }

        void OnValueChangedInternal(bool state)
        {
            if (_layoutElement == null || _accordion == null) return;

            var mode = _accordion.TransitionMode;
            var vertical = _accordion.ExpandVertical;

            if (mode == Accordion.Transition.Instant)
            {
                if (vertical)
                    _layoutElement.preferredHeight = state ? -1f : _minHeight;
                else
                    _layoutElement.preferredWidth = state ? -1f : _minWidth;
                return;
            }

            // Tween: 현재 크기 → 목표 크기로 보간
            if (vertical)
            {
                float from = state ? _minHeight : _rectTransform.rect.height;
                float to = state ? GetExpandedHeight() : _minHeight;
                StartTween(from, to, h => _layoutElement.preferredHeight = h);
            }
            else
            {
                float from = state ? _minWidth : _rectTransform.rect.width;
                float to = state ? GetExpandedWidth() : _minWidth;
                StartTween(from, to, w => _layoutElement.preferredWidth = w);
            }
        }

        void StartTween(float from, float to, System.Action<float> setter)
        {
            _tweenHandle.TryCancel();
            _tweenHandle = LMotion.Create(from, to, _accordion.TransitionDuration)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(setter);
        }

        float GetExpandedHeight()
        {
            float original = _layoutElement.preferredHeight;
            _layoutElement.preferredHeight = -1f;
            float h = LayoutUtility.GetPreferredHeight(_rectTransform);
            _layoutElement.preferredHeight = original;
            return h;
        }

        float GetExpandedWidth()
        {
            float original = _layoutElement.preferredWidth;
            _layoutElement.preferredWidth = -1f;
            float w = LayoutUtility.GetPreferredWidth(_rectTransform);
            _layoutElement.preferredWidth = original;
            return w;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _accordion = GetComponentInParent<Accordion>();

            if (group == null)
            {
                var tg = GetComponentInParent<ToggleGroup>();
                if (tg != null) group = tg;
            }

            // Inspector에서 isOn 토글하면 즉시 반영 (실행 전)
            var le = GetComponent<LayoutElement>();
            if (le != null && _accordion != null)
            {
                if (_accordion.ExpandVertical)
                    le.preferredHeight = isOn ? -1f : _minHeight;
                else
                    le.preferredWidth = isOn ? -1f : _minWidth;
            }
        }
#endif
    }
}
