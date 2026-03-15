using System;
using DG.Tweening;
using Tjdtjq5.EditorToolkit;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// UI 베지어 곡선 비행 연출.
    /// 아이콘이 시작점에서 목표 RectTransform까지 포물선으로 날아가는 효과.
    /// 코인 획득, 아이템 획득, 별 수집 등 범용 사용.
    /// </summary>
    public class UIFlyEffect : MonoBehaviour
    {
        [SectionHeader("Fly Effect", 0.95f, 0.7f, 0.3f)]
        [SerializeField] private Image _flyIcon;
        [SerializeField] private float _duration = 0.6f;
        [SerializeField] private float _curveHeight = 150f;

        private RectTransform _rectTransform;
        private Canvas _rootCanvas;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 아이콘 + 시작점 → 목표까지 베지어 비행.
        /// </summary>
        public void Play(Sprite icon, Vector2 fromScreen, RectTransform target, Action onComplete = null)
        {
            if (_rootCanvas == null) return;

            gameObject.SetActive(true);

            if (_flyIcon != null)
            {
                _flyIcon.sprite = icon;
                _flyIcon.enabled = icon != null;
            }

            PlayInternal(fromScreen, target, onComplete);
        }

        /// <summary>
        /// 아이콘 없이 RectTransform 자체가 시작점에서 목표까지 비행.
        /// </summary>
        public void Play(Vector2 fromScreen, RectTransform target, Action onComplete = null)
        {
            if (_rootCanvas == null) return;

            gameObject.SetActive(true);
            PlayInternal(fromScreen, target, onComplete);
        }

        private void PlayInternal(Vector2 fromScreen, RectTransform target, Action onComplete)
        {
            var canvasRect = _rootCanvas.transform as RectTransform;

            // 시작점 (스크린 → 캔버스 로컬)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, fromScreen, _rootCanvas.worldCamera, out var fromLocal);

            // 도착점 (타겟 → 캔버스 로컬)
            var targetScreenPos = RectTransformUtility.WorldToScreenPoint(
                _rootCanvas.worldCamera, target.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, targetScreenPos, _rootCanvas.worldCamera, out var toLocal);

            // 베지어 제어점 (중간 위로 올라감)
            var controlPoint = (fromLocal + toLocal) * 0.5f + Vector2.up * _curveHeight;

            _rectTransform.anchoredPosition = fromLocal;
            _rectTransform.localScale = Vector3.one;

            float t = 0f;
            DOTween.To(() => t, v =>
                {
                    t = v;
                    // 쿼드라틱 베지어: B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2
                    float oneMinusT = 1f - t;
                    var pos = oneMinusT * oneMinusT * fromLocal
                              + 2f * oneMinusT * t * controlPoint
                              + t * t * toLocal;
                    _rectTransform.anchoredPosition = pos;

                    // 도착에 가까울수록 축소
                    float scale = Mathf.Lerp(1f, 0.5f, t);
                    _rectTransform.localScale = Vector3.one * scale;
                }, 1f, _duration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    gameObject.SetActive(false);
                    onComplete?.Invoke();
                });
        }
    }
}
