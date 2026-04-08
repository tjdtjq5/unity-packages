using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// JWT Bearer 토큰 인증 전략. Session이 있고 만료 안 됐을 때만 Authorization 헤더 첨부.
    /// 사용처: SupaRunClient (Cloud Run 호출).
    ///
    /// 만료/없음일 때 헤더를 안 붙이는 이유: Cloud Run 서버가 자체 401로 응답하면
    /// HttpExecutor가 IAuthRefresher를 호출해 토큰 갱신 → 1회 재시도.
    /// </summary>
    public class BearerTokenAuth : IAuthStrategy
    {
        readonly Func<AuthSession> _getSession;

        public BearerTokenAuth(Func<AuthSession> getSession)
        {
            _getSession = getSession ?? throw new ArgumentNullException(nameof(getSession));
        }

        public void Apply(HttpTransportRequest request)
        {
            var session = _getSession();
            if (session != null && !session.IsExpired)
                request.Headers["Authorization"] = $"Bearer {session.accessToken}";
        }
    }
}
