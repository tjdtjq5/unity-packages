using System;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 승격 오케스트레이션 (ADR-0010 결정 5, #51) — **순차 실행 + 단계별 기록**.
    ///
    /// 서버 트래픽 전환 → 데이터 게시 → logic 게이트 갱신 순서로 돌고, 각 단계의 성공/실패를
    /// 릴리스 매니페스트(suparun_release.steps)에 남긴다. 교차 시스템 원자성은 주장하지 않는다 —
    /// 실패한 단계에서 멈추고 기록이 남으며, additive-only 스키마 전제로 옛 리비전이 새 데이터를
    /// 읽어도 안전하다. 실행 위치가 에디터(브리지)인 이유: gcloud 와 PAT 를 같이 쥔 곳이 여기뿐이다.
    /// </summary>
    public static class ReleaseOrchestrator
    {
        /// <summary>릴리스를 실행한다. 성공 시 릴리스 id, 실패 시 null(매니페스트에 실패 단계가 남는다).
        /// actor 는 어드민 로그인 이메일 — 매니페스트의 "누가"를 채운다(ADR-0009 취지).</summary>
        public static async UniTask<string> RunAsync(
            SupaRunSettings s, int logicVersion, int logicMin,
            string versionSchema, string memo, string revisionTag, string actor)
        {
            var env = s.Current;
            var pid = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var pat = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(pat))
            { Debug.LogError("[SupaRun:Release] 환경의 URL/PAT 가 없습니다."); return null; }
            if (logicVersion <= 0 || string.IsNullOrEmpty(versionSchema))
            { Debug.LogError("[SupaRun:Release] logic version 과 대상 버전이 필요합니다."); return null; }
            if (logicMin <= 0) logicMin = 1;
            if (logicMin > logicVersion)
            { Debug.LogError("[SupaRun:Release] 허용 최소가 릴리스 버전보다 큽니다."); return null; }
            // 태그는 cmd 한 줄에 보간된다 — 규약(deploy.yml: 소문자·숫자·하이픈) 밖 문자는
            // 셸 메타문자일 수 있으니 여기서 자른다. 설정 좌표 3종도 같은 줄에 들어가므로 같이 검문.
            if (!string.IsNullOrEmpty(revisionTag) && !Regex.IsMatch(revisionTag, "^[a-z0-9-]+$"))
            { Debug.LogError($"[SupaRun:Release] 리비전 태그는 소문자·숫자·하이픈만 됩니다: {revisionTag}"); return null; }
            foreach (var v in new[] { s.gcpServiceName, s.gcpRegion, s.gcpProjectId })
                if (!string.IsNullOrEmpty(v) && !Regex.IsMatch(v, @"^[A-Za-z0-9._-]+$"))
                { Debug.LogError($"[SupaRun:Release] GCP 설정값에 허용되지 않는 문자가 있습니다: {v}"); return null; }
            if (string.IsNullOrEmpty(actor)) actor = "bridge";

            // 대상 버전의 좌표(해시·git SHA)를 매니페스트에 옮겨 적는다.
            var ver = await SupabaseManagementApi.RunQuery(pid, pat,
                $"SELECT content_hash, git_sha FROM suparun_snapshot WHERE schema_name = '{Escape(versionSchema)}' AND is_version;");
            if (!ver.Ok) { ver.LogIfFailed("릴리스 대상 버전 조회"); return null; }
            var rows = JArray.Parse(ver.Value ?? "[]");
            if (rows.Count == 0)
            { Debug.LogError($"[SupaRun:Release] 없는 버전입니다: {versionSchema}"); return null; }
            var hash = (string)((JObject)rows[0])["content_hash"] ?? "";
            var gitSha = (string)((JObject)rows[0])["git_sha"] ?? "";

            var relId = "rel_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var created = await SupabaseManagementApi.RunQuery(pid, pat,
                "INSERT INTO suparun_release (id, logic_version, logic_min, git_sha, content_hash, revision_tag, memo, status, created_at, created_by) " +
                $"VALUES ('{relId}', {logicVersion}, {logicMin}, '{Escape(gitSha)}', '{Escape(hash)}', " +
                $"'{Escape(revisionTag ?? "")}', '{Escape(memo ?? "")}', 'running', " +
                $"(extract(epoch from now()) * 1000)::bigint, '{Escape(actor)}');");
            if (!created.Ok) { created.LogIfFailed("릴리스 매니페스트 생성"); return null; }

            // ── 1. 서버 트래픽 전환 ──
            // 태그가 없으면(데이터만의 릴리스) 스킵을 **기록**한다 — 안 한 것과 못 한 것을 구분해야 한다.
            if (string.IsNullOrEmpty(revisionTag) || string.IsNullOrEmpty(s.gcpServiceName))
            {
                await RecordStep(pid, pat, relId, "traffic", true,
                    string.IsNullOrEmpty(revisionTag) ? "스킵 — 리비전 태그 미지정(데이터만의 릴리스)" : "스킵 — Cloud Run 서비스 미설정");
            }
            else
            {
                var (ok, output) = await RunGcloud(
                    $"run services update-traffic {s.gcpServiceName} --to-tags {revisionTag}=100 " +
                    $"--region {s.gcpRegion} --project {s.gcpProjectId} --quiet");
                await RecordStep(pid, pat, relId, "traffic", ok, Truncate(output, 300));
                if (!ok) { await Fail(pid, pat, relId); return null; }
            }

            // ── 2. 데이터 게시 ──
            // publish RPC 는 is_admin 을 요구한다 — 관리자 신원을 트랜잭션 동안만 빌린다(승격 관용구).
            var pub = await SupabaseManagementApi.RunQuery(pid, pat,
                "SELECT set_config('request.jwt.claims', json_build_object('sub', " +
                "(SELECT user_id FROM admin_user_role WHERE role = 'game-admin' ORDER BY user_id LIMIT 1))::text, true); " +
                $"SELECT suparun_version_publish('{Escape(versionSchema)}') AS backup;");
            await RecordStep(pid, pat, relId, "publish", pub.Ok,
                pub.Ok ? $"게시 완료 — {versionSchema}" : Truncate(pub.Message, 300));
            if (!pub.Ok) { await Fail(pid, pat, relId); return null; }

            // ── 3. logic version 게이트 갱신 (#35 협상이 읽는다) ──
            var gate = await SupabaseManagementApi.RunQuery(pid, pat,
                "INSERT INTO suparun_meta (key, value, updated_at) VALUES ('logic_version_range', " +
                $"jsonb_build_object('min', {logicMin}, 'max', {logicVersion}), now()) " +
                "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at;");
            await RecordStep(pid, pat, relId, "logic_gate", gate.Ok,
                gate.Ok ? $"허용 범위 {logicMin}~{logicVersion}" : Truncate(gate.Message, 300));
            if (!gate.Ok) { await Fail(pid, pat, relId); return null; }

            // 이 UPDATE 가 실패하면 성공한 릴리스가 'running' 으로 남는다 — 최소한 로그는 남긴다.
            var done = await SupabaseManagementApi.RunQuery(pid, pat,
                $"UPDATE suparun_release SET status = 'done', " +
                $"published_at = (extract(epoch from now()) * 1000)::bigint, published_by = '{Escape(actor)}' " +
                $"WHERE id = '{relId}';");
            done.LogIfFailed("릴리스 완료 기록");

            Debug.Log($"[SupaRun:Release] {relId} 완료 — logic {logicMin}~{logicVersion}, {versionSchema}" +
                      (string.IsNullOrEmpty(revisionTag) ? "" : $", 트래픽 {revisionTag}"));
            return relId;
        }

        static async UniTask RecordStep(string pid, string pat, string relId, string step, bool ok, string detail)
        {
            var r = await SupabaseManagementApi.RunQuery(pid, pat,
                "UPDATE suparun_release SET steps = steps || jsonb_build_object(" +
                $"'step', '{Escape(step)}', 'ok', {(ok ? "true" : "false")}, " +
                "'at', (extract(epoch from now()) * 1000)::bigint, " +
                $"'detail', '{Escape(detail ?? "")}') WHERE id = '{relId}';");
            r.LogIfFailed("릴리스 단계 기록");
        }

        static async UniTask Fail(string pid, string pat, string relId)
        {
            var r = await SupabaseManagementApi.RunQuery(pid, pat,
                $"UPDATE suparun_release SET status = 'failed' WHERE id = '{relId}';");
            r.LogIfFailed("릴리스 실패 기록");
        }

        /// <summary>gcloud 실행. 브리지 메인 스레드를 얼리지 않게 스레드풀에서 기다린다.</summary>
        static async UniTask<(bool ok, string output)> RunGcloud(string args)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // Windows 의 gcloud 는 .cmd 라 CreateProcess 가 못 찾는다(PATHEXT 미적용 —
                    // 실측 Win32Exception). 셸을 한 겹 끼워 확장자 해석을 맡긴다.
                    var isWin = System.Runtime.InteropServices.RuntimeInformation
                        .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                    var psi = isWin
                        ? new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c gcloud " + args)
                        : new System.Diagnostics.ProcessStartInfo("gcloud", args);
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) return (false, "gcloud 를 시작하지 못했습니다");
                    if (!p.WaitForExit(120_000)) { try { p.Kill(); } catch { } return (false, "gcloud 타임아웃(120s)"); }
                    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    return (p.ExitCode == 0, output.Trim());
                }
                catch (Exception ex) { return (false, ex.Message); }
            });
        }

        static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

        /// <summary>
        /// SQL 문자열 리터럴 방어. 따옴표 이스케이프에 더해 **제어문자를 걷어낸다** —
        /// Mono 의 Win32Exception.Message 는 NUL(\0) 꼬리를 달고 오는데, 그대로 임베드하면
        /// Postgres 가 invalid message format 으로 거부해 실패 기록마저 유실된다(실측).
        /// </summary>
        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\'') sb.Append("''");
                else if (!char.IsControl(c) || c == '\n' || c == '\t') sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
