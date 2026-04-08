using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 401 Unauthorized 응답 시 토큰 갱신 콜백.
    /// HttpExecutor가 401을 받으면 1회만 TryRefreshAsync를 호출하고,
    /// 성공 시 IAuthStrategy를 다시 적용해 재시도한다.
    ///
    /// 옵셔널 — null이면 401 처리 없이 그대로 응답 반환.
    ///
    /// 구현체:
    /// - <see cref="CallbackAuthRefresher"/> : Func 델리게이트 wrapping (SupaRunAuth.TryRefreshToken 등에 연결)
    /// </summary>
    public interface IAuthRefresher
    {
        /// <summary>
        /// 토큰 갱신 시도. true 반환 시 HttpExecutor가 1회 재시도.
        /// false 반환 시 401 응답을 그대로 반환.
        /// </summary>
        Task<bool> TryRefreshAsync();
    }
}
