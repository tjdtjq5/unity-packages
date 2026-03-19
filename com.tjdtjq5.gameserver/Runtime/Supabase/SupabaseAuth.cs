namespace Tjdtjq5.GameServer.Supabase
{
    /// <summary>Supabase Auth. 게스트 + OAuth 인증.</summary>
    public class SupabaseAuth
    {
        readonly SupabaseClient _client;

        internal SupabaseAuth(SupabaseClient client) => _client = client;

        // TODO: Phase 1에서 구현
        // - SignInAnonymously()
        // - RefreshToken()
        // - GetSession()
        // Phase 3:
        // - SignInWithOAuth(provider)
        // - LinkIdentity(provider)
        // - SignOut()
    }
}
