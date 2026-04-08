using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// IHttpTransport의 UnityWebRequest 기반 구현체.
    /// 패키지 전체에서 사용되는 단일 HTTP 송신 메커니즘.
    ///
    /// 정책 (인증 헤더, 재시도, 응답 파싱)은 모름 — strategy 패턴이 담당.
    /// 이 클래스는 raw HTTP 송수신만.
    /// </summary>
    public class UnityHttpTransport : IHttpTransport
    {
        public async Task<HttpTransportResponse> SendAsync(HttpTransportRequest request)
        {
            try
            {
                using var req = new UnityWebRequest(request.Url, request.Method);
                req.downloadHandler = new DownloadHandlerBuffer();

                if (request.Body != null && request.Body.Length > 0)
                    req.uploadHandler = new UploadHandlerRaw(request.Body);

                if (request.Headers != null)
                {
                    foreach (var kv in request.Headers)
                        req.SetRequestHeader(kv.Key, kv.Value);
                }

                req.timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30;

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                return new HttpTransportResponse
                {
                    Success = req.result == UnityWebRequest.Result.Success,
                    StatusCode = (int)req.responseCode,
                    ResponseText = req.downloadHandler?.text,
                    Error = req.error,
                    IsConnectionError = req.result == UnityWebRequest.Result.ConnectionError,
                };
            }
            catch (Exception ex)
            {
                // 예외도 응답 객체로 통일 (호출자가 분기 처리 안 해도 됨)
                return new HttpTransportResponse
                {
                    Success = false,
                    Error = ex.Message,
                    IsConnectionError = true,
                };
            }
        }
    }
}
