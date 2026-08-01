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
    /// PostgREST 계약 테스트 — 롤별 접근 매트릭스 (#24, ADR-0009 결정 5).
    ///
    /// **실제 편집 환경(dev) DB 에 붙는 통합 테스트다.** RLS 는 코드에 없고 DB 에만 있어서
    /// 목으로는 계약을 증명할 수 없다. 편집 환경 설정(URL·anon key·PAT)이 없으면 Ignore 로
    /// 빠지므로 순수 유닛 러너를 막지 않는다.
    ///
    /// 검증 대상 3종:
    ///   anon   — 공개 config 읽기만. 롤 표는 비어 보이고, 어떤 INSERT 도 403
    ///   viewer — operator_read 로 열람 가능, 쓰기(INSERT)는 403 (is_admin=game-admin 이므로)
    ///   admin  — 롤 부여/회수(쓰기)가 실제로 통과
    ///
    /// 실데이터는 건드리지 않는다 — 쓰기는 전용 테스트 계정의 롤 행뿐이다.
    /// 계정(contract-*@suparun.test)과 롤은 러너가 멱등하게 준비한다.
    /// </summary>
    class RoleAccessContractTests
    {
        const string ViewerEmail = "contract-viewer@suparun.test";
        const string AdminEmail = "contract-admin@suparun.test";
        // dev 전용 테스트 계정 비밀번호. prod 에는 애초에 계정을 만들지 않는다(아래 가드).
        const string Password = "spr-contract-4Qx7";

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        class Ctx
        {
            public string Url = "", Anon = "", Pat = "", Ref = "";
            public string ViewerToken = "", ViewerUid = "", AdminToken = "", AdminUid = "";
        }

        static Task<Ctx>? _init;
        static Task<Ctx> Init() => _init ??= InitAsync();

        /// <summary>
        /// 환경 확인 → 계정 로그인/가입 → PAT 로 롤을 멱등 세팅.
        /// NUnit 의 OneTimeSetUp 은 async 를 못 받는 버전이 있어 지연 초기화로 공유한다.
        /// </summary>
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
                Assert.Ignore("prod 환경에는 계약 테스트 계정을 만들지 않습니다.");

            (c.ViewerToken, c.ViewerUid) = await EnsureAccount(c, ViewerEmail);
            (c.AdminToken, c.AdminUid) = await EnsureAccount(c, AdminEmail);

            // 롤 멱등 세팅. viewer 에서 game-admin 을 걷어내는 이유: 어쩌다 로컬 어드민에
            // 이 계정으로 로그인하면 claim 이 승격시킨다 — 그 잔재가 매트릭스를 거짓 통과시킨다.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await PatQuery(c,
                $"DELETE FROM admin_user_role WHERE user_id = '{c.ViewerUid}' AND role <> 'game-viewer'; " +
                $"INSERT INTO admin_user_role (user_id, role, granted_at, granted_by) VALUES " +
                $"('{c.ViewerUid}', 'game-viewer', {now}, 'contract-test'), " +
                $"('{c.AdminUid}', 'game-admin', {now}, 'contract-test') " +
                "ON CONFLICT (user_id, role) DO NOTHING;");
            return c;
        }

        /// <summary>로그인, 안 되면 가입(셋업이 autoconfirm 을 켜 두므로 세션이 바로 온다).</summary>
        static async Task<(string token, string uid)> EnsureAccount(Ctx c, string email)
        {
            var body = $"{{\"email\":\"{email}\",\"password\":\"{Password}\"}}";
            var r = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/token?grant_type=password", c.Anon, null, body);
            if (r.status != 200)
                r = await Send(HttpMethod.Post, $"{c.Url}/auth/v1/signup", c.Anon, null, body);

            var token = ExtractAccessToken(r.body);
            var uid = ExtractUid(r.body);
            if (r.status != 200 || token == null || uid == null)
                Assert.Inconclusive($"계약 테스트 계정({email}) 준비 실패 — HTTP {r.status}. " +
                                    "비밀번호가 바뀌었으면 계정을 지우고 다시 돌리세요.");
            return (token!, uid!);
        }

        // ── anon ──

        [Test]
        public async Task Anon_Reads_Public_Config()
        {
            var c = await Init();
            var r = await Send(HttpMethod.Get, $"{c.Url}/rest/v1/suparun_meta?select=key&limit=1", c.Anon, null);
            Assert.AreEqual(200, r.status, "public_read 는 anon 에게 열려 있어야 한다");
        }

        [Test]
        public async Task Anon_Sees_No_Roles()
        {
            var c = await Init();
            var r = await Send(HttpMethod.Get, $"{c.Url}/rest/v1/admin_user_role?select=role", c.Anon, null);
            Assert.AreEqual(200, r.status);
            Assert.AreEqual("[]", r.body.Trim(), "anon 에게 롤 표는 빈 것으로 보여야 한다 (RLS 필터)");
        }

        [Test]
        public async Task Anon_Cannot_Insert_Roles()
        {
            var c = await Init();
            var r = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/admin_user_role", c.Anon, null,
                "{\"user_id\":\"intruder\",\"role\":\"game-admin\",\"granted_at\":0}");
            Assert.AreEqual(403, r.status, "anon 의 롤 self-grant 는 RLS 가 거부해야 한다");
        }

        // ── game-viewer ──

        [Test]
        public async Task Viewer_Reads_Own_Roles_And_Operator_Tables()
        {
            var c = await Init();
            var own = await Send(HttpMethod.Get,
                $"{c.Url}/rest/v1/admin_user_role?select=role&user_id=eq.{c.ViewerUid}", c.Anon, c.ViewerToken);
            Assert.AreEqual(200, own.status);
            StringAssert.Contains("game-viewer", own.body, "viewer 는 자기 롤을 읽을 수 있어야 한다");

            // operator_read — 롤 보유자의 열람 통로 (audit·server_log·env 등 공통 형태)
            var audit = await Send(HttpMethod.Get,
                $"{c.Url}/rest/v1/admin_audit_log?select=id&limit=1", c.Anon, c.ViewerToken);
            Assert.AreEqual(200, audit.status, "viewer 는 감사 로그를 읽을 수 있어야 한다 (operator_read)");
        }

        [Test]
        public async Task Viewer_Cannot_Write()
        {
            var c = await Init();
            // 쓰기의 대표: 롤 self-grant. is_admin() = game-admin 이라 viewer 는 403 이어야 한다.
            var grant = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/admin_user_role", c.Anon, c.ViewerToken,
                $"{{\"user_id\":\"{c.ViewerUid}\",\"role\":\"game-admin\",\"granted_at\":0}}");
            Assert.AreEqual(403, grant.status, "viewer 의 롤 self-grant 는 RLS 가 거부해야 한다");

            // 관리 표(suparun_env — admin_all + operator_read)에도 쓰기는 거부돼야 한다.
            // NOT NULL 을 전부 채운 유효한 행이어야 거부 사유가 RLS 하나로 좁혀진다.
            var env = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/suparun_env", c.Anon, c.ViewerToken,
                "{\"key\":\"contract-test-should-not-exist\",\"value\":\"x\",\"updated_at\":0}");
            Assert.AreEqual(403, env.status, "viewer 의 관리 표 INSERT 는 RLS 가 거부해야 한다");
        }

        // ── game-admin ──

        [Test]
        public async Task Admin_Can_Grant_And_Revoke()
        {
            var c = await Init();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 이전 실행이 남긴 부여가 있으면 걷어낸다 — 멱등.
            await Send(HttpMethod.Delete,
                $"{c.Url}/rest/v1/admin_user_role?user_id=eq.{c.ViewerUid}&role=eq.cs-agent", c.Anon, c.AdminToken);

            var grant = await Send(HttpMethod.Post, $"{c.Url}/rest/v1/admin_user_role", c.Anon, c.AdminToken,
                $"{{\"user_id\":\"{c.ViewerUid}\",\"role\":\"cs-agent\",\"granted_at\":{now},\"granted_by\":\"contract-test\"}}");
            Assert.AreEqual(201, grant.status, "game-admin 의 롤 부여는 통과해야 한다");

            var revoke = await Send(HttpMethod.Delete,
                $"{c.Url}/rest/v1/admin_user_role?user_id=eq.{c.ViewerUid}&role=eq.cs-agent", c.Anon, c.AdminToken);
            Assert.AreEqual(204, revoke.status, "game-admin 의 롤 회수는 통과해야 한다");
        }

        // ── HTTP 유틸 (패키지 HTTP 스택에 의존하지 않는다 — 계약은 바깥에서 두드려야 계약이다) ──

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

        /// <summary>Management API 로 SQL 실행 (PAT). 테스트 준비 전용 — 검증 경로에는 안 쓴다.</summary>
        static async Task PatQuery(Ctx c, string sql)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.supabase.com/v1/projects/{c.Ref}/database/query");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + c.Pat);
            req.Content = new StringContent(
                "{\"query\":" + ToJsonString(sql) + "}", Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                Assert.Inconclusive($"PAT 준비 쿼리 실패 — HTTP {(int)res.StatusCode}");
        }

        // Newtonsoft 를 참조하지 않으려는 최소 구현들 (asmdef 의 precompiledReferences 유지)

        static string ToJsonString(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        static string? ExtractAccessToken(string body)
        {
            var m = Regex.Match(body, "\"access_token\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string? ExtractUid(string body)
        {
            // GoTrue 응답의 user.id — UUID 형태만 집는다.
            var m = Regex.Match(body,
                "\"id\"\\s*:\\s*\"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
