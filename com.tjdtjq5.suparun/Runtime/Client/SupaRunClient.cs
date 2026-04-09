#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Tjdtjq5.SupaRun
{
    /// <summary>HTTP 클라이언트 (Cloud Run). UnityWebRequest 직접 사용에서 HttpExecutor + Strategy 패턴으로 리팩터됨.</summary>
    /// <remarks>
    /// P2-1f: HTTP 송신/재시도/토큰 갱신을 HttpExecutor에 위임.
    /// - 인증 헤더: BearerTokenAuth (Session 있고 만료 안 됐을 때만 첨부)
    /// - 재시도: ExponentialBackoffRetry (3회, 1s/2s/4s 지수 백오프)
    /// - 401 갱신: CallbackAuthRefresher (OnTokenRefresh 콜백 위임)
    ///
    /// public API (GetAsync, PostAsync, PostAsync&lt;T&gt;, Session, OnTokenRefresh) 는 그대로 유지.
    /// </remarks>
    public class SupaRunClient : IServerClient
    {
        readonly ServerConfig _config;
        readonly HttpExecutor _executor;

        /// <summary>인증 세션. SupabaseAuth가 설정. 그 전까지 null.</summary>
        public AuthSession? Session { get; internal set; }

        /// <summary>토큰 갱신 콜백. 401 시 호출되어 새 세션을 반환. 실패 시 null.</summary>
        public Func<Task<AuthSession?>>? OnTokenRefresh { get; set; }

        public SupaRunClient(ServerConfig config, IHttpTransport? transport = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // Strategy 조합:
            // - BearerTokenAuth: Session lazy read (매 송신 시 평가)
            // - ExponentialBackoffRetry: 5xx/Timeout 시 1s/2s/4s 백오프, 최대 3회
            // - CallbackAuthRefresher: OnTokenRefresh 프로퍼티를 lazy read (closure)
            var t = transport ?? new UnityHttpTransport();
            var auth = new BearerTokenAuth(() => Session);
            var retry = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 1000);
            var refresher = new CallbackAuthRefresher(async () =>
                OnTokenRefresh != null ? await OnTokenRefresh() : null);
            _executor = new HttpExecutor(t, auth, retry, refresher);
        }

        string BaseUrl => _config.cloudRunUrl;

        public async Task<ServerResponse<T>> GetAsync<T>(string endpoint)
        {
            var request = BuildRequest("GET", endpoint, null);
            var response = await _executor.ExecuteAsync(request);
            return ParseResponse<T>(response);
        }

        public async Task<ServerResponse<T>> PostAsync<T>(string endpoint, object payload)
        {
            var request = BuildRequest("POST", endpoint, payload);
            var response = await _executor.ExecuteAsync(request);
            return ParseResponse<T>(response);
        }

        public async Task<ServerResponse> PostAsync(string endpoint, object payload)
        {
            var result = await PostAsync<object>(endpoint, payload);
            return new ServerResponse
            {
                success = result.success,
                error = result.error,
                errorType = result.errorType,
                statusCode = result.statusCode,
                isAuthenticated = result.isAuthenticated,
                hint = result.hint,
            };
        }

        HttpTransportRequest BuildRequest(string method, string endpoint, object payload)
        {
            var request = new HttpTransportRequest
            {
                Url = $"{BaseUrl}/{endpoint}",
                Method = method,
                TimeoutSeconds = 30,
            };
            request.Headers["Content-Type"] = "application/json";

            if (payload != null)
            {
                var json = JsonConvert.SerializeObject(payload);
                request.Body = Encoding.UTF8.GetBytes(json);
            }

            return request;
        }

        ServerResponse<T> ParseResponse<T>(HttpTransportResponse response)
        {
            var result = new ServerResponse<T>
            {
                statusCode = response.StatusCode
            };

            if (response.Success)
            {
                result.success = true;
                var text = response.ResponseText;
                if (!string.IsNullOrEmpty(text))
                {
                    try
                    {
                        result.data = JsonConvert.DeserializeObject<T>(text);
                    }
                    catch (JsonException)
                    {
                        // plain text 응답 (string, int 등)
                        if (typeof(T) == typeof(string))
                            result.data = (T)(object)text;
                        else
                            result.data = (T)Convert.ChangeType(text.Trim(), typeof(T));
                    }
                }
            }
            else
            {
                result.success = false;
                // 서버 응답 본문에 상세 에러가 있으면 포함
                var body = response.ResponseText;
                result.error = !string.IsNullOrEmpty(body)
                    ? $"{response.Error}\n{body}"
                    : response.Error;
                result.errorType = ClassifyError(response);
            }

            return result;
        }

        static ErrorType ClassifyError(HttpTransportResponse response)
        {
            if (response.IsConnectionError)
                return ErrorType.NetworkError;

            return response.StatusCode switch
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
