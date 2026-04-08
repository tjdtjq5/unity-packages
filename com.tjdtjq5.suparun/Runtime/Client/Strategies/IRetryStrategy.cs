namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// HTTP 응답을 보고 재시도 여부와 대기 시간을 결정하는 전략.
    /// HttpExecutor가 응답 수신 후 호출한다.
    ///
    /// 구현체:
    /// - <see cref="NoRetry"/> : 항상 재시도 안 함
    /// - <see cref="ExponentialBackoffRetry"/> : 5xx/Timeout 에 1s/2s/4s 지수 백오프
    ///
    /// 401(인증 만료)은 IAuthRefresher가 처리하므로 IRetryStrategy는 신경 쓰지 않아도 된다.
    /// </summary>
    public interface IRetryStrategy
    {
        /// <summary>
        /// 재시도 전 대기 시간(밀리초). -1 반환 시 재시도 안 함.
        /// </summary>
        /// <param name="response">방금 받은 HTTP 응답</param>
        /// <param name="attemptNumber">현재 시도 횟수 (0부터 시작)</param>
        int GetRetryDelay(HttpTransportResponse response, int attemptNumber);
    }
}
