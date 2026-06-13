#nullable enable
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>SupaRunAuth 세션 관리 + 로그인 흐름 테스트.</summary>
    class SupaRunAuthTests
    {
        const string SupabaseUrl = "https://test.supabase.co";
        const string AnonKey = "test-anon-key";

        // ── 세션 저장/로드/삭제 ──

        [Test]
        public void ClearSession_Removes_All_Keys()
        {
            var storage = new MemorySessionStorage();
            var auth = MakeAuth(storage);

            // 세션이 없어도 에러 없이 동작
            Assert.DoesNotThrow(() => auth.ClearSession());
            Assert.IsFalse(auth.IsLoggedIn);
        }

        [Test]
        public void IsLoggedIn_False_Initially()
        {
            var auth = MakeAuth(new MemorySessionStorage());
            Assert.IsFalse(auth.IsLoggedIn);
            Assert.IsTrue(auth.IsGuest);
            Assert.IsNull(auth.UserId);
        }

        // ── 로그인 흐름 (MockAuthApi) ──

        [Test]
        public async Task EnsureLoggedIn_Anonymous_Creates_Guest_Session()
        {
            var mockApi = new MockAuthApi();
            // Supabase anonymous signup 응답
            mockApi.Enqueue(@"{
                ""access_token"": ""guest-jwt"",
                ""refresh_token"": ""guest-refresh"",
                ""expires_in"": 3600,
                ""user"": { ""id"": ""guest-user-id"" }
            }");

            var storage = new MemorySessionStorage();
            var auth = MakeAuth(storage, mockApi);

            await auth.EnsureLoggedIn();

            Assert.IsTrue(auth.IsLoggedIn);
            Assert.IsTrue(auth.IsGuest);
            Assert.AreEqual("guest-jwt", auth.Session!.accessToken);
            Assert.AreEqual("guest-user-id", auth.UserId);
            Assert.AreEqual(1, mockApi.CallCount);
            Assert.That(mockApi.Requests[0].endpoint, Does.Contain("/auth/v1/signup"));
        }

        [Test]
        public async Task EnsureLoggedIn_Restores_Saved_Session()
        {
            var jwt = FakeJwt("saved-user");
            var mockApi = new MockAuthApi();
            // 첫 로그인: 게스트 생성 (LoadSession → ParseExpFromJwt가 유효한 exp를 읽도록 FakeJwt 사용)
            mockApi.Enqueue($@"{{
                ""access_token"": ""{jwt}"",
                ""refresh_token"": ""saved-refresh"",
                ""expires_in"": 3600,
                ""user"": {{ ""id"": ""saved-user"" }}
            }}");

            var storage = new MemorySessionStorage();
            var auth1 = MakeAuth(storage, mockApi);
            await auth1.EnsureLoggedIn();
            Assert.AreEqual(1, mockApi.CallCount);

            // 두 번째 auth 인스턴스: 같은 storage에서 복원 (HTTP 호출 없음)
            var mockApi2 = new MockAuthApi();
            var auth2 = MakeAuth(storage, mockApi2);
            await auth2.EnsureLoggedIn();

            Assert.IsTrue(auth2.IsLoggedIn);
            Assert.AreEqual(0, mockApi2.CallCount); // HTTP 미호출
        }

        [Test]
        public async Task EnsureLoggedIn_Dedup_Multiple_Calls()
        {
            var mockApi = new MockAuthApi();
            mockApi.Enqueue(@"{
                ""access_token"": ""jwt"",
                ""refresh_token"": ""ref"",
                ""expires_in"": 3600,
                ""user"": { ""id"": ""user"" }
            }");

            var auth = MakeAuth(new MemorySessionStorage(), mockApi);

            // gate로 첫 호출을 in-flight 상태로 묶어 두 번째 호출이 dedup되는지 검증.
            // (동기 mock으로는 "진행 중" 윈도우가 없어 동시성 dedup을 표현할 수 없음)
            var gate = mockApi.Gate();
            var t1 = auth.EnsureLoggedIn();   // signup PostAsync 진입 후 gate에서 대기 (in-flight)
            var t2 = auth.EnsureLoggedIn();   // 진행 중 작업에 합류 — 두 번째 PostAsync 없음
            gate.TrySetResult();              // 해제
            await UniTask.WhenAll(t1, t2);

            Assert.AreEqual(1, mockApi.CallCount);
        }

        [Test]
        public async Task EnsureLoggedIn_Fail_Stays_Logged_Out()
        {
            var mockApi = new MockAuthApi();
            // null 응답 = 실패
            mockApi.Enqueue(null);

            var auth = MakeAuth(new MemorySessionStorage(), mockApi);
            await auth.EnsureLoggedIn();

            Assert.IsFalse(auth.IsLoggedIn);
        }

        [Test]
        public async Task OnSessionChanged_Fires_On_Login()
        {
            var mockApi = new MockAuthApi();
            mockApi.Enqueue(@"{
                ""access_token"": ""jwt"",
                ""refresh_token"": ""ref"",
                ""expires_in"": 3600,
                ""user"": { ""id"": ""user"" }
            }");

            var auth = MakeAuth(new MemorySessionStorage(), mockApi);
            AuthSession? received = null;
            auth.OnSessionChanged += s => received = s;

            await auth.EnsureLoggedIn();

            Assert.IsNotNull(received);
            Assert.AreEqual("jwt", received!.accessToken);
        }

        [Test]
        public async Task TryRefreshToken_Concurrent_Dedups()
        {
            var jwt = FakeJwt("u");
            var mockApi = new MockAuthApi();
            // 1) 최초 로그인 (refresh token 보유)
            mockApi.Enqueue($@"{{
                ""access_token"": ""{jwt}"",
                ""refresh_token"": ""refresh-1"",
                ""expires_in"": 3600,
                ""user"": {{ ""id"": ""u"" }}
            }}");
            var auth = MakeAuth(new MemorySessionStorage(), mockApi);
            await auth.EnsureLoggedIn();
            Assert.AreEqual(1, mockApi.CallCount);   // login 1회

            // 2) refresh 응답 1개 + gate로 동시 호출을 in-flight로 묶기
            mockApi.Enqueue($@"{{
                ""access_token"": ""{jwt}"",
                ""refresh_token"": ""refresh-2"",
                ""expires_in"": 3600,
                ""user"": {{ ""id"": ""u"" }}
            }}");
            var gate = mockApi.Gate();
            var r1 = auth.TryRefreshToken();   // refresh PostAsync 진입 후 gate 대기
            var r2 = auth.TryRefreshToken();   // dedup — 두 번째 refresh POST 없음
            gate.TrySetResult();
            await UniTask.WhenAll(r1, r2);

            Assert.AreEqual(2, mockApi.CallCount);   // login 1 + refresh 1 (dedup 안 됐으면 3)
        }

        // ── 헬퍼 ──

        SupaRunAuth MakeAuth(ISessionStorage storage, IAuthApi? authApi = null)
        {
            return new SupaRunAuth(SupabaseUrl, AnonKey,
                storage: storage,
                authApi: authApi ?? new MockAuthApi());
        }

        /// <summary>LoadSession → ParseExpFromJwt가 유효한 exp를 읽을 수 있는 가짜 JWT 생성.</summary>
        static string FakeJwt(string sub, long? expUnix = null)
        {
            var exp = expUnix ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200;
            var header = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
            var payload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{{\"exp\":{exp},\"sub\":\"{sub}\"}}"));
            return $"{header}.{payload}.fake";
        }
    }
}
