using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 5xx 또는 Timeout 응답에 대해 지수 백오프로 재시도하는 전략.
    /// 기본: 최대 3회, baseDelayMs=1000 → 1s, 2s, 4s 대기 후 재시도.
    ///
    /// 사용처: SupaRunClient (Cloud Run 일시 장애 회복).
    ///
    /// 4xx (401, 403, 404 등)는 재시도하지 않음 — 클라이언트 에러는 같은 요청 보내봐야 같은 결과.
    /// 401은 IAuthRefresher가 별도로 처리.
    /// </summary>
    public class ExponentialBackoffRetry : IRetryStrategy
    {
        readonly int _maxAttempts;
        readonly int _baseDelayMs;

        public ExponentialBackoffRetry(int maxAttempts = 3, int baseDelayMs = 1000)
        {
            _maxAttempts = maxAttempts;
            _baseDelayMs = baseDelayMs;
        }

        public int GetRetryDelay(HttpTransportResponse response, int attemptNumber)
        {
            if (attemptNumber >= _maxAttempts) return -1;

            // 5xx 서버 에러 또는 connection error(timeout/dns 등) 일 때만 재시도
            bool retryable = response.StatusCode >= 500 ||
                             (response.StatusCode == 0 && response.IsConnectionError);
            if (!retryable) return -1;

            // 지수 백오프: baseDelay * 2^attempt → 1000ms, 2000ms, 4000ms
            return _baseDelayMs * (int)Math.Pow(2, attemptNumber);
        }
    }
}
