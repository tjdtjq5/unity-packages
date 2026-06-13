#nullable enable
namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 인증 세션의 단일 읽기 출처(토큰의 home). HTTP 클라이언트가 요청마다 여기서 pull한다.
    /// <see cref="SupaRunAuth"/>가 구현한다.
    ///
    /// push 미러(각 client가 들고 있던 Session 복사본) 대신 pull로 전환해
    /// 토큰 staleness(갱신 후 클라이언트가 옛 토큰을 들고 있는 문제)를 구조적으로 제거한다.
    /// Realtime 소켓만은 pull 불가라 별도로 push(SetAccessToken)된다.
    /// </summary>
    public interface ISessionProvider
    {
        /// <summary>현재 인증 세션. 미로그인 시 null.</summary>
        AuthSession? CurrentSession { get; }
    }
}
