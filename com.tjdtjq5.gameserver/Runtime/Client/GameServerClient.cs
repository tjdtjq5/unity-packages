using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.GameServer
{
    /// <summary>HTTP 클라이언트. UnityWebRequest 기반, 자동 재시도 + 토큰 갱신.</summary>
    public class GameServerClient
    {
        readonly ServerConfig _config;
        const int MaxRetries = 3;

        /// <summary>인증 세션. Phase 3 (Auth)에서 SupabaseAuth가 설정. 그 전까지 null.</summary>
        public AuthSession Session { get; internal set; }

        public GameServerClient(ServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        string BaseUrl => _config.cloudRunUrl;

        public async Task<ServerResponse<T>> GetAsync<T>(string endpoint)
        {
            return await RequestWithRetry<T>("GET", endpoint, null);
        }

        public async Task<ServerResponse<T>> PostAsync<T>(string endpoint, object payload)
        {
            return await RequestWithRetry<T>("POST", endpoint, payload);
        }

        public async Task<ServerResponse> PostAsync(string endpoint, object payload)
        {
            var result = await RequestWithRetry<object>("POST", endpoint, payload);
            return new ServerResponse
            {
                success = result.success,
                error = result.error,
                errorType = result.errorType,
                statusCode = result.statusCode
            };
        }

        async Task<ServerResponse<T>> RequestWithRetry<T>(string method, string endpoint, object payload)
        {
            ServerResponse<T> lastResponse = null;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                lastResponse = await SendRequest<T>(method, endpoint, payload);

                // 성공
                if (lastResponse.success)
                    return lastResponse;

                // 토큰 만료 → 자동 갱신 후 재시도
                if (lastResponse.errorType == ErrorType.AuthExpired && Session != null)
                {
                    // TODO: RefreshToken 구현 후 연결
                    return lastResponse;
                }

                // 재시도 가능한 에러 (5xx, Timeout)
                if (lastResponse.errorType is ErrorType.ServerError or ErrorType.Timeout && attempt < MaxRetries)
                {
                    int delayMs = (int)(Math.Pow(2, attempt) * 1000); // 1s, 2s, 4s
                    await Task.Delay(delayMs);
                    continue;
                }

                // 재시도 불가능한 에러
                break;
            }

            return lastResponse;
        }

        async Task<ServerResponse<T>> SendRequest<T>(string method, string endpoint, object payload)
        {
            var url = $"{BaseUrl}/{endpoint}";

            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            if (payload != null)
            {
                var json = JsonUtility.ToJson(payload);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            }

            if (Session != null && !Session.IsExpired)
                request.SetRequestHeader("Authorization", $"Bearer {Session.accessToken}");

            request.timeout = 30;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            return ParseResponse<T>(request);
        }

        ServerResponse<T> ParseResponse<T>(UnityWebRequest request)
        {
            var response = new ServerResponse<T>
            {
                statusCode = (int)request.responseCode
            };

            if (request.result == UnityWebRequest.Result.Success)
            {
                response.success = true;
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
                    response.data = JsonUtility.FromJson<T>(request.downloadHandler.text);
            }
            else
            {
                response.success = false;
                response.error = request.error;
                response.errorType = ClassifyError(request);
            }

            return response;
        }

        static ErrorType ClassifyError(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError)
                return ErrorType.NetworkError;

            return request.responseCode switch
            {
                0 => ErrorType.Timeout,
                401 => ErrorType.AuthExpired,
                403 => ErrorType.AuthFailed,
                400 => ErrorType.BadRequest,
                404 => ErrorType.NotFound,
                429 => ErrorType.RateLimit,
                >= 500 => ErrorType.ServerError,
                _ => ErrorType.BadRequest
            };
        }
    }
}
