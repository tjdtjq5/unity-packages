using System;
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// HTTP 송신 오케스트레이터. transport + 인증/재시도/갱신 strategy를 조합하여 실행.
    /// 호출자는 strategy 조합으로 정책을 선언, 실행 디테일은 모른다.
    ///
    /// 책임:
    /// 1. 매 송신 직전 IAuthStrategy로 헤더 적용 (토큰 갱신 후 새 토큰 반영)
    /// 2. IHttpTransport로 송신
    /// 3. 401 응답 시 IAuthRefresher로 1회만 토큰 갱신 후 재시도
    /// 4. IRetryStrategy로 5xx/Timeout 등 일반 재시도 처리 (delay 반환)
    /// </summary>
    public class HttpExecutor
    {
        readonly IHttpTransport _transport;
        readonly IAuthStrategy _auth;
        readonly IRetryStrategy _retry;
        readonly IAuthRefresher _refresher; // optional — null이면 401 시 그대로 응답 반환

        public HttpExecutor(IHttpTransport transport, IAuthStrategy auth,
                            IRetryStrategy retry = null, IAuthRefresher refresher = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _retry = retry ?? new NoRetry();
            _refresher = refresher;
        }

        /// <summary>
        /// HTTP 요청 실행. 인증 적용 + 송신 + 401 갱신 재시도 + 일반 재시도까지 처리.
        /// 최종 응답을 반환 (재시도 한도 도달 또는 더 이상 재시도 불가능 시).
        /// </summary>
        public async Task<HttpTransportResponse> ExecuteAsync(HttpTransportRequest request)
        {
            bool refreshAttempted = false;

            for (int attempt = 0; ; attempt++)
            {
                // 1) 인증 헤더 적용 (매번 호출 — 토큰 갱신 후 새 토큰 반영)
                _auth.Apply(request);

                // 2) 송신
                var response = await _transport.SendAsync(request);

                // 3) 401 처리: 토큰 갱신 후 1회만 재시도
                if (response.StatusCode == 401 && _refresher != null && !refreshAttempted)
                {
                    refreshAttempted = true;
                    if (await _refresher.TryRefreshAsync()) continue; // 재시도 (attempt 증가 안 함, auth 재적용)
                }

                // 4) 일반 재시도 (5xx, Timeout 등)
                var delay = _retry.GetRetryDelay(response, attempt);
                if (delay < 0) return response; // 재시도 불가 → 최종 반환

                if (delay > 0) await Task.Delay(delay);
                // delay==0 또는 대기 후 → 다음 attempt
            }
        }
    }
}
