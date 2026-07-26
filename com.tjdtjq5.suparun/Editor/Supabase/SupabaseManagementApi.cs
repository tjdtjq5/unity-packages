using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// Supabase Management API. Access Token(PAT) 기반.
    ///
    /// 모든 메서드가 <see cref="SupabaseResult{T}"/> 를 돌려준다 — 실패의 종류(401 인지 402 인지 409 인지)에
    /// 따라 사용자가 할 일이 완전히 다른데, 예전처럼 `"HTTP 402: {...}"` 문자열 하나로 뭉개면
    /// 화면에서 그걸 되살릴 방법이 없다.
    ///
    /// **재시도하지 않는다.** 프로젝트 생성처럼 멱등하지 않은 호출에서 '응답만 유실된' 재시도는
    /// 프로젝트를 두 개 만들고, 그건 곧 과금과 한도로 이어진다.
    /// </summary>
    public static class SupabaseManagementApi
    {
        const string ROOT = "https://api.supabase.com/v1";
        const string BASE = ROOT + "/projects";

        // ── 모델 ──

        public struct ProjectInfo
        {
            public string id;     // ref
            public string name;
            public string status; // ACTIVE_HEALTHY / INACTIVE / COMING_UP …
            public string region;
            public string organizationId;
            public string createdAt;

            /// <summary>일시정지 상태인가. 무료 플랜은 안 쓰면 자동으로 여기 들어간다.</summary>
            public bool IsInactive =>
                string.Equals(status, "INACTIVE", StringComparison.OrdinalIgnoreCase);

            /// <summary>바로 쓸 수 있는 상태인가.</summary>
            public bool IsHealthy =>
                string.Equals(status, "ACTIVE_HEALTHY", StringComparison.OrdinalIgnoreCase);

            public string Url => string.IsNullOrEmpty(id) ? "" : $"https://{id}.supabase.co";
        }

        public struct OrganizationInfo
        {
            public string id;
            public string slug;
            public string name;
        }

        public struct RegionInfo
        {
            /// <summary>API 에 넘기는 값 (`ap-northeast-1`).</summary>
            public string code;
            /// <summary>사람이 읽는 이름. 없으면 code 를 그대로 쓴다.</summary>
            public string displayName;

            public string Label => string.IsNullOrEmpty(displayName) ? code : $"{displayName} ({code})";
        }

        /// <summary>프로젝트 생성 요청. 필수는 이름·비밀번호·조직이다.</summary>
        public struct CreateProjectRequest
        {
            public string name;
            public string organizationSlug;
            public string dbPass;
            /// <summary>비우면 Supabase 기본값. 생성 이후에는 바꿀 수 없다.</summary>
            public string region;
            /// <summary>`free` 또는 `pro`. 비우면 조직 기본값.</summary>
            public string plan;
        }

        // ── 조직 ──

        public static async Task<SupabaseResult<OrganizationInfo[]>> ListOrganizations(string token)
        {
            var r = await RequestJson("GET", $"{ROOT}/organizations", token);
            if (!r.Ok) return r.CarryFailure<OrganizationInfo[]>();

            return Parse(r, tok =>
            {
                var list = new List<OrganizationInfo>();
                foreach (var o in tok as JArray ?? new JArray())
                    list.Add(new OrganizationInfo
                    {
                        id = (string)o["id"],
                        slug = (string)o["slug"],
                        name = (string)o["name"],
                    });
                return list.ToArray();
            });
        }

        // ── 프로젝트 ──

        public static async Task<SupabaseResult<ProjectInfo[]>> ListProjects(string token)
        {
            var r = await RequestJson("GET", BASE, token);
            if (!r.Ok) return r.CarryFailure<ProjectInfo[]>();

            return Parse(r, tok =>
            {
                var list = new List<ProjectInfo>();
                foreach (var p in tok as JArray ?? new JArray())
                    list.Add(ToProject(p));
                return list.ToArray();
            });
        }

        public static async Task<SupabaseResult<ProjectInfo>> GetProject(string projectRef, string token)
        {
            var r = await RequestJson("GET", $"{BASE}/{projectRef}", token);
            if (!r.Ok) return r.CarryFailure<ProjectInfo>();
            return Parse(r, ToProject);
        }

        /// <summary>
        /// 프로젝트 생성. 응답은 즉시 오지만 **DB 는 아직 뜨는 중**(status=COMING_UP)이다.
        /// 쓸 수 있게 되기까지 보통 2분 넘게 걸리므로 호출한 쪽이 상태를 폴링해야 한다.
        /// </summary>
        public static async Task<SupabaseResult<ProjectInfo>> CreateProject(
            string token, CreateProjectRequest req)
        {
            var body = new JObject
            {
                ["name"] = req.name,
                ["organization_slug"] = req.organizationSlug,
                ["db_pass"] = req.dbPass,
            };
            if (!string.IsNullOrEmpty(req.region)) body["region"] = req.region;
            if (!string.IsNullOrEmpty(req.plan)) body["plan"] = req.plan;

            var r = await RequestJson("POST", BASE, token, body.ToString(Formatting.None));
            if (!r.Ok) return r.CarryFailure<ProjectInfo>();
            return Parse(r, ToProject);
        }

        /// <summary>프로젝트 삭제. **되돌릴 수 없다** — 데이터·백업·스냅샷이 함께 사라진다.</summary>
        public static async Task<SupabaseResult<bool>> DeleteProject(string projectRef, string token)
        {
            var r = await RequestJson("DELETE", $"{BASE}/{projectRef}", token);
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        /// <summary>이름 변경. PATCH 가 바꿀 수 있는 것은 이름뿐이다 — 리전은 생성 후 못 바꾼다.</summary>
        public static async Task<SupabaseResult<bool>> RenameProject(
            string projectRef, string token, string newName)
        {
            var body = new JObject { ["name"] = newName }.ToString(Formatting.None);
            var r = await RequestJson("PATCH", $"{BASE}/{projectRef}", token, body);
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        /// <summary>일시정지된 프로젝트를 되살린다.</summary>
        public static async Task<SupabaseResult<bool>> RestoreProject(string projectRef, string token)
        {
            var r = await RequestJson("POST", $"{BASE}/{projectRef}/restore", token, "{}");
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        /// <summary>프로젝트 일시정지.</summary>
        public static async Task<SupabaseResult<bool>> PauseProject(string projectRef, string token)
        {
            var r = await RequestJson("POST", $"{BASE}/{projectRef}/pause", token, "{}");
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        /// <summary>
        /// 생성 시 고를 수 있는 리전 목록.
        ///
        /// ⚠ `organization_slug` 는 **필수**다. 없으면 400 을 준다.
        /// 응답은 배열이 아니라 객체이고, 우리가 쓰는 것은 `all.specific` 이다:
        /// <code>{ recommendations: {...}, all: { smartGroup: [...], specific: [{code,name,provider}] } }</code>
        /// `smartGroup`(Americas·APAC …)은 대륙 묶음이라 구체 리전이 필요한 이 자리에는 쓰지 않는다.
        /// </summary>
        public static async Task<SupabaseResult<RegionInfo[]>> AvailableRegions(
            string token, string organizationSlug)
        {
            var url = $"{BASE}/available-regions?organization_slug={Uri.EscapeDataString(organizationSlug ?? "")}";
            var r = await RequestJson("GET", url, token);
            if (!r.Ok) return r.CarryFailure<RegionInfo[]>();

            return Parse(r, tok =>
            {
                var list = new List<RegionInfo>();
                var specific = tok?["all"]?["specific"] as JArray;
                foreach (var x in specific ?? new JArray())
                {
                    var code = (string)x["code"];
                    if (string.IsNullOrEmpty(code)) continue;
                    var name = (string)x["name"];
                    var provider = (string)x["provider"];
                    list.Add(new RegionInfo
                    {
                        code = code,
                        displayName = string.IsNullOrEmpty(provider) ? name : $"{name} · {provider}",
                    });
                }
                return list.ToArray();
            });
        }

        // ── API Keys ──

        /// <summary>anon key 조회. 새 프로젝트를 환경에 등록할 때 자동으로 채워 넣는다.</summary>
        public static async Task<SupabaseResult<string>> GetAnonKey(string projectRef, string token)
        {
            var r = await RequestJson("GET", $"{BASE}/{projectRef}/api-keys", token);
            if (!r.Ok) return r.CarryFailure<string>();

            var parsed = Parse(r, tok =>
            {
                foreach (var k in tok as JArray ?? new JArray())
                    if ((string)k["name"] == "anon")
                        return (string)k["api_key"];
                return null;
            });

            if (parsed.Ok && string.IsNullOrEmpty(parsed.Value))
                return SupabaseResult<string>.Failure(r.HttpStatus,
                    "{\"message\":\"응답에 anon key 가 없습니다. 프로젝트가 아직 준비 중일 수 있습니다.\"}");
            return parsed;
        }

        // ── Auth Config ──

        public static async Task<SupabaseResult<string>> GetAuthConfig(string projectRef, string token)
        {
            var r = await RequestJson("GET", $"{BASE}/{projectRef}/config/auth", token);
            return r.Ok ? SupabaseResult<string>.Success(r.Raw, r.HttpStatus, r.Raw) : r.CarryFailure<string>();
        }

        public static async Task<SupabaseResult<bool>> PatchAuthConfig(
            string projectRef, string token, string jsonBody)
        {
            var r = await RequestJson("PATCH", $"{BASE}/{projectRef}/config/auth", token, jsonBody);
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        // ── Edge Function ──
        // 소스를 **문자열 그대로** 올린다. `POST .../functions` 는 eszip 번들과 JSON 둘 다 받는데,
        // JSON 쪽은 `{slug, name, body, verify_jwt}` 만 있으면 되고 Supabase 가 서버에서 번들링한다.
        // 그 덕에 에디터에 Deno 툴체인이 없어도 배포가 된다.

        /// <summary>있으면 현재 버전, 없으면 NotFound.</summary>
        public static async Task<SupabaseResult<string>> GetFunction(
            string projectRef, string token, string slug)
            => await RequestJson("GET", $"{BASE}/{projectRef}/functions/{slug}", token);

        public static async Task<SupabaseResult<bool>> CreateFunction(
            string projectRef, string token, string slug, string name, string source, bool verifyJwt)
        {
            var body = new JObject
            {
                ["slug"] = slug,
                ["name"] = name,
                ["body"] = source,
                ["verify_jwt"] = verifyJwt,
            }.ToString(Formatting.None);

            var r = await RequestJson("POST", $"{BASE}/{projectRef}/functions", token, body);
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        public static async Task<SupabaseResult<bool>> UpdateFunction(
            string projectRef, string token, string slug, string name, string source, bool verifyJwt)
        {
            var body = new JObject
            {
                ["name"] = name,
                ["body"] = source,
                ["verify_jwt"] = verifyJwt,
            }.ToString(Formatting.None);

            var r = await RequestJson("PATCH", $"{BASE}/{projectRef}/functions/{slug}", token, body);
            return r.Ok ? SupabaseResult<bool>.Success(true, r.HttpStatus, r.Raw) : r.CarryFailure<bool>();
        }

        /// <summary>
        /// 프로젝트 하위 경로를 그대로 GET 한다. 응답은 파싱하지 않고 본문 그대로 준다.
        ///
        /// 전용 메서드를 만들지 않는 이유: 현황판이 쓰는 `health` / `config/disk/util` /
        /// `analytics/endpoints/metrics` 는 응답 형태가 제각각이고(마지막은 **Prometheus 텍스트**라
        /// JSON 도 아니다), 호출하는 쪽이 필요한 만큼만 뜯어 쓰는 편이 낫다.
        ///
        /// `path` 는 호출자가 지어낸 상수만 넘긴다 — 사용자 입력을 그대로 붙이지 말 것.
        /// </summary>
        public static async Task<SupabaseResult<string>> RawGet(
            string projectRef, string token, string path)
        {
            var r = await RequestJson("GET", $"{BASE}/{projectRef}/{path}", token);
            return r;   // 성공이면 Value 에 본문이 그대로 들어 있다
        }

        // ── Database ──

        /// <summary>SQL 원격 실행. 반환은 응답 본문(JSON 배열) 원문이다.</summary>
        public static async Task<SupabaseResult<string>> RunQuery(
            string projectRef, string token, string sql)
        {
            var body = new JObject { ["query"] = sql }.ToString(Formatting.None);
            var r = await RequestJson("POST", $"{BASE}/{projectRef}/database/query", token, body);
            return r.Ok ? SupabaseResult<string>.Success(r.Raw, r.HttpStatus, r.Raw) : r.CarryFailure<string>();
        }

        /// <summary>DB 의 max_connections. 스케일링 값 추천에 쓴다.</summary>
        public static async Task<SupabaseResult<int>> GetMaxConnections(string projectRef, string token)
        {
            var q = await RunQuery(projectRef, token, "SHOW max_connections;");
            if (!q.Ok) return q.CarryFailure<int>();

            try
            {
                var arr = JToken.Parse(q.Value) as JArray;
                var val = arr != null && arr.Count > 0 ? (string)((JObject)arr[0])["max_connections"] : null;
                if (int.TryParse(val, out var n)) return SupabaseResult<int>.Success(n, q.HttpStatus, q.Raw);
            }
            catch { /* 아래에서 실패로 처리 */ }

            return SupabaseResult<int>.Failure(q.HttpStatus,
                "{\"message\":\"응답에서 max_connections 를 찾지 못했습니다.\"}");
        }

        // ── 내부 ──

        static ProjectInfo ToProject(JToken p) => new()
        {
            id = (string)(p["id"] ?? p["ref"]),
            name = (string)p["name"],
            status = (string)p["status"],
            region = (string)p["region"],
            organizationId = (string)p["organization_id"],
            createdAt = (string)p["created_at"],
        };

        /// <summary>본문을 파싱해 T 로 바꾼다. 파싱이 깨지면 그것도 실패로 만든다(예외를 밖으로 던지지 않는다).</summary>
        static SupabaseResult<T> Parse<T>(SupabaseResult<string> raw, Func<JToken, T> map)
        {
            try
            {
                var token = string.IsNullOrWhiteSpace(raw.Value) ? null : JToken.Parse(raw.Value);
                return SupabaseResult<T>.Success(map(token), raw.HttpStatus, raw.Raw);
            }
            catch (Exception ex)
            {
                return SupabaseResult<T>.Failure(raw.HttpStatus,
                    new JObject { ["message"] = $"응답을 해석하지 못했습니다: {ex.Message}" }.ToString());
            }
        }

        /// <summary>HTTP 한 번. 성공이면 Value 에 본문이 담긴다.</summary>
        static async Task<SupabaseResult<string>> RequestJson(
            string method, string url, string token, string jsonBody = null)
        {
            try
            {
                using var request = new UnityWebRequest(url, method);
                if (jsonBody != null)
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");
                request.timeout = 30;

                var op = request.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                var code = request.responseCode;
                var body = request.downloadHandler.text;

                return code is >= 200 and < 300
                    ? SupabaseResult<string>.Success(body, code, body)
                    : SupabaseResult<string>.Failure(code, body);
            }
            catch (Exception ex)
            {
                return SupabaseResult<string>.Failure(ex);
            }
        }
    }
}
