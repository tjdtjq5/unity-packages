#nullable enable
using System;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class ApiKeyAuthTests
    {
        [Test]
        public void Apply_Sets_ApiKey_Header()
        {
            var auth = new ApiKeyAuth("test-key");
            var req = new HttpTransportRequest();
            auth.Apply(req);

            Assert.AreEqual("test-key", req.Headers["apikey"]);
        }

        [Test]
        public void Null_ApiKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ApiKeyAuth(null!));
        }
    }
}
