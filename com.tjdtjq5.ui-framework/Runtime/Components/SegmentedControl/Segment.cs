using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// SegmentedControl의 개별 segment. 자식 Selectable에 부착.
    /// 클릭 시 SegmentedControl에 선택 통지. 시각 상태(ColorTint)는 직접 적용.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public sealed class Segment : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        internal int Index;
        internal SegmentedControl SegmentedControl;

        Selectable _button;

        Selectable Button
        {
            get
            {
                if (_button == null) _button = GetComponent<Selectable>();
                return _button;
            }
        }

        public bool IsSelected => SegmentedControl != null && SegmentedControl.SelectedSegment == Button;

        void OnEnable()
        {
            if (SegmentedControl != null) ApplyVisualState(IsSelected);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            ToggleSelect();
        }

        public void OnSubmit(BaseEventData eventData) => ToggleSelect();

        void ToggleSelect()
        {
            if (Button.IsActive() && Button.IsInteractable())
                SegmentedControl?.NotifySelectionChanged(Button, isSelected: true);
        }

        /// <summary>SegmentedControl이 호출. 색상 tint를 selected/unselected 상태로 전환.</summary>
        internal void ApplyVisualState(bool selected)
        {
            if (Button.transition != Selectable.Transition.ColorTint) return;
            if (Button.targetGraphic == null) return;

            var colors = Button.colors;
            var tint = selected ? colors.pressedColor : colors.normalColor;
            Button.targetGraphic.CrossFadeColor(tint * colors.colorMultiplier, colors.fadeDuration, true, true);

            // 자식 텍스트 색 토글 (선택 시 normal, 미선택 시 pressed — 반전된 텍스트)
            var text = GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                var textColor = selected ? colors.normalColor : colors.pressedColor;
                text.color = textColor * colors.colorMultiplier;
            }
        }
    }
}
