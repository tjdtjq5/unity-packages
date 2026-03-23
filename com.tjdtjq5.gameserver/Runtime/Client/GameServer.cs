using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.GameServer
{
    /// <summary>
    /// 게임 서버 정적 API 진입점.
    /// 읽기: Get, Query, GetAll — 로직별 판단 (배포됨→서버, 미배포→LocalGameDB)
    /// 쓰기: Source Generator가 생성하는 타입 안전 프록시 (GameServer.XXXService.Method())
    /// </summary>
    public static partial class GameServer
    {
        static GameServerClient _client;
        static LocalGameDB _localDB;
        static SupabaseAuth _auth;
        static Task _authTask;
        static bool _initialized;

        /// <summary>초기화. 앱 시작 시 한 번 호출.</summary>
        public static void Initialize(ServerConfig config)
        {
            if (config.IsProduction)
                _client = new GameServerClient(config);

            _localDB = LocalGameDB.Instance;
        }

        /// <summary>단건 조회. 서버 URL 설정 시 서버, 아니면 LocalGameDB.</summary>
        public static async Task<ServerResponse<T>> Get<T>(object id)
        {
            if (_client != null)
            {
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

        /// <summary>전체 조회 (Config용).</summary>
        public static async Task<ServerResponse<List<T>>> GetAll<T>()
        {
            if (_client != null)
            {
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

        /// <summary>HTTP 클라이언트 (Source Generator 프록시에서 사용). 자동 초기화.</summary>
        public static GameServerClient Client
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
            var guids = UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject GameServerSettings");
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
                anonKey = UnityEditor.EditorPrefs.GetString("GameServer_SupabaseAnonKey", "");
            }
            #else
            // 빌드: Resources/GameServerConfig.json에서 읽기
            var configAsset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("GameServerConfig");
            if (configAsset != null)
            {
                var runtimeConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerRuntimeConfig>(configAsset.text);
                if (runtimeConfig != null)
                {
                    url = runtimeConfig.cloudRunUrl ?? "";
                    supabaseUrl = runtimeConfig.supabaseUrl ?? "";
                    anonKey = runtimeConfig.supabaseAnonKey ?? "";
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("[GameServer] GameServerConfig.json을 찾을 수 없습니다. 빌드가 정상적으로 되었는지 확인하세요.");
            }
            #endif

            if (!string.IsNullOrEmpty(url))
            {
                var config = new ServerConfig { cloudRunUrl = url, supabaseUrl = supabaseUrl, supabaseAnonKey = anonKey };
                _client = new GameServerClient(config);
            }

            // Auth 초기화 + 자동 로그인 시작
            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(anonKey))
            {
                _auth = new SupabaseAuth(supabaseUrl, anonKey, url);
                _auth.OnSessionChanged += session =>
                {
                    if (_client != null) _client.Session = session;
                };
                _authTask = _auth.EnsureLoggedIn(); // fire-and-forget 시작
            }

            _localDB = LocalGameDB.Instance;
        }
    }
}
