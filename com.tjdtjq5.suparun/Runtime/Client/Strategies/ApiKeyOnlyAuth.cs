using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// apikey 헤더만 추가하는 인증 전략 (Bearer 없음).
    /// 사용처: Supabase Auth API 호출 (signup, refresh token 등).
    ///
    /// Auth API는 anon key만으로 접근 가능하며 Bearer 토큰은 무의미.
    /// (P2-1 범위에서는 SupaRunAuth.Post가 이 전략을 직접 사용하지 않음 — 미래 SupabaseAuthApi 분리 시 사용 예정)
    /// </summary>
    public class ApiKeyOnlyAuth : IAuthStrategy
    {
        readonly string _apiKey;

        public ApiKeyOnlyAuth(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public void Apply(HttpTransportRequest request)
        {
            request.Headers["apikey"] = _apiKey;
        }
    }
}
