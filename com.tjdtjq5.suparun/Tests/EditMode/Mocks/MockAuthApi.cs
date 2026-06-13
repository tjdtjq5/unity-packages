#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

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

        // null이면 PostAsync 즉시 완료. 세팅 시 PostAsync가 이 gate가 풀릴 때까지 in-flight로 대기
        // (동기 mock으로는 만들 수 없는 "진행 중" 윈도우를 만들어 동시 호출 dedup을 검증).
        UniTaskCompletionSource? _gate;

        /// <summary>이후 PostAsync들을 in-flight 상태로 묶어두는 gate를 설치하고 반환. TrySetResult()로 해제.</summary>
        public UniTaskCompletionSource Gate() => _gate = new UniTaskCompletionSource();

        public async UniTask<string?> PostAsync(string endpoint, string jsonBody, CancellationToken ct = default)
        {
            _requests.Add((endpoint, jsonBody));
            if (_gate != null) await _gate.Task;
            return _responses.Count > 0 ? _responses.Dequeue() : null;
        }

        public UniTask<string?> GetAuthenticatedAsync(string endpoint, string accessToken, CancellationToken ct = default)
        {
            _getRequests.Add((endpoint, accessToken));
            // 기본값 "ok": 큐를 따로 세팅하지 않은 기존 테스트의 세션 복원 경로가 VerifySession을 통과하도록.
            // 검증 실패 시나리오는 EnqueueGet(null)을 명시적으로 호출.
            var response = _getResponses.Count > 0 ? _getResponses.Dequeue() : "ok";
            return UniTask.FromResult(response);
        }
    }
}
