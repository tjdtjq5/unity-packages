using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.GameServer
{
    /// <summary>
    /// 게임 서버 정적 API 진입점.
    /// 읽기: Get, Query, GetAll (자동 생성)
    /// 쓰기: Source Generator가 생성하는 타입 안전 프록시 (GameServer.XXXService.Method())
    /// </summary>
    public static partial class GameServer
    {
        static GameServerClient _client;

        /// <summary>초기화. 앱 시작 시 한 번 호출.</summary>
        public static void Initialize(ServerConfig config)
        {
            _client = new GameServerClient(config);
        }

        /// <summary>단건 조회.</summary>
        public static Task<ServerResponse<T>> Get<T>(object id)
        {
            var typeName = typeof(T).Name.ToLower();
            return _client.GetAsync<T>($"api/{typeName}/{id}");
        }

        /// <summary>전체 조회 (SpecData용).</summary>
        public static Task<ServerResponse<List<T>>> GetAll<T>()
        {
            var typeName = typeof(T).Name.ToLower();
            return _client.GetAsync<List<T>>($"api/{typeName}");
        }

        /// <summary>현재 인증 세션.</summary>
        public static AuthSession CurrentSession => _client?.Session;

        /// <summary>초기화 여부.</summary>
        public static bool IsInitialized => _client != null;

        /// <summary>내부용: HTTP 클라이언트 접근 (Source Generator 프록시에서 사용).</summary>
        internal static GameServerClient Client => _client;
    }
}
