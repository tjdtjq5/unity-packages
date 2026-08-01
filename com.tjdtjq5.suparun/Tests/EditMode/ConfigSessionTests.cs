#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using Tjdtjq5.SupaRun;

// 소스젠(DefGenerator)이 [SpecData] 타입마다 Def 를 생성하며 **비정규화 이름**을 참조한다 —
// 중첩·네임스페이스 타입이면 생성물이 컴파일되지 않는다(테이블 클래스 네임스페이스 금지 규칙과
// 같은 함정). 그래서 테스트용 표는 네임스페이스 밖 top-level public 이어야 한다.
[SpecData]
public class PinCfg
{
    public string id = "";
    public int power;
}

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>
    /// config 세션 고정 (#35, ADR-0010) — 세션 중 [SpecData] 조회 불변, 새 세션에서 갱신,
    /// logic version 게이트. 전부 MockHttpTransport 로 오프라인 검증한다.
    /// </summary>
    class ConfigSessionTests
    {
        static SupaRunRuntime MakeRuntime(MockHttpTransport transport, int logicVersion = 0)
        {
            return new SupaRunRuntime(new SupaRunRuntimeOptions
            {
                SupabaseUrl = "https://test.supabase.co",
                AnonKey = "anon",
                // CloudRunUrl 이 있어야 데이터 API 가 서버 경로를 탄다 (없으면 LocalGameDB fallback).
                CloudRunUrl = "https://server.test",
                Transport = transport,
                SessionStorage = new MemorySessionStorage(),
                Realtime = new MockRealtimeClient(),
                LogicVersion = logicVersion,
            });
        }

        [Test]
        public async Task SpecData_Is_Pinned_During_Session()
        {
            var t = new MockHttpTransport();
            using var rt = MakeRuntime(t);

            t.Enqueue(200, @"[{""id"":""a"",""power"":1}]");
            var first = await rt.GetAll<PinCfg>();
            Assert.IsTrue(first.success);
            Assert.AreEqual(1, first.data![0].power);
            var sends = t.SendCount;

            // 세션 중 게시가 일어나 서버 값이 바뀌어도(다음 응답 큐) 조회는 캐시를 쓴다 —
            // 요청 자체가 안 나가고 값도 그대로다.
            t.Enqueue(200, @"[{""id"":""a"",""power"":999}]");
            var second = await rt.GetAll<PinCfg>();
            Assert.AreEqual(1, second.data![0].power, "세션 중에는 첫 조회 값으로 고정돼야 한다");
            Assert.AreEqual(sends, t.SendCount, "캐시 히트는 네트워크로 나가면 안 된다");

            // 단건도 같은 캐시다. 없는 id 는 404.
            var one = await rt.Get<PinCfg>("a");
            Assert.AreEqual(1, one.data!.power);
            var none = await rt.Get<PinCfg>("zzz");
            Assert.AreEqual(404, none.statusCode);
            Assert.AreEqual(sends, t.SendCount);
        }

        [Test]
        public async Task New_Session_Sees_New_Version()
        {
            var t = new MockHttpTransport();
            using var rt = MakeRuntime(t);

            t.Enqueue(200, @"[{""id"":""a"",""power"":1}]");
            await rt.GetAll<PinCfg>();

            // 새 세션 = 재협상. 협상(메타 1회) + 다음 조회(새 값 1회)가 다시 나간다.
            t.Enqueue(200, @"[{""key"":""active_config_version"",""value"":{""content_hash"":""abc123"",""published_at"":42}}]");
            var info = await rt.RefreshConfigSessionAsync();
            Assert.AreEqual("abc123", info.ActiveVersionHash);
            Assert.AreEqual(42, info.ActivePublishedAt);

            t.Enqueue(200, @"[{""id"":""a"",""power"":2}]");
            var after = await rt.GetAll<PinCfg>();
            Assert.AreEqual(2, after.data![0].power, "새 세션은 새 버전을 봐야 한다");
        }

        [Test]
        public async Task Logic_Version_Gate()
        {
            var t = new MockHttpTransport();
            using var rt = MakeRuntime(t, logicVersion: 3);

            // 허용 범위 5~9 — 3 은 범위 밖이다.
            t.Enqueue(200, @"[{""key"":""logic_version_range"",""value"":{""min"":5,""max"":9}}]");
            var outOfRange = await rt.RefreshConfigSessionAsync();
            Assert.IsFalse(outOfRange.LogicCompatible);
            Assert.AreEqual(5, outOfRange.LogicMin);
            Assert.AreEqual(9, outOfRange.LogicMax);

            using var rt7 = MakeRuntime(t, logicVersion: 7);
            t.Enqueue(200, @"[{""key"":""logic_version_range"",""value"":{""min"":5,""max"":9}}]");
            Assert.IsTrue((await rt7.RefreshConfigSessionAsync()).LogicCompatible);

            // 범위 메타가 없는 프로젝트는 게이트가 없다 — 호환으로 통과.
            using var rtNoMeta = MakeRuntime(t, logicVersion: 3);
            t.Enqueue(200, @"[]");
            Assert.IsTrue((await rtNoMeta.RefreshConfigSessionAsync()).LogicCompatible);
        }

        [Test]
        public async Task Negotiation_Failure_Does_Not_Block()
        {
            var t = new MockHttpTransport();
            using var rt = MakeRuntime(t);

            // 협상 실패(네트워크) → 스탬프 없음·호환 통과로 계속 간다.
            t.Enqueue(0, success: false, isConnectionError: true, error: "offline");
            var info = await rt.RefreshConfigSessionAsync();
            Assert.IsNull(info.ActiveVersionHash);
            Assert.IsTrue(info.LogicCompatible);

            // 조회는 여전히 동작한다.
            t.Enqueue(200, @"[{""id"":""a"",""power"":1}]");
            var r = await rt.GetAll<PinCfg>();
            Assert.IsTrue(r.success);
        }
    }
}
