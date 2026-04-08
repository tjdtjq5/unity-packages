using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 저수준 HTTP 송신 추상화. UnityWebRequest 같은 구체 구현을 캡슐화.
    /// 정책(인증 헤더, 재시도, 응답 파싱)은 모름 — HttpExecutor와 strategy 패턴이 담당.
    ///
    /// 구현체:
    /// - <see cref="UnityHttpTransport"/> : UnityWebRequest 기반 단일 구현
    /// </summary>
    public interface IHttpTransport
    {
        /// <summary>
        /// HTTP 요청 1회 송신. 재시도/인증 적용은 호출자 책임.
        /// 네트워크 에러든 HTTP 4xx/5xx든 응답 객체로 반환 (예외 throw 안 함).
        /// </summary>
        Task<HttpTransportResponse> SendAsync(HttpTransportRequest request);
    }
}
