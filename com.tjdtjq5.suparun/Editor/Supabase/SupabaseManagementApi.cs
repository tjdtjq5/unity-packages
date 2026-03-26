using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>Supabase Management API 유틸리티. Access Token 기반.</summary>
    public static class SupabaseManagementApi
    {
        const string BASE = "https://api.supabase.com/v1/projects";

        // ── 프로젝트 목록/정보 ──

        public struct ProjectInfo
        {
            public string id;     // ref
            public string name;
            public string status;
            public string region;
        }

        /// <summary>계정의 전체 프로젝트 목록 조회.</summary>
        public static async Task<(bool ok, ProjectInfo[] projects, string error)>
            ListProjects(string token)
        {
            var (code, body) = await Request("GET", BASE, token);
            if (code != 200)
                return (false, null, $"HTTP {code}: {body}");

            try
            {
                var list = new System.Collections.Generic.List<ProjectInfo>();
                var searchIdx = 0;
                while (searchIdx < body.Length)
                {
                    var objStart = body.IndexOf('{', searchIdx);
                    if (objStart < 0) break;

                    // 중첩 객체 건너뛰기 위해 brace depth 추적
                    var depth = 0;
                    var objEnd = objStart;
                    for (var i = objStart; i < body.Length; i++)
                    {
                        if (body[i] == '{') depth++;
                        else if (body[i] == '}') depth--;
                        if (depth == 0) { objEnd = i; break; }
                    }

                    var obj = body.Substring(objStart, objEnd - objStart + 1);
                    searchIdx = objEnd + 1;

                    var id = JsonHelper.GetString(obj, "id");
                    var name = JsonHelper.GetString(obj, "name");
                    if (string.IsNullOrEmpty(id)) continue;

                    list.Add(new ProjectInfo
                    {
                        id = id,
                        name = name,
                        status = JsonHelper.GetString(obj, "status"),
                        region = JsonHelper.GetString(obj, "region"),
                    });
                }

                return (true, list.ToArray(), null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>프로젝트 상태 조회. 연결 검증용.</summary>
        public static async Task<(bool ok, string name, string status, string region, string error)>
            GetProjectInfo(string projectRef, string token)
        {
            var (code, body) = await Request("GET", $"{BASE}/{projectRef}", token);
            if (code != 200)
                return (false, null, null, null, $"HTTP {code}: {body}");

            try
            {
                var name = JsonHelper.GetString(body, "name");
                var status = JsonHelper.GetString(body, "status");
                var region = JsonHelper.GetString(body, "region");
                return (true, name, status, region, null);
            }
            catch (Exception ex)
            {
                return (false, null, null, null, ex.Message);
            }
        }

        // ── API Keys ──

        /// <summary>Anon Key 자동 조회.</summary>
        public static async Task<(bool ok, string anonKey, string error)>
            GetAnonKey(string projectRef, string token)
        {
            var (code, body) = await Request("GET", $"{BASE}/{projectRef}/api-keys", token);
            if (code != 200)
                return (false, null, $"HTTP {code}: {body}");

            try
            {
                // 배열에서 name="anon" 또는 "publishable" 타입 항목 찾기
                // 배열의 각 객체를 순회하면서 anon key를 찾음
                string anonKey = null;
                var searchIdx = 0;
                while (searchIdx < body.Length)
                {
                    var objStart = body.IndexOf('{', searchIdx);
                    if (objStart < 0) break;

                    // 중첩 없는 단순 객체이므로 다음 }를 찾음
                    var objEnd = body.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    var obj = body.Substring(objStart, objEnd - objStart + 1);
                    searchIdx = objEnd + 1;

                    var name = JsonHelper.GetString(obj, "name");
                    if (name != "anon") continue;

                    anonKey = JsonHelper.GetString(obj, "api_key");
                    break;
                }

                if (string.IsNullOrEmpty(anonKey))
                    return (false, null, "anon key를 찾을 수 없습니다");

                return (true, anonKey, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        // ── Auth Config ──

        /// <summary>현재 Auth 설정 조회.</summary>
        public static async Task<(bool ok, string json, string error)>
            GetAuthConfig(string projectRef, string token)
        {
            var (code, body) = await Request("GET", $"{BASE}/{projectRef}/config/auth", token);
            if (code != 200)
                return (false, null, $"HTTP {code}: {body}");
            return (true, body, null);
        }

        /// <summary>Auth 설정 변경 (PATCH).</summary>
        public static async Task<(bool ok, string error)>
            PatchAuthConfig(string projectRef, string token, string jsonBody)
        {
            var (code, body) = await Request("PATCH", $"{BASE}/{projectRef}/config/auth", token, jsonBody);
            if (code == 200)
                return (true, null);
            return (false, $"HTTP {code}: {body}");
        }

        // ── Database ──

        /// <summary>SQL 쿼리 원격 실행 (Beta).</summary>
        public static async Task<(bool ok, string result, string error)>
            RunQuery(string projectRef, string token, string sql)
        {
            var jsonBody = $"{{\"query\":\"{EscapeJson(sql)}\"}}";
            var (code, body) = await Request("POST", $"{BASE}/{projectRef}/database/query", token, jsonBody);
            if (code == 200 || code == 201)
                return (true, body, null);
            return (false, null, $"HTTP {code}: {body}");
        }

        // ── Subscription ──

        /// <summary>DB의 max_connections 조회로 실제 연결 한도를 감지.</summary>
        public static async Task<(bool ok, int maxConnections, string error)>
            GetMaxConnections(string projectRef, string token)
        {
            var (queryOk, result, queryErr) = await RunQuery(projectRef, token, "SHOW max_connections;");
            if (!queryOk)
                return (false, 0, queryErr);

            // 응답에서 숫자 추출 — "max_connections":"60" 또는 [{"max_connections":"60"}]
            var val = JsonHelper.GetString(result, "max_connections");
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, out var maxConn))
                return (true, maxConn, null);

            return (false, 0, $"max_connections not found in: {result[..Math.Min(300, result.Length)]}");
        }

        // ── HTTP 헬퍼 ──

        static async Task<(long code, string body)> Request(string method, string url, string token, string jsonBody = null)
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

                return (request.responseCode, request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        // ── 간이 JSON 파서 ──

        static class JsonHelper
        {
            public static string GetString(string json, string key)
            {
                var pattern = $"\"{key}\"";
                var idx = json.IndexOf(pattern, StringComparison.Ordinal);
                if (idx < 0) return null;

                idx += pattern.Length;
                // : 찾기
                idx = json.IndexOf(':', idx);
                if (idx < 0) return null;
                idx++;

                // 공백 스킵
                while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t'))
                    idx++;

                if (idx >= json.Length) return null;

                // "문자열" 파싱
                if (json[idx] == '"')
                {
                    idx++;
                    var end = json.IndexOf('"', idx);
                    return end < 0 ? null : json.Substring(idx, end - idx);
                }

                // null
                if (json[idx] == 'n') return null;

                // 숫자/bool
                var valEnd = json.IndexOfAny(new[] { ',', '}', ']' }, idx);
                return valEnd < 0 ? json.Substring(idx).Trim() : json.Substring(idx, valEnd - idx).Trim();
            }

            public static bool GetBool(string json, string key)
            {
                var val = GetString(json, key);
                return val == "true";
            }
        }
    }
}
