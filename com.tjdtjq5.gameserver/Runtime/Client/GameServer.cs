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

        /// <summary>전체 조회 (SpecData용).</summary>
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

        /// <summary>현재 인증 세션.</summary>
        public static AuthSession CurrentSession => _client?.Session;

        /// <summary>초기화 여부. Initialize 호출 안 해도 LocalGameDB는 자동 사용.</summary>
        public static bool IsInitialized => true;

        /// <summary>HTTP 클라이언트 (Source Generator 프록시에서 사용).</summary>
        public static GameServerClient Client => _client;

        /// <summary>LocalGameDB (Source Generator 프록시에서 사용).</summary>
        public static LocalGameDB LocalDB => _localDB ?? LocalGameDB.Instance;
    }
}
