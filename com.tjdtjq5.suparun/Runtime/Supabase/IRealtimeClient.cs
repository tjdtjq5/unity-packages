#nullable enable
namespace Tjdtjq5.SupaRun.Supabase
{
    /// <summary>
    /// Realtime WebSocket 클라이언트 추상화.
    /// SupaRunRuntime이 구체 SupabaseRealtime 대신 이 인터페이스를 보유하여 테스트 시 mock 주입 가능.
    /// </summary>
    public interface IRealtimeClient
    {
        bool IsConnected { get; }
        void SetAccessToken(string token);
        RealtimeChannel Channel(string name);
        void Disconnect();
    }
}
