using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 어드민(브라우저)이 로컬 Unity 를 통해 PAT 가 필요한 일을 시키는 통로.
    ///
    /// **왜 있는가**: 프로젝트 생성·삭제·복구는 Management API + PAT 를 요구하는데, PAT 는
    /// 로컬에만 두기로 했다(어드민 계정이 털려도 Supabase 계정 전체는 안 넘어가게).
    /// 그래서 어드민은 명령만 보내고 **PAT 를 쥔 쪽은 여기**다.
    ///
    /// 부수 효과가 곧 안전장치다 — **Unity 가 꺼져 있으면 그 기능이 자동으로 잠긴다.**
    ///
    /// 브라우저가 HTTPS 페이지에서 `http://127.0.0.1` 을 부를 수 있는지는 실측으로 확인했다
    /// (Mixed Content · CORS · Private Network Access 전부 통과). 그래서 응답에 그 헤더 셋을 반드시 붙인다.
    /// </summary>
    [InitializeOnLoad]
    public static class SupaRunBridge
    {
        /// <summary>기본 포트. 쓰이고 있으면 이 뒤로 몇 개를 더 시도한다.</summary>
        const int BasePort = 47821;
        const int PortTries = 10;

        const string TokenPrefKey = "BridgeToken";

        static HttpListener _listener;
        static Thread _accept;
        static readonly ConcurrentQueue<HttpListenerContext> _pending = new();

        public static int Port { get; private set; }
        public static string Token { get; private set; }
        public static bool Running => _listener?.IsListening == true;

        static SupaRunBridge()
        {
            // 도메인 리로드마다 static 생성자가 다시 돈다 — 그래서 컴파일 후에도 알아서 되살아난다.
            EditorApplication.delayCall += Start;
        }

        // ── 수명 ──

        /// <summary>
        /// 꺼져 있으면 켠다. 어드민을 이 브리지가 서빙하므로 꺼져 있으면 **어드민 자체가 안 열린다.**
        /// `[InitializeOnLoad]` 의 delayCall 이 도메인 리로드 상황에 따라 걸리지 않는 것을 실측으로
        /// 확인했다(강제 재컴파일 뒤 `Running=False`). 여는 쪽에서 한 번 더 보장한다.
        /// </summary>
        public static void EnsureRunning()
        {
            if (!Running) Start();
        }

        static void Start()
        {
            if (Running) return;

            Token = LoadOrCreateToken();

            for (var i = 0; i < PortTries; i++)
            {
                var port = BasePort + i;
                try
                {
                    var l = new HttpListener();
                    // 127.0.0.1 만 연다. `+` 나 `*` 로 열면 같은 네트워크의 다른 기기도 닿는다.
                    l.Prefixes.Add($"http://127.0.0.1:{port}/");
                    l.Start();
                    _listener = l;
                    Port = port;
                    break;
                }
                catch (HttpListenerException)
                {
                    // 포트가 이미 쓰인다 — 다음 것을 시도한다.
                }
            }

            if (_listener == null)
            {
                Debug.LogWarning(
                    $"[SupaRun:Bridge] {BasePort}~{BasePort + PortTries - 1} 이 모두 사용 중이라 브리지를 열지 못했습니다. " +
                    "어드민의 프로젝트 관리 버튼이 잠깁니다.");
                return;
            }

            // GetContext 는 블로킹이라 별도 스레드에서 받는다.
            // **다만 여기서 Unity API 를 만지면 안 된다** — 큐에 넣고 메인 스레드에서 처리한다.
            _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "SupaRunBridge" };
            _accept.Start();

            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        static void Stop()
        {
            EditorApplication.update -= Pump;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;

            try { _listener?.Stop(); } catch { /* 이미 닫혔으면 무시 */ }
            try { _listener?.Close(); } catch { }
            _listener = null;

            // 스레드는 IsBackground 라 알아서 죽는다. Abort 는 쓰지 않는다.
            _accept = null;
        }

        static void AcceptLoop()
        {
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }   // 리스너가 닫히면 여기로 온다 — 정상 종료다
                _pending.Enqueue(ctx);
            }
        }

        /// <summary>메인 스레드. 여기서만 Unity API·설정·UnityWebRequest 를 만진다.</summary>
        static void Pump()
        {
            while (_pending.TryDequeue(out var ctx))
                Handle(ctx).Forget();
        }

        // ── 라우팅 ──

        static async UniTaskVoid Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            // 브라우저가 요구하는 셋. 하나라도 빠지면 fetch 가 조용히 실패한다.
            var origin = req.Headers["Origin"];
            res.AddHeader("Access-Control-Allow-Origin", string.IsNullOrEmpty(origin) ? "*" : origin);
            res.AddHeader("Access-Control-Allow-Methods", "GET,POST,DELETE,OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "content-type,x-bridge-token");
            res.AddHeader("Access-Control-Allow-Private-Network", "true");
            res.AddHeader("Cache-Control", "no-store");

            if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }

            var path = req.Url.AbsolutePath.TrimEnd('/');
            try
            {
                // ping 만 토큰 없이 답한다 — 어드민이 "Unity 가 켜져 있나" 를 물어보는 통로다.
                if (path == "/ping")
                {
                    var s = SupaRunSettings.Instance;
                    Write(res, 200, new JObject
                    {
                        ["ok"] = true,
                        ["unity"] = Application.unityVersion,
                        ["editor_env"] = s.EditorEnvironment,
                        // 토큰은 여기서 주지 않는다. 어드민은 서빙될 때 `window.__SUPARUN_BRIDGE` 로 받는다
                        // (InjectEnv 참조) — 같은 출처가 아니면 애초에 손에 넣을 수 없다.
                        ["needs_token"] = true,
                    });
                    return;
                }

                // ── 어드민 정적 서빙 ──
                // 토큰 검사 **앞**에 둔다. 브라우저가 <script src> 로 가져가므로 헤더를 붙일 수 없다.
                // 127.0.0.1 만 열려 있고 내보내는 것은 공개값(anon key)뿐이라 열어도 잃을 것이 없다.
                var raw = req.Url.AbsolutePath;
                if (raw == "/admin")
                {
                    // 끝의 `/` 가 없으면 index.html 의 `./assets/…` 가 루트로 풀려 404 가 난다.
                    res.Redirect("/admin/");
                    res.Close();
                    return;
                }
                if (raw.StartsWith("/admin/"))
                {
                    ServeAdmin(res, raw.Substring("/admin/".Length));
                    return;
                }

                if (req.Headers["x-bridge-token"] != Token)
                {
                    Write(res, 401, Err("토큰이 올바르지 않습니다"));
                    return;
                }

                // 같은 출처만 받는다. 어드민을 이 브리지가 서빙하므로 다른 오리진이 부를 이유가 없다 —
                // 토큰이 새더라도 다른 사이트가 PAT 를 쓰지 못하게 막는 두 번째 벽이다.
                if (!string.IsNullOrEmpty(origin) && origin != $"http://127.0.0.1:{Port}")
                {
                    Write(res, 403, Err($"허용되지 않은 출처: {origin}"));
                    return;
                }

                // 배포 체크리스트는 라우트가 많아 별도 파일로 뺐다.
                if (BridgeDeployRoutes.Matches(path))
                {
                    await BridgeDeployRoutes.Handle(req, res, path);
                    return;
                }

                // 실행 계열(스키마 반영·배포·승격·환경). 준비(위)와 나눠 둔 이유는
                // 파일 크기가 아니라 성격이다 — 이쪽은 되돌리기 어려운 일을 한다.
                if (BridgeOpsRoutes.Matches(path))
                {
                    await BridgeOpsRoutes.Handle(req, res, path);
                    return;
                }

                await HandlePat(req, res, path);
                return;
            }
            catch (Exception ex)
            {
                // 브리지가 죽으면 어드민은 이유를 모른다. 예외도 응답으로 돌려준다.
                try { Write(res, 500, Err(ex.Message)); } catch { }
            }
        }

        // ── PAT 대행 ──

        /// <summary>
        /// 브라우저가 못 하는 Management API 호출을 대신한다.
        ///
        /// 브라우저가 직접 못 부르는 이유가 둘이다 — `api.supabase.com` 의 CORS 가 `https://supabase.com`
        /// 오리진만 허용하고, PAT 는 계정 마스터키라 내려보낼 수 없다.
        ///
        /// 한때 이 자리를 `suparun-admin` Edge Function 이 맡았다. 근거는 "Unity 가 꺼져 있어도
        /// 웹만 열어 보는 사람" 이었는데, **어드민 자체를 이 브리지가 서빙하게 되면서 그 전제가 사라졌다.**
        /// </summary>
        static async UniTask HandlePat(HttpListenerRequest req, HttpListenerResponse res, string path)
        {
            var env = SupaRunSettings.Instance.Current;
            var pat = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(pat))
            {
                Write(res, 409, Err("Access Token 이 없습니다.",
                    "대시보드 > Settings > Supabase 에서 PAT 를 입력하세요."));
                return;
            }

            var m = req.HttpMethod;

            // ── 프로젝트 ──

            if (path == "/projects" && m == "GET")
            {
                var r = await SupabaseManagementApi.ListProjects(pat);
                if (!r.Ok) { Fail(res, r); return; }

                var arr = new JArray();
                foreach (var p in r.Value)
                    arr.Add(new JObject
                    {
                        ["ref"] = p.id,
                        ["name"] = p.name,
                        ["status"] = p.status,
                        ["region"] = p.region,
                        ["url"] = $"https://{p.id}.supabase.co",
                    });
                Write(res, 200, new JObject { ["projects"] = arr });
                return;
            }

            if (path == "/projects" && m == "POST")
            {
                var body = ReadBody(req);
                var name = ((string)body["name"] ?? "").Trim();
                if (name.Length == 0) { Write(res, 400, Err("이름이 필요합니다.")); return; }

                var slug = await FirstOrgSlug(pat);
                if (slug == null) { Write(res, 502, Err("조직을 찾지 못했습니다.")); return; }

                var created = await SupabaseManagementApi.CreateProject(pat,
                    new SupabaseManagementApi.CreateProjectRequest
                    {
                        name = name,
                        organizationSlug = slug,
                        dbPass = GeneratePassword(),
                        region = (string)body["region"] ?? "",
                        plan = (string)body["plan"] ?? "",
                    });
                if (!created.Ok) { Fail(res, created); return; }

                // 만든 직후는 아직 COMING_UP 이다. 어드민이 상태를 폴링한다.
                Write(res, 200, new JObject
                {
                    ["ref"] = created.Value.id,
                    ["name"] = created.Value.name,
                    ["status"] = created.Value.status,
                });
                return;
            }

            if (path == "/projects" && m == "DELETE")
            {
                var target = req.QueryString["ref"];
                if (string.IsNullOrEmpty(target)) { Write(res, 400, Err("ref 가 필요합니다.")); return; }

                var r = await SupabaseManagementApi.DeleteProject(target, pat);
                if (!r.Ok) { Fail(res, r); return; }
                Write(res, 200, new JObject { ["ok"] = true });
                return;
            }

            // ── 생성 보조 ──

            if (path == "/regions" && m == "GET")
            {
                var slug = await FirstOrgSlug(pat);
                if (slug == null) { Write(res, 502, Err("조직을 찾지 못했습니다.")); return; }

                var r = await SupabaseManagementApi.AvailableRegions(pat, slug);
                if (!r.Ok) { Fail(res, r); return; }

                var arr = new JArray();
                foreach (var g in r.Value)
                    arr.Add(new JObject { ["code"] = g.code, ["label"] = g.Label });
                Write(res, 200, new JObject { ["regions"] = arr });
                return;
            }

            if (path == "/api-keys" && m == "GET")
            {
                var target = req.QueryString["ref"];
                if (string.IsNullOrEmpty(target)) { Write(res, 400, Err("ref 가 필요합니다.")); return; }

                var r = await SupabaseManagementApi.GetAnonKey(target, pat);
                if (!r.Ok) { Fail(res, r); return; }
                Write(res, 200, new JObject { ["anonKey"] = r.Value });
                return;
            }

            // ── 사람 로그인 보조 (ADR-0009, #23) ──

            // 로그인한 신원을 이 환경의 admin_user 에 admin 으로 등록한다.
            // 여기까지 온 사람은 이미 PAT 대행 전권을 쥐고 있어 승인을 따로 묻지 않는다 —
            // 빈 표(첫 관리자)의 매듭을 PAT 가 끊는 자리다. 신원은 토큰을 GoTrue 에 되물어 확정한다.
            if (path == "/auth/claim-admin" && m == "POST")
            {
                var access = (string)ReadBody(req)["access_token"];
                if (string.IsNullOrEmpty(access)) { Write(res, 400, Err("access_token 이 필요합니다.")); return; }

                var claim = await SupaRunAdminClaim.ClaimAsync(env, access);
                if (!claim.Ok) { Fail(res, claim); return; }
                Write(res, 200, new JObject
                {
                    ["userId"] = claim.Value.userId,
                    ["email"] = claim.Value.email,
                });
                return;
            }

            // 웹 로그인 프로바이더 라우트(/auth-config)는 없다 — 어드민 로그인은 이메일+비밀번호
            // 전용이라(ADR-0009 — 매직링크·OAuth 기각) 웹 OAuth 를 켜고 끌 자리가 없다.

            Write(res, 404, Err($"알 수 없는 경로: {path}"));
        }

        /// <summary>계정의 첫 조직 slug. 프로젝트 생성과 리전 조회가 요구한다.</summary>
        static async UniTask<string> FirstOrgSlug(string pat)
        {
            var r = await SupabaseManagementApi.ListOrganizations(pat);
            return r.Ok && r.Value.Length > 0 ? r.Value[0].slug : null;
        }


        /// <summary>Management API 실패를 그대로 전달한다. 원인이 어드민 화면까지 도달해야 한다.</summary>
        static void Fail<T>(HttpListenerResponse res, SupabaseResult<T> r) =>
            Write(res, 502, Err(r.Message, r.Hint));

        // ── 유틸 ──

        static string LoadOrCreateToken()
        {
            var key = EditorPrefUtils.ProjectPrefix + TokenPrefKey;
            var t = EditorPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(t)) return t;
            t = GeneratePassword();
            EditorPrefs.SetString(key, t);
            return t;
        }

        static string GeneratePassword()
        {
            const string chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        // 응답 헬퍼는 BridgeIo 로 옮겼다 — 배포 라우트(BridgeDeployRoutes)와 공용이다.
        static JObject Err(string message, string hint = null) => BridgeIo.Err(message, hint);

        static void Write(HttpListenerResponse res, int status, JObject body) =>
            BridgeIo.Write(res, status, body);

        static JObject ReadBody(HttpListenerRequest req) => BridgeIo.ReadBody(req);

        static void WriteHtml(HttpListenerResponse res, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            res.StatusCode = 200;
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }


        // ── 어드민 정적 서빙 ──

        /// <summary>
        /// 어드민 dist 를 여기서 내보낸다.
        ///
        /// **왜 Supabase 가 아닌가**: `*.supabase.co` 는 HTML 을 `text/plain` 으로 강제 다운그레이드한다
        /// (Storage · Edge Function 둘 다, 확장자·명시적 헤더와 무관 — 실측 확인). 브라우저가 렌더링하지 않는다.
        ///
        /// **왜 Cloud Run 이 아닌가**: 어드민에서 배포 설정을 입력하려는데 그 어드민이 배포돼야 열리는
        /// 순환이 생긴다. 로컬은 배포와 무관하게 열려 그 고리를 끊는다.
        /// </summary>
        static void ServeAdmin(HttpListenerResponse res, string rel)
        {
            var root = AdminDistRoot();
            if (root == null)
            {
                Write(res, 503, Err("어드민 빌드 산출물(AdminTemplate~/dist)이 없습니다",
                    "AdminTemplate~ 에서 `npm ci && npm run build` 를 실행하세요."));
                return;
            }

            if (string.IsNullOrEmpty(rel)) rel = "index.html";

            // `..` 로 패키지 밖 파일을 읽어가지 못하게 막는다. 로컬이라도 브라우저가 부르는 경로다.
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(full))
            {
                Write(res, 404, Err($"없는 파일: {rel}"));
                return;
            }

            if (Path.GetFileName(full).Equals("index.html", StringComparison.OrdinalIgnoreCase))
                WriteHtml(res, InjectEnv(File.ReadAllText(full)));
            else
                WriteFile(res, full);
        }

        /// <summary>
        /// 어드민 dist 의 **물리** 경로. `file:` 패키지는 `Packages/` 아래에 실재하지 않으므로
        /// AssetDatabase 가상 경로로는 File IO 가 되지 않는다. `resolvedPath` 가 유일하게 옳다.
        /// </summary>
        static string AdminDistRoot()
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SupaRunBridge).Assembly);
            if (pkg == null) return null;
            var dist = Path.GetFullPath(Path.Combine(pkg.resolvedPath, "Templates/AdminTemplate~/dist"));
            return Directory.Exists(dist) ? dist : null;
        }

        /// <summary>
        /// 접속 정보를 **런타임에** 꽂는다. 배포할 때 치환하던 것을 여기로 옮기면
        /// 환경을 바꿔도 다시 빌드할 일이 없다 — 새로고침이면 된다.
        /// </summary>
        static string InjectEnv(string html)
        {
            var s = SupaRunSettings.Instance;

            // 브리지 접속 정보는 **여기서만** 준다.
            // 예전에는 `suparun_meta.bridge` 에 적어 어드민이 DB 에서 읽어 갔는데, 그 표는
            // `public_read` 라 anon key 만 있으면 토큰이 그대로 읽혔다. anon key 는 게임 빌드에서
            // 뽑히므로 사실상 공개다 — 아래 PAT 대행 라우트가 붙은 지금 그건 곧 계정 전권이다.
            // 같은 출처로 내려주면 그 경로 자체가 사라진다.
            //
            // 세션은 꽂지 않는다 — 사람 로그인(이메일+비밀번호)이 신원이다 (ADR-0009, #23).
            // 한때 기계 계정 세션(`__SUPARUN_SESSION`)을 여기서 만들어 줬는데, 그 논거("로컬
            // 전용이라 로그인이 보안을 더하지 않는다")는 원격 접근자가 생기면 무너진다.
            var bridge = "<script>window.__SUPARUN_BRIDGE={port:" + Port + ",token:\"" + Token + "\"};</script>";

            return html
                .Replace("</head>", bridge + "</head>")
                .Replace("{{SUPABASE_URL}}", s.supabaseUrl ?? "")
                .Replace("{{SUPABASE_ANON_KEY}}", s.SupabaseAnonKey ?? "");
        }

        static void WriteFile(HttpListenerResponse res, string full)
        {
            var bytes = File.ReadAllBytes(full);
            res.StatusCode = 200;
            res.ContentType = MimeOf(Path.GetExtension(full));
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        static string MimeOf(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".js":    return "text/javascript; charset=utf-8";
                case ".css":   return "text/css; charset=utf-8";
                case ".json":
                case ".map":   return "application/json; charset=utf-8";
                case ".svg":   return "image/svg+xml";
                case ".png":   return "image/png";
                case ".woff2": return "font/woff2";
                default:       return "application/octet-stream";
            }
        }
    }
}
