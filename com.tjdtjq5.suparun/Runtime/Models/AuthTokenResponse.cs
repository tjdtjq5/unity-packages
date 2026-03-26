using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>서버에서 플랫폼 인증 후 반환하는 JWT 응답.</summary>
    [Serializable]
    public class AuthTokenResponse
    {
        public string accessToken;
        public string refreshToken;
        public string userId;
    }
}
