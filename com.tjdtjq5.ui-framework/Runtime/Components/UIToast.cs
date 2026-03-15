using DG.Tweening;
using Tjdtjq5.EditorToolkit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 짧은 알림 메시지 토스트.
    /// 아래에서 위로 슬라이드 + 페이드 → 대기 → 페이드 아웃.
    /// </summary>
    public class UIToast : MonoBehaviour
    {
        [SectionHeader("Toast", 0.95f, 0.85f, 0.3f)]
        [Required] [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Image _icon;

        [BoxGroup("Timing")]
        [SerializeField] private float _displayDuration = 2f;
        [BoxGroup("Timing")]
        [SerializeField] private float _fadeDuration = 0.3f;

        [Separator]
        [SerializeField] private float _slideOffset = 50f;

        private RectTransform _rect;
        private CanvasGroup _canvasGroup;
        private Vector2 _originPosition;
        private Sequence _sequence;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _originPosition = _rect.anchoredPosition;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 메시지 표시.
        /// </summary>
        public void Show(string message)
        {
            ShowInternal(message, null);
        }

        /// <summary>
        /// 아이콘 + 메시지 표시.
        /// </summary>
        public void Show(string message, Sprite icon)
        {
            ShowInternal(message, icon);
        }

        private void ShowInternal(string message, Sprite iconSprite)
        {
            KillSequence();

            _messageText.text = message;

            if (_icon != null)
            {
                _icon.sprite = iconSprite;
                _icon.gameObject.SetActive(iconSprite != null);
            }

            gameObject.SetActive(true);
            _canvasGroup.alpha = 0f;
            _rect.anchoredPosition = _originPosition - new Vector2(0f, _slideOffset);

            _sequence = DOTween.Sequence()
                .Join(_canvasGroup.DOFade(1f, _fadeDuration))
                .Join(_rect.DOAnchorPos(_originPosition, _fadeDuration).SetEase(Ease.OutQuad))
                .AppendInterval(_displayDuration)
                .Append(_canvasGroup.DOFade(0f, _fadeDuration))
                .SetUpdate(true)
                .OnComplete(() => gameObject.SetActive(false));
        }

        private void KillSequence()
        {
            _sequence?.Kill();
            _sequence = null;
        }

        private void OnDisable()
        {
            KillSequence();
        }
    }
}
