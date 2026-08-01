using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 환경 간 [SpecData] 승격 — dev 데이터를 대상 환경의 **미게시 버전**으로 올린다 (ADR-0010, #30).
    ///
    /// 예전에는 대상 public 에 즉시 주입했다(TRUNCATE+INSERT). 지금 업로드는 라이브에 아무
    /// 영향이 없다 — 대상 안에 버전 스냅샷(ver_*)만 만들고, 반영은 어드민의 게시(publish)가
    /// 따로 한다. **prod 를 직접 고치지 않는다** 규칙이 리뷰 단계를 얻은 것이다.
    ///
    /// 실행 위치가 에디터인 이유: 두 환경의 PAT 를 동시에 쥔 곳이 여기뿐이다.
    /// 어드민은 환경 하나만 보므로 이 일을 할 수 없다.
    ///
    /// **스키마는 옮기지 않는다.** 마이그레이션이 코드 생성 + 멱등이므로 대상 환경에서
    /// <see cref="SchemaAutoSync.SyncToEnvironment"/> 를 먼저 돌리면 같은 구조가 나온다.
    /// </summary>
    public static class EnvironmentPromoter
    {
        /// <summary>
        /// from 의 [SpecData] 전체를 to 의 미게시 버전으로 올린다. 성공 시 버전 스키마명(ver_*), 실패 시 null.
        ///
        /// 버전 ID 는 **정규화 페이로드의 SHA-256** 이다 — 행을 id 로, 테이블 키를 jsonb 정렬로
        /// 고정해 같은 내용이면 언제 뽑아도 같은 해시가 나온다. 같은 내용 재업로드는 새 버전을
        /// 만들지 않고 기존 좌표를 돌려받는다. 재현 좌표로 git SHA 를 함께 기록한다.
        /// </summary>
        public static async UniTask<string> UploadVersionAsync(
            SupaRunSettings.EnvironmentData from, SupaRunSettings.EnvironmentData to)
        {
            if (from == null || to == null) { Debug.LogError("[SupaRun:Upload] 환경이 지정되지 않았습니다."); return null; }
            if (from.name == to.name) { Debug.LogError("[SupaRun:Upload] 원본과 대상이 같습니다."); return null; }

            var fromId = SupaRunSettings.ProjectIdOf(from.supabaseUrl);
            var toId = SupaRunSettings.ProjectIdOf(to.supabaseUrl);
            var fromToken = SupaRunSettings.AccessTokenOf(from);
            var toToken = SupaRunSettings.AccessTokenOf(to);
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(fromToken))
            { Debug.LogError($"[SupaRun:Upload] 환경 '{from.name}' 의 URL/Access Token 이 없습니다."); return null; }
            if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(toToken))
            { Debug.LogError($"[SupaRun:Upload] 환경 '{to.name}' 의 URL/Access Token 이 없습니다."); return null; }

            try
            {
                // ── 1. 대상 테이블 목록 ──
                // **대상 기준**으로 뽑는다. 원본에만 있는 새 테이블은 대상에 스키마가 아직 없다는 뜻이고,
                // 그 상태로 주입하면 jsonb_populate_recordset 이 없는 타입을 참조해 죽는다.
                EditorUtility.DisplayProgressBar("SupaRun 업로드", "대상 테이블 확인 중…", 0.15f);
                var tables = await FetchTables(toId, toToken);
                if (tables == null) return null;
                if (tables.Count == 0)
                {
                    Debug.LogError(
                        $"[SupaRun:Upload] 환경 '{to.name}' 에 [SpecData] 테이블이 없습니다.\n" +
                        "먼저 '스키마 반영' 으로 구조를 만드세요.");
                    return null;
                }

                // ── 2. 원본 추출 (정규화) ──
                EditorUtility.DisplayProgressBar("SupaRun 업로드", $"'{from.name}' 데이터 읽는 중…", 0.45f);
                var payload = await FetchData(fromId, fromToken, tables);
                if (payload == null) return null;

                // ── 3. 버전 좌표 ──
                var hash = Sha256Hex(payload);
                var gitSha = TryGetGitSha();

                // ── 4. 대상에 미게시 버전 생성 ──
                // 관리자 신원을 트랜잭션 동안만 빌린다(옛 승격의 관용구) — RPC 가 is_admin 을 요구하는데
                // Management API 에는 로그인 사용자가 없다. 대상에 game-admin 이 없으면 여기서 걸리며,
                // 그건 실제로 업로드하면 안 되는 상태다.
                EditorUtility.DisplayProgressBar("SupaRun 업로드", $"'{to.name}' 에 버전 생성 중…", 0.8f);
                // 페이로드는 is_local=false — 옛 승격의 검증된 관용구다(트랜잭션 경계에 기대지 않는다).
                var sql =
                    "SELECT set_config('request.jwt.claims', json_build_object('sub', " +
                    "(SELECT user_id FROM admin_user_role WHERE role = 'game-admin' LIMIT 1))::text, true); " +
                    $"SELECT set_config('suparun.upload_payload', $upload${payload}$upload$, false); " +
                    $"SELECT suparun_version_upload('{Escape(from.name)}', '{Escape(hash)}', '{Escape(gitSha)}') AS ver;";

                var r = await SupabaseManagementApi.RunQuery(toId, toToken, sql);
                if (!r.Ok)
                {
                    EditorUtility.ClearProgressBar();
                    r.ShowErrorDialog($"'{to.name}' 에 버전 업로드");
                    return null;
                }

                var schema = ParseSingle(r.Value, "ver");
                Debug.Log(
                    $"[SupaRun:Upload] '{from.name}' → '{to.name}' 버전 업로드 완료 — {schema} " +
                    $"(테이블 {tables.Count}개, 해시 {hash.Substring(0, 12)}). 라이브 반영은 어드민의 게시가 합니다.");
                return schema;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SupaRun:Upload] 예외 — {ex.Message}");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>대상 환경의 [SpecData] 테이블 목록. 서버 함수를 그대로 쓴다.</summary>
        static async UniTask<List<string>> FetchTables(string projectId, string token)
        {
            var r = await SupabaseManagementApi.RunQuery(
                projectId, token, "SELECT * FROM suparun_snapshot_tables() AS t;");
            if (!r.Ok)
            {
                EditorUtility.ClearProgressBar();
                r.ShowErrorDialog("대상 환경 테이블 목록 조회");
                Debug.LogError("[SupaRun:Upload] 대상 환경에 스키마가 반영되지 않았을 수 있습니다.");
                return null;
            }

            var list = new List<string>();
            foreach (var row in JArray.Parse(r.Value))
            {
                // 컬럼 이름은 함수 반환에 따라 't' 또는 첫 프로퍼티다. 이름에 기대지 않는다.
                var first = ((JObject)row).First as JProperty;
                var name = first?.Value?.ToString();
                if (!string.IsNullOrEmpty(name)) list.Add(name);
            }
            return list;
        }

        /// <summary>
        /// 원본에서 테이블별 행을 JSON 한 덩어리로 받는다. 왕복 1회.
        ///
        /// **정규화가 곧 버전 ID 다**: 행은 id 로 정렬하고(물리 순서는 비결정적이다),
        /// 테이블 키와 객체 키는 jsonb 가 정렬한다. 그래서 같은 내용 = 같은 텍스트 = 같은 해시.
        /// </summary>
        static async UniTask<string> FetchData(string projectId, string token, List<string> tables)
        {
            var sb = new StringBuilder("SELECT coalesce(jsonb_object_agg(tbl, data), '{}'::jsonb)::text AS payload FROM (");
            for (int i = 0; i < tables.Count; i++)
            {
                if (i > 0) sb.Append(" UNION ALL ");
                var t = Ident(tables[i]);
                sb.Append($"SELECT '{Escape(tables[i])}'::text AS tbl, " +
                          $"coalesce(jsonb_agg(x ORDER BY x.id), '[]'::jsonb) AS data FROM public.{t} x");
            }
            sb.Append(") s;");

            var r = await SupabaseManagementApi.RunQuery(projectId, token, sb.ToString());
            if (!r.Ok)
            {
                EditorUtility.ClearProgressBar();
                r.ShowErrorDialog("원본 데이터 조회");
                return null;
            }

            var rows = JArray.Parse(r.Value);
            if (rows.Count == 0)
            {
                Debug.LogError("[SupaRun:Upload] 원본 데이터가 비어 있습니다.");
                return null;
            }
            return ((JObject)rows[0])["payload"]?.ToString() ?? "{}";
        }

        /// <summary>RunQuery 결과(JSON 배열)에서 첫 행의 컬럼 하나를 꺼낸다.</summary>
        static string ParseSingle(string raw, string column)
        {
            try
            {
                var rows = JArray.Parse(raw ?? "[]");
                return rows.Count > 0 ? (string)((JObject)rows[0])[column] : null;
            }
            catch { return null; }
        }

        static string Sha256Hex(string s)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// 재현 좌표용 git SHA. 데이터를 만든 코드 상태를 가리키므로 Unity 프로젝트 레포 기준이다.
        /// git 이 없거나 레포가 아니면 빈 문자열 — 좌표는 부가 정보라 업로드를 막지 않는다.
        /// </summary>
        static string TryGetGitSha()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                {
                    WorkingDirectory = System.IO.Path.GetFullPath(Application.dataPath + "/.."),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return "";
                if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return ""; }
                return p.ExitCode == 0 ? p.StandardOutput.ReadToEnd().Trim() : "";
            }
            catch { return ""; }
        }

        /// <summary>식별자 방어. 테이블 이름은 서버가 준 것이지만 그대로 붙이지 않는다.</summary>
        static string Ident(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            return sb.ToString();
        }

        static string Escape(string s) => (s ?? "").Replace("'", "''");
    }
}
