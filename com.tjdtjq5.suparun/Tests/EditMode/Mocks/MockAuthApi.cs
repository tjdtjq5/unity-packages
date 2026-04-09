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

        public IReadOnlyList<(string endpoint, string body)> Requests => _requests;
        public int CallCount => _requests.Count;

        public void Enqueue(string? response) => _responses.Enqueue(response);

        public Task<string?> PostAsync(string endpoint, string jsonBody)
        {
            _requests.Add((endpoint, jsonBody));
            var response = _responses.Count > 0 ? _responses.Dequeue() : null;
            return Task.FromResult(response);
        }
    }
}
