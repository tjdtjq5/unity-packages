using Tjdtjq5.EditorToolkit;
using UnityEngine;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 월드 오브젝트 위치를 따라가는 UI.
    /// HP바, 이름표, 말풍선 등에 사용.
    /// </summary>
    public class UIFollowWorld : MonoBehaviour
    {
        [SectionHeader("Follow World", 0.4f, 0.75f, 0.95f)]
        [Required] [SerializeField] private Canvas _rootCanvas;
        [SerializeField] private Camera _worldCamera;
        [SerializeField] private Vector2 _screenOffset;
        [SerializeField] private bool _hideWhenBehind = true;

        private Transform _target;
        private Vector3 _worldOffset;
        private RectTransform _rect;
        private CanvasGroup _canvasGroup;
        private RectTransform _canvasRect;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>
        /// 타겟 설정.
        /// </summary>
        /// <param name="target">따라갈 월드 Transform</param>
        /// <param name="worldOffset">타겟 기준 월드 좌표 오프셋 (예: Vector3.up * 2 = 머리 위)</param>
        public void SetTarget(Transform target, Vector3 worldOffset = default)
        {
            _target = target;
            _worldOffset = worldOffset;
            gameObject.SetActive(target != null);
        }

        /// <summary>
        /// 타겟 해제.
        /// </summary>
        public void ClearTarget()
        {
            _target = null;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                SetVisible(false);
                return;
            }

            var cam = _worldCamera != null ? _worldCamera : Camera.main;
            if (cam == null) return;

            if (_canvasRect == null && _rootCanvas != null)
                _canvasRect = _rootCanvas.transform as RectTransform;
            if (_canvasRect == null) return;

            var worldPos = _target.position + _worldOffset;
            var screenPos = cam.WorldToScreenPoint(worldPos);

            // 카메라 뒤에 있으면 숨김
            if (_hideWhenBehind && screenPos.z < 0f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPos, _rootCanvas.worldCamera, out var localPoint);

            _rect.anchoredPosition = localPoint + _screenOffset;
        }

        private void SetVisible(bool visible)
        {
            if (_canvasGroup.alpha > 0f != visible)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.blocksRaycasts = visible;
            }
        }
    }
}
