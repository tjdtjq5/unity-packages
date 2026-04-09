#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>테스트용 HTTP 전송. 응답 큐 + 요청 기록.</summary>
    class MockHttpTransport : IHttpTransport
    {
        readonly Queue<HttpTransportResponse> _responses = new();
        readonly List<HttpTransportRequest> _sent = new();

        public IReadOnlyList<HttpTransportRequest> SentRequests => _sent;
        public HttpTransportRequest LastRequest => _sent[^1];
        public int SendCount => _sent.Count;

        public void Enqueue(HttpTransportResponse response) => _responses.Enqueue(response);

        public void Enqueue(int statusCode, string body = "", bool success = true,
                            bool isConnectionError = false, string? error = null)
        {
            _responses.Enqueue(new HttpTransportResponse
            {
                StatusCode = statusCode,
                ResponseText = body,
                Success = success,
                IsConnectionError = isConnectionError,
                Error = error,
            });
        }

        public Task<HttpTransportResponse> SendAsync(HttpTransportRequest request)
        {
            _sent.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
