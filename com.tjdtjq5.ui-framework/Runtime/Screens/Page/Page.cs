using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.UIFramework.Screens.Core.Transitions;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Page
{
    /// <summary>
    /// Page 베이스. PageContainer가 push/pop과 lifecycle을 관리.
    /// 사용처는 이 클래스를 상속해 lifecycle override.
    ///
    /// 4종 transition slot:
    /// - PushEnter: 새 page 진입 시
    /// - PushExit: 새 page push로 인해 이전 page가 나갈 때
    /// - PopEnter: pop으로 page가 다시 보일 때
    /// - PopExit: pop으로 현재 page가 나갈 때
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Page : MonoBehaviour, IPageLifecycle
    {
        [SerializeField] private TransitionAnimationObject _pushEnterAnimation;
        [SerializeField] private TransitionAnimationObject _pushExitAnimation;
        [SerializeField] private TransitionAnimationObject _popEnterAnimation;
        [SerializeField] private TransitionAnimationObject _popExitAnimation;

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;

        /// <summary>PageContainer가 등록 시 할당하는 페이지 식별자.</summary>
        public string PageId { get; internal set; }

        /// <summary>현재 push/pop transition 진행 중 여부.</summary>
        public bool IsTransitioning { get; private set; }

        // ─── IPageLifecycle (사용처가 override) ─────────────

        public virtual UniTask Initialize() => UniTask.CompletedTask;
        public virtual UniTask WillPushEnter() => UniTask.CompletedTask;
        public virtual void DidPushEnter() { }
        public virtual UniTask WillPushExit() => UniTask.CompletedTask;
        public virtual void DidPushExit() { }
        public virtual UniTask WillPopEnter() => UniTask.CompletedTask;
        public virtual void DidPopEnter() { }
        public virtual UniTask WillPopExit() => UniTask.CompletedTask;
        public virtual void DidPopExit() { }
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

        internal async UniTask BeforePushEnterAsync()
        {
            IsTransitioning = true;
            gameObject.SetActive(true);
            FillParent();
            _canvasGroup.alpha = 0f;
            await WillPushEnter();
        }

        internal UniTask PushEnterAsync(bool playAnimation, Page partner, CancellationToken ct)
        {
            return PlayAsync(_pushEnterAnimation, playAnimation, partner, alphaTo: 1f, ct);
        }

        internal void AfterPushEnter()
        {
            DidPushEnter();
            IsTransitioning = false;
        }

        internal async UniTask BeforePushExitAsync()
        {
            IsTransitioning = true;
            gameObject.SetActive(true);
            FillParent();
            await WillPushExit();
        }

        internal UniTask PushExitAsync(bool playAnimation, Page partner, CancellationToken ct)
        {
            return PlayAsync(_pushExitAnimation, playAnimation, partner, alphaTo: 0f, ct);
        }

        internal void AfterPushExit()
        {
            DidPushExit();
            gameObject.SetActive(false);
            IsTransitioning = false;
        }

        internal async UniTask BeforePopEnterAsync()
        {
            IsTransitioning = true;
            gameObject.SetActive(true);
            FillParent();
            _canvasGroup.alpha = 0f;
            await WillPopEnter();
        }

        internal UniTask PopEnterAsync(bool playAnimation, Page partner, CancellationToken ct)
        {
            return PlayAsync(_popEnterAnimation, playAnimation, partner, alphaTo: 1f, ct);
        }

        internal void AfterPopEnter()
        {
            DidPopEnter();
            IsTransitioning = false;
        }

        internal async UniTask BeforePopExitAsync()
        {
            IsTransitioning = true;
            gameObject.SetActive(true);
            FillParent();
            await WillPopExit();
        }

        internal UniTask PopExitAsync(bool playAnimation, Page partner, CancellationToken ct)
        {
            return PlayAsync(_popExitAnimation, playAnimation, partner, alphaTo: 0f, ct);
        }

        internal void AfterPopExit()
        {
            DidPopExit();
            gameObject.SetActive(false);
            IsTransitioning = false;
        }

        internal UniTask BeforeReleaseAsync() => Cleanup();

        // ─── 내부 헬퍼 ──────────────────────────────────────

        private async UniTask PlayAsync(
            TransitionAnimationObject animation,
            bool playAnimation,
            Page partner,
            float alphaTo,
            CancellationToken ct)
        {
            // 애니메이션 없이 즉시 alpha 전환
            if (!playAnimation || animation == null)
            {
                _canvasGroup.alpha = alphaTo;
                return;
            }

            _canvasGroup.alpha = 1f; // 애니메이션 자체가 alpha 처리
            animation.Setup(_rectTransform);
            animation.SetPartner(partner != null ? (RectTransform)partner.transform : null);
            await animation.PlayAsync(progress: null, ct: ct);
        }

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
