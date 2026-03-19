namespace Tjdtjq5.GameServer.Supabase
{
    /// <summary>Supabase Realtime. 채널 구독 (읽기만).</summary>
    public class SupabaseRealtime
    {
        readonly SupabaseClient _client;

        internal SupabaseRealtime(SupabaseClient client) => _client = client;

        // TODO: Phase 3에서 구현
        // - Subscribe(channel, callback)
        // - Unsubscribe(channel)
    }
}
