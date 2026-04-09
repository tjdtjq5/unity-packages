#nullable enable
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class ExponentialBackoffRetryTests
    {
        ExponentialBackoffRetry _retry = null!;

        [SetUp]
        public void SetUp()
        {
            _retry = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 1000);
        }

        [Test]
        public void Success_200_No_Retry()
        {
            var resp = new HttpTransportResponse { StatusCode = 200, Success = true };
            Assert.AreEqual(-1, _retry.GetRetryDelay(resp, 0));
        }

        [Test]
        public void Client_Error_400_No_Retry()
        {
            var resp = new HttpTransportResponse { StatusCode = 400, Success = false };
            Assert.AreEqual(-1, _retry.GetRetryDelay(resp, 0));
        }

        [Test]
        public void Server_Error_500_Attempt0_Returns_BaseDelay()
        {
            var resp = new HttpTransportResponse { StatusCode = 500, Success = false };
            Assert.AreEqual(1000, _retry.GetRetryDelay(resp, 0));
        }

        [Test]
        public void Server_Error_500_Attempt1_Returns_Double()
        {
            var resp = new HttpTransportResponse { StatusCode = 500, Success = false };
            Assert.AreEqual(2000, _retry.GetRetryDelay(resp, 1));
        }

        [Test]
        public void Server_Error_500_Attempt2_Returns_Quadruple()
        {
            var resp = new HttpTransportResponse { StatusCode = 500, Success = false };
            Assert.AreEqual(4000, _retry.GetRetryDelay(resp, 2));
        }

        [Test]
        public void Server_Error_Max_Attempts_Exceeded()
        {
            var resp = new HttpTransportResponse { StatusCode = 500, Success = false };
            Assert.AreEqual(-1, _retry.GetRetryDelay(resp, 3));
        }

        [Test]
        public void Connection_Error_Retries()
        {
            var resp = new HttpTransportResponse
            {
                StatusCode = 0, Success = false, IsConnectionError = true
            };
            Assert.AreEqual(1000, _retry.GetRetryDelay(resp, 0));
        }
    }
}
