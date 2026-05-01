#nullable enable
using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// Supabase Auth HTTP API 구현체. HttpExecutor + ApiKeyAuth 조합.
    /// 패키지 전체 HTTP가 단일 IHttpTransport 경로를 사용하게 됨 (P2-1d 완성).
    /// </summary>
    class SupabaseAuthApi : IAuthApi
    {
        readonly string _supabaseUrl;
        readonly HttpExecutor _executor;

        public SupabaseAuthApi(string supabaseUrl, string anonKey, IHttpTransport? transport = null)
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? "";

            var t = transport ?? new UnityHttpTransport();
            var auth = new ApiKeyAuth(anonKey);
            _executor = new HttpExecutor(t, auth, new NoRetry());
        }

        public async UniTask<string?> PostAsync(string endpoint, string jsonBody, CancellationToken ct = default)
        {
            var url = _supabaseUrl + endpoint;
            SupaRun.LogVerbose($"[SupaRun:Auth] POST {url}");

            var request = new HttpTransportRequest
            {
                Url = url,
                Method = "POST",
                TimeoutSeconds = 15,
            };
            request.Headers["Content-Type"] = "application/json";
            request.Body = Encoding.UTF8.GetBytes(jsonBody);

            var response = await _executor.ExecuteAsync(request, ct);

            SupaRun.LogVerbose($"[SupaRun:Auth] Response {response.StatusCode}: " +
                $"{response.ResponseText?.Substring(0, Math.Min(200, response.ResponseText?.Length ?? 0))}");

            if (response.Success || response.StatusCode < 500)
                return response.ResponseText;

            Debug.LogWarning($"[SupaRun:Auth] HTTP {response.StatusCode}: {response.Error}");
            return null;
        }

        public async UniTask<string?> GetAuthenticatedAsync(string endpoint, string accessToken, CancellationToken ct = default)
        {
            var url = _supabaseUrl + endpoint;
            SupaRun.LogVerbose($"[SupaRun:Auth] GET {url} (Bearer)");

            var request = new HttpTransportRequest
            {
                Url = url,
                Method = "GET",
                TimeoutSeconds = 10,
            };
            // ApiKeyAuth 전략이 apikey 헤더를 세팅하고, 여기서 Authorization을 Bearer로 덮어쓴다.
            // ApiKeyAuth는 Authorization을 건드리지 않으므로 충돌 없음.
            request.Headers["Authorization"] = $"Bearer {accessToken}";

            var response = await _executor.ExecuteAsync(request, ct);

            SupaRun.LogVerbose($"[SupaRun:Auth] Response {response.StatusCode}");

            // 2xx 성공만 유효 세션으로 간주. 401/403/5xx 등은 null → 호출자가 invalid 판정.
            if (response.Success)
                return response.ResponseText;

            return null;
        }
    }
}
