#nullable enable
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

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
        internal readonly Supabase.IRealtimeClient _realtime;
        internal readonly LocalGameDB _localDB;
        internal readonly ISessionStorage _sessionStorage;

        // [SpecData] 세션 캐시 (#35) — 첫 조회 시점 값으로 세션 동안 고정된다.
        // 값은 List<T> 다. 비우는 곳은 RefreshConfigSessionAsync 하나뿐이다.
        readonly Dictionary<Type, object> _configCache = new Dictionary<Type, object>();

        bool _disposed;

        /// <summary>세션 협상 결과 (#35). Login()/RefreshConfigSessionAsync() 가 채운다. 협상 전엔 null.</summary>
        public ConfigSessionInfo? ConfigSession { get; private set; }

        // ── public 프로퍼티 ──
        /// <summary>HTTP 클라이언트(Cloud Run). null 가능.</summary>
        public IServerClient? ServerClient => _client;

        /// <summary>인증 매니저. null 가능 (Supabase 설정 없을 때).</summary>
        public SupaRunAuth? Auth => _auth;

        /// <summary>실시간 채널 클라이언트. null 가능.</summary>
        public Supabase.IRealtimeClient? Realtime => _realtime;

        /// <summary>로컬 DB (개발 모드 fallback). 항상 non-null.</summary>
        public LocalGameDB LocalDB => _localDB ?? LocalGameDB.Instance;

        /// <summary>세션 저장소.</summary>
        public ISessionStorage SessionStorage => _sessionStorage;

        /// <summary>현재 로그인되어 있는지 여부.</summary>
        public bool IsLoggedIn => _auth?.IsLoggedIn ?? false;

        /// <summary>현재 인증 세션. null 가능. 토큰의 단일 home인 Auth에서 읽는다.</summary>
        public AuthSession? CurrentSession => _auth?.CurrentSession;

        /// <summary>현재 로그인된 플레이어 ID. null 가능.</summary>
        public string? PlayerId => CurrentSession?.userId;

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

            // Session storage: 옵션 우선, 없으면 SecureSessionStorage + MPPM 자동 prefix (P2-2)
            _sessionStorage = options.SessionStorage ?? new SecureSessionStorage(SupaRun.GetMppmInstanceId());

            // Auth를 먼저 생성한다 — Auth가 토큰의 단일 home(ISessionProvider).
            // HTTP 클라이언트가 ctor로 Auth를 주입받아 요청 시 토큰을 pull하므로, client↔auth 생성 순환을
            // 끊기 위해 Auth는 server client 없이 먼저 만들고 아래에서 AttachServerClient로 late-bind한다.
            // 로그인 자체는 Login()을 명시적으로 호출해야 시작됨.
            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(anonKey))
            {
                // P3: IAuthApi 주입 (Auth HTTP 추상화 — UnityWebRequest 직접 사용 제거). P2-2: ISessionStorage 주입.
                var authApi = options.AuthApi ?? new SupabaseAuthApi(supabaseUrl, anonKey, _transport);
                _auth = new SupaRunAuth(supabaseUrl, anonKey, cloudRunUrl, _sessionStorage, authApi);
            }

            // Cloud Run client (cloudRunUrl 있을 때만) — Auth(ISessionProvider)에서 토큰을 pull.
            if (!string.IsNullOrEmpty(cloudRunUrl))
            {
                var config = new ServerConfig { cloudRunUrl = cloudRunUrl, supabaseUrl = supabaseUrl, supabaseAnonKey = anonKey };
                _client = new SupaRunClient(config, _transport, _auth);
            }

            // REST/Realtime + 토큰 갱신 배선 (Auth 있을 때만).
            if (_auth != null)
            {
                // 서버 의존 기능(DeleteAccount/CheckBan/플랫폼 인증)용 client를 late-bind (생성 순환 회피).
                _auth.AttachServerClient(_client);

                // 401 → SupaRunAuth.TryRefreshToken (single-flight). 갱신 후 클라이언트는 새 토큰을 pull.
                if (_client != null)
                    _client.OnTokenRefresh = async () => await _auth.TryRefreshToken();

                // [SpecData] PostgREST 클라이언트 — 동일 refresher 주입 + Auth(ISessionProvider)에서 토큰 pull.
                var restRefresher = new CallbackAuthRefresher(async () => await _auth.TryRefreshToken());
                _restClient = new SupabaseRestClient(supabaseUrl, anonKey, _transport, restRefresher, _auth);

                // Realtime: 옵션 우선, 없으면 기본. 소켓은 pull 불가라 세션 변경 시 push(OnAuthSessionChanged).
                _realtime = options.Realtime ?? new Supabase.SupabaseRealtime(supabaseUrl, anonKey);
                _auth.OnSessionChanged += OnAuthSessionChanged;
            }

            _localDB = LocalGameDB.Instance;
        }

        /// <summary>
        /// SupaRunProjectSettings.json (Editor) 또는 Resources/SupaRunConfig.json (Build) 에서
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
            // ProjectSettings/SupaRunProjectSettings.json (공유 데이터) 우선.
            // 마이그레이션 직전 상태에서는 레거시 UserSettings/SupaRunSettings.json fallback.
            // MPPM Virtual Player도 진짜 프로젝트 루트를 공유하도록 GetProjectRoot() 사용.
            var projectRoot = SupaRun.GetProjectRoot() ?? ".";
            var primaryPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(projectRoot, "ProjectSettings", "SupaRunProjectSettings.json"));
            var legacyPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(projectRoot, "UserSettings", "SupaRunSettings.json"));

            var settingsPath = System.IO.File.Exists(primaryPath) ? primaryPath
                : System.IO.File.Exists(legacyPath) ? legacyPath
                : null;

            if (settingsPath != null)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(settingsPath);
                    var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<EditorSettingsDto>(json);
                    if (settings != null)
                    {
                        // 에디터 플레이는 **editorEnvironment 가 가리키는 환경**에 붙는다.
                        // 이 한 줄이 "컴파일이 곧 라이브 반영" 을 끊는다 — 에디터가 dev 를 보면
                        // 스키마 자동 반영도, 플레이 중 읽는 데이터도 전부 dev 다.
                        var env = settings.environments?.Find(e => e.name == settings.editorEnvironment)
                                  ?? (settings.environments != null && settings.environments.Count > 0
                                        ? settings.environments[0] : null);

                        // 환경이 아직 없는 옛 설정 파일이면 평면 필드로 물러난다.
                        options.CloudRunUrl = env?.cloudRunUrl ?? settings.cloudRunUrl ?? "";
                        options.SupabaseUrl = env?.supabaseUrl ?? settings.supabaseUrl ?? "";
                        options.AnonKey = env?.supabaseAnonKey ?? settings.supabaseAnonKey ?? "";
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
                    $"[SupaRun] Settings 파일을 찾을 수 없습니다: {primaryPath}\n" +
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

#if UNITY_EDITOR
        /// <summary>
        /// 에디터 설정 파일에서 **읽는 것만** 담은 형태. 쓰기는 SupaRunSettings(Editor 어셈블리)가 한다.
        /// 여기서 Editor 어셈블리를 참조할 수 없으므로 필요한 모양만 따로 선언한다.
        /// </summary>
        [System.Serializable]
        class EditorSettingsDto
        {
            public List<EditorEnvDto> environments;
            public string editorEnvironment;

            // 환경 도입 전 형식 — 옛 설정 파일을 만나면 이쪽으로 물러난다.
            public string supabaseUrl;
            public string supabaseAnonKey;
            public string cloudRunUrl;
        }

        [System.Serializable]
        class EditorEnvDto
        {
            public string name;
            public string supabaseUrl;
            public string supabaseAnonKey;
            public string cloudRunUrl;
        }
#endif

        // ── 데이터 API ──

        /// <summary>단건 조회. [SpecData]→세션 캐시(#35)→Supabase REST, [UserData]→Cloud Run, 미배포→LocalGameDB.</summary>
        public async UniTask<ServerResponse<T>> Get<T>(object id)
        {
            if (_client != null)
            {
                if (SupaRun.IsConfig<T>() && _restClient != null)
                {
                    // 세션 고정을 위해 단건도 캐시(전체 목록)에서 찾는다 — [SpecData] 는 작다.
                    // id 필드 관례 밖 타입만 직접 조회로 남긴다(고정 대상에서 빠진다).
                    var idField = typeof(T).GetField("id");
                    if (idField == null)
                        return await _restClient.Get<T>(id);

                    var all = await GetAll<T>();
                    if (!all.success)
                        return new ServerResponse<T>
                        {
                            success = false, error = all.error, errorType = all.errorType,
                            statusCode = all.statusCode, isAuthenticated = all.isAuthenticated, hint = all.hint,
                        };

                    var key = id?.ToString();
                    var found = default(T);
                    foreach (var row in all.data ?? new List<T>())
                        if (Equals(idField.GetValue(row)?.ToString(), key)) { found = row; break; }

                    return new ServerResponse<T>
                    {
                        success = found != null,
                        data = found,
                        statusCode = found != null ? 200 : 404,
                        errorType = found != null ? ErrorType.None : ErrorType.NotFound,
                        error = found != null ? null : $"{typeof(T).Name} not found: {id}",
                        isAuthenticated = all.isAuthenticated,
                    };
                }

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

        /// <summary>전체 조회. [SpecData]→세션 캐시(#35)→Supabase REST, [UserData]→Cloud Run, 미배포→LocalGameDB.</summary>
        public async UniTask<ServerResponse<List<T>>> GetAll<T>()
        {
            if (_client != null)
            {
                if (SupaRun.IsConfig<T>() && _restClient != null)
                {
                    // 세션 캐시 (#35, Metaplay OTA 시맨틱) — 게시가 세션 중에 일어나도 이 세션의
                    // 조회는 안 바뀐다. 새 값은 새 세션(Login/RefreshConfigSessionAsync)부터다.
                    // **목록만** 사본이다 — 행 객체는 공유 참조라, 받은 행의 필드를 고치면
                    // 캐시가 같이 바뀐다. config 는 읽기 전용 데이터라는 관례가 전제다.
                    if (_configCache.TryGetValue(typeof(T), out var cached))
                        return new ServerResponse<List<T>>
                        {
                            success = true,
                            data = new List<T>((List<T>)cached),
                            statusCode = 200,
                            isAuthenticated = IsLoggedIn,
                        };

                    var r = await _restClient.GetAll<T>();
                    if (r.success && r.data != null) _configCache[typeof(T)] = new List<T>(r.data);
                    return r;
                }

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
        public async UniTask Login()
        {
            if (_auth == null)
            {
                UnityEngine.Debug.LogError(
                    "[SupaRun] Auth 미초기화 — SupaRunSettings.json의 supabaseUrl/supabaseAnonKey를 확인하세요.");
                return;
            }
            if (_auth.IsLoggedIn)
            {
                // 저장소에서 복원된 세션으로 이미 로그인돼 있어도 협상은 한 번은 있어야 한다 —
                // 안 그러면 ConfigSession 이 영영 null 이다.
                if (ConfigSession == null) await RefreshConfigSessionAsync();
                return;
            }
            await _auth.EnsureLoggedIn();        // SupaRunAuth 자체에서 동시 호출 dedup

            // 세션 협상 (#35) — 활성 config 버전 스탬프 + logic version 게이트.
            // 실패해도 로그인은 성립한다: 협상은 부가 정보이지 관문이 아니다.
            await RefreshConfigSessionAsync();
        }

        /// <summary>
        /// config 세션을 새로 연다 (#35) — 세션 캐시를 비우고 활성 버전을 다시 스탬프한다.
        /// Login() 이 자동으로 부르고, 프로세스 재시작 없이 새 세션을 원하면(매치 사이 등)
        /// 게임이 직접 부른다. 협상 실패는 스탬프 없음으로 계속 간다 — 오프라인·미게시
        /// 환경에서 조회가 막히면 안 된다.
        /// </summary>
        public async UniTask<ConfigSessionInfo> RefreshConfigSessionAsync()
        {
            _configCache.Clear();
            var info = new ConfigSessionInfo();

            if (_restClient != null)
            {
                try
                {
                    var r = await _restClient.GetMeta("active_config_version", "logic_version_range");
                    if (!r.success)
                        // fail-open 이다(스탬프 없음·게이트 통과) — 강제 업데이트 게이트가 필요한
                        // 게임은 이 경고와 ConfigSession.ActiveVersionHash==null 을 신호로 삼아야 한다.
                        UnityEngine.Debug.LogWarning($"[SupaRun] config 세션 협상 실패(HTTP) — 스탬프 없이 계속합니다: {r.error}");
                    if (r.success && r.data != null)
                    {
                        foreach (var row in r.data)
                        {
                            if (row.key == "active_config_version" && row.value != null)
                            {
                                info.ActiveVersionHash = (string?)row.value["content_hash"];
                                info.ActiveVersionGitSha = (string?)row.value["git_sha"];
                                info.ActivePublishedAt = (long?)row.value["published_at"] ?? 0;
                            }
                            else if (row.key == "logic_version_range" && row.value != null)
                            {
                                info.LogicMin = (int?)row.value["min"] ?? 0;
                                info.LogicMax = (int?)row.value["max"] ?? 0;
                            }
                        }

                        var lv = _options.LogicVersion;
                        if (lv > 0 && (info.LogicMin > 0 || info.LogicMax > 0))
                            info.LogicCompatible = (info.LogicMin <= 0 || lv >= info.LogicMin)
                                                && (info.LogicMax <= 0 || lv <= info.LogicMax);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SupaRun] config 세션 협상 실패 — 스탬프 없이 계속합니다: {ex.Message}");
                }
            }

            ConfigSession = info;
            return info;
        }

        /// <summary>
        /// 로그인 완료 대기. SG 프록시(ServerAPI.*)에서 서버 호출 전 방어용.
        /// 정상 흐름: 앱 시작 시 Login()을 먼저 호출 → 여기서는 IsLoggedIn 체크로 즉시 리턴.
        /// 비정상 흐름: Login() 미호출 상태로 서비스 호출이 들어오면 에러 로그 출력 후
        ///           안전망으로 자동 게스트 로그인 (silent failure 방지).
        /// </summary>
        public async UniTask WaitForAuth()
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

        /// <summary>SupaRunAuth.OnSessionChanged 핸들러 — Realtime 소켓에만 토큰 push.
        /// HTTP/REST 클라이언트는 ISessionProvider로 Auth에서 직접 pull하므로 push 대상이 아니다.</summary>
        internal void OnAuthSessionChanged(AuthSession session)
        {
            // 소켓은 열린 연결이라 pull 불가 — 세션 변경 시 명시적으로 토큰을 push한다.
            if (_realtime != null && session != null)
                _realtime.SetAccessToken(session.accessToken);
        }

        // ── IDisposable ──

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 이벤트 핸들러 해제 (생성자에서 등록한 것) + OAuthHandler deep-link/HttpListener 정리
            if (_auth != null)
            {
                _auth.OnSessionChanged -= OnAuthSessionChanged;
                _auth.Dispose();
            }

            // Realtime WebSocket 연결 해제
            _realtime?.Disconnect();

            // SupaRunClient/SupabaseRestClient/LocalGameDB는 IDisposable 아님 — 자원 해제 불필요.
        }
    }
}
