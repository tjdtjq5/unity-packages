using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.AddrX;
using Tjdtjq5.UIFramework.Screens.Core;
using Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using AddrXApi = Tjdtjq5.AddrX.AddrX;

namespace Tjdtjq5.UIFramework.Screens.Modal
{
    /// <summary>
    /// Modal 컨테이너. stack 기반 다중 모달 관리. backdrop은 strategy에 따라 처리.
    ///
    /// Page와의 차이:
    /// - 모달은 항상 stack에 쌓임 (replace 옵션 없음)
    /// - 이전 모달은 background에 그대로 보임 (사라지지 않음)
    /// - Backdrop이 raycast 차단으로 입력을 top modal로만 전달
    /// - popCount > 1 시 모든 exit modal이 동시 애니메이션
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModalContainer : MonoBehaviour, IScreenContainer
    {
        [SerializeField] private ModalBackdropStrategy _backdropStrategy = ModalBackdropStrategy.GeneratePerModal;
        [SerializeField] private ModalBackdrop _backdropPrefab;

        readonly Dictionary<string, Modal> _modals = new();
        readonly Dictionary<string, SafeHandle<GameObject>> _handles = new();
        readonly List<string> _orderedModalIds = new();
        readonly ModalEvents _events = new();

        CanvasGroup _canvasGroup;
        IObjectResolver _resolver;
        IModalBackdropHandler _backdropHandler;

        bool _isInTransition;

        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public ModalEvents Events => _events;

        public IReadOnlyDictionary<string, Modal> Modals => _modals;

        public IReadOnlyList<string> StackModalIds => _orderedModalIds;

        public string TopModalId => _orderedModalIds.Count > 0 ? _orderedModalIds[^1] : null;

        public Modal TopModal =>
            TopModalId != null && _modals.TryGetValue(TopModalId, out var m) ? m : null;

        public bool CanPop => _orderedModalIds.Count > 0;

        public bool IsInTransition => _isInTransition;

        public bool Interactable
        {
            get => _canvasGroup.interactable;
            set => _canvasGroup.interactable = value;
        }

        void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _backdropHandler = ModalBackdropHandlerFactory.Create(_backdropStrategy, _backdropPrefab);
        }

        async void OnDestroy()
        {
            foreach (var (_, modal) in _modals)
            {
                if (modal != null) await modal.BeforeReleaseAsync();
            }

            foreach (var (_, handle) in _handles)
            {
                handle?.Dispose();
            }

            _modals.Clear();
            _handles.Clear();
            _orderedModalIds.Clear();
            _events.Dispose();
        }

        /// <summary>
        /// 새 modal을 stack에 push. 항상 stack에 쌓임 (replace 옵션 없음).
        /// </summary>
        public async UniTask<string> PushAsync(
            string addressableKey,
            bool playAnimation = true,
            string modalId = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(addressableKey))
                throw new ArgumentNullException(nameof(addressableKey));
            if (_isInTransition)
                throw new InvalidOperationException("[ModalContainer] 이미 전환 중입니다.");

