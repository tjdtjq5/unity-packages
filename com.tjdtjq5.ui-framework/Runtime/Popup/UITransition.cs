using System;
using DG.Tweening;
using Tjdtjq5.EditorToolkit;
using UnityEngine;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 패널 열기/닫기 전환.
    /// Animator가 있으면 SetTrigger("Open"/"Close"), 없으면 DOTween Scale+Fade 폴백.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UITransition : MonoBehaviour
    {
        [BoxGroup("Duration")]
        [SerializeField] private float _openDuration = 0.25f;
        [BoxGroup("Duration")]
        [SerializeField] private float _closeDuration = 0.2f;

        [BoxGroup("Ease")]
        [SerializeField] private Ease _openEase = Ease.OutBack;
        [BoxGroup("Ease")]
        [SerializeField] private Ease _closeEase = Ease.InBack;

        [Separator]
        [SerializeField] private float _openFromScale = 0.8f;
        [SerializeField] private bool _openOnEnable;

        public event Action OnOpenComplete;
        public event Action OnCloseComplete;

        private CanvasGroup _canvasGroup;
        private Animator _animator;
        private Sequence _sequence;

        private static readonly int TriggerOpen = Animator.StringToHash("Open");
        private static readonly int TriggerClose = Animator.StringToHash("Close");

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (_openOnEnable)
                Open();
        }

        public void Open()
        {
            gameObject.SetActive(true);
            KillSequence();

            if (_animator != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
                _animator.SetTrigger(TriggerOpen);
                OnOpenComplete?.Invoke();
                return;
            }

            transform.localScale = Vector3.one * _openFromScale;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            _sequence = DOTween.Sequence()
                .Join(transform.DOScale(Vector3.one, _openDuration).SetEase(_openEase))
                .Join(_canvasGroup.DOFade(1f, _openDuration))
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                    OnOpenComplete?.Invoke();
                });
        }

        public void Close()
        {
            KillSequence();

            if (_animator != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
                _animator.SetTrigger(TriggerClose);
                OnCloseComplete?.Invoke();
                gameObject.SetActive(false);
                return;
            }

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            _sequence = DOTween.Sequence()
                .Join(transform.DOScale(Vector3.one * _openFromScale, _closeDuration).SetEase(_closeEase))
                .Join(_canvasGroup.DOFade(0f, _closeDuration))
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    gameObject.SetActive(false);
                    OnCloseComplete?.Invoke();
                });
        }

        public void SetOpenImmediate()
        {
            KillSequence();
            gameObject.SetActive(true);
            transform.localScale = Vector3.one;
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }

        public void SetCloseImmediate()
        {
            KillSequence();
            transform.localScale = Vector3.one * _openFromScale;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
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
