using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>진행 중인 로그인의 1회용 state. 콜백이 우리가 시작한 것인지 확인한다.</summary>
        static string _authState;

        /// <summary>이 브리지가 받을 OAuth 콜백 주소. Supabase 허용 목록에 있어야 한다.</summary>
        public static string CallbackUrl => $"http://127.0.0.1:{Port}/auth/callback";

        /// <summary>로그인을 시작하며 state 를 발급한다. 반환값을 redirect_to 에 실어 보낸다.</summary>
        public static string BeginAuth()
        {
            _authState = GeneratePassword();
            return $"{CallbackUrl}?state={_authState}";
        }

        /// <summary>
        /// 콜백 중간 페이지. fragment 를 query 로 옮겨 다시 부른다.
        /// state 는 이미 URL 에 있으므로 그대로 실어 보낸다.
        /// </summary>
        const string CallbackHtml = @"<!doctype html><html><head><meta charset=""utf-8"">
<title>SupaRun</title><style>
body{font-family:ui-monospace,Menlo,Consolas,monospace;background:#050807;color:#d8e4d8;
display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
.b{text-align:center}.g{color:#00ff66}
</style></head><body><div class=""b"" id=""m"">로그인 처리 중…</div><script>
(function(){
  var hash = location.hash.replace(/^#/, '');
  var state = new URLSearchParams(location.search).get('state') || '';
  var m = document.getElementById('m');
  if (!hash) { m.textContent = '토큰을 받지 못했습니다.'; return; }
  fetch('/auth/token?' + hash + '&state=' + encodeURIComponent(state))
    .then(function(r){ return r.json(); })
    .then(function(j){
      m.innerHTML = j && j.ok
        ? '<span class=""g"">로그인되었습니다.</span><br>이 창을 닫고 Unity 로 돌아가세요.'
        : '실패: ' + ((j && j.error) || '알 수 없음');
    })
    .catch(function(e){ m.textContent = '실패: ' + e; });
})();
</script></body></html>";

        static SupaRunBridge()
        {
            // 도메인 리로드마다 static 생성자가 다시 돈다 — 그래서 컴파일 후에도 알아서 되살아난다.
            EditorApplication.delayCall += Start;
        }

        // ── 수명 ──

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

            PublishEndpointAsync().Forget();
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
                // ── OAuth 콜백 ──
                // 브라우저가 리다이렉트로 도착하므로 **토큰을 요구할 수 없다.** 대신 로그인 시작 때
                // 발급한 state 를 확인한다.
                //
                // 두 번 오가는 이유: Supabase 는 토큰을 URL **fragment**(`#access_token=…`)로 주는데
                // fragment 는 서버로 전송되지 않는다. 그래서 1차로 HTML 을 주고, 그 안의 JS 가
                // fragment 를 읽어 2차 요청으로 넘긴다. 런타임 OAuthHandler 도 같은 방식이다.
                if (path == "/auth/callback")
                {
                    WriteHtml(res, CallbackHtml);
                    return;
                }

                if (path == "/auth/token")
                {
                    var state = req.QueryString["state"];
                    if (string.IsNullOrEmpty(_authState) || state != _authState)
                    {
                        Write(res, 400, Err("로그인 요청이 만료되었거나 일치하지 않습니다"));
                        return;
                    }
                    _authState = null;   // 1회용

                    var access = req.QueryString["access_token"];
                    var refresh = req.QueryString["refresh_token"];
                    if (string.IsNullOrEmpty(access))
                    {
                        Write(res, 400, Err("토큰이 없습니다"));
                        return;
                    }

                    SupaRunEditorAuth.StoreTokens(access, refresh);
                    Write(res, 200, new JObject { ["ok"] = true });
                    return;
                }

                // ping 만 토큰 없이 답한다 — 어드민이 "Unity 가 켜져 있나" 를 물어보는 통로다.
                if (path == "/ping")
                {
                    var s = SupaRunSettings.Instance;
                    Write(res, 200, new JObject
                    {
                        ["ok"] = true,
                        ["unity"] = Application.unityVersion,
                        ["editor_env"] = s.EditorEnvironment,
                        // 토큰 자체는 주지 않는다. 어드민은 DB 에서 읽는다.
                        ["needs_token"] = true,
                    });
                    return;
                }

                if (req.Headers["x-bridge-token"] != Token)
                {
                    Write(res, 401, Err("토큰이 올바르지 않습니다"));
                    return;
                }

                // 프로젝트·리전은 `suparun-admin` Edge Function 으로 옮겼다.
                // 브리지를 거치면 Unity 가 켜져 있어야만 되는데, 그건 기획자가 웹만 여는 경우를 막는다.
                // 여기 남는 것은 **로컬에서만 할 수 있는 일**뿐이다.
                Write(res, 404, Err($"알 수 없는 경로: {path}"));
                return;
            }
            catch (Exception ex)
            {
                // 브리지가 죽으면 어드민은 이유를 모른다. 예외도 응답으로 돌려준다.
                try { Write(res, 500, Err(ex.Message)); } catch { }
            }
        }

        // ── 접속 정보 게시 ──

        /// <summary>
        /// 포트·토큰을 `suparun_meta.bridge` 에 적어 어드민이 찾아올 수 있게 한다.
        /// 관리자만 읽는 자리라 토큰을 여기 두는 것이 안전하다.
        ///
        /// 값이 그대로면 쓰지 않는다 — 컴파일마다 DB 를 두드릴 이유가 없다.
        /// </summary>
        static async UniTaskVoid PublishEndpointAsync()
        {
            var settings = SupaRunSettings.Instance;
            var env = settings.Current;
            var id = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return;

            var stamp = $"{Port}:{Token}";
            var prefKey = EditorPrefUtils.ProjectPrefix + "BridgePublished";
            if (EditorPrefs.GetString(prefKey, "") == stamp) return;

            var payload = new JObject
            {
                ["port"] = Port,
                ["token"] = Token,
                ["unity"] = Application.unityVersion,
            }.ToString(Formatting.None);

            var sql =
                "INSERT INTO suparun_meta(key, value, updated_at) " +
                $"VALUES ('bridge', $bridge${payload}$bridge$::jsonb, now()) " +
                "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now();";

            var r = await SupabaseManagementApi.RunQuery(id, token, sql);
            if (r.Ok) EditorPrefs.SetString(prefKey, stamp);
            else r.LogIfFailed("브리지 접속 정보 게시");
        }

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

        static JObject Err(string message, string hint = null)
        {
            var o = new JObject { ["error"] = message };
            if (!string.IsNullOrEmpty(hint)) o["hint"] = hint;
            return o;
        }

        static void WriteHtml(HttpListenerResponse res, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            res.StatusCode = 200;
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        static void Write(HttpListenerResponse res, int status, JObject body)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }
    }
}
