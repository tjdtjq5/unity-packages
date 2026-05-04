#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.Codemagic.Editor.Codemagic
{
    /// <summary>
    /// Codemagic REST API HTTP 호출 래퍼 (Phase 1 MVP).
    /// 시크릿(token, env var value)은 절대 로깅하지 않는다.
    /// </summary>
    public sealed class CodemagicApiClient
    {
        const string BaseUrl = "https://api.codemagic.io";

        readonly string _token;

        public CodemagicApiClient(string token)
        {
            _token = token;
        }

        // ── 1. 토큰 검증 ──────────────────────────────────────────────────

        /// <summary>
        /// /apps?limit=1 호출로 토큰 유효성 확인. 200=ok, 401=invalid.
        /// </summary>
        public async UniTask<(bool ok, string error)> ValidateTokenAsync(CancellationToken ct = default)
        {
            var (status, body, error) = await SendAsync(UnityWebRequest.kHttpVerbGET, "/apps?limit=1", null, ct);

            if (status == 200) return (true, null);
            if (status == 401) return (false, "Invalid token");
            if (status == 0)   return (false, error ?? "Network error");
            return (false, $"HTTP {status}: {body}");
        }

        // ── 2. 앱 목록 ────────────────────────────────────────────────────

        /// <summary>
        /// /apps 호출 후 앱 목록 반환. 실패 시 빈 리스트 + LogError(토큰 노출 X).
        /// </summary>
        public async UniTask<List<CodemagicAppDto>> ListAppsAsync(CancellationToken ct = default)
        {
            var result = new List<CodemagicAppDto>();
            var (status, body, error) = await SendAsync(UnityWebRequest.kHttpVerbGET, "/apps", null, ct);

            if (status != 200)
            {
                Debug.LogError($"[CodemagicApiClient] ListApps failed: status={status} error={error ?? body}");
                return result;
            }

            try
            {
                var resp = JsonUtility.FromJson<_AppsResponse>(body);
                if (resp?.applications != null)
                {
                    foreach (var app in resp.applications)
                    {
                        if (app == null) continue;
                        result.Add(new CodemagicAppDto
                        {
                            AppId   = app._id,
                            AppName = app.appName,
                            RepoUrl = app.repository?.htmlUrl,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CodemagicApiClient] ListApps parse error: {ex.Message}");
            }

            return result;
        }

        // ── 3. 환경 변수 Upsert (DELETE → POST) ───────────────────────────
        //
        // ⚠ 사용 안 함 (v0.1.0). Codemagic은 환경 변수 그룹 관리를 공개 REST API로
        //   제공하지 않음 — `/apps/{id}/environment-variables`는 404 반환.
        //   Step 4/5는 GUI 가이드 + 클립보드 복사 walk-through로 대체됨.
        //   이 메서드는 v0.2.0+ 비공개 endpoint 역추적 시도 또는 제거 대상.

        /// <summary>
        /// [Deprecated for v0.1.0] Codemagic 공개 API에 환경 변수 endpoint 없음.
        /// 같은 key가 있어도 멱등하게 갱신하려는 의도였으나 404 발생.
        /// </summary>
        [Obsolete("Codemagic 공개 REST API에 환경 변수 endpoint 없음 — 호출 금지. " +
                  "Step 4/5의 GUI 가이드 walk-through 사용.")]
        public async UniTask<(bool ok, string error)> UpsertEnvVarAsync(
            string appId, string variableGroup, string key, string value, bool secure,
            CancellationToken ct = default)
        {
            // 1) 기존 키 삭제 시도. 404는 정상으로 간주.
            var deletePath = $"/apps/{appId}/environment-variables" +
                             $"?key={UnityWebRequest.EscapeURL(key)}" +
                             $"&group={UnityWebRequest.EscapeURL(variableGroup)}";
            var (delStatus, delBody, delError) = await SendAsync(
                UnityWebRequest.kHttpVerbDELETE, deletePath, null, ct);

            // 200/204/404는 OK (없는 걸 지운 경우 404). 그 외 에러는 흘려보내고 POST 시도.
            if (delStatus != 200 && delStatus != 204 && delStatus != 404)
            {
                Debug.LogWarning($"[CodemagicApiClient] UpsertEnvVar pre-DELETE non-fatal: status={delStatus} key={key}");
            }

            // 2) POST.
            var postBody = BuildEnvVarBody(key, value, variableGroup, secure);
            var (postStatus, postBodyText, postError) = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                $"/apps/{appId}/environment-variables",
                postBody, ct);

            if (postStatus == 200 || postStatus == 201) return (true, null);
            if (postStatus == 0) return (false, postError ?? "Network error");
            return (false, $"HTTP {postStatus}: {postBodyText}");
        }

        // ── 4. 빌드 시작 ──────────────────────────────────────────────────

        /// <summary>
        /// /builds 호출. 200/201이면 buildId 반환.
        /// </summary>
        public async UniTask<(bool ok, string buildId, string error)> StartBuildAsync(
            string appId, string workflowId, string branch, CancellationToken ct = default)
        {
            var jsonBody = BuildStartBuildBody(appId, workflowId, branch);
            var (status, body, error) = await SendAsync(
                UnityWebRequest.kHttpVerbPOST, "/builds", jsonBody, ct);

            if (status != 200 && status != 201)
            {
                if (status == 0) return (false, null, error ?? "Network error");
                return (false, null, $"HTTP {status}: {body}");
            }

            try
            {
                var resp = JsonUtility.FromJson<_BuildIdResponse>(body);
                if (resp != null && !string.IsNullOrEmpty(resp.buildId))
                    return (true, resp.buildId, null);
                return (false, null, "buildId missing in response");
            }
            catch (Exception ex)
            {
                return (false, null, $"Parse error: {ex.Message}");
            }
        }

        // ── 5. 빌드 상태 조회 ─────────────────────────────────────────────

        /// <summary>
        /// /builds/{buildId} 호출. 실패 시 null 반환.
        /// </summary>
        public async UniTask<CodemagicBuildDto> GetBuildAsync(string buildId, CancellationToken ct = default)
        {
            var (status, body, error) = await SendAsync(
                UnityWebRequest.kHttpVerbGET, $"/builds/{buildId}", null, ct);

            if (status != 200)
            {
                Debug.LogError($"[CodemagicApiClient] GetBuild failed: status={status} error={error ?? body} buildId={buildId}");
                return null;
            }

            try
            {
                var resp = JsonUtility.FromJson<_BuildResponse>(body);
                if (resp?.build == null) return null;
                return new CodemagicBuildDto
                {
                    BuildId    = resp.build._id,
                    Status     = resp.build.status,
                    WorkflowId = resp.build.workflowId,
                    CreatedAt  = resp.build.createdAt,
                    FinishedAt = resp.build.finishedAt,
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CodemagicApiClient] GetBuild parse error: {ex.Message}");
                return null;
            }
        }

        // ── 내부 헬퍼 ─────────────────────────────────────────────────────

        async UniTask<(int status, string body, string error)> SendAsync(
            string method, string path, string jsonBody = null, CancellationToken ct = default)
        {
            using var req = new UnityWebRequest(BaseUrl + path, method);

            if (!string.IsNullOrEmpty(jsonBody))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("x-auth-token", _token);
            req.SetRequestHeader("Content-Type", "application/json");

            try
            {
                await req.SendWebRequest().ToUniTask(cancellationToken: ct);
            }
            catch (UnityWebRequestException ex)
            {
                // HTTP 에러 (4xx/5xx)도 이 경로로 옴 — responseCode/text는 ex로부터 회수.
                return ((int)ex.ResponseCode, ex.Text, ex.Message);
            }
            catch (OperationCanceledException ex)
            {
                return (0, null, ex.Message);
            }
            catch (Exception ex)
            {
                return (0, null, ex.Message);
            }

            return ((int)req.responseCode, req.downloadHandler?.text, req.error);
        }

        // ── JSON 빌더 (시크릿 노출 방지를 위해 수동 구성) ─────────────────

        static string BuildEnvVarBody(string key, string value, string group, bool secure)
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            AppendJsonString(sb, "key",   key);    sb.Append(',');
            AppendJsonString(sb, "value", value);  sb.Append(',');
            AppendJsonString(sb, "group", group);  sb.Append(',');
            sb.Append("\"secure\":").Append(secure ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }

        static string BuildStartBuildBody(string appId, string workflowId, string branch)
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            AppendJsonString(sb, "appId",      appId);      sb.Append(',');
            AppendJsonString(sb, "workflowId", workflowId); sb.Append(',');
            AppendJsonString(sb, "branch",     branch);
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendJsonString(StringBuilder sb, string field, string value)
        {
            sb.Append('"').Append(field).Append("\":\"");
            EscapeJsonString(sb, value ?? "");
            sb.Append('"');
        }

        static void EscapeJsonString(StringBuilder sb, string s)
        {
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else          sb.Append(c);
                        break;
                }
            }
        }

        // ── JsonUtility wrapper DTO (private) ─────────────────────────────

        [Serializable] class _AppsResponse    { public _AppDto[] applications; }
        [Serializable] class _AppDto          { public string _id; public string appName; public _RepoDto repository; }
        [Serializable] class _RepoDto         { public string htmlUrl; }
        [Serializable] class _BuildResponse   { public _BuildDto build; }
        [Serializable] class _BuildDto        { public string _id; public string status; public string workflowId; public string createdAt; public string finishedAt; }
        [Serializable] class _BuildIdResponse { public string buildId; }
    }

    // ── public DTO ────────────────────────────────────────────────────────

    [Serializable]
    public sealed class CodemagicAppDto
    {
        public string AppId;
        public string AppName;
        public string RepoUrl;
    }

    [Serializable]
    public sealed class CodemagicBuildDto
    {
        public string BuildId;
        public string Status;
        public string WorkflowId;
        public string CreatedAt;
        public string FinishedAt;
    }
}
#endif
