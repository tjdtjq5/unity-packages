using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.UIFramework.Screens.Core.Transitions;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Modal
{
    /// <summary>
    /// Modal 베이스. ModalContainer가 stack 관리.
    ///
    /// Page와의 핵심 차이:
    /// - PushExit / PopEnter: 시각 변화 없음 (Modal은 background에서도 보이는 상태 유지)
    /// - PushEnter (alpha 0→1) / PopExit (alpha 1→0)만 애니메이션 동반
    /// - 따라서 SerializeField slot은 2개 (enter/exit)
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Modal : MonoBehaviour, IModalLifecycle
    {
        [SerializeField] private TransitionAnimationObject _enterAnimation;
        [SerializeField] private TransitionAnimationObject _exitAnimation;

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;

        /// <summary>ModalContainer가 등록 시 할당하는 modal 식별자.</summary>
        public string ModalId { get; internal set; }

        /// <summary>현재 push/pop transition 진행 중 여부.</summary>
        public bool IsTransitioning { get; private set; }

        // ─── IModalLifecycle (사용처가 override) ─────────────

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

        internal async UniTask PushEnterAsync(bool playAnimation, Modal partner, CancellationToken ct)
        {
            _canvasGroup.alpha = 1f;

            if (playAnimation && _enterAnimation != null)
            {
                _enterAnimation.Setup(_rectTransform);
                _enterAnimation.SetPartner(partner != null ? (RectTransform)partner.transform : null);
                await _enterAnimation.PlayAsync(progress: null, ct: ct);
            }

            FillParent();
        }

        internal void AfterPushEnter()
        {
            DidPushEnter();
            IsTransitioning = false;
        }

        // PushExit: Modal은 그대로 보이는 상태. lifecycle hook만 호출.
        internal async UniTask BeforePushExitAsync()
        {
            IsTransitioning = true;
            await WillPushExit();
        }

        internal UniTask PushExitAsync(bool playAnimation, Modal partner, CancellationToken ct)
        {
            // Modal은 push 시 background로 갈 때 시각 변화 없음
            return UniTask.CompletedTask;
        }

        internal void AfterPushExit()
        {
            DidPushExit();
            IsTransitioning = false;
            // gameObject.SetActive(false) 호출 안 함 — Modal은 background 상태 유지
        }

        // PopEnter: 다시 top으로. 이미 보이는 상태이므로 시각 변화 없음.
        internal async UniTask BeforePopEnterAsync()
        {
            IsTransitioning = true;
            await WillPopEnter();
        }

        internal UniTask PopEnterAsync(bool playAnimation, Modal partner, CancellationToken ct)
        {
            // 이미 보이는 상태 — 변화 없음
            return UniTask.CompletedTask;
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
            _canvasGroup.alpha = 1f;
            await WillPopExit();
        }

        internal async UniTask PopExitAsync(bool playAnimation, Modal partner, CancellationToken ct)
        {
            if (playAnimation && _exitAnimation != null)
            {
                _exitAnimation.Setup(_rectTransform);
                _exitAnimation.SetPartner(partner != null ? (RectTransform)partner.transform : null);
                await _exitAnimation.PlayAsync(progress: null, ct: ct);
            }

            _canvasGroup.alpha = 0f;
        }

        internal void AfterPopExit()
        {
            DidPopExit();
            gameObject.SetActive(false);
            IsTransitioning = false;
        }

        internal UniTask BeforeReleaseAsync() => Cleanup();

        // ─── 내부 헬퍼 ──────────────────────────────────────

        private void FillParent()
        {
            if (_rectTransform == null || _rectTransform.parent == null) return;
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.one;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
            _rectTransform.localScale = Vector3.one;
        }
    }
}
