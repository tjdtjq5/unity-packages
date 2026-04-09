#nullable enable
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// Supabase Auth HTTP API 추상화.
    /// SupaRunAuth가 직접 UnityWebRequest를 사용하지 않고 이 인터페이스를 통해 HTTP 송신.
    /// 테스트 시 MockHttpTransport 주입으로 로그인 흐름 전체를 검증 가능.
    /// </summary>
    public interface IAuthApi
    {
        /// <summary>Supabase Auth 엔드포인트에 JSON POST. 성공 시 응답 텍스트, 실패 시 null.</summary>
        Task<string?> PostAsync(string endpoint, string jsonBody);
    }
}
