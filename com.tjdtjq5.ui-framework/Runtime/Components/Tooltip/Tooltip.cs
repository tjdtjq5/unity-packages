using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// 툴팁 박스. TooltipTrigger가 호출하여 텍스트 + 위치 설정.
    /// ScreenSpaceOverlay 캔버스 기준 화면 경계 자동 보정.
    ///
    /// 사용:
    /// 1. Canvas의 자식으로 Tooltip prefab 배치 (CanvasGroup + Image 배경 + TMP_Text 자식)
    /// 2. TooltipTrigger에서 _tooltip 슬롯에 이 인스턴스 와이어
    /// 3. trigger의 호버/long-press 시 자동 표시
    ///
    /// Origin: Unity-UI-Extensions ToolTip (Emiliano Pastorelli, BSD-3).
    /// 우리 namespace + Singleton 제거 + TMP_Text 강제.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class Tooltip : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private Vector2 _offset = new(10f, 10f);

        Canvas _canvas;
        RectTransform _canvasRect;
        RectTransform _rect;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null)
            {
                _canvas = _canvas.rootCanvas;
                _canvasRect = (RectTransform)_canvas.transform;
            }

            if (_text == null) _text = GetComponentInChildren<TMP_Text>();

            gameObject.SetActive(false);
        }

        /// <summary>지정 화면 좌표 근처에 툴팁 표시. 화면 경계 보정 자동.</summary>
        public void Show(string content, Vector2 screenPos)
        {
            if (_text != null) _text.text = content;
            gameObject.SetActive(true);

            ApplyPosition(screenPos);
        }

        public void Hide() => gameObject.SetActive(false);

        public bool IsVisible => gameObject.activeSelf;

        void ApplyPosition(Vector2 screenPos)
        {
            if (_canvas == null) return;

            // ScreenSpaceOverlay/Camera 모두에서 동작하도록 ScreenPointToLocalPointInRectangle 사용
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPos + _offset, cam, out var localPos);

            // 화면 경계 보정 (캔버스 사이즈 기준)
            var canvasSize = _canvasRect.rect.size;
            var canvasPivot = _canvasRect.pivot;
            var size = _rect.rect.size;
            var pivot = _rect.pivot;

            // 캔버스 내부 경계 (0,0)이 좌측 하단이라 가정 (anchorMin=0, anchorMax=0)
            float minX = -canvasSize.x * canvasPivot.x;
            float maxX = canvasSize.x * (1f - canvasPivot.x);
            float minY = -canvasSize.y * canvasPivot.y;
            float maxY = canvasSize.y * (1f - canvasPivot.y);

            float left = localPos.x - size.x * pivot.x;
            float right = localPos.x + size.x * (1f - pivot.x);
            float bottom = localPos.y - size.y * pivot.y;
            float top = localPos.y + size.y * (1f - pivot.y);

            if (right > maxX) localPos.x -= right - maxX;
            if (left < minX) localPos.x += minX - left;
            if (top > maxY) localPos.y -= top - maxY;
            if (bottom < minY) localPos.y += minY - bottom;

            _rect.anchoredPosition = localPos;
        }
    }
}
