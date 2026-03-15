using System;
using DG.Tweening;
using Tjdtjq5.EditorToolkit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 화살표 방향.
    /// </summary>
    public enum ArrowDirection
    {
        None,
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// 튜토리얼 스텝 데이터.
    /// </summary>
    [Serializable]
    public class TutorialStep
    {
        [Required] public RectTransform target;
        public string message;
        public ArrowDirection arrowDirection;
    }

    /// <summary>
    /// 튜토리얼 하이라이트 마스크.
    /// 화면 전체를 어둡게 덮고 특정 RectTransform만 구멍을 뚫어 강조.
    /// Stencil Buffer 기반 — 원형/둥근사각 등 HolePunch Image의 Sprite로 모양 결정.
    ///
    /// 셰이더:
    ///   - HolePunch Image → Material: Tjdtjq5/UI/TutorialMaskHolePunch
    ///   - Overlay Image  → Material: Tjdtjq5/UI/TutorialMaskOverlay
    /// </summary>
    public class UITutorialMask : MonoBehaviour
    {
        [SectionHeader("References", 0.4f, 0.75f, 0.95f)]
        [Required] [SerializeField] private Image _overlay;
        [Required] [SerializeField] private Image _holePunch;
        [SerializeField] private TMP_Text _guideText;
        [SerializeField] private Image _arrow;
        [Required] [SerializeField] private Button _overlayButton;

        [SectionHeader("Settings", 0.95f, 0.7f, 0.3f)]
        [SerializeField] private float _padding = 20f;
        [SerializeField] private float _fadeDuration = 0.3f;
        [SerializeField] private float _arrowOffset = 30f;

        private RectTransform _holePunchRect;
        private RectTransform _arrowRect;
        private RectTransform _guideRect;
        private CanvasGroup _canvasGroup;
        private Canvas _rootCanvas;

        private Action _onClicked;
        private TutorialStep[] _sequenceSteps;
        private Action _onSequenceComplete;
        private int _sequenceIndex;
        private Tweener _fadeTween;

        private void Awake()
        {
            _holePunchRect = _holePunch.GetComponent<RectTransform>();
            _arrowRect = _arrow != null ? _arrow.GetComponent<RectTransform>() : null;
            _guideRect = _guideText != null ? _guideText.GetComponent<RectTransform>() : null;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

            if (_overlayButton != null)
                _overlayButton.onClick.AddListener(OnOverlayClicked);

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 타겟을 하이라이트하고 안내 텍스트 표시.
        /// </summary>
        public void Show(RectTransform target, string message, Action onClicked = null)
        {
            Show(target, message, ArrowDirection.None, onClicked);
        }

        /// <summary>
        /// 타겟을 하이라이트 + 화살표 방향 지정.
        /// </summary>
        public void Show(RectTransform target, string message,
            ArrowDirection arrowDir, Action onClicked = null)
        {
            _onClicked = onClicked;
            gameObject.SetActive(true);

            PositionHole(target);
            SetGuideText(message);
            SetArrow(arrowDir, target);
            FadeIn();
        }

        /// <summary>
        /// 마스크 닫기.
        /// </summary>
        public void Hide()
        {
            _fadeTween?.Kill();
            _fadeTween = _canvasGroup.DOFade(0f, _fadeDuration)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    gameObject.SetActive(false);
                    _onClicked = null;
                });
        }

        /// <summary>
        /// 여러 스텝을 순차 실행. 각 스텝 클릭 시 다음으로.
        /// </summary>
        public void ShowSequence(TutorialStep[] steps, Action onComplete = null)
        {
            if (steps == null || steps.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            _sequenceSteps = steps;
            _onSequenceComplete = onComplete;
            _sequenceIndex = 0;
            ShowStep(_sequenceIndex);
        }

        private void ShowStep(int index)
        {
            if (index >= _sequenceSteps.Length)
            {
                _sequenceSteps = null;
                Hide();
                _onSequenceComplete?.Invoke();
                _onSequenceComplete = null;
                return;
            }

            var step = _sequenceSteps[index];
            Show(step.target, step.message, step.arrowDirection, () =>
            {
                _sequenceIndex++;
                ShowStep(_sequenceIndex);
            });
        }

        private void PositionHole(RectTransform target)
        {
            if (target == null || _holePunchRect == null || _rootCanvas == null) return;

            var canvasRect = _rootCanvas.transform as RectTransform;

            // 타겟의 4 코너 → 캔버스 로컬 좌표
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Vector2 min = canvasRect.InverseTransformPoint(corners[0]);
            Vector2 max = canvasRect.InverseTransformPoint(corners[2]);

            var center = (min + max) * 0.5f;
            var size = max - min;

            _holePunchRect.anchoredPosition = center;
            _holePunchRect.sizeDelta = new Vector2(
                Mathf.Abs(size.x) + _padding * 2f,
                Mathf.Abs(size.y) + _padding * 2f);
        }

        private void SetGuideText(string message)
        {
            if (_guideText != null)
                _guideText.text = message ?? "";
        }

        private void SetArrow(ArrowDirection dir, RectTransform target)
        {
            if (_arrow == null || _arrowRect == null) return;

            if (dir == ArrowDirection.None)
            {
                _arrow.gameObject.SetActive(false);
                return;
            }

            _arrow.gameObject.SetActive(true);

            // 화살표 위치: 구멍 가장자리 + offset
            var holePos = _holePunchRect.anchoredPosition;
            var holeSize = _holePunchRect.sizeDelta * 0.5f;

            float rotation = 0f;
            Vector2 arrowPos = holePos;

            switch (dir)
            {
                case ArrowDirection.Up:
                    arrowPos.y = holePos.y + holeSize.y + _arrowOffset;
                    rotation = 180f;
                    break;
                case ArrowDirection.Down:
                    arrowPos.y = holePos.y - holeSize.y - _arrowOffset;
                    rotation = 0f;
                    break;
                case ArrowDirection.Left:
                    arrowPos.x = holePos.x - holeSize.x - _arrowOffset;
                    rotation = -90f;
                    break;
                case ArrowDirection.Right:
                    arrowPos.x = holePos.x + holeSize.x + _arrowOffset;
                    rotation = 90f;
                    break;
            }

            _arrowRect.anchoredPosition = arrowPos;
            _arrowRect.localRotation = Quaternion.Euler(0f, 0f, rotation);

            // 가이드 텍스트도 화살표 근처에 배치
            if (_guideRect != null)
            {
                var textPos = arrowPos;
                switch (dir)
                {
                    case ArrowDirection.Up:    textPos.y += 40f; break;
                    case ArrowDirection.Down:  textPos.y -= 40f; break;
                    case ArrowDirection.Left:  textPos.x -= 40f; break;
                    case ArrowDirection.Right: textPos.x += 40f; break;
                }
                _guideRect.anchoredPosition = textPos;
            }
        }

        private void FadeIn()
        {
            _fadeTween?.Kill();
            _canvasGroup.alpha = 0f;
            _fadeTween = _canvasGroup.DOFade(1f, _fadeDuration)
                .SetUpdate(true);
        }

        private void OnOverlayClicked()
        {
            var callback = _onClicked;
            if (callback != null)
            {
                _onClicked = null;
                callback.Invoke();
            }
            else
            {
                Hide();
            }
        }

        private void OnDisable()
        {
            _fadeTween?.Kill();
            _fadeTween = null;
        }
    }
}
