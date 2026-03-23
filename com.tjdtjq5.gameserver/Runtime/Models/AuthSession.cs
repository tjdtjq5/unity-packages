using System;

namespace Tjdtjq5.GameServer
{
    [Serializable]
    public class AuthSession
    {
        public string accessToken;
        public string refreshToken;
        public string userId;
        public long expiresAt;
        public bool isGuest;

        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt;
    }
}
