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
    /// CS 액션 계약 테스트 (③ 트랙 #38·#39·#42) — **dev Cloud Run 서버**에 붙는 통합 테스트다.
    ///
    /// 롤 게이트·감사·밴 반영은 전부 서버(코드젠 산출물)에 있어 목으로는 계약을 증명할 수 없다.
    /// 편집 환경 설정 또는 Cloud Run URL 이 없으면 Ignore 로 빠진다.
    ///
    /// 검증 대상:
    ///   토큰 없음 — CS 엔드포인트·ban-check 401
    ///   viewer    — CS 액션 403 (cs 계열 롤이 아님), 타인 ban-check 403
    ///   admin     — 재화 지급 200 + 잔액 반영 + 감사 행 (#38)
    ///   밴        — SetBan 후 본인 ban-check 가 banned=true, 해제 후 false (#39)
    ///   GDPR      — viewer 로는 403(senior 게이트), admin 삭제 후 로그인·조회 불가 (#42)
    ///
    /// 실데이터는 건드리지 않는다 — 쓰기 대상은 전용 계정(contract-*)의 행뿐이고,
    /// 삭제 검증은 매 실행 재생성되는 희생 계정(contract-victim)이 맡는다.
    /// </summary>
    class CsActionContractTests
    {
        const string ViewerEmail = "contract-viewer@suparun.test";
        const string AdminEmail = "contract-admin@suparun.test";
        const string VictimEmail = "contract-victim@suparun.test";
        const string Password = "spr-contract-4Qx7";

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        class Ctx
        {
            public string Url = "", Anon = "", Pat = "", Ref = "", Server = "";
            public string ViewerToken = "", ViewerUid = "", AdminToken = "", AdminUid = "";
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
                Server = SupaRunSettings.CloudRunUrlOf(env).TrimEnd('/'),
            };
            if (c.Url.Length == 0 || c.Anon.Length == 0 || string.IsNullOrEmpty(c.Pat))
                Assert.Ignore("편집 환경 설정(URL/anon/PAT)이 없어 계약 테스트를 건너뜁니다.");
            if (c.Server.Length == 0)
            {
                // suparun_env 캐시는 메모리뿐이라 테스트 러너의 도메인 리로드가 비운다 — DB에서 다시 당긴다.
                await SupaRunSettings.RefreshEnvAsync();
                c.Server = SupaRunSettings.CloudRunUrlOf(env).TrimEnd('/');
            }
            if (c.Server.Length == 0)
                Assert.Ignore("Cloud Run URL 이 없어 CS 계약 테스트를 건너뜁니다 — 서버 배포가 먼저입니다.");
            if ((SupaRunSettings.Instance!.EnvName ?? "").IndexOf("prod", StringComparison.OrdinalIgnoreCase) >= 0)
                Assert.Ignore("prod 환경에는 계약 테스트 계정을 만들지 않습니다.");

            (c.ViewerToken, c.ViewerUid) = await EnsureAccount(c, ViewerEmail);
            (c.AdminToken, c.AdminUid) = await EnsureAccount(c, AdminEmail);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await PatQuery(c,
                $"DELETE FROM admin_user_role WHERE user_id = '{c.ViewerUid}' AND role <> 'game-viewer'; " +
                $"INSERT INTO admin_user_role (user_id, role, granted_at, granted_by) VALUES " +
                $"('{c.ViewerUid}', 'game-viewer', {now}, 'contract-test'), " +
                $"('{c.AdminUid}', 'game-admin', {now}, 'contract-test') " +
                "ON CONFLICT (user_id, role) DO NOTHING;");
            return c;
        }

        // ── 토큰 없음 ──

        [Test]
        public async Task Anonymous_Is_Rejected()
        {
            var c = await Init();
            var ban = await Send(HttpMethod.Get, $"{c.Server}/api/auth/ban-check/{c.ViewerUid}", null);
            Assert.AreEqual(401, ban.status, "토큰 없는 ban-check 는 401 이어야 한다");

            var grant = await Send(HttpMethod.Post, $"{c.Server}/api/cs_tools_service/GrantCurrency", null,
                $"{{\"playerId\":\"{c.ViewerUid}\",\"currencyId\":\"gold\",\"amount\":1,\"reason\":\"contract\"}}");
            Assert.AreEqual(401, grant.status, "토큰 없는 CS 액션은 401 이어야 한다");
        }

        // ── viewer (cs 계열 롤 아님) ──

        [Test]
        public async Task Viewer_Cannot_Run_Cs_Actions()
        {
            var c = await Init();
            var grant = await Send(HttpMethod.Post, $"{c.Server}/api/cs_tools_service/GrantCurrency", c.ViewerToken,
                $"{{\"playerId\":\"{c.ViewerUid}\",\"currencyId\":\"gold\",\"amount\":1,\"reason\":\"contract\"}}");
            Assert.AreEqual(403, grant.status, $"viewer 의 재화 지급은 403 이어야 한다 — {grant.body}");

            var other = await Send(HttpMethod.Get, $"{c.Server}/api/auth/ban-check/{c.AdminUid}", c.ViewerToken);
            Assert.AreEqual(403, other.status, "타인 ban-check 는 cs 롤 없이는 403 이어야 한다");

            var gdpr = await Send(HttpMethod.Post, $"{c.Server}/api/cs/system/GdprDelete", c.ViewerToken,
                $"{{\"playerId\":\"{c.ViewerUid}\"}}");
            Assert.AreEqual(403, gdpr.status, "viewer 의 GDPR 삭제는 403 이어야 한다 (senior 게이트)");
        }

        // ── admin — 재화 지급 트레이서 (#38) ──

        [Test]
        public async Task Admin_Grant_Currency_Updates_Balance_And_Audits()
        {
            var c = await Init();

            // 탐침 흔적을 먼저 걷어낸다 — 멱등.
            await PatQuery(c,
                $"DELETE FROM currency WHERE playerid = '{c.ViewerUid}' AND currencyid = 'gold'; " +
                $"DELETE FROM currency_log WHERE playerid = '{c.ViewerUid}';");

            var grant = await Send(HttpMethod.Post, $"{c.Server}/api/cs_tools_service/GrantCurrency", c.AdminToken,
                $"{{\"playerId\":\"{c.ViewerUid}\",\"currencyId\":\"gold\",\"amount\":7,\"reason\":\"contract-grant\"}}");
            Assert.AreEqual(200, grant.status, $"game-admin 의 재화 지급은 통과해야 한다 — {grant.body}");

            // 잔액 반영 — 표가 진실이다 (operator_read 로 viewer 도 읽는다).
            var bal = await Rest(c, $"currency?playerid=eq.{c.ViewerUid}&currencyid=eq.gold&select=amount", c.ViewerToken);
            StringAssert.Contains("7", bal, "지급 후 잔액이 표에 반영돼야 한다");

            // 감사 — cs:GrantCurrency 행이 admin 신원으로 남는다.
            var audit = await Rest(c,
                $"admin_audit_log?action=eq.cs:GrantCurrency&row_id=eq.{c.ViewerUid}&select=admin_id&limit=1&order=created_at.desc",
                c.ViewerToken);
            StringAssert.Contains(c.AdminUid, audit, "재화 지급이 감사 로그에 남아야 한다");

            // 차감도 같은 게이트를 지난다 — 잔액이 0 으로 돌아온다.
            var sub = await Send(HttpMethod.Post, $"{c.Server}/api/cs_tools_service/SubtractCurrency", c.AdminToken,
                $"{{\"playerId\":\"{c.ViewerUid}\",\"currencyId\":\"gold\",\"amount\":7,\"reason\":\"contract-sub\"}}");
            Assert.AreEqual(200, sub.status, $"game-admin 의 재화 차감은 통과해야 한다 — {sub.body}");
            var after = await Rest(c, $"currency?playerid=eq.{c.ViewerUid}&currencyid=eq.gold&select=amount", c.ViewerToken);
            StringAssert.Contains("\"amount\":0", after.Replace(" ", ""), "차감 후 잔액이 0 이어야 한다");
        }

        // ── 밴/해제 (#39) + GDPR 삭제 (#42) — 희생 계정 하나로 순서 실행 ──

        [Test]
        public async Task Ban_Blocks_And_GdprDelete_Erases_Victim()
        {
            var c = await Init();
            var (victimToken, victimUid) = await EnsureAccount(c, VictimEmail);

            // 밴 — 본인 ban-check 가 banned=true 를 돌려준다 (클라 CheckBan 이 보는 그 응답).
            var ban = await Send(HttpMethod.Post, $"{c.Server}/api/cs/system/SetBan", c.AdminToken,
                $"{{\"playerId\":\"{victimUid}\",\"banned\":true,\"reason\":\"contract-ban\",\"bannedUntil\":0}}");
            Assert.AreEqual(200, ban.status, $"game-admin 의 밴은 통과해야 한다 — {ban.body}");
            var check = await Send(HttpMethod.Get, $"{c.Server}/api/auth/ban-check/{victimUid}", victimToken);
            Assert.AreEqual(200, check.status);
            StringAssert.Contains("\"banned\":true", check.body.Replace(" ", ""), "밴 후 ban-check 가 banned=true 여야 한다");
            StringAssert.Contains("contract-ban", check.body, "밴 사유가 내려와야 한다");

            // 해제 — banned=false 로 복구된다.
            var unban = await Send(HttpMethod.Post, $"{c.Server}/api/cs/system/SetBan", c.AdminToken,
                $"{{\"playerId\":\"{victimUid}\",\"banned\":false,\"reason\":null,\"bannedUntil\":0}}");
            Assert.AreEqual(200, unban.status);
            var check2 = await Send(HttpMethod.Get, $"{c.Server}/api/auth/ban-check/{victimUid}", victimToken);
            StringAssert.Contains("\"banned\":false", check2.body.Replace(" ", ""), "해제 후 ban-check 가 banned=false 여야 한다");

            // GDPR 삭제 — game-admin 은 senior 게이트를 지난다. 계정·데이터가 사라진다.
            var del = await Send(HttpMethod.Post, $"{c.Server}/api/cs/system/GdprDelete", c.AdminToken,
                $"{{\"playerId\":\"{victimUid}\"}}");
            Assert.AreEqual(200, del.status, $"game-admin 의 GDPR 삭제는 통과해야 한다 — {del.body}");

            // 로그인 불가 — GoTrue 에 계정이 없다.
            var relogin = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/token?grant_type=password", null,
                $"{{\"email\":\"{VictimEmail}\",\"password\":\"{Password}\"}}", anonAuth: c.Anon);
            Assert.AreNotEqual(200, relogin.status, "삭제된 계정의 로그인은 실패해야 한다");

            // 조회 불가 — 플레이어 RPC 가 0행을 돌려준다 (#37 의 '없는 ID' 경로).
            var get = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/rpc/suparun_player_get", c.AdminToken,
                $"{{\"p_id\":\"{victimUid}\"}}", anonAuth: c.Anon);
            Assert.AreEqual("[]", get.body.Trim(), "삭제된 계정은 플레이어 조회에서 0행이어야 한다");

            // 감사 — 삭제가 cs:GdprDelete 로 남는다.
            var audit = await Rest(c,
                $"admin_audit_log?action=eq.cs:GdprDelete&row_id=eq.{victimUid}&select=admin_id&limit=1",
                c.AdminToken);
            StringAssert.Contains(c.AdminUid, audit, "GDPR 삭제가 감사 로그에 남아야 한다");
        }

        // ── 유틸 ──

        static async Task<(string token, string uid)> EnsureAccount(Ctx c, string email)
        {
            var body = $"{{\"email\":\"{email}\",\"password\":\"{Password}\"}}";
            var r = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/token?grant_type=password", null, body, anonAuth: c.Anon);
            if (r.status != 200)
                r = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/signup", null, body, anonAuth: c.Anon);

            var token = Regex.Match(r.body, "\"access_token\"\\s*:\\s*\"([^\"]+)\"");
            var uid = Regex.Match(r.body,
                "\"id\"\\s*:\\s*\"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\"");
            if (r.status != 200 || !token.Success || !uid.Success)
                Assert.Inconclusive($"계약 테스트 계정({email}) 준비 실패 — HTTP {r.status}");
            return (token.Groups[1].Value, uid.Groups[1].Value);
        }

        /// <summary>PostgREST GET — Supabase REST 를 두드린다 (서버가 아니라 표를 검증할 때).</summary>
        static async Task<string> Rest(Ctx c, string pathAndQuery, string bearer)
        {
            var r = await Send(HttpMethod.Get, $"{c.Url}/rest/v1/{pathAndQuery}", bearer, anonAuth: c.Anon);
            Assert.AreEqual(200, r.status, $"REST 조회 실패({pathAndQuery}) — {r.body}");
            return r.body;
        }

        /// <summary>HTTP 호출. anonAuth 를 주면 Supabase(apikey 헤더), 아니면 Cloud Run(Bearer만)이다.</summary>
        static async Task<(int status, string body)> Send(
            HttpMethod method, string url, string? bearer, string? json = null, string? anonAuth = null)
        {
            using var req = new HttpRequestMessage(method, url);
            if (anonAuth != null)
            {
                req.Headers.TryAddWithoutValidation("apikey", anonAuth);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (bearer ?? anonAuth));
            }
            else if (bearer != null)
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            }
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
