using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// **에디터가 Supabase 에 로그인한다.**
    ///
    /// 왜 필요한가: 지금까지 Unity 는 PAT(계정 마스터키)만 들고 다녔다. 그래서 설정을 공유하려면
    /// 파일을 git 에 올리는 수밖에 없었고, 그 파일에 비밀이 들어갔다.
    ///
    /// 로그인할 수 있게 되면 `is_admin()` 으로 보호된 테이블에서 설정을 받아올 수 있다 —
    /// **git 에는 공개값(URL·anon key)만 남기고 나머지는 DB 에 둘 수 있게 된다.**
    /// 새 팀원은 클론 + 로그인이면 끝이고, 사람이 나가면 계정 하나만 막으면 된다.
    ///
    /// 토큰은 `EditorPrefs`(프로젝트별)에 둔다 — git 에 올라가지 않는 자리다.
    /// </summary>
    public static class SupaRunEditorAuth
    {
        const string AccessKey = "EditorAuthAccess";
        const string RefreshKey = "EditorAuthRefresh";
        const string EmailKey = "EditorAuthEmail";
        const string UserIdKey = "EditorAuthUserId";

        static string P(string k) => EditorPrefUtils.ProjectPrefix + k;

        public static string AccessToken => EditorPrefs.GetString(P(AccessKey), "");
        public static string Email => EditorPrefs.GetString(P(EmailKey), "");
        /// <summary>Supabase auth 의 uid(JWT `sub`). `admin_user.user_id` 와 맞춰야 한다.</summary>
        public static string UserId => EditorPrefs.GetString(P(UserIdKey), "");
        public static bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

        /// <summary>브리지가 콜백에서 받은 토큰을 여기 넣는다.</summary>
        public static void StoreTokens(string access, string refresh)
        {
            EditorPrefs.SetString(P(AccessKey), access ?? "");
            EditorPrefs.SetString(P(RefreshKey), refresh ?? "");
            // 이메일은 토큰 안에 있다. 화면에 "누구로 로그인했는지" 를 띄우는 데만 쓴다.
            EditorPrefs.SetString(P(EmailKey), ClaimFromJwt(access, "email") ?? "");
            EditorPrefs.SetString(P(UserIdKey), ClaimFromJwt(access, "sub") ?? "");
            Debug.Log($"[SupaRun:Auth] 에디터 로그인 완료 — {EditorPrefs.GetString(P(EmailKey), "(이메일 없음)")}");
        }

        public static void SignOut()
        {
            EditorPrefs.DeleteKey(P(AccessKey));
            EditorPrefs.DeleteKey(P(RefreshKey));
            EditorPrefs.DeleteKey(P(EmailKey));
            EditorPrefs.DeleteKey(P(UserIdKey));
        }

        /// <summary>
        /// 브라우저를 열어 Google 로그인을 시작하고, 콜백이 올 때까지 기다린다.
        ///
        /// 콜백을 받는 것은 **이미 돌고 있는 로컬 브리지**다 — gcloud auth login 과 같은 방식이고,
        /// OAuth 전용 서버를 따로 띄우지 않아도 된다.
        /// </summary>
        public static async UniTask<bool> SignInWithGoogleAsync(CancellationToken ct = default)
        {
            var settings = SupaRunSettings.Instance;
            var supabaseUrl = settings.supabaseUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(supabaseUrl))
            {
                EditorUtility.DisplayDialog("로그인", "Supabase URL 이 설정되지 않았습니다.", "확인");
                return false;
            }
            if (!SupaRunBridge.Running)
            {
                EditorUtility.DisplayDialog("로그인",
                    "로컬 브리지가 실행 중이 아닙니다. 콜백을 받을 수 없습니다.\n" +
                    "Unity 를 재시작하거나 포트 충돌을 확인하세요.", "확인");
                return false;
            }

            var redirect = SupaRunBridge.BeginAuth();
            var url = $"{supabaseUrl}/auth/v1/authorize?provider=google" +
                      $"&redirect_to={Uri.EscapeDataString(redirect)}";

            Application.OpenURL(url);

            // 콜백은 브리지가 받아 EditorPrefs 에 넣는다. 여기서는 그것이 채워지길 기다린다.
            var before = AccessToken;
            for (var i = 0; i < 120; i++)   // 최대 2분
            {
                ct.ThrowIfCancellationRequested();
                await EditorDelay(1, ct);
                if (AccessToken != before && !string.IsNullOrEmpty(AccessToken)) return true;
            }
            return false;
        }

        /// <summary>
        /// 로그인한 사람이 이 환경의 관리자인가. `admin_user` 를 자기 토큰으로 조회한다 —
        /// RLS 가 막으면 결과가 비므로, 그 자체가 판정이 된다.
        /// </summary>
        public static async UniTask<bool> IsAdminAsync()
        {
            if (!IsSignedIn) return false;
            var settings = SupaRunSettings.Instance;
            var url = $"{settings.supabaseUrl?.TrimEnd('/')}/rest/v1/admin_user" +
                      "?select=role&role=eq.admin&limit=1";

            using var req = UnityEngine.Networking.UnityWebRequest.Get(url);
            req.SetRequestHeader("apikey", settings.SupabaseAnonKey);
            req.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
            req.timeout = 15;

            var op = req.SendWebRequest();
            while (!op.isDone) await UniTask.Yield();

            if (req.responseCode != 200) return false;
            try
            {
                return JArray.Parse(req.downloadHandler.text).Count > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 로그인한 계정을 **이 환경의 관리자로 등록**한다.
        ///
        /// 왜 여기서만 할 수 있는가: `is_admin()` 은 `admin_user` 를 보는데, 그 표에 쓰려면
        /// 이미 관리자여야 한다. 표가 비어 있으면 아무도 관리자가 아니고 아무도 등록할 수 없다 —
        /// **웹만으로는 첫 관리자를 만들 수 없다.** 그 매듭을 PAT(RLS 우회)로 끊는다.
        ///
        /// 등록 대상은 편집 환경 하나다. 환경마다 DB 가 다르므로 prod 는 prod 에서 다시 눌러야 한다.
        /// </summary>
        public static async UniTask<SupabaseResult<string>> RegisterSelfAsAdminAsync()
        {
            var env = SupaRunSettings.Instance.Current;
            var projectId = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(token))
                return SupabaseResult<string>.Local("편집 환경에 Supabase URL 또는 Access Token 이 없습니다.");
            if (string.IsNullOrEmpty(UserId))
                return SupabaseResult<string>.Local(
                    "로그인 토큰에서 사용자 ID를 찾지 못했습니다.", "로그아웃 후 다시 로그인하세요.");

            var uid = Sql(UserId);
            var mail = Sql(Email);

            // UPSERT 를 안 쓰는 이유: user_id 와 email 각각에 **부분 유니크 인덱스**가 걸려 있어
            // ON CONFLICT 로 두 경우를 한 번에 잡을 수 없다. 갱신 먼저, 없으면 삽입이 더 단순하다.
            var sql =
                $"UPDATE admin_user SET user_id = '{uid}', email = '{mail}', role = 'admin' " +
                $"WHERE user_id = '{uid}' OR email = '{mail}'; " +
                "INSERT INTO admin_user (id, user_id, email, role, created_at, created_by) " +
                $"SELECT '{uid}', '{uid}', '{mail}', 'admin', " +
                "(extract(epoch from now()) * 1000)::bigint, 'editor' " +
                $"WHERE NOT EXISTS (SELECT 1 FROM admin_user WHERE user_id = '{uid}');";

            return await SupabaseManagementApi.RunQuery(projectId, token, sql);
        }

        // ── 유틸 ──

        /// <summary>SQL 문자열 리터럴 이스케이프. 값은 우리가 만든 JWT 에서 오지만 그래도 막는다.</summary>
        static string Sql(string s) => (s ?? "").Replace("'", "''");

        /// <summary>JWT payload 에서 클레임 하나를 꺼낸다. **서명은 검증하지 않는다** — 표시·조회용이다.</summary>
        static string ClaimFromJwt(string jwt, string claim)
        {
            try
            {
                var parts = jwt?.Split('.');
                if (parts == null || parts.Length < 2) return null;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return (string)JObject.Parse(json)[claim];
            }
            catch { return null; }
        }

        /// <summary>`UniTask.Delay` 는 PlayerLoop 에 매여 비플레이 모드에서 돌지 않는다.</summary>
        static UniTask EditorDelay(double seconds, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            var until = EditorApplication.timeSinceStartup + seconds;
            void Tick()
            {
                if (ct.IsCancellationRequested)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetCanceled(ct);
                    return;
                }
                if (EditorApplication.timeSinceStartup < until) return;
                EditorApplication.update -= Tick;
                tcs.TrySetResult();
            }
            EditorApplication.update += Tick;
            return tcs.Task;
        }
    }
}
