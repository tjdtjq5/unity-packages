using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.AddrX;
using Tjdtjq5.UIFramework.Screens.Core;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using AddrXApi = Tjdtjq5.AddrX.AddrX;

namespace Tjdtjq5.UIFramework.Screens.Page
{
    /// <summary>
    /// Page 컨테이너. history stack 기반 시퀀스 화면 관리.
    ///
    /// 흐름: PushAsync(addressableKey) → 다음 화면으로 진입(이전 화면은 stack에 보존),
    ///       PopAsync() → 이전 화면으로 복귀.
    ///
    /// 주요 기능:
    /// - push/pop with optional stack 보존 (stack=false면 이전 page destroy)
    /// - popCount로 한 번에 여러 page pop
    /// - PopToAsync로 특정 pageId까지 pop
    /// - 진입/퇴장 transition 병렬 실행 + partner page 정보 전달
    ///
    /// 의존성: AddrX (에셋 로드), UniTask (async), R3 (이벤트), VContainer (선택적 DI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PageContainer : MonoBehaviour, IScreenContainer
    {
        readonly Dictionary<string, Page> _pages = new();
        readonly Dictionary<string, SafeHandle<GameObject>> _handles = new();
        readonly List<string> _orderedPageIds = new();
        readonly PageEvents _events = new();

        CanvasGroup _canvasGroup;
        IObjectResolver _resolver;

        bool _isInTransition;
        bool _isActivePageStacked = true;

        /// <summary>VContainer 사용 시 IObjectResolver 자동 주입. DI 미사용 시 null 안전.</summary>
        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>R3 Observable로 push/pop lifecycle 관찰.</summary>
        public PageEvents Events => _events;

        /// <summary>등록된 모든 Page (pageId → instance).</summary>
        public IReadOnlyDictionary<string, Page> Pages => _pages;

        /// <summary>stack 순서 (마지막 = 현재 활성).</summary>
        public IReadOnlyList<string> StackPageIds => _orderedPageIds;

        /// <summary>현재 활성 페이지의 ID. 없으면 null.</summary>
        public string CurrentPageId => _orderedPageIds.Count > 0 ? _orderedPageIds[^1] : null;

        /// <summary>현재 활성 페이지. 없으면 null.</summary>
        public Page CurrentPage =>
            CurrentPageId != null && _pages.TryGetValue(CurrentPageId, out var p) ? p : null;

        /// <summary>pop 가능 여부 (stack 크기 ≥ 2).</summary>
        public bool CanPop => _orderedPageIds.Count >= 2;

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
        }

        async void OnDestroy()
        {
            // 모든 page에 Cleanup 호출 후 핸들 해제
            foreach (var (_, page) in _pages)
            {
                if (page != null) await page.BeforeReleaseAsync();
            }

            foreach (var (_, handle) in _handles)
            {
                handle?.Dispose();
            }

            _pages.Clear();
            _handles.Clear();
            _orderedPageIds.Clear();
            _events.Dispose();
        }

        /// <summary>
        /// 새 Page를 스택에 push.
        /// </summary>
        /// <param name="addressableKey">프리팹의 Addressable 키.</param>
        /// <param name="playAnimation">애니메이션 재생 여부.</param>
        /// <param name="stack">true면 이전 page를 stack에 보존(pop 시 복귀), false면 이전 page를 destroy.</param>
        /// <param name="pageId">명시적 ID(null이면 GUID 생성).</param>
        /// <param name="ct">취소 토큰.</param>
        /// <returns>새 page의 ID.</returns>
        public async UniTask<string> PushAsync(
            string addressableKey,
            bool playAnimation = true,
            bool stack = true,
            string pageId = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(addressableKey))
                throw new ArgumentNullException(nameof(addressableKey));
            if (_isInTransition)
                throw new InvalidOperationException("[PageContainer] 이미 전환 중입니다.");

