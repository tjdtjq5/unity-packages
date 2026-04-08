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
    /// <remarks>
    /// P2-3 리팩터: 정적 클래스가 <see cref="SupaRunRuntime"/>의 lazy singleton facade로 변경됨.
    /// 모든 instance state는 SupaRunRuntime이 보유. SupaRun 정적 API는 호환성을 위한 얇은 위임 wrapper.
    /// 단위 테스트/DI 시 SupaRunRuntime을 직접 생성하면 정적 facade를 우회 가능.
    /// </remarks>
    public static partial class SupaRun
    {
        // ── Singleton instance (lazy + double-check locking) ──
        static SupaRunRuntime _instance;
        static readonly object _initLock = new object();

        /// <summary>
        /// SupaRunRuntime singleton 인스턴스. 첫 호출 시 SupaRunSettings.json 자동 로드 후 생성.
        /// 단위 테스트에서는 SupaRunRuntime을 직접 생성해 사용하면 이 facade를 우회할 수 있다.
        /// </summary>
        public static SupaRunRuntime Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_initLock)
                {
                    if (_instance == null)
                        _instance = SupaRunRuntime.CreateFromSettings();
                    return _instance;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unity Editor 도메인 리로드(스크립트 컴파일) 직전에 SupaRunRuntime 인스턴스를 정리.
        /// Realtime WebSocket 연결 같은 자원 누수 방지.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterDomainReloadCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                lock (_initLock)
                {
                    _instance?.Dispose();
                    _instance = null;
                }
            };
        }

        /// <summary>
        /// [테스트용] 인스턴스 강제 교체. 기존 인스턴스가 있으면 Dispose 후 교체.
        /// instance가 null이면 현재 인스턴스 정리만 수행 (다음 Instance 호출 시 새로 생성).
        /// 정상 흐름에서는 사용하지 말 것 — Instance lazy 프로퍼티가 자동 생성.
        /// </summary>
        internal static void SetInstance(SupaRunRuntime instance)
        {
            lock (_initLock)
            {
                _instance?.Dispose();
                _instance = instance;
            }
        }
