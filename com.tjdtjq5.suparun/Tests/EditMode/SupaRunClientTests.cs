#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class SupaRunClientTests
    {
        const string CloudRunUrl = "https://api.example.com";
        MockHttpTransport _transport = null!;
        StubSessionProvider _session = null!;

        [SetUp]
        public void SetUp()
        {
            _transport = new MockHttpTransport();
            _session = new StubSessionProvider();
        }

        SupaRunClient MakeClient() => new SupaRunClient(
            new ServerConfig { cloudRunUrl = CloudRunUrl }, _transport, _session);

        // ── GetAsync ──

        [Test]
        public async Task GetAsync_Success_Deserializes()
        {
            _transport.Enqueue(200, "{\"value\":42}", success: true);
            var client = MakeClient();

            var resp = await client.GetAsync<TestPayload>("api/test");

            Assert.IsTrue(resp.success);
            Assert.AreEqual(42, resp.data!.value);
        }

        // ── PostAsync<T> ──

        [Test]
        public async Task PostAsync_Generic_Success()
        {
            _transport.Enqueue(200, "{\"value\":99}", success: true);
            var client = MakeClient();

            var resp = await client.PostAsync<TestPayload>("api/test", new { input = "data" });

            Assert.IsTrue(resp.success);
            Assert.AreEqual(99, resp.data!.value);
        }

        // ── PostAsync (non-generic) ──

        [Test]
        public async Task PostAsync_NonGeneric_Success()
        {
            _transport.Enqueue(200, "{}", success: true);
            var client = MakeClient();

            var resp = await client.PostAsync("api/test", new { action = "save" });

            Assert.IsTrue(resp.success);
        }

        // ── 에러 분류 (4xx만 — 5xx/ConnectionError는 내부 retry 때문에 느림, HttpExecutorTests에서 커버) ──

        [TestCase(401, ErrorType.AuthExpired)]
        [TestCase(403, ErrorType.AuthFailed)]
        [TestCase(400, ErrorType.BadRequest)]
        [TestCase(404, ErrorType.NotFound)]
        [TestCase(429, ErrorType.RateLimit)]
        public async Task Error_Classification(int statusCode, ErrorType expected)
        {
            _transport.Enqueue(statusCode, "error body", success: false, error: "http error");
            var client = MakeClient();

            var resp = await client.GetAsync<TestPayload>("api/test");

            Assert.IsFalse(resp.success);
            Assert.AreEqual(expected, resp.errorType);
        }

        // ── 요청 구성 ──

        [Test]
        public async Task Request_Has_ContentType_And_Correct_Url()
        {
            _transport.Enqueue(200, "{}", success: true);
            var client = MakeClient();

            await client.PostAsync<TestPayload>("api/endpoint", new { data = 1 });

            var sent = _transport.LastRequest;
            Assert.AreEqual($"{CloudRunUrl}/api/endpoint", sent.Url);
            Assert.AreEqual("POST", sent.Method);
            Assert.AreEqual("application/json", sent.Headers["Content-Type"]);
            Assert.IsNotNull(sent.Body);
        }

        [Test]
        public async Task GetAsync_Has_No_Body()
        {
            _transport.Enqueue(200, "{}", success: true);
            var client = MakeClient();

            await client.GetAsync<TestPayload>("api/test");

            Assert.IsNull(_transport.LastRequest.Body);
        }

        // ── 토큰 pull (ISessionProvider) ──

        [Test]
        public async Task Request_Pulls_Bearer_From_SessionProvider()
        {
            _transport.Enqueue(200, "{}", success: true);
            _session.CurrentSession = new AuthSession
            {
                accessToken = "pulled-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };
            var client = MakeClient();

            await client.GetAsync<TestPayload>("api/test");

            Assert.That(_transport.LastRequest.Headers["Authorization"], Does.Contain("pulled-jwt"));
        }

        [Test]
        public async Task No_Session_Sends_No_Bearer()
        {
            _transport.Enqueue(200, "{}", success: true);
            var client = MakeClient();   // provider.CurrentSession == null

            await client.GetAsync<TestPayload>("api/test");

            Assert.IsFalse(_transport.LastRequest.Headers.ContainsKey("Authorization"));
        }

        // ── 테스트 모델 ──

        class TestPayload
        {
            public int value;
        }
    }
}
