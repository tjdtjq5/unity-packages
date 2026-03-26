using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 게임 서버 정적 API 진입점.
    /// 읽기: Get, Query, GetAll — 로직별 판단 (배포됨→서버, 미배포→LocalGameDB)
    /// 쓰기: Source Generator가 생성하는 타입 안전 프록시 (SupaRun.XXXService.Method())
    /// </summary>
    public static partial class SupaRun
    {
        static SupaRunClient _client;
        static SupabaseRestClient _restClient;
        static LocalGameDB _localDB;
        static SupabaseAuth _auth;
        static Supabase.SupabaseRealtime _realtime;
        static Task _authTask;
        static bool _initialized;

        // Config 타입 캐시
        static readonly System.Collections.Generic.Dictionary<Type, bool> _configCache = new();
        static bool IsConfig<T>()
        {
            var type = typeof(T);
            if (!_configCache.TryGetValue(type, out var result))
            {
                result = Attribute.GetCustomAttribute(type, typeof(ConfigAttribute)) != null;
                _configCache[type] = result;
            }
            return result;
        }

        /// <summary>초기화. 앱 시작 시 한 번 호출.</summary>
        public static void Initialize(ServerConfig config)
        {
            if (config.IsProduction)
                _client = new SupaRunClient(config);

            _localDB = LocalGameDB.Instance;
        }

        /// <summary>단건 조회. [Config]→Supabase REST 직접, [Table]→Cloud Run, 미배포→LocalGameDB.</summary>
        public static async Task<ServerResponse<T>> Get<T>(object id)
        {
            if (_client != null)
            {
                if (IsConfig<T>() && _restClient != null)
                    return await _restClient.Get<T>(id);

                var typeName = typeof(T).Name.ToLower();
                return await _client.GetAsync<T>($"api/{typeName}/{id}");
            }

            // LocalGameDB
            var data = await _localDB.Get<T>(id);
            return new ServerResponse<T>
            {
                success = data != null,
                data = data,
                statusCode = data != null ? 200 : 404,
                errorType = data != null ? ErrorType.None : ErrorType.NotFound,
                error = data != null ? null : $"{typeof(T).Name} not found: {id}"
            };
        }

        /// <summary>전체 조회. [Config]→Supabase REST 직접, [Table]→Cloud Run, 미배포→LocalGameDB.</summary>
        public static async Task<ServerResponse<List<T>>> GetAll<T>()
        {
            if (_client != null)
            {
                if (IsConfig<T>() && _restClient != null)
                    return await _restClient.GetAll<T>();

                var typeName = typeof(T).Name.ToLower();
                return await _client.GetAsync<List<T>>($"api/{typeName}");
            }

            var data = await _localDB.GetAll<T>();
            return new ServerResponse<List<T>>
            {
                success = true,
                data = data,
                statusCode = 200
            };
        }

        /// <summary>Auth. 자동 로그인 + 토큰 관리. 자동 초기화.</summary>
        public static SupabaseAuth Auth
        {
            get
            {
                if (!_initialized) AutoInitialize();
                return _auth;
            }
        }

        /// <summary>현재 인증 세션.</summary>
        public static AuthSession CurrentSession => _auth?.Session ?? _client?.Session;

        /// <summary>현재 로그인된 플레이어 ID. Feature API에 전달용.</summary>
        public static string PlayerId => CurrentSession?.userId;

        /// <summary>초기화 여부.</summary>
        public static bool IsInitialized => _auth != null || _client != null;

        /// <summary>Realtime. 채널 기반 실시간 통신 (Broadcast/Presence/PostgresChanges).</summary>
        public static Supabase.SupabaseRealtime Realtime
        {
            get
            {
                if (!_initialized) AutoInitialize();
                return _realtime;
            }
        }

        /// <summary>HTTP 클라이언트 (Source Generator 프록시에서 사용). 자동 초기화.</summary>
        public static SupaRunClient Client
        {
            get
            {
                if (!_initialized) AutoInitialize();
                return _client;
            }
        }

        /// <summary>LocalGameDB (Source Generator 프록시에서 사용).</summary>
        public static LocalGameDB LocalDB => _localDB ?? LocalGameDB.Instance;

        /// <summary>Auth 완료 대기. SG 프록시에서 서버 호출 전 사용.</summary>
        public static async Task WaitForAuth()
        {
            if (_authTask != null) await _authTask;
        }

        /// <summary>Settings에서 읽어 자동 초기화.</summary>
        static void AutoInitialize()
        {
            _initialized = true;
            var url = "";
            var supabaseUrl = "";
            var anonKey = "";

            #if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject SupaRunSettings");
            if (guids.Length > 0)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(path);
                if (settings != null)
                {
                    var t = settings.GetType();
                    url = t.GetField("cloudRunUrl")?.GetValue(settings) as string ?? "";
                    supabaseUrl = t.GetField("supabaseUrl")?.GetValue(settings) as string ?? "";
                }
                anonKey = UnityEditor.EditorPrefs.GetString("SupaRun_SupabaseAnonKey", "");
            }
            #else
            // 빌드: Resources/SupaRunConfig.json에서 읽기
            var configAsset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("SupaRunConfig");
            if (configAsset != null)
            {
                var runtimeConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<SupaRunRuntimeConfig>(configAsset.text);
                if (runtimeConfig != null)
                {
                    url = runtimeConfig.cloudRunUrl ?? "";
                    supabaseUrl = runtimeConfig.supabaseUrl ?? "";
                    anonKey = runtimeConfig.supabaseAnonKey ?? "";
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("[SupaRun] SupaRunConfig.json을 찾을 수 없습니다. 빌드가 정상적으로 되었는지 확인하세요.");
            }
            #endif

            if (!string.IsNullOrEmpty(url))
            {
                var config = new ServerConfig { cloudRunUrl = url, supabaseUrl = supabaseUrl, supabaseAnonKey = anonKey };
                _client = new SupaRunClient(config);
            }

            // [Config] 직접 조회용 REST 클라이언트
            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(anonKey))
                _restClient = new SupabaseRestClient(supabaseUrl, anonKey);

            // Auth + Realtime 초기화
            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(anonKey))
            {
                _auth = new SupabaseAuth(supabaseUrl, anonKey, url);
                _auth.OnSessionChanged += session =>
                {
                    if (_client != null) _client.Session = session;
                    // Realtime에 액세스 토큰 전달
                    if (_realtime != null && session != null)
                        _realtime.SetAccessToken(session.accessToken);
                };
                // 토큰 갱신 콜백 연결
                if (_client != null)
                    _client.OnTokenRefresh = async () => await _auth.TryRefreshToken();
                _authTask = _auth.EnsureLoggedIn(); // fire-and-forget 시작

                // Realtime 초기화 (연결은 첫 채널 Subscribe 시)
                _realtime = new Supabase.SupabaseRealtime(supabaseUrl, anonKey);
            }

            _localDB = LocalGameDB.Instance;
        }
    }
}
