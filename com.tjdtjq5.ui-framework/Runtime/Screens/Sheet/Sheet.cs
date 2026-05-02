using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.UIFramework.Screens.Core.Transitions;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Sheet
{
    /// <summary>
    /// Sheet 베이스. SheetContainer가 등록·표시·숨김을 관리.
    /// 사용처는 이 클래스를 상속해 lifecycle override.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Sheet : MonoBehaviour, ISheetLifecycle
    {
        [SerializeField] private TransitionAnimationObject _enterAnimation;
        [SerializeField] private TransitionAnimationObject _exitAnimation;

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;

        /// <summary>현재 전환(enter/exit) 진행 중 여부.</summary>
        public bool IsTransitioning { get; private set; }

        // ─── ISheetLifecycle (사용처가 override) ─────────────

        public virtual UniTask Initialize() => UniTask.CompletedTask;
        public virtual UniTask WillEnter() => UniTask.CompletedTask;
        public virtual void DidEnter() { }
        public virtual UniTask WillExit() => UniTask.CompletedTask;
        public virtual void DidExit() { }
        public virtual UniTask Cleanup() => UniTask.CompletedTask;

        // ─── 컨테이너가 호출하는 internal 단계 ───────────────

        internal async UniTask AfterLoadAsync(RectTransform parentTransform)
        {
            _rectTransform = (RectTransform)transform;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _rectTransform.SetParent(parentTransform, false);
            FillParent();
            gameObject.SetActive(false);
            _canvasGroup.alpha = 0f;

            await Initialize();
        }

        internal async UniTask BeforeEnterAsync()
        {
            IsTransitioning = true;
            gameObject.SetActive(true);
            FillParent();
            _canvasGroup.alpha = 0f;
            await WillEnter();
        }

        internal async UniTask EnterAsync(bool playAnimation, CancellationToken ct)
        {
            _canvasGroup.alpha = 1f;

            if (playAnimation && _enterAnimation != null)
            {
                _enterAnimation.Setup(_rectTransform);
                _enterAnimation.SetPartner(null);
                await _enterAnimation.PlayAsync(progress: null, ct: ct);
            }

            FillParent();
        }

        internal void AfterEnter()
        {
            DidEnter();
            IsTransitioning = false;
        }

        internal async UniTask BeforeExitAsync()
        {
            IsTransitioning = true;
            gameObject.SetActive(true);
            FillParent();
            await WillExit();
        }

        internal async UniTask ExitAsync(bool playAnimation, CancellationToken ct)
        {
            if (playAnimation && _exitAnimation != null)
            {
                _exitAnimation.Setup(_rectTransform);
                _exitAnimation.SetPartner(null);
                await _exitAnimation.PlayAsync(progress: null, ct: ct);
            }

            _canvasGroup.alpha = 0f;
        }

        internal void AfterExit()
        {
            DidExit();
            gameObject.SetActive(false);
            IsTransitioning = false;
        }

        internal UniTask BeforeReleaseAsync() => Cleanup();

        // ─── 내부 헬퍼 ──────────────────────────────────────

        private void FillParent()
        {
            if (_rectTransform == null) return;
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.one;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
            _rectTransform.localScale = Vector3.one;
        }
    }
}
