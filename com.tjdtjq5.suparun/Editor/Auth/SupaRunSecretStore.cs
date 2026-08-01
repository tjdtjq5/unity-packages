using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 팀이 공유해야 하는 비밀을 **환경 DB에** 두고 주고받는다.
    ///
    /// 왜: PAT·DB 비밀번호·GitHub 토큰이 `ProjectSettings/SupaRunProjectSettings.json` 에 담겨
    /// git 에 올라가 있었다. gitignore 로 빼면 이번엔 팀원이 설정을 못 받는다 —
    /// **공유와 보안이 정면으로 부딪히는 자리**였다. 표에 두고 PAT 로만 오가면 둘 다 된다 —
    /// 값을 읽는 SELECT 정책이 없어서 브라우저에서는 관리자라도 꺼낼 수 없다.
    ///
    /// 접근은 에디터 로그인 JWT 로 한다. PAT 로 하지 않는 이유: PAT 를 받으러 가는 길에
    /// PAT 가 필요하면 새 팀원은 영영 못 받는다. 권한 판정은 `suparun_secret` 의
    /// `is_admin()` RLS 가 한다 — 관리자가 아니면 조회 결과가 그냥 빈다.
    ///
    /// **환경마다 자기 것만 담는다.** dev DB 에 prod DB 비밀번호를 넣으면 dev 관리자가
    /// prod 를 열 수 있게 된다. 환경을 나눈 이유가 사라진다.
    /// </summary>
    public static class SupaRunSecretStore
    {
        /// <summary>
        /// 공유 대상 하나. `read`/`write` 로 로컬 설정과 이어 붙인다.
        /// URL·anon key 는 공개값이라 여기 없다 — git 에 남아 있어야 부트스트랩이 된다.
        /// </summary>
        readonly struct Entry
        {
            public readonly string Key;
            public readonly string Label;
            public readonly Func<SupaRunSettings, string> Read;
            public readonly Action<SupaRunSettings, string> Write;

            public Entry(string key, string label,
                Func<SupaRunSettings, string> read, Action<SupaRunSettings, string> write)
            {
                Key = key; Label = label; Read = read; Write = write;
            }
        }

        /// <summary>
        /// 공유 대상. **PAT 는 여기 없다** — Supabase 의 Personal Access Token 은 이름 그대로 개인
        /// 계정 토큰이고 계정 전체의 마스터키다. 팀이 하나를 돌려쓰면 감사 추적이 사라지고,
        /// 그 사람이 나가면 전부 끊기며, 계정 하나가 팀 전체의 단일 실패점이 된다.
        /// 팀원은 자기 것을 발급해 이 컴퓨터(EditorPrefs)에만 둔다.
        /// </summary>
        static readonly Entry[] Entries =
        {
            new("supabase_db_password", "Supabase DB 비밀번호",
                s => s.SupabaseDbPassword, (s, v) => s.SupabaseDbPassword = v),
            // ⚠ GitHub 토큰만 **환경 공통**이다 — 레포 하나를 모든 환경이 공유하므로.
            // 로컬(EditorPrefs)에는 env 없이 하나만 두고(SupaRunSettings.GithubToken),
            // 표는 환경별이라 편집 환경 DB 에 담긴다. 어드민 화면이 '모든 환경 공통' 이라고 적는다.
            new("github_token", "GitHub 토큰",
                s => s.GithubToken, (s, v) => s.GithubToken = v),
            new("cron_secret", "Cron Secret",
                s => s.CronSecret, (s, v) => s.CronSecret = v),
        };

        /// <summary>공유 대상 항목 이름들. 화면에 "무엇이 오가는지" 를 적을 때 쓴다.</summary>
        public static IEnumerable<string> Labels
        {
            get { foreach (var e in Entries) yield return e.Label; }
        }

        // ── 올리기 ──

        /// <summary>
        /// 로컬에 있는 값들을 편집 환경 DB 로 올린다. **비어 있는 항목은 건너뛴다** —
        /// 빈 값을 올리면 다른 사람이 넣어 둔 것을 지우게 된다.
        /// </summary>
        public static async UniTask<SupabaseResult<int>> PushAsync()
        {
            var settings = SupaRunSettings.Instance;
            var baseCheck = Precondition(settings);
            if (baseCheck.HasValue) return baseCheck.Value.CarryFailure<int>();

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var by = SupaRunMachineAccount.Email;

            var values = new List<string>();
            foreach (var e in Entries)
            {
                var v = e.Read(settings);
                if (string.IsNullOrEmpty(v)) continue;
                // 달러 인용 — 값에 따옴표가 있어도 SQL 이 깨지지 않는다.
                values.Add($"('{e.Key}', $sec${v}$sec$, {now}, $by${by}$by$)");
            }

            if (values.Count == 0)
                return SupabaseResult<int>.Local("올릴 값이 없습니다.", "설정에 값을 먼저 채우세요.");

            var env = settings.Current;
            var r = await SupabaseManagementApi.RunQuery(
                SupaRunSettings.ProjectIdOf(env.supabaseUrl), SupaRunSettings.AccessTokenOf(env),
                "INSERT INTO suparun_secret(key, value, updated_at, updated_by) VALUES " +
                string.Join(",", values) +
                " ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, " +
                "updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by;");

            return r.Ok ? SupabaseResult<int>.Success(values.Count) : r.CarryFailure<int>();
        }

        // ── 내려받기 ──

        /// <summary>
        /// 편집 환경 DB 에 있는 값들을 로컬 설정에 채운다. 반환값은 채운 개수다.
        ///
        /// **로컬에 이미 있는 값도 덮어쓴다.** 공유본이 진실이어야 "팀원마다 다른 값을 들고
        /// 있어서 나만 배포가 되는" 상황이 안 생긴다.
        /// </summary>
        public static async UniTask<SupabaseResult<int>> PullAsync()
        {
            var settings = SupaRunSettings.Instance;
            var baseCheck = Precondition(settings);
            if (baseCheck.HasValue) return baseCheck.Value.CarryFailure<int>();

            var env = settings.Current;
            var r = await SupabaseManagementApi.RunQuery(
                SupaRunSettings.ProjectIdOf(env.supabaseUrl), SupaRunSettings.AccessTokenOf(env),
                "SELECT key, value FROM suparun_secret;");
            if (!r.Ok) return r.CarryFailure<int>();

            JArray rows;
            try { rows = JArray.Parse(r.Value ?? "[]"); }
            catch (Exception ex) { return SupabaseResult<int>.Failure(ex); }

            var applied = 0;
            foreach (var row in rows)
            {
                var key = (string)row["key"];
                var value = (string)row["value"];
                if (string.IsNullOrEmpty(value)) continue;

                foreach (var e in Entries)
                {
                    if (e.Key != key) continue;
                    e.Write(settings, value);
                    applied++;
                    break;
                }
            }

            settings.Save();
            return SupabaseResult<int>.Success(applied);
        }

        // ── 공통 ──

        /// <summary>
        /// 못 하는 이유가 있으면 그 실패를, 없으면 null.
        ///
        /// 에디터 로그인은 더 이상 요구하지 않는다 — 이제 PostgREST(로그인 JWT)가 아니라
        /// Management API(PAT)로 오가기 때문이다. 비밀 표에는 SELECT 정책이 없어서
        /// 로그인만으로는 애초에 읽히지 않는다.
        /// </summary>
        static SupabaseResult<string>? Precondition(SupaRunSettings settings)
        {
            var env = settings.Current;
            if (string.IsNullOrEmpty(env.supabaseUrl))
                return SupabaseResult<string>.Local("편집 환경에 Supabase URL 이 없습니다.");

            if (string.IsNullOrEmpty(SupaRunSettings.AccessTokenOf(env)))
                return SupabaseResult<string>.Local(
                    "편집 환경에 Access Token 이 없습니다.",
                    "Settings > Supabase 에서 PAT 를 입력하세요. PAT 는 개인이 발급해 이 컴퓨터에만 둡니다.");

            return null;
        }
    }
}
