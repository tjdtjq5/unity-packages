using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// **기계 계정** — 로컬 어드민의 자동 로그인 신원.
    ///
    /// 로컬 어드민에서 사람 로그인은 보안을 더하지 않는다. 페이지를 연 사람은 이미 브리지
    /// 토큰(=PAT 대행 전권)을 쥐고 있어서, 로그인 화면은 전권자에게 또 세운 문이었다.
    /// 그렇다고 화면만 지우면 안 된다 — 데이터 조작은 PostgREST + RLS(`is_admin()`)를 타므로
    /// **누군가의 정식 세션**은 여전히 필요하다.
    ///
    /// 그래서 머신마다 계정을 만들어 브리지가 대신 로그인한다. jwt_secret 으로 토큰을 직접
    /// 찍지 않는 이유: Supabase 신규 프로젝트는 비대칭 서명키 체계로 넘어가는 중이라 서명
    /// 비밀을 내주지 않는 날이 온다. 정식 발급 창구(Supabase Auth)에 줄을 서면 체계와 무관하다.
    ///
    /// 신원은 `{OS계정}.{머신명}@suparun.local` — 입력 없이 감사로그(updated_by)에서 사람이
    /// 읽힌다. 비밀번호는 이 머신의 EditorPrefs 에만 있다. 계정은 환경(프로젝트)마다 따로
    /// 만들어진다 — `auth.users` 가 프로젝트별이기 때문이고, 같은 이메일·비밀번호를 쓴다.
    /// </summary>
    public static class SupaRunMachineAccount
    {
        const string PasswordKey = "machine_account_pw";

        /// <summary>이 머신의 신원. 감사로그·admin_user 에 그대로 남는다.</summary>
        public static string Email
        {
            get
            {
                var user = Sanitize(Environment.UserName);
                var machine = Sanitize(Environment.MachineName);
                return $"{user}.{machine}@suparun.local";
            }
        }

        /// <summary>영문·숫자만 남긴다. 한글 계정명처럼 전부 걸러지면 'editor' 로 대체.</summary>
        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant())
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "editor";
        }

        static string GetOrCreatePassword()
        {
            var pw = SupaRunSecretPrefs.Get(PasswordKey, "", "");
            if (!string.IsNullOrEmpty(pw)) return pw;
            pw = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
            SupaRunSecretPrefs.Set(PasswordKey, "", pw);
            return pw;
        }

        // ── 세션 캐시 ──
        // 서빙마다 로그인하면 리로드가 느려진다. 만료 5분 전까지는 같은 세션을 다시 준다.
        // 도메인 리로드로 비워져도 다음 요청이 새로 로그인하므로 상태 손실이 없다.

        static readonly Dictionary<string, (string access, string refresh, long expiresAt)> _sessions = new();
        static readonly HashSet<string> _registered = new();

        /// <summary>
        /// 이 환경의 기계 계정 세션을 보장한다. 성공 시 (access, refresh), 실패 시 null.
        /// 로그인 → (없으면) 가입 → (autoconfirm 문제면) 켜고 재가입 →
        /// (계정은 있는데 비밀번호가 다르면 — EditorPrefs 초기화 등) PAT 로 리셋 후 재로그인.
        /// </summary>
        public static async UniTask<(string access, string refresh)?> EnsureSessionAsync(
            SupaRunSettings.EnvironmentData env)
        {
            var url = (env?.supabaseUrl ?? "").TrimEnd('/');
            var anon = env?.supabaseAnonKey ?? "";
            var pid = SupaRunSettings.ProjectIdOf(url);
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(anon)) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_sessions.TryGetValue(pid, out var cached) && cached.expiresAt - now > 300)
                return (cached.access, cached.refresh);

            var pw = GetOrCreatePassword();

            var session = await PasswordGrant(url, anon, pw);
            if (session == null)
            {
                session = await SignUp(url, anon, pw);
                if (session == null)
                {
                    // 새 프로젝트는 autoconfirm 이 꺼져 있어 가입이 확인 메일 경로로 빠진다 — 켜고 재시도.
                    await EnsureAutoConfirm(env);
                    session = await SignUp(url, anon, pw);
                }
                if (session == null)
                {
                    // 계정은 있는데 저장된 비밀번호와 다르다. PAT 가 비밀번호의 진실을 되찾아 준다.
                    await ResetPasswordViaPat(env, pw);
                    session = await PasswordGrant(url, anon, pw);
                }
            }

            if (session == null)
            {
                Debug.LogWarning($"[SupaRun:Auth] 기계 계정 로그인 실패 — {Email} @ {pid}. Console 위 로그를 확인하세요.");
                return null;
            }

            _sessions[pid] = (session.Value.access, session.Value.refresh, now + session.Value.expiresIn);
            await EnsureAdminRow(env, pid, session.Value.userId);
            return (session.Value.access, session.Value.refresh);
        }

        // ── GoTrue 호출 ──

        static async UniTask<(string access, string refresh, long expiresIn, string userId)?> PasswordGrant(
            string url, string anon, string pw)
        {
            var r = await PostJson($"{url}/auth/v1/token?grant_type=password", anon,
                new JObject { ["email"] = Email, ["password"] = pw });
            return ParseSession(r);
        }

        static async UniTask<(string access, string refresh, long expiresIn, string userId)?> SignUp(
            string url, string anon, string pw)
        {
            var r = await PostJson($"{url}/auth/v1/signup", anon,
                new JObject { ["email"] = Email, ["password"] = pw });
            // autoconfirm 이 꺼져 있으면 200 이어도 세션 없이 user 만 온다 — 실패로 취급해 위에서 켜게 한다.
            return ParseSession(r);
        }

        static (string access, string refresh, long expiresIn, string userId)? ParseSession(JObject r)
        {
            var access = (string)r?["access_token"];
            if (string.IsNullOrEmpty(access)) return null;
            return (access,
                (string)r["refresh_token"] ?? "",
                (long?)r["expires_in"] ?? 3600,
                (string)r["user"]?["id"] ?? "");
        }

        /// <summary>실패해도 던지지 않는다 — 위 시퀀스가 다음 수단으로 넘어가야 한다.</summary>
        static async UniTask<JObject> PostJson(string url, string anon, JObject body)
        {
            using var req = new UnityEngine.Networking.UnityWebRequest(url, "POST");
            req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(
                Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
            req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            req.SetRequestHeader("apikey", anon);
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15;

            var op = req.SendWebRequest();
            while (!op.isDone) await UniTask.Yield();

            if (req.responseCode != 200) return null;
            try { return JObject.Parse(req.downloadHandler.text); }
            catch { return null; }
        }

        // ── PAT 로 하는 보정 ──

        static async UniTask EnsureAutoConfirm(SupaRunSettings.EnvironmentData env)
        {
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(token)) return;
            var r = await SupabaseManagementApi.PatchAuthConfig(
                SupaRunSettings.ProjectIdOf(env.supabaseUrl), token, "{\"mailer_autoconfirm\":true}");
            r.LogIfFailed("autoconfirm 설정");
        }

        /// <summary>
        /// `auth.users` 의 비밀번호를 직접 되돌린다. GoTrue 는 bcrypt 해시를 그대로 읽으므로
        /// 이렇게 바꾼 비밀번호로 곧장 로그인된다. pgcrypto 는 `extensions` 스키마에 있어
        /// 스키마를 명시해야 한다. 확인 시각도 같이 채운다 — 미확인 계정은 비밀번호가 맞아도 거부된다.
        /// </summary>
        static async UniTask ResetPasswordViaPat(SupaRunSettings.EnvironmentData env, string pw)
        {
            var token = SupaRunSettings.AccessTokenOf(env);
            var pid = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(pid)) return;

            var r = await SupabaseManagementApi.RunQuery(pid, token,
                "UPDATE auth.users SET " +
                $"encrypted_password = extensions.crypt($sprpw${pw}$sprpw$, extensions.gen_salt('bf')), " +
                "email_confirmed_at = coalesce(email_confirmed_at, now()), " +
                "updated_at = now() " +
                $"WHERE lower(email) = lower($sprem${Email}$sprem$);");
            r.LogIfFailed("기계 계정 비밀번호 복구");
        }

        /// <summary>
        /// `admin_user` 에 이 신원을 등록한다. `is_admin()` RLS 가 이 행을 근거로 열린다.
        /// 표가 비어 있으면 아무도 자기를 등록할 수 없는 매듭을 PAT 가 끊는다 — 멱등이고,
        /// 프로젝트당 한 번만 실제로 나간다(메모리 캐시).
        /// </summary>
        static async UniTask EnsureAdminRow(SupaRunSettings.EnvironmentData env, string pid, string userId)
        {
            if (string.IsNullOrEmpty(userId) || _registered.Contains(pid)) return;
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(token)) return;

            var r = await SupabaseManagementApi.RunQuery(pid, token,
                $"UPDATE admin_user SET role = 'admin', email = $e${Email}$e$ WHERE user_id = $u${userId}$u$; " +
                "INSERT INTO admin_user (id, user_id, email, role, created_at, created_by) " +
                $"SELECT $u${userId}$u$, $u${userId}$u$, $e${Email}$e$, 'admin', " +
                "(extract(epoch from now()) * 1000)::bigint, 'machine' " +
                $"WHERE NOT EXISTS (SELECT 1 FROM admin_user WHERE user_id = $u${userId}$u$);");
            if (r.LogIfFailed("기계 계정 관리자 등록")) _registered.Add(pid);
        }
    }
}
