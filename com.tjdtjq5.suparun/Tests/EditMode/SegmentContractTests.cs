#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Tjdtjq5.SupaRun.Editor;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>
    /// 세그먼트 평가 계약 테스트 (③ 트랙 #45, ADR-0011) — dev DB 의 평가 함수에 붙는다.
    ///
    /// 평가기는 DB 함수가 유일한 구현이라 계약도 거기서 두드린다. 검증 대상:
    ///   빈 조건 — all=전원 참, any=전원 거짓 (합집합의 항등원)
    ///   account since_days / table count·sum 경계값 / any-all 결합
    ///   화이트리스트 — 없는 표·컬럼은 예외(조용한 무시 금지)
    ///   가드 — anon 은 RPC 거부, viewer 는 통과
    /// 세그먼트 행은 전용 id(seg-contract-*)만 만들고 끝나면 지운다.
    /// </summary>
    class SegmentContractTests
    {
        const string ViewerEmail = "contract-viewer@suparun.test";
        const string Password = "spr-contract-4Qx7";

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        class Ctx
        {
            public string Url = "", Anon = "", Pat = "", Ref = "";
            public string ViewerToken = "", ViewerUid = "";
        }

        static Task<Ctx>? _init;
        static Task<Ctx> Init() => _init ??= InitAsync();

        static async Task<Ctx> InitAsync()
        {
            var env = SupaRunSettings.Instance?.Current;
            var c = new Ctx
            {
                Url = (env?.supabaseUrl ?? "").TrimEnd('/'),
                Anon = env?.supabaseAnonKey ?? "",
                Pat = SupaRunSettings.AccessTokenOf(env),
                Ref = SupaRunSettings.ProjectIdOf(env?.supabaseUrl ?? ""),
            };
            if (c.Url.Length == 0 || c.Anon.Length == 0 || string.IsNullOrEmpty(c.Pat))
                Assert.Ignore("편집 환경 설정(URL/anon/PAT)이 없어 계약 테스트를 건너뜁니다.");
            if ((SupaRunSettings.Instance!.EnvName ?? "").IndexOf("prod", StringComparison.OrdinalIgnoreCase) >= 0)
                Assert.Ignore("prod 환경에는 계약 테스트 데이터를 만들지 않습니다.");

            // viewer 로그인 (없으면 가입) — 평가 RPC 는 롤 보유자 전체가 부를 수 있어야 한다.
            var body = $"{{\"email\":\"{ViewerEmail}\",\"password\":\"{Password}\"}}";
            var r = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/token?grant_type=password", c.Anon, null, body);
            if (r.status != 200)
                r = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/signup", c.Anon, null, body);
            var token = Regex.Match(r.body, "\"access_token\"\\s*:\\s*\"([^\"]+)\"");
            var uid = Regex.Match(r.body,
                "\"id\"\\s*:\\s*\"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\"");
            if (!token.Success || !uid.Success)
                Assert.Inconclusive("viewer 계정 준비 실패");
            c.ViewerToken = token.Groups[1].Value;
            c.ViewerUid = uid.Groups[1].Value;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await PatQuery(c,
                $"INSERT INTO admin_user_role (user_id, role, granted_at, granted_by) VALUES " +
                $"('{c.ViewerUid}', 'game-viewer', {now}, 'contract-test') ON CONFLICT (user_id, role) DO NOTHING;");
            return c;
        }

        [Test]
        public async Task Empty_Conditions_All_Matches_Any_Does_Not()
        {
            var c = await Init();
            await Seed(c, "seg-contract-all", "all", "[]");
            await Seed(c, "seg-contract-any", "any", "[]");
            try
            {
                Assert.IsTrue(await Match(c, "seg-contract-all", c.ViewerUid), "빈 all 은 전원 참이어야 한다");
                Assert.IsFalse(await Match(c, "seg-contract-any", c.ViewerUid), "빈 any 는 전원 거짓이어야 한다");
            }
            finally { await Cleanup(c); }
        }

        [Test]
        public async Task Account_And_Table_Conditions_With_Boundaries()
        {
            var c = await Init();
            // 방금 로그인했으므로 since_days 1 은 참, 재화 max 는 경계(= 값)에서 참/거짓이 갈린다.
            await PatQuery(c,
                $"DELETE FROM currency WHERE playerid = '{c.ViewerUid}'; " +
                $"INSERT INTO currency (id, playerid, currencyid, amount, lastrechargeat, updatedat) " +
                $"VALUES ('{c.ViewerUid}_seggold', '{c.ViewerUid}', 'seggold', 100, 0, 0);");

            await Seed(c, "seg-contract-active", "all",
                "[{\"source\":\"account\",\"column\":\"last_sign_in_at\",\"op\":\"since_days\",\"value\":1}]");
            await Seed(c, "seg-contract-rich", "all",
                "[{\"source\":\"table\",\"table\":\"currency\",\"table_filter\":{\"currencyid\":\"seggold\"}," +
                "\"column\":\"amount\",\"agg\":\"max\",\"op\":\">=\",\"value\":100}]");
            await Seed(c, "seg-contract-richer", "all",
                "[{\"source\":\"table\",\"table\":\"currency\",\"table_filter\":{\"currencyid\":\"seggold\"}," +
                "\"column\":\"amount\",\"agg\":\"max\",\"op\":\">=\",\"value\":101}]");
            // any 결합 — 거짓(101) 하나 + 참(count>=1) 하나면 참이어야 한다.
            await Seed(c, "seg-contract-anymix", "any",
                "[{\"source\":\"table\",\"table\":\"currency\",\"table_filter\":{\"currencyid\":\"seggold\"}," +
                "\"column\":\"amount\",\"agg\":\"max\",\"op\":\">=\",\"value\":101}," +
                "{\"source\":\"table\",\"table\":\"currency\",\"agg\":\"count\",\"op\":\">=\",\"value\":1}]");
            try
            {
                Assert.IsTrue(await Match(c, "seg-contract-active", c.ViewerUid), "방금 로그인 = since_days 1 참");
                Assert.IsTrue(await Match(c, "seg-contract-rich", c.ViewerUid), "max 100 >= 100 경계는 참");
                Assert.IsFalse(await Match(c, "seg-contract-richer", c.ViewerUid), "max 100 >= 101 은 거짓");
                Assert.IsTrue(await Match(c, "seg-contract-anymix", c.ViewerUid), "any 는 하나만 참이어도 참");

                // 대상 수 — rich 는 최소 1(viewer 본인). 소속 목록에도 나타난다.
                var count = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/rpc/suparun_segment_count",
                    c.Anon, c.ViewerToken, "{\"p_segment_id\":\"seg-contract-rich\"}");
                Assert.AreEqual(200, count.status, count.body);
                Assert.GreaterOrEqual(int.Parse(count.body.Trim()), 1, "대상 수는 1 이상이어야 한다");

                var of = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/rpc/suparun_segments_of",
                    c.Anon, c.ViewerToken, $"{{\"p_player_id\":\"{c.ViewerUid}\"}}");
                StringAssert.Contains("seg-contract-rich", of.body, "소속 목록에 나타나야 한다");
            }
            finally
            {
                await PatQuery(c, $"DELETE FROM currency WHERE playerid = '{c.ViewerUid}' AND currencyid = 'seggold';");
                await Cleanup(c);
            }
        }

        [Test]
        public async Task Whitelist_Rejects_Unknown_Table_And_Anon()
        {
            var c = await Init();
            await Seed(c, "seg-contract-evil", "all",
                "[{\"source\":\"table\",\"table\":\"admin_user_role\",\"agg\":\"count\",\"op\":\">=\",\"value\":0}]");
            try
            {
                // 화이트리스트 밖 표(playerColumn 없는 관리 표)는 **예외**여야 한다 — 조용한 무시 금지.
                var r = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/rpc/suparun_segment_match",
                    c.Anon, c.ViewerToken, $"{{\"p_segment_id\":\"seg-contract-evil\",\"p_player_id\":\"{c.ViewerUid}\"}}");
                Assert.AreEqual(400, r.status, $"허용 밖 표는 평가가 거부돼야 한다 — {r.body}");
                StringAssert.Contains("허용되지 않는", r.body);

                // anon 은 RPC 자체가 거부된다.
                var anon = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/rpc/suparun_segment_match",
                    c.Anon, null, $"{{\"p_segment_id\":\"seg-contract-evil\",\"p_player_id\":\"x\"}}");
                Assert.IsTrue(anon.status >= 400, "anon 의 평가는 거부돼야 한다");
            }
            finally { await Cleanup(c); }
        }

        // ── 유틸 ──

        static async Task Seed(Ctx c, string id, string match, string conditionsJson)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await PatQuery(c,
                $"INSERT INTO suparun_segment (id, name, match, conditions, created_at, created_by) " +
                $"VALUES ('{id}', '{id}', '{match}', '{conditionsJson.Replace("'", "''")}'::jsonb, {now}, 'contract-test') " +
                $"ON CONFLICT (id) DO UPDATE SET match = EXCLUDED.match, conditions = EXCLUDED.conditions;");
        }

        static async Task Cleanup(Ctx c) =>
            await PatQuery(c, "DELETE FROM suparun_segment WHERE id LIKE 'seg-contract-%';");

        static async Task<bool> Match(Ctx c, string segId, string playerId)
        {
            var r = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/rpc/suparun_segment_match",
                c.Anon, c.ViewerToken, $"{{\"p_segment_id\":\"{segId}\",\"p_player_id\":\"{playerId}\"}}");
            Assert.AreEqual(200, r.status, $"평가 실패({segId}) — {r.body}");
            return r.body.Trim() == "true";
        }

        static async Task<(int status, string body)> Send(
            HttpMethod method, string url, string anon, string? bearer, string? json = null)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("apikey", anon);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (bearer ?? anon));
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            return ((int)res.StatusCode, await res.Content.ReadAsStringAsync());
        }

        static async Task PatQuery(Ctx c, string sql)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.supabase.com/v1/projects/{c.Ref}/database/query");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + c.Pat);
            var escaped = sql.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
            req.Content = new StringContent("{\"query\":\"" + escaped + "\"}", Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                Assert.Inconclusive($"PAT 준비 쿼리 실패 — HTTP {(int)res.StatusCode}");
        }
    }
}
