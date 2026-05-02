using System;
using R3;

namespace Tjdtjq5.UIFramework.Screens.Page
{
    /// <summary>
    /// PageContainer의 lifecycle 이벤트 R3 Observable로 노출.
    /// 외부 코드에서 push/pop 전환을 관찰하여 분석/사운드/카메라 등 부수효과 처리.
    /// </summary>
    public sealed class PageEvents : IDisposable
    {
        readonly Subject<Page> _onWillPushEnter = new();
        readonly Subject<Page> _onDidPushEnter = new();
        readonly Subject<Page> _onWillPushExit = new();
        readonly Subject<Page> _onDidPushExit = new();
        readonly Subject<Page> _onWillPopEnter = new();
        readonly Subject<Page> _onDidPopEnter = new();
        readonly Subject<Page> _onWillPopExit = new();
        readonly Subject<Page> _onDidPopExit = new();

        bool _disposed;

        /// <summary>push 진입 직전.</summary>
        public Observable<Page> OnWillPushEnter => _onWillPushEnter;

        /// <summary>push 진입 직후 (transition 완료).</summary>
        public Observable<Page> OnDidPushEnter => _onDidPushEnter;

        /// <summary>push 시 이전 페이지가 나가기 직전.</summary>
        public Observable<Page> OnWillPushExit => _onWillPushExit;

        /// <summary>push 시 이전 페이지가 나간 직후.</summary>
        public Observable<Page> OnDidPushExit => _onDidPushExit;

        /// <summary>pop 시 페이지가 다시 보이기 직전.</summary>
        public Observable<Page> OnWillPopEnter => _onWillPopEnter;

        /// <summary>pop 시 페이지가 다시 보인 직후.</summary>
        public Observable<Page> OnDidPopEnter => _onDidPopEnter;

        /// <summary>pop 시 현재 페이지가 나가기 직전.</summary>
        public Observable<Page> OnWillPopExit => _onWillPopExit;

        /// <summary>pop 시 현재 페이지가 나간 직후.</summary>
        public Observable<Page> OnDidPopExit => _onDidPopExit;

        internal void NotifyWillPushEnter(Page p) => _onWillPushEnter.OnNext(p);
        internal void NotifyDidPushEnter(Page p) => _onDidPushEnter.OnNext(p);
        internal void NotifyWillPushExit(Page p) => _onWillPushExit.OnNext(p);
        internal void NotifyDidPushExit(Page p) => _onDidPushExit.OnNext(p);
        internal void NotifyWillPopEnter(Page p) => _onWillPopEnter.OnNext(p);
        internal void NotifyDidPopEnter(Page p) => _onDidPopEnter.OnNext(p);
        internal void NotifyWillPopExit(Page p) => _onWillPopExit.OnNext(p);
        internal void NotifyDidPopExit(Page p) => _onDidPopExit.OnNext(p);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _onWillPushEnter.Dispose();
            _onDidPushEnter.Dispose();
            _onWillPushExit.Dispose();
            _onDidPushExit.Dispose();
            _onWillPopEnter.Dispose();
            _onDidPopEnter.Dispose();
            _onWillPopExit.Dispose();
            _onDidPopExit.Dispose();
        }
    }
}