#endif

        /// <summary>
        /// 디버그 로그 verbose 모드. true면 SupaRun 내부의 자세한 진단 로그(HTTP POST 본문, LocalDB 작업 등)가 출력됨.
        /// 기본 false. 디버깅 시에만 명시적으로 켜서 사용 (예: GameInitializer 진입점에서 `SupaRun.Verbose = true`).
        /// LogWarning/LogError는 이 플래그와 무관하게 항상 출력된다.
        /// </summary>
        public static bool Verbose { get; set; }

        /// <summary>Verbose 모드일 때만 Debug.Log로 출력. 패키지 내부 진단용 헬퍼.</summary>
        internal static void LogVerbose(string message)
        {
            if (Verbose) UnityEngine.Debug.Log(message);
        }

        // ── MPPM(Multiplayer Play Mode) 감지 헬퍼 ──
        // Main Editor: Application.dataPath = <projectRoot>/Assets
        // Virtual Player: Application.dataPath = <projectRoot>/Library/VP/<vp-id>/Assets
        // → '/Library/VP/' 패턴을 감지해 진짜 프로젝트 루트와 VP 인스턴스 ID를 도출.

        /// <summary>현재 Editor가 MPPM Virtual Player라면 VP 인스턴스 ID(예: "mppm40870be5"), 아니면 빈 문자열.</summary>
        internal static string GetMppmInstanceId()
        {
#if UNITY_EDITOR
            var dataPath = UnityEngine.Application.dataPath?.Replace('\\', '/');
            var vpIdx = dataPath?.IndexOf("/Library/VP/", System.StringComparison.OrdinalIgnoreCase) ?? -1;
            if (vpIdx >= 0)
            {
                var afterVP = dataPath.Substring(vpIdx + "/Library/VP/".Length);
                var slashIdx = afterVP.IndexOf('/');
                return slashIdx >= 0 ? afterVP.Substring(0, slashIdx) : afterVP;
            }
#endif
            return "";
        }

        /// <summary>
        /// 진짜 프로젝트 루트 절대 경로 (Application.dataPath의 부모, MPPM VP 가상 루트는 거슬러 올라감).
        /// UserSettings/ 같은 공유 디렉토리 접근 시 사용.
        /// </summary>
        internal static string GetProjectRoot()
        {
            var projectRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath)?.Replace('\\', '/');
            var vpIdx = projectRoot?.IndexOf("/Library/VP/", System.StringComparison.OrdinalIgnoreCase) ?? -1;
            if (vpIdx > 0)
                projectRoot = projectRoot.Substring(0, vpIdx);
            return projectRoot;
        }

        // Config 타입 캐시 — SupaRunRuntime이 internal 호출 (P2-3b)
        static readonly System.Collections.Generic.Dictionary<Type, bool> _configCache = new();
        internal static bool IsConfig<T>()
        {
            var type = typeof(T);
            if (!_configCache.TryGetValue(type, out var result))
            {
                result = Attribute.GetCustomAttribute(type, typeof(ConfigAttribute)) != null;
                _configCache[type] = result;
            }
            return result;
        }

        // LocalDB fallback 진단 — 첫 호출 1회만 경고
        static bool _localDbFallbackWarned;
        internal static void WarnLocalDbFallbackOnce(string source)
        {
            if (_localDbFallbackWarned) return;
            _localDbFallbackWarned = true;
            UnityEngine.Debug.LogWarning(
                $"[SupaRun] {source}: 서버 클라이언트 미초기화 — LocalGameDB로 fallback 합니다. " +
                "원인: SupaRunSettings.json 로드 실패 또는 supabaseUrl/anonKey 미설정. " +
                "프로덕션에서는 모든 GetAll/Get 결과가 빈 리스트가 됩니다. " +
                "이 경고는 1회만 표시됩니다.");
        }

        /// <summary>
        /// [Deprecated] 사용처 0. 호환성을 위해 시그니처만 유지하되 noop.
        /// 사용 금지 — `SupaRun.Login()`을 호출하면 자동으로 SupaRunRuntime이 생성된다.
        /// </summary>
        [System.Obsolete("Use SupaRun.Login() instead. Initialization is automatic via SupaRunRuntime.", false)]
        public static void Initialize(ServerConfig config)
        {
            UnityEngine.Debug.LogWarning(
                "[SupaRun] Initialize(ServerConfig)는 더 이상 동작하지 않습니다 (P2-3 리팩터). " +
                "SupaRun.Login()을 호출하면 자동으로 SupaRunRuntime이 생성됩니다.");
        }

        // ── 데이터 API (Instance 위임) ──

        /// <summary>단건 조회. [Config]→Supabase REST 직접, [Table]→Cloud Run, 미배포→LocalGameDB.</summary>
        public static Task<ServerResponse<T>> Get<T>(object id) => Instance.Get<T>(id);

        /// <summary>전체 조회. [Config]→Supabase REST 직접, [Table]→Cloud Run, 미배포→LocalGameDB.</summary>
        public static Task<ServerResponse<List<T>>> GetAll<T>() => Instance.GetAll<T>();

        // ── Auth 진입점 (Instance 위임) ──

        /// <summary>Auth. 토큰 관리. 자동 초기화.</summary>
        public static SupaRunAuth Auth => Instance.Auth;

        /// <summary>현재 인증 세션.</summary>
        public static AuthSession CurrentSession => Instance.CurrentSession;

        /// <summary>현재 로그인된 플레이어 ID. Feature API에 전달용.</summary>
        public static string PlayerId => Instance.PlayerId;

        /// <summary>초기화 여부. P2-3 후에는 "Instance가 한 번이라도 생성되었는가" 의미.</summary>
        public static bool IsInitialized => _instance != null;

        /// <summary>현재 로그인되어 있는지 여부.</summary>
        public static bool IsLoggedIn => Instance.IsLoggedIn;

        /// <summary>
        /// 명시적 로그인. 앱 시작 시 한 번 호출. 중복 호출 안전.
        /// SignOut/DeleteAccount 후 재호출도 안전 (IsLoggedIn 체크로 진실 소스는 SupaRunAuth).
        /// 이 호출 이전에는 데이터 API(GetAll/Get/서비스 프록시)를 쓰면 안 됨.
        /// </summary>
        public static Task Login() => Instance.Login();

        /// <summary>Realtime. 채널 기반 실시간 통신 (Broadcast/Presence/PostgresChanges).</summary>
        public static Supabase.SupabaseRealtime Realtime => Instance.Realtime;

        /// <summary>HTTP 클라이언트 (Source Generator 프록시에서 사용). 자동 초기화.</summary>
        public static SupaRunClient Client => Instance._client;

        /// <summary>LocalGameDB (Source Generator 프록시에서 사용).</summary>
        public static LocalGameDB LocalDB => Instance.LocalDB;

        /// <summary>
        /// 로그인 완료 대기. SG 프록시(ServerAPI.*)에서 서버 호출 전 방어용.
        /// 정상 흐름: 앱 시작 시 SupaRun.Login()을 먼저 호출 → 여기서는 IsLoggedIn 체크로 즉시 리턴.
        /// 비정상 흐름: Login() 미호출 상태로 서비스 호출이 들어오면 에러 로그 출력 후
        ///           안전망으로 자동 게스트 로그인 (silent failure 방지).
        /// </summary>
        public static Task WaitForAuth() => Instance.WaitForAuth();
    }
}
