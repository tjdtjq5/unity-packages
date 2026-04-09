#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class HttpExecutorTests
    {
        MockHttpTransport _transport = null!;

        [SetUp]
        public void SetUp()
        {
            _transport = new MockHttpTransport();
        }

        HttpTransportRequest MakeRequest() => new HttpTransportRequest
        {
            Url = "https://example.com/test",
            Method = "GET",
        };

        // ── Happy path ──

        [Test]
        public async Task Success_200_Returns_Response()
        {
            _transport.Enqueue(200, "{}", success: true);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), new NoRetry());

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(200, resp.StatusCode);
            Assert.IsTrue(resp.Success);
            Assert.AreEqual(1, _transport.SendCount);
        }

        // ── 401 + Auth Refresh ──

        [Test]
        public async Task Auth_401_Refresh_Success_Retries()
        {
            // 첫 번째: 401, 두 번째: 200
            _transport.Enqueue(401, "", success: false);
            _transport.Enqueue(200, "{\"ok\":true}", success: true);
            var refresher = new FakeRefresher(true);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), new NoRetry(), refresher);

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(200, resp.StatusCode);
            Assert.AreEqual(2, _transport.SendCount);
            Assert.AreEqual(1, refresher.CallCount);
        }

        [Test]
        public async Task Auth_401_Refresh_Fail_Returns_401()
        {
            _transport.Enqueue(401, "", success: false);
            var refresher = new FakeRefresher(false);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), new NoRetry(), refresher);

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(401, resp.StatusCode);
            Assert.AreEqual(1, _transport.SendCount);
            Assert.AreEqual(1, refresher.CallCount);
        }

        [Test]
        public async Task Auth_401_No_Refresher_Returns_401()
        {
            _transport.Enqueue(401, "", success: false);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), new NoRetry());

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(401, resp.StatusCode);
            Assert.AreEqual(1, _transport.SendCount);
        }

        [Test]
        public async Task Auth_401_Double_No_Second_Refresh()
        {
            // 401 → refresh 성공 → 재시도 → 또 401 → refresh 안 함
            _transport.Enqueue(401, "", success: false);
            _transport.Enqueue(401, "", success: false);
            var refresher = new FakeRefresher(true);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), new NoRetry(), refresher);

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(401, resp.StatusCode);
            Assert.AreEqual(2, _transport.SendCount);
            Assert.AreEqual(1, refresher.CallCount); // 1회만
        }

        // ── 5xx Retry ──

        [Test]
        public async Task Server_500_Retry_Succeeds()
        {
            _transport.Enqueue(500, "", success: false);
            _transport.Enqueue(200, "{}", success: true);
            var retry = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 0);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), retry);

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(200, resp.StatusCode);
            Assert.AreEqual(2, _transport.SendCount);
        }

        [Test]
        public async Task Server_500_Max_Retries_Returns_Last_Error()
        {
            // maxAttempts=3 → attempt 0,1,2에서 retry → attempt 3에서 GetRetryDelay=-1 → 반환
            // 총 4회 송신 (attempt 0,1,2,3)
            _transport.Enqueue(500, "", success: false);
            _transport.Enqueue(500, "", success: false);
            _transport.Enqueue(500, "", success: false);
            _transport.Enqueue(500, "", success: false);
            var retry = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 0);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), retry);

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(500, resp.StatusCode);
            Assert.AreEqual(4, _transport.SendCount);
        }

        [Test]
        public async Task Connection_Error_Retries()
        {
            _transport.Enqueue(0, "", success: false, isConnectionError: true);
            _transport.Enqueue(200, "{}", success: true);
            var retry = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 0);
            var executor = new HttpExecutor(_transport, new NoAuthStrategy(), retry);

            var resp = await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(200, resp.StatusCode);
            Assert.AreEqual(2, _transport.SendCount);
        }

        // ── Auth 적용 검증 ──

        [Test]
        public async Task Auth_Applied_On_Each_Attempt()
        {
            _transport.Enqueue(500, "", success: false);
            _transport.Enqueue(200, "{}", success: true);
            var authCounter = new CountingAuthStrategy();
            var retry = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 0);
            var executor = new HttpExecutor(_transport, authCounter, retry);

            await executor.ExecuteAsync(MakeRequest());

            Assert.AreEqual(2, authCounter.ApplyCount); // 매 시도마다 Apply
        }

        // ── 테스트 헬퍼 ──

        /// <summary>인증 헤더를 추가하지 않는 stub.</summary>
        class NoAuthStrategy : IAuthStrategy
        {
            public void Apply(HttpTransportRequest request) { }
        }

        /// <summary>Apply 호출 횟수를 기록하는 stub.</summary>
        class CountingAuthStrategy : IAuthStrategy
        {
            public int ApplyCount;
            public void Apply(HttpTransportRequest request) => ApplyCount++;
        }

        /// <summary>결과를 고정 반환하는 fake refresher.</summary>
        class FakeRefresher : IAuthRefresher
        {
            readonly bool _result;
            public int CallCount;
            public FakeRefresher(bool result) => _result = result;
            public Task<bool> TryRefreshAsync() { CallCount++; return Task.FromResult(_result); }
        }
    }
}
