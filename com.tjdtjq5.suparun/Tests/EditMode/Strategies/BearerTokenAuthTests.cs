#nullable enable
using System;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class BearerTokenAuthTests
    {
        [Test]
        public void Session_Null_No_Header()
        {
            var auth = new BearerTokenAuth(() => null);
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.IsFalse(req.Headers.ContainsKey("Authorization"));
        }

        [Test]
        public void Session_Valid_Adds_Bearer()
        {
            var session = new AuthSession
            {
                accessToken = "test-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };
            var auth = new BearerTokenAuth(() => session);
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.AreEqual("Bearer test-jwt", req.Headers["Authorization"]);
        }

        [Test]
        public void Session_Expired_No_Header()
        {
            var session = new AuthSession
            {
                accessToken = "expired-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100
            };
            var auth = new BearerTokenAuth(() => session);
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.IsFalse(req.Headers.ContainsKey("Authorization"));
        }
    }
}
