using System;
using R3;

namespace Tjdtjq5.UIFramework.Screens.Modal
{
    /// <summary>
    /// ModalContainer의 lifecycle 이벤트 R3 Observable로 노출.
    /// 외부 코드에서 modal 전환 관찰하여 부수효과(BGM, 분석, 게임 일시정지 등) 처리.
    /// </summary>
    public sealed class ModalEvents : IDisposable
    {
        readonly Subject<Modal> _onWillPushEnter = new();
        readonly Subject<Modal> _onDidPushEnter = new();
        readonly Subject<Modal> _onWillPushExit = new();
        readonly Subject<Modal> _onDidPushExit = new();
        readonly Subject<Modal> _onWillPopEnter = new();
        readonly Subject<Modal> _onDidPopEnter = new();
        readonly Subject<Modal> _onWillPopExit = new();
        readonly Subject<Modal> _onDidPopExit = new();

        bool _disposed;

        public Observable<Modal> OnWillPushEnter => _onWillPushEnter;
        public Observable<Modal> OnDidPushEnter => _onDidPushEnter;
        public Observable<Modal> OnWillPushExit => _onWillPushExit;
        public Observable<Modal> OnDidPushExit => _onDidPushExit;
        public Observable<Modal> OnWillPopEnter => _onWillPopEnter;
        public Observable<Modal> OnDidPopEnter => _onDidPopEnter;
        public Observable<Modal> OnWillPopExit => _onWillPopExit;
        public Observable<Modal> OnDidPopExit => _onDidPopExit;

        internal void NotifyWillPushEnter(Modal m) => _onWillPushEnter.OnNext(m);
        internal void NotifyDidPushEnter(Modal m) => _onDidPushEnter.OnNext(m);
        internal void NotifyWillPushExit(Modal m) => _onWillPushExit.OnNext(m);
        internal void NotifyDidPushExit(Modal m) => _onDidPushExit.OnNext(m);
        internal void NotifyWillPopEnter(Modal m) => _onWillPopEnter.OnNext(m);
        internal void NotifyDidPopEnter(Modal m) => _onDidPopEnter.OnNext(m);
        internal void NotifyWillPopExit(Modal m) => _onWillPopExit.OnNext(m);
        internal void NotifyDidPopExit(Modal m) => _onDidPopExit.OnNext(m);

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
