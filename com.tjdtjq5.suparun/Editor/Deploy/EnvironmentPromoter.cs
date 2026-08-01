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
    /// 환경 간 [SpecData] 승격 — dev 에서 만든 데이터를 prod 로 올린다.
    ///
    /// **prod 를 직접 고치지 않는다**는 규칙을 도구로 만든 것이다. 기획 데이터는 dev 어드민에서
    /// 만들고, 검증이 끝나면 통째로 올린다. 부분 승격을 두지 않는 이유는 두 환경이 서서히
    /// 달라지는 것을 막기 위해서다 — 'prod 는 항상 dev 의 사본' 이면 어디가 다른지 고민할 일이 없다.
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
        /// from 의 [SpecData] 전체를 to 로 옮긴다. 성공하면 true.
        ///
        /// 순서: 대상 스냅샷 → 원본 추출 → 대상 주입. 스냅샷이 먼저인 이유는
        /// 주입이 TRUNCATE 로 시작하기 때문이다 — 되돌릴 자리를 만들고 들어간다.
        /// </summary>
        public static async UniTask<bool> PromoteAsync(
            SupaRunSettings.EnvironmentData from, SupaRunSettings.EnvironmentData to)
        {
            if (from == null || to == null) { Debug.LogError("[SupaRun:Promote] 환경이 지정되지 않았습니다."); return false; }
            if (from.name == to.name) { Debug.LogError("[SupaRun:Promote] 원본과 대상이 같습니다."); return false; }

            var fromId = SupaRunSettings.ProjectIdOf(from.supabaseUrl);
            var toId = SupaRunSettings.ProjectIdOf(to.supabaseUrl);
            var fromToken = SupaRunSettings.AccessTokenOf(from);
            var toToken = SupaRunSettings.AccessTokenOf(to);
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(fromToken))
            { Debug.LogError($"[SupaRun:Promote] 환경 '{from.name}' 의 URL/Access Token 이 없습니다."); return false; }
            if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(toToken))
            { Debug.LogError($"[SupaRun:Promote] 환경 '{to.name}' 의 URL/Access Token 이 없습니다."); return false; }

            try
            {
                // ── 1. 대상 테이블 목록 ──
                // **대상 기준**으로 뽑는다. 원본에만 있는 새 테이블은 대상에 스키마가 아직 없다는 뜻이고,
                // 그 상태로 주입하면 jsonb_populate_recordset 이 없는 타입을 참조해 죽는다.
                EditorUtility.DisplayProgressBar("SupaRun 승격", "대상 테이블 확인 중…", 0.1f);
                var tables = await FetchTables(toId, toToken);
                if (tables == null) return false;
                if (tables.Count == 0)
                {
                    Debug.LogError(
                        $"[SupaRun:Promote] 환경 '{to.name}' 에 [SpecData] 테이블이 없습니다.\n" +
                        "먼저 '스키마 반영' 으로 구조를 만드세요.");
                    return false;
                }

                // ── 2. 대상 스냅샷 ──
                EditorUtility.DisplayProgressBar("SupaRun 승격", $"'{to.name}' 스냅샷 저장 중…", 0.3f);
                if (!await SnapshotTarget(toId, toToken, from.name)) return false;

                // ── 3. 원본 추출 ──
                EditorUtility.DisplayProgressBar("SupaRun 승격", $"'{from.name}' 데이터 읽는 중…", 0.5f);
                var payload = await FetchData(fromId, fromToken, tables);
                if (payload == null) return false;

                // ── 4. 대상 주입 ──
                EditorUtility.DisplayProgressBar("SupaRun 승격", $"'{to.name}' 에 적용 중…", 0.8f);
                var sql = BuildApplySql(tables, payload);
                var applied = await SupabaseManagementApi.RunQuery(toId, toToken, sql);
                if (!applied.Ok)
                {
                    EditorUtility.ClearProgressBar();   // 팝업 전에 진행바를 걷는다
                    applied.ShowErrorDialog($"'{to.name}' 에 데이터 적용");
                    Debug.LogError(
                        $"[SupaRun:Promote] '{to.name}' 은 직전 스냅샷으로 되돌릴 수 있습니다(어드민 > snapshots).");
                    return false;
                }

                Debug.Log($"[SupaRun:Promote] '{from.name}' → '{to.name}' 승격 완료 — 테이블 {tables.Count}개.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SupaRun:Promote] 예외 — {ex.Message}");
                return false;
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
                Debug.LogError("[SupaRun:Promote] 대상 환경에 스키마가 반영되지 않았을 수 있습니다.");
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
        /// 승격 직전 대상 스냅샷. 어드민 RPC 를 그대로 쓴다 — 스냅샷 로직이 두 곳에 생기면 갈라진다.
        ///
        /// RPC 가 is_admin() 을 요구하는데 Management API 에는 로그인 사용자가 없다.
        /// 그래서 **관리자 신원을 트랜잭션 동안만 빌린다**(set_config 는 is_local=true).
        /// 대상에 관리자가 아직 없으면 여기서 걸리며, 그건 실제로 승격하면 안 되는 상태다.
        /// </summary>
        static async UniTask<bool> SnapshotTarget(string projectId, string token, string fromName)
        {
            var sql =
                "SELECT set_config('request.jwt.claims', json_build_object('sub', " +
                "(SELECT user_id FROM admin_user_role WHERE role = 'game-admin' LIMIT 1))::text, true); " +
                $"SELECT suparun_snapshot_create('promote', '{Escape(fromName)} 승격 직전 자동 저장', true);";

            var r = await SupabaseManagementApi.RunQuery(projectId, token, sql);
            if (r.Ok) return true;

            EditorUtility.ClearProgressBar();
            r.ShowErrorDialog("대상 환경 스냅샷");
            Debug.LogError(
                "[SupaRun:Promote] 대상 환경에 game-admin 롤 보유자가 없으면 이 단계에서 막힙니다. " +
                "그 환경의 어드민에 로그인해 관리자를 만든 뒤 다시 시도하세요.");
            return false;
        }

        /// <summary>원본에서 테이블별 행을 JSON 한 덩어리로 받는다. 왕복 1회.</summary>
        static async UniTask<string> FetchData(string projectId, string token, List<string> tables)
        {
            var sb = new StringBuilder("SELECT coalesce(jsonb_object_agg(tbl, data), '{}'::jsonb)::text AS payload FROM (");
            for (int i = 0; i < tables.Count; i++)
            {
                if (i > 0) sb.Append(" UNION ALL ");
                var t = Ident(tables[i]);
                sb.Append($"SELECT '{Escape(tables[i])}'::text AS tbl, " +
                          $"coalesce(jsonb_agg(x), '[]'::jsonb) AS data FROM public.{t} x");
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
                Debug.LogError("[SupaRun:Promote] 원본 데이터가 비어 있습니다.");
                return null;
            }
            return ((JObject)rows[0])["payload"]?.ToString() ?? "{}";
        }

        /// <summary>
        /// 대상에 주입하는 SQL. 테이블마다 TRUNCATE 후 JSON 을 레코드로 펼쳐 넣는다.
        ///
        /// jsonb_populate_recordset 을 쓰는 이유: 대상 테이블 정의를 기준으로 채우므로
        /// **원본에만 있는 컬럼은 무시되고 대상에만 있는 컬럼은 기본값으로 남는다.**
        /// 컬럼이 어긋나도 승격이 죽지 않는다 — 스냅샷 복원의 '공통 컬럼만' 과 같은 원리다.
        ///
        /// SupaRun 의 FK 는 DB 제약이 아니라 어드민 메타라 테이블 순서를 맞출 필요가 없다.
        /// </summary>
        static string BuildApplySql(List<string> tables, string payload)
        {
            var sb = new StringBuilder();

            // 페이로드는 **세션 변수**에 올려 두고 테이블마다 꺼내 쓴다.
            // TEMP TABLE 은 트랜잭션 경계에 따라 사라질 수 있어(ON COMMIT DROP) 쓰지 않는다 —
            // Management API 가 statement 들을 한 트랜잭션으로 묶는지에 기대지 않으려는 것이다.
            // 인라인 반복도 피한다. 테이블 수만큼 payload 가 복제되면 SQL 이 수십 배가 된다.
            sb.AppendLine($"SELECT set_config('suparun.promote_payload', $promote${payload}$promote$, false);");

            foreach (var t in tables)
            {
                var id = Ident(t);
                sb.AppendLine($"TRUNCATE public.{id};");
                sb.AppendLine(
                    $"INSERT INTO public.{id} SELECT * FROM jsonb_populate_recordset(null::public.{id}, " +
                    $"current_setting('suparun.promote_payload')::jsonb -> '{Escape(t)}');");
            }
            return sb.ToString();
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
