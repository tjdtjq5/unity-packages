#nullable enable
using System;

namespace Tjdtjq5.SupaRun
{
    [Serializable]
    public class AuthSession
    {
        public string? accessToken;
        public string? refreshToken;
        public string? userId;
        public long expiresAt;
        public bool isGuest;

        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt;
    }
}
