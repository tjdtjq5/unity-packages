using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun
{
    /// <summary>Supabase Auth. 게스트 자동 로그인 + 토큰 관리.</summary>
    public class SupabaseAuth
    {
        readonly string _supabaseUrl;
        readonly string _anonKey;

        const string PREF_ACCESS  = "SupaRun_AccessToken";
        const string PREF_REFRESH = "SupaRun_RefreshToken";
        const string PREF_USER_ID = "SupaRun_UserId";
        const string PREF_IS_GUEST = "SupaRun_IsGuest";

        public AuthSession Session { get; private set; }
        public bool IsLoggedIn => Session != null && !string.IsNullOrEmpty(Session.accessToken);
        public string UserId => Session?.userId;
        public bool IsGuest => Session?.isGuest ?? true;

        public event Action<AuthSession> OnSessionChanged;
        public event Action OnSessionExpired;
        /// <summary>다른 기기에서 로그인 시 호출. 서버 세션 관리 구현 후 동작.</summary>
        #pragma warning disable CS0067
        public event Action OnKicked;
        #pragma warning restore CS0067
        public event Action<string> OnBanned;

        OAuthHandler _oauthHandler;

        public SupabaseAuth(string supabaseUrl, string anonKey, string cloudRunUrl = null)
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/');
            _anonKey = anonKey;
            _oauthHandler = new OAuthHandler(supabaseUrl, cloudRunUrl);
        }

        Task _loginTask;

        /// <summary>자동 로그인. 중복 호출 방지.</summary>
        public Task EnsureLoggedIn()
        {
            if (_loginTask != null) return _loginTask;
            _loginTask = DoEnsureLoggedIn();
            return _loginTask;
        }

        async Task DoEnsureLoggedIn()
        {
            try
            {
                // 1. 저장된 세션 복원
                var saved = LoadSession();
                if (saved != null)
                {
                    if (!saved.IsExpired)
                    {
                        Session = saved;
                        OnSessionChanged?.Invoke(Session);
                        Debug.Log($"[SupaRun:Auth] 세션 복원: {Session.userId}");
                        return;
                    }

                    // 2. 만료 → 갱신 시도
                    if (!string.IsNullOrEmpty(saved.refreshToken))
                    {
                        var refreshed = await RefreshSession(saved.refreshToken);
                        if (refreshed != null)
                        {
                            refreshed.isGuest = saved.isGuest;
                            Session = refreshed;
                            SaveSession(Session);
                            OnSessionChanged?.Invoke(Session);
                            Debug.Log($"[SupaRun:Auth] 토큰 갱신: {Session.userId}");
                            return;
                        }
                        OnSessionExpired?.Invoke();
                    }
                }

                // 3. 익명 로그인 (Supabase Anonymous Sign-in)
                var guest = await SignInAnonymously();
                if (guest != null)
                {
                    guest.isGuest = true;
                    Session = guest;
                    SaveSession(Session);
                    OnSessionChanged?.Invoke(Session);
                    Debug.Log($"[SupaRun:Auth] 게스트 생성: {Session.userId}");
                }
                else
                {
                    Debug.LogWarning("[SupaRun:Auth] 로그인 실패. Supabase 설정을 확인하세요.\n" +
                        "Supabase > Auth > Settings > Anonymous Sign-ins이 활성화되어 있어야 합니다.");
                }
            }
            finally
            {
                _loginTask = null;
            }
        }

        // ── Supabase API ──

        /// <summary>Supabase Anonymous Sign-in. 이메일/비밀번호 불필요.</summary>
        async Task<AuthSession> SignInAnonymously()
        {
            var result = await Post("/auth/v1/signup", "{}");
            return result != null ? ParseAuthResponse(result) : null;
        }

        /// <summary>현재 세션의 토큰을 갱신. 성공 시 새 세션 반환 + OnSessionChanged 발행.</summary>
        public async Task<AuthSession> TryRefreshToken()
        {
            if (Session == null || string.IsNullOrEmpty(Session.refreshToken))
                return null;

            var refreshed = await RefreshSession(Session.refreshToken);
            if (refreshed != null)
            {
                refreshed.isGuest = Session.isGuest;
                Session = refreshed;
                SaveSession(refreshed);
                OnSessionChanged?.Invoke(refreshed);
            }
            return refreshed;
        }

        async Task<AuthSession> RefreshSession(string refreshToken)
        {
            var body = JsonConvert.SerializeObject(new { refresh_token = refreshToken });
            var result = await Post("/auth/v1/token?grant_type=refresh_token", body);
            return result != null ? ParseAuthResponse(result) : null;
        }

        AuthSession ParseAuthResponse(string json)
        {
            try
            {
                var obj = JObject.Parse(json);

                // 에러 체크
                if (obj["error"] != null || obj["error_code"] != null || obj["msg"] != null)
                {
                    var error = obj["error"]?.ToString() ?? obj["msg"]?.ToString() ?? "Unknown error";
                    var desc = obj["error_description"]?.ToString() ?? obj["message"]?.ToString() ?? "";
                    var code = obj["error_code"]?.ToString() ?? "";
                    Debug.LogWarning($"[SupaRun:Auth] {error} {desc} (code: {code})");
                    return null;
                }

                var accessToken = obj["access_token"]?.ToString();
                if (string.IsNullOrEmpty(accessToken)) return null;

                var expiresIn = obj["expires_in"]?.Value<long>() ?? 3600;

                return new AuthSession
                {
                    accessToken = accessToken,
                    refreshToken = obj["refresh_token"]?.ToString(),
                    userId = obj["user"]?["id"]?.ToString(),
                    expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:Auth] 파싱 실패: {ex.Message}");
                return null;
            }
        }

        // ── HTTP ──

        async Task<string> Post(string endpoint, string jsonBody)
        {
            var url = _supabaseUrl + endpoint;
            Debug.Log($"[SupaRun:Auth] POST {url}");

            if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_anonKey))
            {
                Debug.LogWarning($"[SupaRun:Auth] Supabase URL 또는 Anon Key가 비어있습니다. url={_supabaseUrl}, key={(_anonKey?.Length > 0 ? "설정됨" : "비어있음")}");
                return null;
            }

            try
            {
                using var request = new UnityWebRequest(url, "POST");
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", _anonKey);
                request.timeout = 15;

                var op = request.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                Debug.Log($"[SupaRun:Auth] Response {request.responseCode}: {request.downloadHandler.text?.Substring(0, System.Math.Min(200, request.downloadHandler.text?.Length ?? 0))}");

                if (request.result == UnityWebRequest.Result.Success ||
                    request.responseCode < 500)
                    return request.downloadHandler.text;

                Debug.LogWarning($"[SupaRun:Auth] HTTP {request.responseCode}: {request.error}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:Auth] 요청 실패: {ex.Message}");
                return null;
            }
        }

        // ── 세션 저장/로드 ──

        void SaveSession(AuthSession session)
        {
            SecureStorage.Set(PREF_ACCESS, session.accessToken);
            SecureStorage.Set(PREF_REFRESH, session.refreshToken ?? "");
            SecureStorage.Set(PREF_USER_ID, session.userId ?? "");
            SecureStorage.SetInt(PREF_IS_GUEST, session.isGuest ? 1 : 0);
            SecureStorage.Save();
        }

        AuthSession LoadSession()
        {
            var token = SecureStorage.Get(PREF_ACCESS, "");
            if (string.IsNullOrEmpty(token)) return null;

            return new AuthSession
            {
                accessToken = token,
                refreshToken = SecureStorage.Get(PREF_REFRESH, ""),
                userId = SecureStorage.Get(PREF_USER_ID, ""),
                isGuest = SecureStorage.GetInt(PREF_IS_GUEST, 1) == 1,
                expiresAt = ParseExpFromJwt(token)
            };
        }

        public void ClearSession()
        {
            Session = null;
            SecureStorage.Delete(PREF_ACCESS);
            SecureStorage.Delete(PREF_REFRESH);
            SecureStorage.Delete(PREF_USER_ID);
            SecureStorage.Delete(PREF_IS_GUEST);
            SecureStorage.Save();
        }

        // ── v2: OAuth 로그인 ──

        /// <summary>소셜 로그인. 브라우저 열고 인증 후 토큰 수신.</summary>
        public async Task SignIn(AuthProvider provider)
        {
            if (provider == AuthProvider.Guest)
            {
                await EnsureLoggedIn();
                return;
            }

            if (provider == AuthProvider.GPGS)
            {
                await SignInWithPlatform(new GPGSAuthHandler());
                return;
            }

            if (provider == AuthProvider.GameCenter)
            {
                await SignInWithPlatform(new GameCenterAuthHandler());
                return;
            }

            var url = await _oauthHandler.Authenticate(provider);
            if (url == null)
            {
                Debug.LogWarning($"[SupaRun:Auth] {provider} 로그인 실패");
                return;
            }

            var (accessToken, refreshToken) = OAuthHandler.ParseTokensFromUrl(url);
            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.LogWarning("[SupaRun:Auth] 토큰 파싱 실패");
                return;
            }

            Session = new AuthSession
            {
                accessToken = accessToken,
                refreshToken = refreshToken,
                userId = ParseUserIdFromJwt(accessToken),
                expiresAt = ParseExpFromJwt(accessToken),
                isGuest = false
            };
            SaveSession(Session);
            OnSessionChanged?.Invoke(Session);
            Debug.Log($"[SupaRun:Auth] {provider} 로그인 성공: {Session.userId}");
        }

        /// <summary>게스트 계정에 소셜 계정 연결. Supabase Identity Link API 사용.</summary>
        public async Task<bool> LinkProvider(AuthProvider provider)
        {
            if (!IsLoggedIn || !IsGuest)
            {
                Debug.LogWarning("[SupaRun:Auth] 게스트 로그인 상태에서만 연결 가능");
                return false;
            }

            var url = await _oauthHandler.AuthenticateForLink(provider, Session.accessToken);
            if (url == null) return false;

            var (accessToken, refreshToken) = OAuthHandler.ParseTokensFromUrl(url);
            if (string.IsNullOrEmpty(accessToken)) return false;

            // Identity Link API → 기존 userId 유지, 새 토큰 발급
            Session = new AuthSession
            {
                accessToken = accessToken,
                refreshToken = refreshToken,
                userId = ParseUserIdFromJwt(accessToken), // 토큰에서 추출 (기존 userId와 동일해야 함)
                expiresAt = ParseExpFromJwt(accessToken),
                isGuest = false
            };
            SaveSession(Session);
            OnSessionChanged?.Invoke(Session);
            Debug.Log($"[SupaRun:Auth] {provider} 연결 완료: {Session.userId}");
            return true;
        }

        /// <summary>로그아웃. 이후 게스트로 자동 재로그인.</summary>
        public async Task SignOut()
        {
            if (!IsLoggedIn) return;

            await Post("/auth/v1/logout", "{}");
            ClearSession();
            Debug.Log("[SupaRun:Auth] 로그아웃");

            // 게스트로 자동 재생성
            await EnsureLoggedIn();
        }

        /// <summary>계정 삭제. 서버에서 데이터 삭제 후 Auth 삭제.</summary>
        public async Task<bool> DeleteAccount()
        {
            if (!IsLoggedIn)
            {
                Debug.LogWarning("[SupaRun:Auth] 로그인 상태에서만 삭제 가능");
                return false;
            }

            // 서버에 삭제 요청 (서버가 service_role로 Supabase 유저 삭제)
            var client = SupaRun.Client;
            if (client != null)
            {
                var result = await client.PostAsync("api/auth/delete-account", null);
                if (!result.success)
                {
                    Debug.LogWarning($"[SupaRun:Auth] 계정 삭제 실패: {result.error}");
                    return false;
                }
            }

            ClearSession();
            Debug.Log("[SupaRun:Auth] 계정 삭제 완료");

            // 게스트로 자동 재생성
            await EnsureLoggedIn();
            return true;
        }

        /// <summary>밴 체크. 서버에서 확인.</summary>
        public async Task CheckBan()
        {
            if (!IsLoggedIn) return;

            var client = SupaRun.Client;
            if (client == null) return;

            var result = await client.GetAsync<BanStatus>($"api/auth/ban-check/{UserId}");
            if (result.success && result.data != null && result.data.banned)
            {
                OnBanned?.Invoke(result.data.reason);
                Debug.LogWarning($"[SupaRun:Auth] 계정 정지: {result.data.reason}");
            }
        }

        // ── 플랫폼 네이티브 인증 (GPGS, Game Center) ──

        /// <summary>플랫폼 SDK → 서버 토큰 교환 → JWT.</summary>
        async Task SignInWithPlatform(IPlatformAuth handler)
        {
            if (!handler.IsAvailable)
            {
                Debug.LogWarning($"[SupaRun:Auth] {handler.Provider}는 이 플랫폼에서 사용할 수 없습니다.");
                return;
            }

            var token = await handler.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"[SupaRun:Auth] {handler.Provider} 토큰 획득 실패");
                return;
            }

            // 서버로 토큰 전송 → JWT 수신
            var endpoint = handler.Provider == AuthProvider.GPGS
                ? "api/auth/gpgs" : "api/auth/gamecenter";

            var client = SupaRun.Client;
            if (client == null)
            {
                Debug.LogWarning("[SupaRun:Auth] 서버 미연결 — cloudRunUrl을 확인하세요.");
                return;
            }

            var result = await client.PostAsync<AuthTokenResponse>(endpoint, new { token });

            if (result.success && result.data != null)
            {
                Session = new AuthSession
                {
                    accessToken = result.data.accessToken,
                    refreshToken = result.data.refreshToken,
                    userId = result.data.userId ?? ParseUserIdFromJwt(result.data.accessToken),
                    expiresAt = ParseExpFromJwt(result.data.accessToken),
                    isGuest = false
                };
                SaveSession(Session);
                OnSessionChanged?.Invoke(Session);
                Debug.Log($"[SupaRun:Auth] {handler.Provider} 로그인 성공: {Session.userId}");
            }
            else
            {
                Debug.LogWarning($"[SupaRun:Auth] {handler.Provider} 서버 인증 실패: {result.error}");
            }
        }

        // ── 유틸 ──

        static string ParseUserIdFromJwt(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var obj = JObject.Parse(json);
                return obj["sub"]?.ToString();
            }
            catch (Exception ex) { Debug.LogWarning($"[SupaRun:Auth] JWT userId 파싱 실패: {ex.Message}"); return null; }
        }

        /// <summary>JWT에서 만료 시간 추출.</summary>
        static long ParseExpFromJwt(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return 0;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                // Base64 패딩
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var obj = JObject.Parse(json);
                return obj["exp"]?.Value<long>() ?? 0;
            }
            catch (Exception ex) { Debug.LogWarning($"[SupaRun:Auth] JWT exp 파싱 실패: {ex.Message}"); return 0; }
        }
    }
}
