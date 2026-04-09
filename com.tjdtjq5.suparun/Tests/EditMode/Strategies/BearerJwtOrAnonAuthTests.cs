#nullable enable
using System;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class BearerJwtOrAnonAuthTests
    {
        const string AnonKey = "test-anon-key";

        [Test]
        public void Session_Null_Uses_AnonKey_As_Bearer()
        {
            var auth = new BearerJwtOrAnonAuth(() => null, AnonKey);
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.AreEqual(AnonKey, req.Headers["apikey"]);
            Assert.AreEqual($"Bearer {AnonKey}", req.Headers["Authorization"]);
        }

        [Test]
        public void Session_Valid_Uses_Jwt_As_Bearer()
        {
            var session = new AuthSession
            {
                accessToken = "user-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };
            var auth = new BearerJwtOrAnonAuth(() => session, AnonKey);
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.AreEqual(AnonKey, req.Headers["apikey"]);
            Assert.AreEqual("Bearer user-jwt", req.Headers["Authorization"]);
        }

        [Test]
        public void Session_Expired_Still_Uses_Jwt()
        {
            // BearerJwtOrAnonAuth는 만료 체크 안 함 — accessToken이 있으면 그대로 사용.
            // 만료 체크는 BearerTokenAuth(SupaRunClient용) 의 책임.
            var session = new AuthSession
            {
                accessToken = "expired-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100
            };
            var auth = new BearerJwtOrAnonAuth(() => session, AnonKey);
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.AreEqual(AnonKey, req.Headers["apikey"]);
            Assert.AreEqual("Bearer expired-jwt", req.Headers["Authorization"]);
        }
    }
}
