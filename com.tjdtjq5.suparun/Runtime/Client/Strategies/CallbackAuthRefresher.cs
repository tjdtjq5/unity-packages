using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 외부 콜백을 IAuthRefresher로 wrap하는 어댑터.
    /// 사용처: SupaRunAuth.TryRefreshToken 같은 외부 토큰 갱신 함수를 HttpExecutor에 연결.
    ///
    /// 콜백이 null 아닌 AuthSession을 반환하면 갱신 성공으로 간주.
    /// </summary>
    public class CallbackAuthRefresher : IAuthRefresher
    {
        readonly Func<UniTask<AuthSession?>> _refresh;

        public CallbackAuthRefresher(Func<UniTask<AuthSession?>> refresh)
        {
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        public async UniTask<bool> TryRefreshAsync(CancellationToken ct = default)
        {
            var session = await _refresh();
            return session != null;
        }
    }
}
