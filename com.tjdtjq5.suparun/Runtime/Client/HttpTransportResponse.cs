#nullable enable
namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// HTTP 응답 DTO. IHttpTransport.SendAsync 의 반환값.
    /// HTTP 4xx/5xx 도 정상 응답으로 간주(Success=true 가능)하며,
    /// 호출자가 StatusCode 보고 application-level 에러를 판단한다.
    /// </summary>
    public class HttpTransportResponse
    {
        /// <summary>
        /// UnityWebRequest.Result.Success 여부.
        /// HTTP 4xx/5xx 응답을 받았어도 네트워크 자체는 성공한 거라 true 일 수 있음.
        /// (UnityWebRequest 의 Result enum 정책에 따름)
        /// </summary>
        public bool Success;

        /// <summary>HTTP status code (예: 200, 401, 500). 0이면 timeout 또는 connection error.</summary>
        public int StatusCode;

        /// <summary>응답 body 텍스트. UTF-8 디코딩됨. 빈 응답이면 null 또는 빈 문자열.</summary>
        public string? ResponseText;

        /// <summary>UnityWebRequest.error 메시지. Success=false 일 때 의미 있음.</summary>
        public string? Error;

        /// <summary>
        /// 네트워크 연결 실패 여부. true면 서버 응답을 못 받은 상태(DNS, timeout, refused 등).
        /// false인데 Success=false면 HTTP 4xx/5xx 응답.
        /// </summary>
        public bool IsConnectionError;
    }
}
