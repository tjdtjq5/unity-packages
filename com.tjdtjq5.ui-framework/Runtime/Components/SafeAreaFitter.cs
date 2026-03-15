using UnityEngine;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 모바일 노치/SafeArea 대응.
    /// RectTransform의 anchor를 Screen.safeArea에 맞게 자동 조정.
    /// Screen.safeArea는 이벤트를 제공하지 않아 Update 폴링이 필수.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        [SerializeField] private bool _applyHorizontal = true;
        [SerializeField] private bool _applyVertical = true;

        private RectTransform _rect;
        private Rect _lastSafeArea;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
        }

        private void Update()
        {
            var safeArea = Screen.safeArea;
            if (safeArea == _lastSafeArea) return;

            _lastSafeArea = safeArea;
            ApplySafeArea(safeArea);
        }

        private void ApplySafeArea(Rect safeArea)
        {
            if (_rect == null) return;

            var screenW = Screen.width;
            var screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0) return;

            var anchorMin = new Vector2(safeArea.xMin / screenW, safeArea.yMin / screenH);
            var anchorMax = new Vector2(safeArea.xMax / screenW, safeArea.yMax / screenH);

            if (!_applyHorizontal)
            {
                anchorMin.x = _rect.anchorMin.x;
                anchorMax.x = _rect.anchorMax.x;
            }

            if (!_applyVertical)
            {
                anchorMin.y = _rect.anchorMin.y;
                anchorMax.y = _rect.anchorMax.y;
            }

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
        }
    }
}
