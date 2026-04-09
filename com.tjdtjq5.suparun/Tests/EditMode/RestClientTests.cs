#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class RestClientTests
    {
        const string SupabaseUrl = "https://test.supabase.co";
        const string AnonKey = "test-anon-key";
        MockHttpTransport _transport = null!;

        [SetUp]
        public void SetUp()
        {
            _transport = new MockHttpTransport();
        }

        SupabaseRestClient MakeClient() => new SupabaseRestClient(SupabaseUrl, AnonKey, _transport);

        // ── GetAll ──

        [Test]
        public async Task GetAll_Success_Deserializes_List()
        {
            _transport.Enqueue(200, "[{\"id\":1,\"name\":\"A\"},{\"id\":2,\"name\":\"B\"}]", success: true);
            var client = MakeClient();

            var resp = await client.GetAll<TestItem>();

            Assert.IsTrue(resp.success);
            Assert.AreEqual(2, resp.data!.Count);
            Assert.AreEqual("A", resp.data[0].name);
        }

        [Test]
        public async Task GetAll_Empty_Returns_Empty_List()
        {
            _transport.Enqueue(200, "[]", success: true);
            var client = MakeClient();

            var resp = await client.GetAll<TestItem>();

            Assert.IsTrue(resp.success);
            Assert.IsNotNull(resp.data);
            Assert.AreEqual(0, resp.data!.Count);
        }

        // ── Get ──

        [Test]
        public async Task Get_Found_Returns_Item()
        {
            _transport.Enqueue(200, "[{\"id\":1,\"name\":\"Found\"}]", success: true);
            var client = MakeClient();

            var resp = await client.Get<TestItem>(1);

            Assert.IsTrue(resp.success);
            Assert.AreEqual("Found", resp.data!.name);
        }

        [Test]
        public async Task Get_Not_Found_Returns_Error()
        {
            _transport.Enqueue(200, "[]", success: true);
            var client = MakeClient();

            var resp = await client.Get<TestItem>(999);

            Assert.IsFalse(resp.success);
            Assert.AreEqual(ErrorType.NotFound, resp.errorType);
        }

        // ── 에러 ──

        [Test]
        public async Task Server_Error_Returns_Failure()
        {
            _transport.Enqueue(500, "Internal Server Error", success: false, error: "500 error");
            var client = MakeClient();

            var resp = await client.GetAll<TestItem>();

            Assert.IsFalse(resp.success);
            Assert.AreEqual(ErrorType.ServerError, resp.errorType);
        }

        [Test]
        public async Task Invalid_Json_Returns_Parse_Error()
        {
            _transport.Enqueue(200, "not-json{{{", success: true);
            var client = MakeClient();

            var resp = await client.GetAll<TestItem>();

            Assert.IsFalse(resp.success);
            Assert.AreEqual(ErrorType.BadRequest, resp.errorType);
            Assert.That(resp.error, Does.Contain("JSON"));
        }

        // ── Anonymous 경고 ──

        [Test]
        public async Task Anonymous_Call_Sets_Hint()
        {
            _transport.Enqueue(200, "[]", success: true);
            var client = MakeClient();
            // Session 미설정 = anonymous

            var resp = await client.GetAll<TestItem>();

            Assert.IsTrue(resp.success);
            Assert.IsFalse(resp.isAuthenticated);
            Assert.IsNotNull(resp.hint);
        }

        [Test]
        public async Task Authenticated_Call_No_Hint()
        {
            _transport.Enqueue(200, "[]", success: true);
            var client = MakeClient();
            client.Session = new AuthSession
            {
                accessToken = "valid-jwt",
                expiresAt = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };

            var resp = await client.GetAll<TestItem>();

            Assert.IsTrue(resp.success);
            Assert.IsTrue(resp.isAuthenticated);
            Assert.IsNull(resp.hint);
        }

        // ── URL 구성 확인 ──

        [Test]
        public async Task Url_Uses_Snake_Case_Table_Name()
        {
            _transport.Enqueue(200, "[]", success: true);
            var client = MakeClient();

            await client.GetAll<TestItem>();

            Assert.That(_transport.LastRequest.Url, Does.Contain("/rest/v1/test_item"));
        }

        // ── 테스트 모델 ──

        class TestItem
        {
            public int id;
            public string name = "";
        }
    }
}
