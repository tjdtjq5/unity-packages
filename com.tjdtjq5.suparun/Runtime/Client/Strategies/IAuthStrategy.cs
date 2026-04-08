namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// HTTP 요청에 인증 정보(헤더)를 추가하는 전략.
    /// HttpExecutor가 매 송신 직전에 호출한다.
    ///
    /// 구현체:
    /// - <see cref="BearerTokenAuth"/> : Authorization Bearer JWT (Cloud Run용)
    /// - <see cref="BearerJwtOrAnonAuth"/> : apikey + Bearer (JWT 우선, 없으면 anon key) (Supabase REST용)
    /// - <see cref="ApiKeyOnlyAuth"/> : apikey 헤더만 (Supabase Auth API용)
    /// - <see cref="NoAuth"/> : 아무것도 안 함
    ///
    /// SRP: 헤더 적용만 책임. 토큰 갱신은 IAuthRefresher가 담당.
    /// </summary>
    public interface IAuthStrategy
    {
        /// <summary>request의 Headers에 인증 정보를 추가한다. 매 송신 직전 호출.</summary>
        void Apply(HttpTransportRequest request);
    }
}