            _isInTransition = true;
            try
            {
                // 1. Load + Instantiate
                var handle = await AddrXApi.InstantiateAsync(addressableKey, transform);
                ct.ThrowIfCancellationRequested();

                var go = handle.Value;
                var enterModal = go.GetComponent<Modal>();
                if (enterModal == null)
                {
                    handle.Dispose();
                    throw new InvalidOperationException(
                        $"[ModalContainer] '{addressableKey}' 프리팹에 Modal 컴포넌트가 없습니다.");
                }

                _resolver?.InjectGameObject(go);

                modalId ??= Guid.NewGuid().ToString();
                enterModal.ModalId = modalId;
                _modals[modalId] = enterModal;
                _handles[modalId] = handle;

                await enterModal.AfterLoadAsync((RectTransform)transform);

                // 2. Determine partner (current top modal)
                var exitModalId = TopModalId;
                Modal exitModal = null;
                if (exitModalId != null) _modals.TryGetValue(exitModalId, out exitModal);

                int newModalIndex = _orderedModalIds.Count; // 새 modal의 stack index

                // 3. Will phases
                await enterModal.BeforePushEnterAsync();
                _events.NotifyWillPushEnter(enterModal);

                if (exitModal != null)
                {
                    await exitModal.BeforePushExitAsync();
                    _events.NotifyWillPushExit(exitModal);
                }

                // 4. Parallel: backdrop enter + modal enter + (modal exit, no-op for Modal)
                var allTasks = new List<UniTask>(3)
                {
                    _backdropHandler.BeforeModalEnterAsync(enterModal, newModalIndex, playAnimation, ct),
                    enterModal.PushEnterAsync(playAnimation, exitModal, ct),
                };
                if (exitModal != null)
                    allTasks.Add(exitModal.PushExitAsync(playAnimation, enterModal, ct));

                await UniTask.WhenAll(allTasks);

                // 5. Backdrop after-enter
                _backdropHandler.AfterModalEnter(enterModal, newModalIndex, playAnimation);

                // 6. After phases
                if (exitModal != null)
                {
                    exitModal.AfterPushExit();
                    _events.NotifyDidPushExit(exitModal);
                }
                enterModal.AfterPushEnter();
                _events.NotifyDidPushEnter(enterModal);

                // 7. Stack 갱신
                _orderedModalIds.Add(modalId);

                return modalId;
            }
            finally
            {
                _isInTransition = false;
            }
        }

        /// <summary>
        /// popCount만큼 modal pop. 모든 exit modal이 동시에 애니메이션 + 각자의 backdrop도 함께.
        /// popCount = stack 크기면 모든 모달 닫힘.
        /// </summary>
        public async UniTask PopAsync(
            bool playAnimation = true,
            int popCount = 1,
            CancellationToken ct = default)
        {
            if (popCount < 1)
                throw new ArgumentOutOfRangeException(nameof(popCount), "1 이상이어야 합니다.");
            if (_isInTransition)
                throw new InvalidOperationException("[ModalContainer] 이미 전환 중입니다.");
            if (_orderedModalIds.Count < popCount)
                throw new InvalidOperationException(
                    $"[ModalContainer] stack({_orderedModalIds.Count})이 popCount({popCount})보다 작습니다.");

            _isInTransition = true;
            try
            {
                // 1. Determine exit modals (top → bottom of pop range)
                var exitModals = new List<Modal>(popCount);
                var exitModalIds = new List<string>(popCount);
                var exitModalIndices = new List<int>(popCount);
                for (int i = 0; i < popCount; i++)
                {
                    var idx = _orderedModalIds.Count - 1 - i;
                    var id = _orderedModalIds[idx];
                    exitModals.Add(_modals[id]);
                    exitModalIds.Add(id);
                    exitModalIndices.Add(idx);
                }

                // 2. Enter modal (next top after pop) — null if popping all
                var enterIndex = _orderedModalIds.Count - 1 - popCount;
                Modal enterModal = null;
                if (enterIndex >= 0)
                    enterModal = _modals[_orderedModalIds[enterIndex]];

                // 3. Will phases
                for (int i = 0; i < popCount; i++)
                {
                    await exitModals[i].BeforePopExitAsync();
                    _events.NotifyWillPopExit(exitModals[i]);
                }
                if (enterModal != null)
                {
                    await enterModal.BeforePopEnterAsync();
                    _events.NotifyWillPopEnter(enterModal);
                }

                // 4. Parallel transitions: 모든 exit modals + 그들의 backdrop + enter modal
                var allTasks = new List<UniTask>(popCount * 2 + 1);
                for (int i = 0; i < popCount; i++)
                {
                    // partner: pop chain의 다음 modal (있으면), 없으면 enterModal
                    var partner = i + 1 < popCount ? exitModals[i + 1] : enterModal;
                    allTasks.Add(exitModals[i].PopExitAsync(playAnimation, partner, ct));
                    allTasks.Add(_backdropHandler.BeforeModalExitAsync(exitModals[i], exitModalIndices[i], playAnimation, ct));
                }
                if (enterModal != null)
                    allTasks.Add(enterModal.PopEnterAsync(playAnimation, exitModals[0], ct));

                await UniTask.WhenAll(allTasks);

                // 5. AfterModalExit hooks (backdrop cleanup/move)
                for (int i = 0; i < popCount; i++)
                    _backdropHandler.AfterModalExit(exitModals[i], exitModalIndices[i], playAnimation);

                // 6. After phases (lifecycle hooks)
                for (int i = 0; i < popCount; i++)
                {
                    exitModals[i].AfterPopExit();
                    _events.NotifyDidPopExit(exitModals[i]);
                }
                if (enterModal != null)
                {
                    enterModal.AfterPopEnter();
                    _events.NotifyDidPopEnter(enterModal);
                }

                // 7. Stack에서 제거 + cleanup + destroy
                for (int i = 0; i < popCount; i++)
                {
                    var lastIdx = _orderedModalIds.Count - 1;
                    var removedId = _orderedModalIds[lastIdx];
                    _orderedModalIds.RemoveAt(lastIdx);
                    await ReleaseModalAsync(removedId);
                }
            }
            finally
            {
                _isInTransition = false;
            }
        }

        /// <summary>지정한 modalId까지 pop.</summary>
        public UniTask PopToAsync(string destinationModalId, bool playAnimation = true, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(destinationModalId))
                throw new ArgumentNullException(nameof(destinationModalId));

            var idx = _orderedModalIds.IndexOf(destinationModalId);
            if (idx < 0)
                throw new InvalidOperationException(
                    $"[ModalContainer] '{destinationModalId}'를 stack에서 찾을 수 없습니다.");

            var popCount = _orderedModalIds.Count - 1 - idx;
            if (popCount < 1) return UniTask.CompletedTask;

            return PopAsync(playAnimation, popCount, ct);
        }

        /// <summary>모든 modal pop.</summary>
        public UniTask PopAllAsync(bool playAnimation = true, CancellationToken ct = default)
        {
            if (_orderedModalIds.Count == 0) return UniTask.CompletedTask;
            return PopAsync(playAnimation, _orderedModalIds.Count, ct);
        }

        // ─── 내부 헬퍼 ──────────────────────────────────────

        async UniTask ReleaseModalAsync(string modalId)
        {
            if (_modals.TryGetValue(modalId, out var modal))
            {
                if (modal != null) await modal.BeforeReleaseAsync();
                if (modal != null) Destroy(modal.gameObject);
                _modals.Remove(modalId);
            }

            if (_handles.TryGetValue(modalId, out var handle))
            {
                handle?.Dispose();
                _handles.Remove(modalId);
            }
        }
    }
}
