#nullable enable
using System;
using Cysharp.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 동시 호출 dedup(single-flight). 진행 중인 작업이 있으면 같은 작업의 결과를 공유한다.
    ///
    /// 핵심 안전장치: <see cref="UniTaskCompletionSource"/>를 *로컬*에 캡처한 뒤 launch하고
    /// 그 로컬의 .Task를 반환한다. 그래서 work가 동기 완료(예: 테스트의 동기 어댑터)되어
    /// <c>finally</c>에서 _inflight를 null로 비워도 반환값에는 영향이 없다.
    /// (이전 SupaRunAuth.EnsureLoggedIn이 launch 후 _loginUcs *필드*를 다시 읽다가 났던 NRE를 구조적으로 차단)
    ///
    /// 주의: Unity의 단일 스레드 협력 비동기를 전제한다. 멀티스레드 락은 제공하지 않는다.
    /// </summary>
    public class SingleFlight
    {
        UniTaskCompletionSource? _inflight;

        /// <summary>진행 중이면 그 작업을, 아니면 새 작업을 시작해 그 UniTask를 반환.</summary>
        public UniTask Run(Func<UniTask> work)
        {
            if (_inflight != null) return _inflight.Task;
            var ucs = new UniTaskCompletionSource();
            _inflight = ucs;
            Exec(ucs, work).Forget();
            return ucs.Task;
        }

        async UniTaskVoid Exec(UniTaskCompletionSource ucs, Func<UniTask> work)
        {
            try { await work(); ucs.TrySetResult(); }
            catch (Exception ex) { ucs.TrySetException(ex); }
            finally { _inflight = null; }
        }
    }

    /// <summary>결과값이 있는 single-flight. 동시 호출자 모두 같은 결과를 받는다.</summary>
    public class SingleFlight<T>
    {
        UniTaskCompletionSource<T>? _inflight;

        public UniTask<T> Run(Func<UniTask<T>> work)
        {
            if (_inflight != null) return _inflight.Task;
            var ucs = new UniTaskCompletionSource<T>();
            _inflight = ucs;
            Exec(ucs, work).Forget();
            return ucs.Task;
        }

        async UniTaskVoid Exec(UniTaskCompletionSource<T> ucs, Func<UniTask<T>> work)
        {
            try { ucs.TrySetResult(await work()); }
            catch (Exception ex) { ucs.TrySetException(ex); }
            finally { _inflight = null; }
        }
    }
}
