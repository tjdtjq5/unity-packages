using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// Tooltip을 트리거. PC: hover (PointerEnter/Exit). 모바일: long-press (PointerDown 일정 시간 유지).
    /// PointerDown 후 임계 시간 안에 PointerUp이 안 오면 long-press로 판정.
    ///
    /// Origin: Unity-UI-Extensions TooltipTrigger (Martin Nerurkar, BSD-3).
    /// 우리 namespace + Singleton 제거 (참조 직접 와이어) + 모바일 long-press 추가.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TooltipTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        [SerializeField] private Tooltip _tooltip;
        [SerializeField, TextArea] private string _text;
        [SerializeField] private float _longPressSeconds = 0.5f;
        [SerializeField] private bool _enableHover = true;
        [SerializeField] private bool _enableLongPress = true;

        public string Text { get => _text; set => _text = value; }

        CancellationTokenSource _longPressCts;
        bool _isShowing;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_enableHover) return;
            ShowAt(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelLongPress();
            HideTooltip();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_enableLongPress) return;
            CancelLongPress();
            _longPressCts = new CancellationTokenSource();
            WaitLongPressAsync(eventData.position, _longPressCts.Token).Forget();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            CancelLongPress();
            // long-press로 표시되었으면 OnPointerExit가 자동 호출되지 않을 수 있음
            // 모바일에서는 손가락 떼면 숨김
            if (_isShowing && !_enableHover) HideTooltip();
        }

        public void OnSelect(BaseEventData eventData)
        {
            // 키보드/게임패드 selection 시 표시
            ShowAt(transform.position);
        }

        public void OnDeselect(BaseEventData eventData) => HideTooltip();

        async UniTask WaitLongPressAsync(Vector2 screenPos, CancellationToken ct)
        {
            try
            {
                await UniTask.WaitForSeconds(_longPressSeconds, cancellationToken: ct);
                if (!ct.IsCancellationRequested) ShowAt(screenPos);
            }
            catch (System.OperationCanceledException) { /* normal cancel */ }
        }

        void ShowAt(Vector2 screenPos)
        {
            if (_tooltip == null) return;
            _tooltip.Show(_text, screenPos);
            _isShowing = true;
        }

        void HideTooltip()
        {
            if (_tooltip != null && _isShowing) _tooltip.Hide();
            _isShowing = false;
        }

        void CancelLongPress()
        {
            _longPressCts?.Cancel();
            _longPressCts?.Dispose();
            _longPressCts = null;
        }

        void OnDisable()
        {
            CancelLongPress();
            HideTooltip();
        }
    }
}
