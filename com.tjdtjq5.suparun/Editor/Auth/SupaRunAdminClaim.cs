using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// **첫 관리자 매듭 끊기** — 로컬 어드민에서 로그인한 사람을 `admin_user` 에 등록한다 (ADR-0009).
    ///
    /// `is_admin()` RLS 는 admin_user 행을 근거로 열리는데, 표가 비어 있으면 아무도 자기를
    /// 등록할 수 없다(등록에도 is_admin 이 필요하다). 그 매듭을 PAT 가 끊는다 — 로컬 브리지를
    /// 연 사람은 이미 PAT 대행 전권을 쥐고 있으므로 승인을 따로 묻지 않는다(늘어나는 안전이
    /// 없는 확인). 원격 접근자는 이 경로 자체가 없다 — 브리지는 로컬 전용이다.
    ///
    /// 신원은 브라우저가 주장하는 값을 믿지 않는다 — 받은 access token 을 GoTrue 에 되물어
    /// (`/auth/v1/user`) 확정한다.
    /// </summary>
    public static class SupaRunAdminClaim
    {
        public static async UniTask<SupabaseResult<(string userId, string email)>> ClaimAsync(
            SupaRunSettings.EnvironmentData env, string accessToken)
        {
            var url = (env?.supabaseUrl ?? "").TrimEnd('/');
            var anon = env?.supabaseAnonKey ?? "";
            var pat = SupaRunSettings.AccessTokenOf(env);
            var pid = SupaRunSettings.ProjectIdOf(url);
            if (url.Length == 0 || anon.Length == 0)
                return SupabaseResult<(string, string)>.Local("환경에 Supabase URL/anon key 가 없습니다.");
            if (string.IsNullOrEmpty(pat))
                return SupabaseResult<(string, string)>.Local("Access Token(PAT) 이 없습니다.",
                    "Settings > Supabase 에서 PAT 를 입력하세요.");

            var user = await GetUser(url, anon, accessToken);
            if (user == null)
                return SupabaseResult<(string, string)>.Local("세션 토큰이 유효하지 않습니다.", "다시 로그인하세요.");

            var (userId, email) = user.Value;

            // ⚠ admin_user.user_id 유니크는 부분 인덱스(WHERE user_id IS NOT NULL)라 ON CONFLICT
            // 를 못 쓴다 — UPDATE 후 없으면 INSERT 로 멱등을 만든다.
            var e = Quote(email);
            var u = Quote(userId);
            var r = await SupabaseManagementApi.RunQuery(pid, pat,
                $"UPDATE admin_user SET role = 'admin', email = {e}, provider = 'email' " +
                $"WHERE user_id = {u}; " +
                "INSERT INTO admin_user (id, user_id, email, role, provider, created_at, created_by) " +
                $"SELECT {u}, {u}, {e}, 'admin', 'email', " +
                "(extract(epoch from now()) * 1000)::bigint, 'local-bridge' " +
                $"WHERE NOT EXISTS (SELECT 1 FROM admin_user WHERE user_id = {u});");
            if (!r.Ok) return r.CarryFailure<(string, string)>();

            return SupabaseResult<(string, string)>.Success((userId, email));
        }

        /// <summary>
        /// SQL 문자열 리터럴. GoTrue 가 준 값이라도 이스케이프한다 — 이메일 local part 에는
        /// `'` 도 `$` 도 올 수 있어서 달러 인용($tag$)은 태그 충돌 여지가 있다.
        /// </summary>
        static string Quote(string s) => "'" + (s ?? "").Replace("'", "''") + "'";

        /// <summary>토큰의 주인을 GoTrue 에 묻는다. 무효 토큰 등 실패는 null.</summary>
        static async UniTask<(string id, string email)?> GetUser(string url, string anon, string accessToken)
        {
            using var req = UnityEngine.Networking.UnityWebRequest.Get($"{url}/auth/v1/user");
            req.SetRequestHeader("apikey", anon);
            req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            req.timeout = 15;

            var op = req.SendWebRequest();
            while (!op.isDone) await UniTask.Yield();

            if (req.responseCode != 200) return null;
            try
            {
                var j = JObject.Parse(req.downloadHandler.text);
                var id = (string)j["id"];
                if (string.IsNullOrEmpty(id)) return null;
                return (id, (string)j["email"] ?? "");
            }
            catch { return null; }
        }
    }
}
