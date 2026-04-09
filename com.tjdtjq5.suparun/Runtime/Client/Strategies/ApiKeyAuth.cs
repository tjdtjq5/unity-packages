#nullable enable
using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// apikey 헤더만 설정하는 인증 전략.
    /// Supabase Auth API 호출용 — Bearer 토큰 불필요 (토큰을 받는 쪽이므로).
    /// </summary>
    public class ApiKeyAuth : IAuthStrategy
    {
        readonly string _apiKey;

        public ApiKeyAuth(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public void Apply(HttpTransportRequest request)
        {
            request.Headers["apikey"] = _apiKey;
        }
    }
}
