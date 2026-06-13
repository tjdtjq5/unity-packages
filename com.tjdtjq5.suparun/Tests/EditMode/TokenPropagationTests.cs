#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>
    /// 토큰 전파 검증. Realtime 소켓만 OnAuthSessionChanged로 push되고,
    /// HTTP/REST 클라이언트는 ISessionProvider(Auth)에서 요청 시 pull한다.
    /// </summary>
    class TokenPropagationTests
    {
        // ── Realtime push (소켓은 pull 불가라 세션 변경 시 push) ──

        [Test]
        public void Session_Change_Propagates_To_Realtime()
        {
            var mockRealtime = new MockRealtimeClient();
            var transport = new MockHttpTransport();
            var runtime = CreateRuntime(transport, mockRealtime);

            var session = new AuthSession
            {
                accessToken = "new-jwt-token",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };
            runtime.OnAuthSessionChanged(session);

            Assert.AreEqual("new-jwt-token", mockRealtime.LastAccessToken);
        }

        [Test]
        public void Null_Session_Does_Not_Set_Realtime_Token()
        {
            var mockRealtime = new MockRealtimeClient();
            var transport = new MockHttpTransport();
            var runtime = CreateRuntime(transport, mockRealtime);

            runtime.OnAuthSessionChanged(null!);

            Assert.AreEqual(0, mockRealtime.AccessTokensSet.Count);
        }

        [Test]
        public void Multiple_Session_Changes_All_Propagate()
        {
            var mockRealtime = new MockRealtimeClient();
            var transport = new MockHttpTransport();
            var runtime = CreateRuntime(transport, mockRealtime);

            runtime.OnAuthSessionChanged(new AuthSession { accessToken = "token-1" });
            runtime.OnAuthSessionChanged(new AuthSession { accessToken = "token-2" });
            runtime.OnAuthSessionChanged(new AuthSession { accessToken = "token-3" });

            Assert.AreEqual(3, mockRealtime.AccessTokensSet.Count);
            Assert.AreEqual("token-3", mockRealtime.LastAccessToken);
        }

        [Test]
        public void Dispose_Disconnects_Realtime()
        {
            var mockRealtime = new MockRealtimeClient();
            var transport = new MockHttpTransport();
            var runtime = CreateRuntime(transport, mockRealtime);

            runtime.Dispose();

            Assert.IsTrue(mockRealtime.DisconnectCalled);
        }

        // ── HTTP 클라이언트 pull (이전 _client.Session/_restClient.Session push 테스트를 대체) ──
        // 로그인으로 Auth.CurrentSession을 채운 뒤, 클라이언트 요청이 그 토큰을 pull해 Bearer에 싣는지 end-to-end 검증.

        [Test]
        public async Task Client_Pulls_Token_From_Auth_After_Login()
        {
            var transport = new MockHttpTransport();
            var authApi = new MockAuthApi();
            authApi.Enqueue(@"{""access_token"":""pulled-jwt"",""refresh_token"":""r"",""expires_in"":3600,""user"":{""id"":""u""}}");
            var runtime = new SupaRunRuntime(new SupaRunRuntimeOptions
            {
                SupabaseUrl = "https://test.supabase.co",
                AnonKey = "test-anon-key",
                CloudRunUrl = "https://api.test.com",
                Transport = transport,
                SessionStorage = new MemorySessionStorage(),
                AuthApi = authApi,
                Realtime = new MockRealtimeClient(),
            });

            await runtime.Login();                       // Auth.CurrentSession = pulled-jwt
            transport.Enqueue(200, "{}", success: true);
            await runtime._client!.GetAsync<object>("api/test");

            Assert.That(transport.LastRequest.Headers["Authorization"], Does.Contain("pulled-jwt"));
        }

        static SupaRunRuntime CreateRuntime(MockHttpTransport transport,
                                            Supabase.IRealtimeClient realtime)
        {
            return new SupaRunRuntime(new SupaRunRuntimeOptions
            {
                SupabaseUrl = "https://test.supabase.co",
                AnonKey = "test-anon-key",
                CloudRunUrl = "https://api.test.com",
                Transport = transport,
                SessionStorage = new MemorySessionStorage(),
                AuthApi = new MockAuthApi(),
                Realtime = realtime,
            });
        }
    }
}
