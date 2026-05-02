using System;
using R3;

namespace Tjdtjq5.UIFramework.Screens.Sheet
{
    /// <summary>
    /// SheetContainer의 lifecycle 이벤트 R3 Observable로 노출.
    /// 외부 코드에서 Sheet 전환을 관찰하여 부수효과(BGM, 분석 등) 처리.
    /// </summary>
    public sealed class SheetEvents : IDisposable
    {
        readonly Subject<Sheet> _onWillEnter = new();
        readonly Subject<Sheet> _onDidEnter = new();
        readonly Subject<Sheet> _onWillExit = new();
        readonly Subject<Sheet> _onDidExit = new();

        bool _disposed;

        /// <summary>표시 직전 (transition 시작 전).</summary>
        public Observable<Sheet> OnWillEnter => _onWillEnter;

        /// <summary>표시 직후 (transition 완료 후).</summary>
        public Observable<Sheet> OnDidEnter => _onDidEnter;

        /// <summary>숨김 직전.</summary>
        public Observable<Sheet> OnWillExit => _onWillExit;

        /// <summary>숨김 직후.</summary>
        public Observable<Sheet> OnDidExit => _onDidExit;

        internal void NotifyWillEnter(Sheet sheet) => _onWillEnter.OnNext(sheet);
        internal void NotifyDidEnter(Sheet sheet) => _onDidEnter.OnNext(sheet);
        internal void NotifyWillExit(Sheet sheet) => _onWillExit.OnNext(sheet);
        internal void NotifyDidExit(Sheet sheet) => _onDidExit.OnNext(sheet);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _onWillEnter.Dispose();
            _onDidEnter.Dispose();
            _onWillExit.Dispose();
            _onDidExit.Dispose();
        }
    }
}
