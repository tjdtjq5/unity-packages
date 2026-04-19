#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>Supabase PostgREST 직접 조회. [Config] 타입 전용.</summary>
    /// <remarks>
    /// P2-1e: HTTP 송신을 HttpExecutor + IHttpTransport로 위임. 인증 헤더는 BearerJwtOrAnonAuth strategy.
    /// 호출자(SupaRun)가 동일 transport 인스턴스를 주입하면 패키지 전체에서 단일 transport 사용.
    /// </remarks>
    class SupabaseRestClient
    {
        readonly string _restUrl; // https://xxx.supabase.co/rest/v1
        readonly HttpExecutor _executor;

        /// <summary>
        /// 현재 인증 세션. SupaRun.Auth.OnSessionChanged에서 주입된다.
        /// 세션이 있으면 Authorization Bearer에 유저 JWT를 사용해
        /// `authenticated` role이 필요한 RLS 정책을 통과한다.
        /// </summary>
        public AuthSession? Session { get; set; }

        public SupabaseRestClient(string supabaseUrl, string anonKey, IHttpTransport? transport = null,
                                  IAuthRefresher? authRefresher = null)
        {
            _restUrl = supabaseUrl?.TrimEnd('/') + "/rest/v1";

            // Strategy 조합: apikey + Bearer(JWT or anon), 재시도 없음, 401 시 authRefresher로 1회 갱신 재시도
            var t = transport ?? new UnityHttpTransport();
            var auth = new BearerJwtOrAnonAuth(() => Session, anonKey);
            _executor = new HttpExecutor(t, auth, new NoRetry(), authRefresher);
        }

        static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }

        public async Task<ServerResponse<T>> Get<T>(object id)
        {
            var table = ToSnakeCase(typeof(T).Name);
            var url = $"{_restUrl}/{table}?id=eq.{id}&limit=1";

            var list = await Fetch<List<T>>(url);
            if (!list.success)
                return new ServerResponse<T> { success = false, error = list.error, errorType = list.errorType, statusCode = list.statusCode };

            var data = list.data != null && list.data.Count > 0 ? list.data[0] : default;
            return new ServerResponse<T>
            {
                success = data != null,
                data = data,
                statusCode = data != null ? 200 : 404,
                errorType = data != null ? ErrorType.None : ErrorType.NotFound,
                error = data != null ? null : $"{typeof(T).Name} not found: {id}"
            };
        }

        public async Task<ServerResponse<List<T>>> GetAll<T>()
        {
            var table = ToSnakeCase(typeof(T).Name);
            var url = $"{_restUrl}/{table}";
            return await Fetch<List<T>>(url);
        }

        async Task<ServerResponse<T>> Fetch<T>(string url)
        {
            // anonymous 호출 사전 경고: silent failure(success=true, count=0) 진단용
            // RLS authenticated 정책이 걸린 테이블이면 빈 결과가 반환된다.
            // (P0-4, P2-4 보존)
            bool isAnonymous = string.IsNullOrEmpty(Session?.accessToken);
            string? anonHint = null;
            if (isAnonymous)
            {
                anonHint = "anonymous 호출 — RLS authenticated 정책이 걸린 테이블이면 빈 결과가 반환됨. SupaRun.Login() 호출 여부 확인 필요.";
                Debug.LogWarning($"[SupaRun:REST] {anonHint} ({url})");
            }

            // 인증 헤더는 BearerJwtOrAnonAuth strategy가 자동 적용
            var request = new HttpTransportRequest
            {
                Url = url,
                Method = "GET",
                TimeoutSeconds = 15,
            };
            var response = await _executor.ExecuteAsync(request);

            if (!response.Success)
            {
                return new ServerResponse<T>
                {
                    success = false,
                    error = response.Error,
                    statusCode = response.StatusCode,
                    errorType = response.IsConnectionError
                        ? ErrorType.NetworkError
                        : (response.StatusCode >= 500 ? ErrorType.ServerError : ErrorType.BadRequest),
                    isAuthenticated = !isAnonymous,
                    hint = anonHint,
                };
            }

            T data = default;
            try
            {
                if (!string.IsNullOrEmpty(response.ResponseText))
                    data = JsonConvert.DeserializeObject<T>(response.ResponseText);
            }
            catch (System.Exception ex)
            {
                return new ServerResponse<T>
                {
                    success = false,
                    error = $"JSON 파싱 실패: {ex.Message}",
                    statusCode = response.StatusCode,
                    errorType = ErrorType.BadRequest,
                    isAuthenticated = !isAnonymous,
                    hint = anonHint,
                };
            }

            return new ServerResponse<T>
            {
                success = true,
                data = data,
                statusCode = 200,
                isAuthenticated = !isAnonymous,
                hint = anonHint, // 성공해도 anonymous 호출이었음을 호출자에게 전달
            };
        }
    }
}
