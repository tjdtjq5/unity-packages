#nullable enable
using System;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>SupaRunRuntime.OnAuthSessionChanged → client/restClient/realtime 토큰 전파 검증.</summary>
    class TokenPropagationTests
    {
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
        public void Session_Change_Propagates_To_Client()
        {
            var transport = new MockHttpTransport();
            var runtime = CreateRuntime(transport, new MockRealtimeClient());

            var session = new AuthSession
            {
                accessToken = "client-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };
            runtime.OnAuthSessionChanged(session);

            Assert.AreEqual("client-jwt", runtime._client?.Session?.accessToken);
        }

        [Test]
        public void Session_Change_Propagates_To_RestClient()
        {
            var transport = new MockHttpTransport();
            var runtime = CreateRuntime(transport, new MockRealtimeClient());

            var session = new AuthSession
            {
                accessToken = "rest-jwt",
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
            };
            runtime.OnAuthSessionChanged(session);

            Assert.AreEqual("rest-jwt", runtime._restClient?.Session?.accessToken);
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
