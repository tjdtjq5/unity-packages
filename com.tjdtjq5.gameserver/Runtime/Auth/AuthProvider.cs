namespace Tjdtjq5.GameServer
{
    public enum AuthProvider
    {
        Guest,
        // Supabase OAuth
        Google,
        Apple,
        Facebook,
        Discord,
        Twitter,
        Kakao,
        Twitch,
        Spotify,
        Slack,
        GitHub,
        // Custom Token (네이티브 SDK)
        GPGS,         // Google Play Games
        GameCenter    // Apple Game Center
    }
}
