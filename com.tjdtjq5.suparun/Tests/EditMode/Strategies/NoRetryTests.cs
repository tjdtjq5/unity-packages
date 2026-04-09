#nullable enable
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class NoRetryTests
    {
        [Test]
        public void Always_Returns_Negative_One()
        {
            var retry = new NoRetry();
            var resp = new HttpTransportResponse { StatusCode = 500, Success = false };
            Assert.AreEqual(-1, retry.GetRetryDelay(resp, 0));
            Assert.AreEqual(-1, retry.GetRetryDelay(resp, 5));
        }
    }
}
