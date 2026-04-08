using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// SupaRun 런타임 인스턴스. 모든 자원(Auth/Client/REST/Realtime/LocalDB/Storage/Transport)을 보유.
    ///
    /// P2-3 의 핵심: 정적 클래스 SupaRun 의 모든 instance state를 이 클래스로 옮김.
    /// SupaRun 정적 클래스는 lazy singleton facade로 이 인스턴스를 노출 (호환성 유지).
    ///
    /// 직접 생성하면 단위 테스트/DI 가능:
    /// <code>
    /// var options = new SupaRunRuntimeOptions
    /// {
    ///     SupabaseUrl = "...", AnonKey = "...",
    ///     Transport = mockTransport,
    ///     SessionStorage = new MemorySessionStorage(),
    /// };
    /// var runtime = new SupaRunRuntime(options);
    /// await runtime.Login();
    /// var data = await runtime.GetAll&lt;MyConfig&gt;();
    /// </code>
    /// </summary>
    public class SupaRunRuntime : IDisposable
    {
        // ── 자원 (internal — SupaRun facade가 접근) ──
        internal readonly SupaRunRuntimeOptions _options;
        internal readonly IHttpTransport _transport;
        internal readonly SupaRunClient _client;
        internal readonly SupabaseRestClient _restClient;
        internal readonly SupaRunAuth _auth;
        internal readonly Supabase.SupabaseRealtime _realtime;
        internal readonly LocalGameDB _localDB;
        internal readonly ISessionStorage _sessionStorage;

        bool _disposed;

        // ── public 프로퍼티 ──
        /// <summary>HTTP 클라이언트(Cloud Run). null 가능.</summary>
        public IServerClient ServerClient => _client;

        /// <summary>인증 매니저. null 가능 (Supabase 설정 없을 때).</summary>
        public SupaRunAuth Auth => _auth;

        /// <summary>실시간 채널 클라이언트. null 가능.</summary>
        public Supabase.SupabaseRealtime Realtime => _realtime;

        /// <summary>로컬 DB (개발 모드 fallback). 항상 non-null.</summary>
        public LocalGameDB LocalDB => _localDB ?? LocalGameDB.Instance;

        /// <summary>세션 저장소.</summary>
        public ISessionStorage SessionStorage => _sessionStorage;

        /// <summary>현재 로그인되어 있는지 여부.</summary>
        public bool IsLoggedIn => _auth?.IsLoggedIn ?? false;

        /// <summary>현재 인증 세션. null 가능.</summary>
        public AuthSession CurrentSession => _auth?.Session ?? _client?.Session;

        /// <summary>현재 로그인된 플레이어 ID. null 가능.</summary>
        public string PlayerId => CurrentSession?.userId;

        // ── 생성 ──

        /// <summary>
        /// 옵션 객체로 명시적 생성. 단위 테스트/DI에 사용.
        /// SupabaseUrl/AnonKey가 비어있으면 Auth/REST/Realtime은 생성되지 않음 (LocalDB만 동작).
        /// </summary>
        public SupaRunRuntime(SupaRunRuntimeOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            var supabaseUrl = options.SupabaseUrl;
            var anonKey = options.AnonKey;
            var cloudRunUrl = options.CloudRunUrl;

            // Transport: 옵션 우선, 없으면 단일 UnityHttpTransport (P2-1g 패턴)
            _transport = options.Transport ?? new UnityHttpTransport();

            // Cloud Run client (cloudRunUrl 있을 때만)
            if (!string.IsNullOrEmpty(cloudRunUrl))
            {
                var config = new ServerConfig { cloudRunUrl = cloudRunUrl, supabaseUrl = supabaseUrl, supabaseAnonKey = anonKey };
                _client = new SupaRunClient(config, _transport);
            }

            // [Config] PostgREST 클라이언트 (Supabase 설정 있을 때만)
            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(anonKey))
                _restClient = new SupabaseRestClient(supabaseUrl, anonKey, _transport);

            // Session storage: 옵션 우선, 없으면 SecureSessionStorage + MPPM 자동 prefix (P2-2)
            _sessionStorage = options.SessionStorage ?? new SecureSessionStorage(SupaRun.GetMppmInstanceId());

            // Auth + Realtime 초기화 (Supabase 설정 있을 때만)
            // 로그인 자체는 Login()을 명시적으로 호출해야 시작됨.
            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(anonKey))
            {
                // P1-3: SupaRunAuth가 정적 SupaRun.Client에 의존하지 않도록 IServerClient(_client)를 주입.
                // P2-2: ISessionStorage 주입.
                _auth = new SupaRunAuth(supabaseUrl, anonKey, cloudRunUrl, _client, _sessionStorage);
                _auth.OnSessionChanged += OnAuthSessionChanged;

                // 토큰 갱신 콜백 연결 (401 시 SupaRunClient → SupaRunAuth.TryRefreshToken)
                if (_client != null)
                    _client.OnTokenRefresh = async () => await _auth.TryRefreshToken();

                // Realtime 초기화 (WebSocket 연결은 첫 채널 Subscribe 시)
                _realtime = new Supabase.SupabaseRealtime(supabaseUrl, anonKey);
            }

            _localDB = LocalGameDB.Instance;
        }

        /// <summary>
        /// SupaRunSettings.json (Editor) 또는 Resources/SupaRunConfig.json (Build) 에서
        /// 자동 로드하여 SupaRunRuntime을 생성하는 static factory.
        /// </summary>
        public static SupaRunRuntime CreateFromSettings()
        {
            var options = LoadOptionsFromSettings();
            return new SupaRunRuntime(options);
        }

        static SupaRunRuntimeOptions LoadOptionsFromSettings()
        {
            var options = new SupaRunRuntimeOptions();

            #if UNITY_EDITOR
            // UserSettings/SupaRunSettings.json에서 직접 읽기.
            // MPPM Virtual Player도 진짜 프로젝트 루트를 공유하도록 GetProjectRoot() 사용.
            var settingsPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(SupaRun.GetProjectRoot() ?? ".", "UserSettings", "SupaRunSettings.json"));
            if (System.IO.File.Exists(settingsPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(settingsPath);
                    var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (dict != null)
                    {
                        options.CloudRunUrl = dict.TryGetValue("cloudRunUrl", out var u) ? u?.ToString() ?? "" : "";
                        options.SupabaseUrl = dict.TryGetValue("supabaseUrl", out var s) ? s?.ToString() ?? "" : "";
                        options.AnonKey = dict.TryGetValue("supabaseAnonKey", out var k) ? k?.ToString() ?? "" : "";
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SupaRun] {settingsPath} 파싱 실패: {ex.Message}");
                }
            }
            else
            {
                UnityEngine.Debug.LogError(
                    $"[SupaRun] Settings 파일을 찾을 수 없습니다: {settingsPath}\n" +
                    "SupaRun Dashboard에서 Supabase URL/Anon Key를 입력했는지 확인하세요.");
            }
            #else
            // 빌드: Resources/SupaRunConfig.json에서 읽기
            var configAsset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("SupaRunConfig");
            if (configAsset != null)
            {
                var runtimeConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<SupaRunRuntimeConfig>(configAsset.text);
                if (runtimeConfig != null)
                {
                    options.CloudRunUrl = runtimeConfig.cloudRunUrl ?? "";
                    options.SupabaseUrl = runtimeConfig.supabaseUrl ?? "";
                    options.AnonKey = runtimeConfig.supabaseAnonKey ?? "";
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("[SupaRun] SupaRunConfig.json을 찾을 수 없습니다. 빌드가 정상적으로 되었는지 확인하세요.");
            }
            #endif

            return options;
        }

        // ── 데이터 API ──

        /// <summary>단건 조회. [Config]→Supabase REST 직접, [Table]→Cloud Run, 미배포→LocalGameDB.</summary>
        public async Task<ServerResponse<T>> Get<T>(object id)
        {
            if (_client != null)
            {
                if (SupaRun.IsConfig<T>() && _restClient != null)
                    return await _restClient.Get<T>(id);

                var typeName = typeof(T).Name.ToLower();
                return await _client.GetAsync<T>($"api/{typeName}/{id}");
            }

            // LocalGameDB fallback — 진단 경고 (1회) + ServerResponse.hint 세팅
            SupaRun.WarnLocalDbFallbackOnce($"Get<{typeof(T).Name}>");
            var data = await _localDB.Get<T>(id);
            return new ServerResponse<T>
            {
                success = data != null,
                data = data,
                statusCode = data != null ? 200 : 404,
                errorType = data != null ? ErrorType.None : ErrorType.NotFound,
                error = data != null ? null : $"{typeof(T).Name} not found: {id}",
                isAuthenticated = false,
                hint = "LocalGameDB fallback — 서버 미연결. SupaRunSettings.json 로드 실패 또는 supabaseUrl/anonKey 미설정.",
            };
        }

        /// <summary>전체 조회. [Config]→Supabase REST 직접, [Table]→Cloud Run, 미배포→LocalGameDB.</summary>
        public async Task<ServerResponse<List<T>>> GetAll<T>()
        {
            if (_client != null)
            {
                if (SupaRun.IsConfig<T>() && _restClient != null)
                    return await _restClient.GetAll<T>();

                var typeName = typeof(T).Name.ToLower();
                return await _client.GetAsync<List<T>>($"api/{typeName}");
            }

            // LocalGameDB fallback — 진단 경고 (1회) + ServerResponse.hint 세팅
            SupaRun.WarnLocalDbFallbackOnce($"GetAll<{typeof(T).Name}>");
            var allData = await _localDB.GetAll<T>();
            return new ServerResponse<List<T>>
            {
                success = true,
                data = allData,
                statusCode = 200,
                isAuthenticated = false,
                hint = "LocalGameDB fallback — 서버 미연결. SupaRunSettings.json 로드 실패 또는 supabaseUrl/anonKey 미설정.",
            };
        }

        // ── Auth API ──

        /// <summary>
        /// 명시적 로그인. 앱 시작 시 한 번 호출. 중복 호출 안전.
        /// SignOut/DeleteAccount 후 재호출도 안전 (IsLoggedIn 체크로 진실 소스는 SupaRunAuth).
        /// 이 호출 이전에는 데이터 API(GetAll/Get/서비스 프록시)를 쓰면 안 됨.
        /// </summary>
        public async Task Login()
        {
            if (_auth == null)
            {
                UnityEngine.Debug.LogError(
                    "[SupaRun] Auth 미초기화 — SupaRunSettings.json의 supabaseUrl/supabaseAnonKey를 확인하세요.");
                return;
            }
            if (_auth.IsLoggedIn) return;       // 이미 로그인됨
            await _auth.EnsureLoggedIn();        // SupaRunAuth 자체에서 동시 호출 dedup
        }

        /// <summary>
        /// 로그인 완료 대기. SG 프록시(ServerAPI.*)에서 서버 호출 전 방어용.
        /// 정상 흐름: 앱 시작 시 Login()을 먼저 호출 → 여기서는 IsLoggedIn 체크로 즉시 리턴.
        /// 비정상 흐름: Login() 미호출 상태로 서비스 호출이 들어오면 에러 로그 출력 후
        ///           안전망으로 자동 게스트 로그인 (silent failure 방지).
        /// </summary>
        public async Task WaitForAuth()
        {
            if (_auth == null) return;
            if (_auth.IsLoggedIn) return;

            UnityEngine.Debug.LogError(
                "[SupaRun] Login() 미호출 상태로 서버 호출이 발생했습니다. " +
                "앱 시작 시 `await SupaRun.Login()`을 먼저 실행하세요. " +
                "지금은 안전망으로 자동 게스트 로그인을 수행합니다.");
            await _auth.EnsureLoggedIn();
        }

        // ── 내부 ──

        /// <summary>SupaRunAuth.OnSessionChanged 핸들러 — client/restClient/realtime에 토큰 전파.</summary>
        void OnAuthSessionChanged(AuthSession session)
        {
            if (_client != null) _client.Session = session;
            // [Config] REST 클라이언트도 유저 JWT 사용 → RLS authenticated 정책 통과
            if (_restClient != null) _restClient.Session = session;
            // Realtime에 액세스 토큰 전달
            if (_realtime != null && session != null)
                _realtime.SetAccessToken(session.accessToken);
        }

        // ── IDisposable ──

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 이벤트 핸들러 해제 (생성자에서 등록한 것)
            if (_auth != null)
                _auth.OnSessionChanged -= OnAuthSessionChanged;

            // Realtime WebSocket 연결 해제
            _realtime?.Disconnect();

            // SupaRunClient/SupabaseRestClient/LocalGameDB는 IDisposable 아님 — 자원 해제 불필요.
        }
    }
}
