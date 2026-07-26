using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 팀이 공유해야 하는 비밀을 **환경 DB에** 두고 주고받는다.
    ///
    /// 왜: PAT·DB 비밀번호·GitHub 토큰이 `ProjectSettings/SupaRunProjectSettings.json` 에 담겨
    /// git 에 올라가 있었다. gitignore 로 빼면 이번엔 팀원이 설정을 못 받는다 —
    /// **공유와 보안이 정면으로 부딪히는 자리**였다. 관리자 로그인 뒤에 두면 둘 다 된다.
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

        static readonly Entry[] Entries =
        {
            new("supabase_access_token", "Supabase Access Token (PAT)",
                s => s.SupabaseAccessToken, (s, v) => s.SupabaseAccessToken = v),
            new("supabase_db_password", "Supabase DB 비밀번호",
                s => s.SupabaseDbPassword, (s, v) => s.SupabaseDbPassword = v),
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

            var rows = new JArray();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var e in Entries)
            {
                var v = e.Read(settings);
                if (string.IsNullOrEmpty(v)) continue;
                rows.Add(new JObject
                {
                    ["key"] = e.Key,
                    ["value"] = v,
                    ["updated_at"] = now,
                    ["updated_by"] = SupaRunEditorAuth.Email,
                });
            }

            if (rows.Count == 0)
                return SupabaseResult<int>.Local("올릴 값이 없습니다.", "설정에 값을 먼저 채우세요.");

            var r = await Send(settings, "POST", "?on_conflict=key",
                rows.ToString(Formatting.None), "resolution=merge-duplicates");

            return r.Ok ? SupabaseResult<int>.Success(rows.Count) : r.CarryFailure<int>();
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

            var r = await Send(settings, "GET", "?select=key,value", null, null);
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

        /// <summary>못 하는 이유가 있으면 그 실패를, 없으면 null.</summary>
        static SupabaseResult<string>? Precondition(SupaRunSettings settings)
        {
            if (!SupaRunEditorAuth.IsSignedIn)
                return SupabaseResult<string>.Local(
                    "에디터가 로그인되어 있지 않습니다.", "Settings > 에디터 로그인에서 Google 로 로그인하세요.");

            var env = settings.Current;
            if (string.IsNullOrEmpty(env.supabaseUrl) || string.IsNullOrEmpty(env.supabaseAnonKey))
                return SupabaseResult<string>.Local(
                    "편집 환경에 Supabase URL 또는 anon key 가 없습니다.");

            return null;
        }

        /// <summary>
        /// PostgREST 호출. anon key 는 `apikey` 로, 신원은 로그인 JWT 로 보낸다 —
        /// RLS 가 보는 것은 JWT 쪽이다.
        /// </summary>
        static async UniTask<SupabaseResult<string>> Send(
            SupaRunSettings settings, string method, string query, string body, string prefer)
        {
            var env = settings.Current;
            var url = $"{env.supabaseUrl.TrimEnd('/')}/rest/v1/suparun_secret{query}";

            using var req = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 20,
            };
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("apikey", env.supabaseAnonKey);
            req.SetRequestHeader("Authorization", $"Bearer {SupaRunEditorAuth.AccessToken}");
            if (prefer != null) req.SetRequestHeader("Prefer", prefer);

            try
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await UniTask.Yield();
            }
            catch (Exception ex) { return SupabaseResult<string>.Failure(ex); }

            var text = req.downloadHandler?.text ?? "";
            return req.responseCode is >= 200 and < 300
                ? SupabaseResult<string>.Success(text, req.responseCode, text)
                : SupabaseResult<string>.Failure(req.responseCode, text);
        }
    }
}
