using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// Supabase REST 인증 전략.
    /// 항상 apikey 헤더 첨부 (Supabase 필수). Bearer는 JWT 우선, 없으면 anon key fallback.
    ///
    /// 사용처: SupabaseRestClient (PostgREST [Config] 조회).
    /// JWT가 있을 때 RLS의 authenticated 정책 통과, 없을 때는 anon role(권한 제한).
    /// </summary>
    public class BearerJwtOrAnonAuth : IAuthStrategy
    {
        readonly Func<AuthSession> _getSession;
        readonly string _anonKey;

        public BearerJwtOrAnonAuth(Func<AuthSession> getSession, string anonKey)
        {
            _getSession = getSession ?? throw new ArgumentNullException(nameof(getSession));
            _anonKey = anonKey ?? throw new ArgumentNullException(nameof(anonKey));
        }

        public void Apply(HttpTransportRequest request)
        {
            request.Headers["apikey"] = _anonKey;

            var session = _getSession();
            var bearer = !string.IsNullOrEmpty(session?.accessToken)
                ? session.accessToken
                : _anonKey;
            request.Headers["Authorization"] = $"Bearer {bearer}";
        }
    }
}