            _isInTransition = true;
            try
            {
                // 1. Load + instantiate
                var handle = await AddrXApi.InstantiateAsync(addressableKey, transform);
                ct.ThrowIfCancellationRequested();

                var go = handle.Value;
                var enterPage = go.GetComponent<Page>();
                if (enterPage == null)
                {
                    handle.Dispose();
                    throw new InvalidOperationException(
                        $"[PageContainer] '{addressableKey}' 프리팹에 Page 컴포넌트가 없습니다.");
                }

                // 2. DI 주입 (선택적)
                _resolver?.InjectGameObject(go);

                // 3. ID 결정 + 등록
                pageId ??= Guid.NewGuid().ToString();
                enterPage.PageId = pageId;
                _pages[pageId] = enterPage;
                _handles[pageId] = handle;

                // 4. Initialize lifecycle
                await enterPage.AfterLoadAsync((RectTransform)transform);

                // 5. Determine partner (current active page)
                var exitPageId = CurrentPageId;
                Page exitPage = null;
                if (exitPageId != null) _pages.TryGetValue(exitPageId, out exitPage);

                // 6. Will phases
                await enterPage.BeforePushEnterAsync();
                _events.NotifyWillPushEnter(enterPage);

                if (exitPage != null)
                {
                    await exitPage.BeforePushExitAsync();
                    _events.NotifyWillPushExit(exitPage);
                }

                // 7. Parallel transitions
                var enterTask = enterPage.PushEnterAsync(playAnimation, exitPage, ct);
                var exitTask = exitPage != null
                    ? exitPage.PushExitAsync(playAnimation, enterPage, ct)
                    : UniTask.CompletedTask;

                await UniTask.WhenAll(enterTask, exitTask);

                // 8. After phases
                if (exitPage != null)
                {
                    exitPage.AfterPushExit();
                    _events.NotifyDidPushExit(exitPage);
                }
                enterPage.AfterPushEnter();
                _events.NotifyDidPushEnter(enterPage);

                // 9. Stack 갱신
                // 이전 push가 non-stack(replace)이었으면 exitPage를 destroy + stack에서 제거
                if (!_isActivePageStacked && exitPage != null && exitPageId != null)
                {
                    await ReleasePageAsync(exitPageId);
                    _orderedPageIds.Remove(exitPageId);
                }

                _orderedPageIds.Add(pageId);
                _isActivePageStacked = stack;

                return pageId;
            }
            finally
            {
                _isInTransition = false;
            }
        }

        /// <summary>
        /// 현재 페이지 pop (popCount만큼). 가장 위 페이지만 transition 재생, 중간 페이지는 cleanup만.
        /// </summary>
        public async UniTask PopAsync(
            bool playAnimation = true,
            int popCount = 1,
            CancellationToken ct = default)
        {
            if (popCount < 1)
                throw new ArgumentOutOfRangeException(nameof(popCount), "1 이상이어야 합니다.");
            if (_isInTransition)
                throw new InvalidOperationException("[PageContainer] 이미 전환 중입니다.");
            if (_orderedPageIds.Count <= popCount)
                throw new InvalidOperationException(
                    $"[PageContainer] 현재 stack({_orderedPageIds.Count})에서 {popCount}개를 pop하면 빈 상태가 됩니다. 최소 1개는 남아있어야 합니다.");

            _isInTransition = true;
            try
            {
                // exit = 현재 top, enter = pop 후 활성될 page
                var exitPageId = _orderedPageIds[^1];
                var exitPage = _pages[exitPageId];

                var enterIndex = _orderedPageIds.Count - 1 - popCount;
                var enterPageId = _orderedPageIds[enterIndex];
                var enterPage = _pages[enterPageId];

                // Will phases
                await exitPage.BeforePopExitAsync();
                _events.NotifyWillPopExit(exitPage);

                await enterPage.BeforePopEnterAsync();
                _events.NotifyWillPopEnter(enterPage);

                // Parallel transitions (top page만 animation)
                var exitTask = exitPage.PopExitAsync(playAnimation, enterPage, ct);
                var enterTask = enterPage.PopEnterAsync(playAnimation, exitPage, ct);

                await UniTask.WhenAll(exitTask, enterTask);

                // After phases
                exitPage.AfterPopExit();
                _events.NotifyDidPopExit(exitPage);

                enterPage.AfterPopEnter();
                _events.NotifyDidPopEnter(enterPage);

                // popCount만큼 stack에서 제거 + cleanup + destroy
                for (int i = 0; i < popCount; i++)
                {
                    var idx = _orderedPageIds.Count - 1;
                    var removedId = _orderedPageIds[idx];
                    _orderedPageIds.RemoveAt(idx);
                    await ReleasePageAsync(removedId);
                }

                _isActivePageStacked = true;
            }
            finally
            {
                _isInTransition = false;
            }
        }

        /// <summary>지정한 pageId까지 pop. 해당 pageId가 stack에 없으면 throw.</summary>
        public UniTask PopToAsync(string destinationPageId, bool playAnimation = true, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(destinationPageId))
                throw new ArgumentNullException(nameof(destinationPageId));

            var idx = _orderedPageIds.IndexOf(destinationPageId);
            if (idx < 0)
                throw new InvalidOperationException(
                    $"[PageContainer] '{destinationPageId}'를 stack에서 찾을 수 없습니다.");

            var popCount = _orderedPageIds.Count - 1 - idx;
            if (popCount < 1)
                return UniTask.CompletedTask; // 이미 destination

            return PopAsync(playAnimation, popCount, ct);
        }

        /// <summary>stack의 root까지 pop (가장 처음 push한 page만 남김).</summary>
        public UniTask PopToRootAsync(bool playAnimation = true, CancellationToken ct = default)
        {
            if (_orderedPageIds.Count <= 1) return UniTask.CompletedTask;
            return PopAsync(playAnimation, _orderedPageIds.Count - 1, ct);
        }

        // ─── 내부 헬퍼 ──────────────────────────────────────

        async UniTask ReleasePageAsync(string pageId)
        {
            if (_pages.TryGetValue(pageId, out var page))
            {
                if (page != null) await page.BeforeReleaseAsync();
                if (page != null) Destroy(page.gameObject);
                _pages.Remove(pageId);
            }

            if (_handles.TryGetValue(pageId, out var handle))
            {
                handle?.Dispose();
                _handles.Remove(pageId);
            }
        }
    }
}
