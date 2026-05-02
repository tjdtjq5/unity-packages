using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.UIFramework.Screens.Core.Transitions;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Screens.Modal
{
    /// <summary>
    /// Modal backdrop. CanvasGroup 기반 alpha 전환, 클릭 시 modal 닫기 옵션.
    /// ModalContainer가 BackdropStrategy에 따라 인스턴스화·재사용·제거.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModalBackdrop : MonoBehaviour
    {
        [SerializeField] private TransitionAnimationObject _enterAnimation;
        [SerializeField] private TransitionAnimationObject _exitAnimation;
        [SerializeField] private bool _closeModalWhenClicked;

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private RectTransform _parentTransform;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            if (_closeModalWhenClicked)
                EnsureClickToClose();
        }

        private void EnsureClickToClose()
        {
            if (!TryGetComponent<Image>(out var image))
            {
                image = gameObject.AddComponent<Image>();
                image.color = Color.clear;
            }

            if (!TryGetComponent<Button>(out var button))
            {
                button = gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            }

            button.onClick.AddListener(OnBackdropClicked);
        }

        private void OnBackdropClicked()
        {
            // 백드롭이 부모로 ModalContainer를 가질 때만 동작
            var container = GetComponentInParent<ModalContainer>();
            if (container == null || container.IsInTransition) return;
            container.PopAsync().Forget();
        }

        /// <summary>ModalContainer 또는 BackdropHandler가 호출. backdrop을 부모에 부착.</summary>
        internal void Setup(RectTransform parentTransform, int modalIndex)
        {
            _parentTransform = parentTransform;
            _rectTransform.SetParent(_parentTransform, false);
            FillParent();
            _canvasGroup.interactable = _closeModalWhenClicked;
            gameObject.SetActive(false);
        }

        internal async UniTask EnterAsync(bool playAnimation, CancellationToken ct = default)
        {
            gameObject.SetActive(true);
            FillParent();
            _canvasGroup.alpha = 1f;

            if (!playAnimation || _enterAnimation == null) return;

            _enterAnimation.Setup(_rectTransform);
            _enterAnimation.SetPartner(null);
            await _enterAnimation.PlayAsync(progress: null, ct: ct);
        }

        internal async UniTask ExitAsync(bool playAnimation, CancellationToken ct = default)
        {
            if (playAnimation && _exitAnimation != null)
            {
                _exitAnimation.Setup(_rectTransform);
                _exitAnimation.SetPartner(null);
                await _exitAnimation.PlayAsync(progress: null, ct: ct);
            }

            _canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void FillParent()
        {
            if (_parentTransform == null) return;
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.one;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
            _rectTransform.localScale = Vector3.one;
        }
    }
}
