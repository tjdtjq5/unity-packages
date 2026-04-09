#nullable enable
namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// SupaRunRuntime 생성 시 사용하는 옵션 객체.
    /// 모든 의존성을 명시적으로 주입하거나 null로 두면 기본 구현체가 사용된다.
    ///
    /// 사용 예:
    /// <code>
    /// // 자동 settings 로드 (기본)
    /// var runtime = SupaRunRuntime.CreateFromSettings();
    ///
    /// // 명시적 옵션 (테스트/DI)
    /// var options = new SupaRunRuntimeOptions
    /// {
    ///     SupabaseUrl = "https://xxx.supabase.co",
    ///     AnonKey = "eyJ...",
    ///     Transport = new MockHttpTransport(),
    ///     SessionStorage = new MemorySessionStorage(),
    /// };
    /// var runtime = new SupaRunRuntime(options);
    /// </code>
    /// </summary>
    public class SupaRunRuntimeOptions
    {
        /// <summary>Supabase 프로젝트 URL (예: "https://xxx.supabase.co"). 비어있으면 Auth/REST 클라이언트 미생성.</summary>
        public string? SupabaseUrl;

        /// <summary>Supabase Anon Key. 비어있으면 Auth/REST 클라이언트 미생성.</summary>
        public string? AnonKey;

        /// <summary>Cloud Run base URL. null/빈값이면 Cloud Run 호출(SupaRunClient) 비활성.</summary>
        public string? CloudRunUrl;

        /// <summary>
        /// HTTP transport 구현체. null이면 <see cref="UnityHttpTransport"/> 기본 사용.
        /// 단위 테스트 시 mock transport 주입에 사용.
        /// </summary>
        public IHttpTransport? Transport;

        /// <summary>
        /// 세션 저장소 구현체. null이면 <see cref="SecureSessionStorage"/> + MPPM 자동 prefix 기본 사용.
        /// 단위 테스트 시 <see cref="MemorySessionStorage"/> 주입에 사용.
        /// </summary>
        public ISessionStorage? SessionStorage;

        /// <summary>
        /// Supabase Auth HTTP API 구현체. null이면 <see cref="SupabaseAuthApi"/> 기본 사용.
        /// 단위 테스트 시 mock 주입으로 로그인 흐름 검증에 사용.
        /// </summary>
        public IAuthApi? AuthApi;

        /// <summary>
        /// Realtime 클라이언트 구현체. null이면 <see cref="Supabase.SupabaseRealtime"/> 기본 사용.
        /// 단위 테스트 시 mock 주입으로 토큰 전파 검증에 사용.
        /// </summary>
        public Supabase.IRealtimeClient? Realtime;
    }
}
