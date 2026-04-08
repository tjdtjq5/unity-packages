namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 재시도하지 않는 전략. 항상 -1 반환.
    /// 사용처: SupabaseRestClient, Supabase Auth API 등 single-shot 호출.
    ///
    /// 401 토큰 갱신은 IAuthRefresher가 별도로 처리하므로 이 전략과 무관하게 동작한다.
    /// </summary>
    public class NoRetry : IRetryStrategy
    {
        public int GetRetryDelay(HttpTransportResponse response, int attemptNumber) => -1;
    }
}
