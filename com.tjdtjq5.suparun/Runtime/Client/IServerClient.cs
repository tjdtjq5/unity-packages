using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 게임 서버 HTTP 클라이언트 추상화. SupaRunAuth 등 SupaRun 내부 컴포넌트가
    /// 정적 진입점(`SupaRun.Client`)에 직접 의존하지 않도록 분리한 인터페이스.
    ///
    /// 구현체:
    /// - <see cref="SupaRunClient"/> : Cloud Run HTTP 호출 (자동 토큰 갱신/재시도 포함)
    ///
    /// P2-1에서 SupabaseRestClient/Auth Post와 통합 검토 예정.
    /// </summary>
    public interface IServerClient
    {
        /// <summary>GET 요청. 서버에서 T로 역직렬화된 데이터 반환.</summary>
        Task<ServerResponse<T>> GetAsync<T>(string endpoint);

        /// <summary>POST 요청 (제네릭). 서버에서 T로 역직렬화된 데이터 반환.</summary>
        Task<ServerResponse<T>> PostAsync<T>(string endpoint, object payload);

        /// <summary>POST 요청 (반환값 없음). 성공 여부와 에러만 반환.</summary>
        Task<ServerResponse> PostAsync(string endpoint, object payload);
    }
}
