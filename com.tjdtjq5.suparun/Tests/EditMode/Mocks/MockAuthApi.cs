#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>테스트용 Auth API. 응답 큐 + 요청 기록.</summary>
    class MockAuthApi : IAuthApi
    {
        readonly Queue<string?> _responses = new();
        readonly List<(string endpoint, string body)> _requests = new();

        // GetAuthenticatedAsync용 별도 큐/기록 — PostAsync와 독립 (기존 테스트 호환 유지)
        readonly Queue<string?> _getResponses = new();
        readonly List<(string endpoint, string token)> _getRequests = new();

        public IReadOnlyList<(string endpoint, string body)> Requests => _requests;
        public IReadOnlyList<(string endpoint, string token)> GetRequests => _getRequests;
        public int CallCount => _requests.Count;

        public void Enqueue(string? response) => _responses.Enqueue(response);

        /// <summary>
        /// GetAuthenticatedAsync 응답 큐에 추가. null이면 검증 실패(401) 시뮬레이션.
        /// 큐가 비어있으면 기본값 "ok" (검증 성공) 반환 — 기존 테스트 호환.
        /// </summary>
        public void EnqueueGet(string? response) => _getResponses.Enqueue(response);

        public Task<string?> PostAsync(string endpoint, string jsonBody)
        {
            _requests.Add((endpoint, jsonBody));
            var response = _responses.Count > 0 ? _responses.Dequeue() : null;
            return Task.FromResult(response);
        }

        public Task<string?> GetAuthenticatedAsync(string endpoint, string accessToken)
        {
            _getRequests.Add((endpoint, accessToken));
            // 기본값 "ok": 큐를 따로 세팅하지 않은 기존 테스트의 세션 복원 경로가 VerifySession을 통과하도록.
            // 검증 실패 시나리오는 EnqueueGet(null)을 명시적으로 호출.
            var response = _getResponses.Count > 0 ? _getResponses.Dequeue() : "ok";
            return Task.FromResult(response);
        }
    }
}
