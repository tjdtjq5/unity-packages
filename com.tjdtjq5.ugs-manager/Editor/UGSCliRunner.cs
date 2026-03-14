#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine.Networking;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// UGS CLI 실행 유틸. Process를 래핑하여 동기/비동기 실행을 제공한다.
    /// 크로스플랫폼 (Windows/macOS/Linux) 지원.
    /// </summary>
    public static class UGSCliRunner
    {
        public struct CliResult
        {
            public bool Success;
            public string Output;
            public string Error;
            public int ExitCode;
        }

        const int DEFAULT_TIMEOUT_MS = 30000;
        const int DEPLOY_TIMEOUT_MS = 120000;

        static string _cliPath;

        // ─── 동기 실행 ──────────────────────────────────

        /// <summary>CLI 명령 동기 실행 (짧은 명령용)</summary>
        public static CliResult Run(string arguments, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            try
            {
                var psi = CreateStartInfo(arguments);
                using var process = Process.Start(psi);
                if (process == null)
                    return new CliResult { Success = false, Error = "프로세스 시작 실패" };

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { /* ignore */ }
                    return new CliResult { Success = false, Error = "타임아웃", ExitCode = -1 };
                }

                error = FilterDeprecationWarnings(error);

                return new CliResult
                {
                    Success = process.ExitCode == 0,
                    Output = output?.Trim() ?? "",
                    Error = error?.Trim() ?? "",
                    ExitCode = process.ExitCode
                };
            }
            catch (Exception ex)
            {
                return new CliResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>CLI 명령 실행 후 JSON 출력 (-j 플래그 자동 추가)</summary>
        public static CliResult RunJson(string arguments, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            return Run($"{arguments} -j -q", timeoutMs);
        }

        // ─── 비동기 실행 ─────────────────────────────────

        /// <summary>CLI 명령 비동기 실행 (Deploy 등 긴 명령용)</summary>
        public static void RunAsync(string arguments, Action<CliResult> onComplete,
            int timeoutMs = DEPLOY_TIMEOUT_MS)
        {
            if (onComplete == null) return;

            try
            {
                var psi = CreateStartInfo(arguments);
                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                var stdOut = new StringBuilder();
                var stdErr = new StringBuilder();

                DataReceivedEventHandler outHandler = (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
                DataReceivedEventHandler errHandler = (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

                process.OutputDataReceived += outHandler;
                process.ErrorDataReceived += errHandler;

                process.Exited += (_, _) =>
                {
                    // 이벤트 핸들러 해제 (메모리 누수 방지)
                    process.OutputDataReceived -= outHandler;
                    process.ErrorDataReceived -= errHandler;

                    string filteredErr = FilterDeprecationWarnings(stdErr.ToString());
                    int exitCode;
                    try { exitCode = process.ExitCode; } catch { exitCode = -1; }

                    var result = new CliResult
                    {
                        Success = exitCode == 0,
                        Output = stdOut.ToString().Trim(),
                        Error = filteredErr?.Trim() ?? "",
                        ExitCode = exitCode
                    };

                    process.Dispose();

                    // 메인 스레드에서 콜백 (도메인 리로드 중이면 무시)
                    EditorApplication.delayCall += () =>
                    {
                        if (!EditorApplication.isCompiling)
                            onComplete.Invoke(result);
                    };
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                var result = new CliResult { Success = false, Error = ex.Message };
                EditorApplication.delayCall += () => onComplete.Invoke(result);
            }
        }

        // ─── 상태 조회 ──────────────────────────────────

        /// <summary>CLI 설치 여부</summary>
        public static bool IsInstalled()
        {
            var result = Run("--version", 5000);
            return result.Success;
        }

        /// <summary>로그인 상태 확인</summary>
        public static bool IsLoggedIn()
        {
            var result = Run("status", 5000);
            return result.Success && !string.IsNullOrEmpty(result.Output) &&
                   result.Output.Contains("Service Account");
        }

        /// <summary>CLI 버전</summary>
        public static string GetCliVersion()
        {
            var result = Run("--version", 5000);
            return result.Success ? result.Output.Trim() : "N/A";
        }

        /// <summary>현재 Project ID</summary>
        public static string GetProjectId()
        {
            var result = Run("config get project-id", 5000);
            return result.Success ? result.Output.Trim() : "";
        }

        /// <summary>현재 Environment</summary>
        public static string GetEnvironment()
        {
            var result = Run("config get environment-name", 5000);
            return result.Success ? result.Output.Trim() : "";
        }

        /// <summary>CLI 경로 캐시 초기화 (경로 변경 시)</summary>
        public static void ResetCliPath() => _cliPath = null;

        // ─── REST API ──────────────────────────────────

        static string _cachedCredentials;
        static string _cachedEnvId;

        /// <summary>Service Account credentials (Base64) 읽기</summary>
        static string GetCredentials()
        {
            if (!string.IsNullOrEmpty(_cachedCredentials)) return _cachedCredentials;

            // {LOCALAPPDATA}/UnityServices/credentials
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string credPath = Path.Combine(localAppData, "UnityServices", "credentials");
            if (!File.Exists(credPath)) return null;

            string raw = File.ReadAllText(credPath).Trim().Trim('"');
            if (!string.IsNullOrEmpty(raw)) _cachedCredentials = raw;
            return _cachedCredentials;
        }

        /// <summary>현재 활성 환경의 ID 조회 (캐시)</summary>
        public static string GetEnvironmentId()
        {
            if (!string.IsNullOrEmpty(_cachedEnvId)) return _cachedEnvId;

            string activeEnv = GetEnvironment();
            if (string.IsNullOrEmpty(activeEnv)) return "";

            var result = RunJson("env list");
            if (!result.Success) return "";

            string json = result.Output;
            int sf = 0;
            while (true)
            {
                int os = json.IndexOf('{', sf); if (os < 0) break;
                int oe = json.IndexOf('}', os); if (oe < 0) break;
                string blk = json.Substring(os, oe - os + 1);

                string nameKey = "\"name\"";
                int ni = blk.IndexOf(nameKey, StringComparison.Ordinal);
                if (ni < 0) { sf = oe + 1; continue; }
                int nc = blk.IndexOf(':', ni + nameKey.Length);
                int nqs = blk.IndexOf('"', nc + 1);
                int nqe = blk.IndexOf('"', nqs + 1);
                string name = nqe > nqs ? blk.Substring(nqs + 1, nqe - nqs - 1) : "";

                if (name == activeEnv)
                {
                    string idKey = "\"id\"";
                    int ii = blk.IndexOf(idKey, StringComparison.Ordinal);
                    if (ii >= 0)
                    {
                        int ic = blk.IndexOf(':', ii + idKey.Length);
                        int iqs = blk.IndexOf('"', ic + 1);
                        int iqe = blk.IndexOf('"', iqs + 1);
                        if (iqe > iqs) _cachedEnvId = blk.Substring(iqs + 1, iqe - iqs - 1);
                    }
                    break;
                }
                sf = oe + 1;
            }
            return _cachedEnvId ?? "";
        }

        /// <summary>환경 변경 시 캐시 초기화</summary>
        public static void ResetEnvIdCache() => _cachedEnvId = null;

        /// <summary>
        /// Cloud Code 스크립트 파라미터 스키마를 REST API로 등록 + publish.
        /// 1) PATCH → draft에 params 저장
        /// 2) POST .../publish → draft를 active로 배포
        /// </summary>
        public static void PatchScriptParameters(string scriptName, string paramsJsonArray, Action<bool, string> onComplete)
        {
            string cred = GetCredentials();
            if (string.IsNullOrEmpty(cred))
            {
                onComplete?.Invoke(false, "credentials 파일을 찾을 수 없습니다.");
                return;
            }

            string pid = GetProjectId();
            string eid = GetEnvironmentId();
            if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(eid))
            {
                onComplete?.Invoke(false, "project-id 또는 environment-id를 가져올 수 없습니다.");
                return;
            }

            string baseUrl = $"https://services.api.unity.com/cloud-code/v1/projects/{pid}/environments/{eid}/scripts/{scriptName}";

            // Step 1: PATCH — params를 draft에 저장
            string patchBody = $"{{\"params\":{paramsJsonArray}}}";
            var patchReq = new UnityWebRequest(baseUrl, "PATCH");
            patchReq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(patchBody));
            patchReq.downloadHandler = new DownloadHandlerBuffer();
            patchReq.SetRequestHeader("Authorization", $"Basic {cred}");
            patchReq.SetRequestHeader("Content-Type", "application/json");

            var patchOp = patchReq.SendWebRequest();
            patchOp.completed += _ =>
            {
                bool patchOk = patchReq.responseCode is 204 or 200;
                if (!patchOk)
                {
                    string err = $"PATCH HTTP {patchReq.responseCode}: {patchReq.downloadHandler?.text}";
                    patchReq.Dispose();
                    EditorApplication.delayCall += () => onComplete?.Invoke(false, err);
                    return;
                }
                patchReq.Dispose();

                // Step 2: POST publish — draft를 active로
                string publishUrl = $"{baseUrl}/publish";
                var pubReq = new UnityWebRequest(publishUrl, "POST");
                pubReq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                pubReq.downloadHandler = new DownloadHandlerBuffer();
                pubReq.SetRequestHeader("Authorization", $"Basic {cred}");
                pubReq.SetRequestHeader("Content-Type", "application/json");

                var pubOp = pubReq.SendWebRequest();
                pubOp.completed += _ =>
                {
                    bool pubOk = pubReq.responseCode is 200 or 204;
                    string error = pubOk ? "" : $"Publish HTTP {pubReq.responseCode}: {pubReq.downloadHandler?.text}";
                    pubReq.Dispose();
                    EditorApplication.delayCall += () => onComplete?.Invoke(pubOk, error);
                };
            };
        }

        // ─── 내부 유틸 ──────────────────────────────────

        /// <summary>CLI 실행 경로 자동 탐색 (Windows/macOS/Linux)</summary>
        static string FindCliPath()
        {
            if (!string.IsNullOrEmpty(_cliPath)) return _cliPath;

            // 1) PATH에서 직접 찾기
            if (TryRunCli("ugs")) { _cliPath = "ugs"; return _cliPath; }

            bool isWindows = System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            if (isWindows)
            {
                // 2) Windows: npm 글로벌 경로
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string[] winCandidates =
                {
                    System.IO.Path.Combine(appData, "npm", "ugs.cmd"),
                    System.IO.Path.Combine(appData, "npm", "ugs"),
                };
                foreach (var path in winCandidates)
                {
                    if (System.IO.File.Exists(path)) { _cliPath = path; return _cliPath; }
                }

                // 3) Windows: where 명령
                _cliPath = FindWithCommand("where", "ugs") ?? "ugs";
            }
            else
            {
                // 2) macOS/Linux: 일반적인 경로
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] unixCandidates =
                {
                    "/usr/local/bin/ugs",
                    "/usr/bin/ugs",
                    System.IO.Path.Combine(home, ".npm-global", "bin", "ugs"),
                    System.IO.Path.Combine(home, ".nvm", "current", "bin", "ugs"),
                };
                foreach (var path in unixCandidates)
                {
                    if (System.IO.File.Exists(path)) { _cliPath = path; return _cliPath; }
                }

                // 3) macOS/Linux: which 명령
                _cliPath = FindWithCommand("which", "ugs") ?? "ugs";
            }

            return _cliPath;
        }

        static bool TryRunCli(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path, Arguments = "--version",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        static string FindWithCommand(string cmd, string arg)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cmd, Arguments = arg,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                if (p.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    return output.Split('\n')[0].Trim();
            }
            catch { /* ignore */ }
            return null;
        }

        static ProcessStartInfo CreateStartInfo(string arguments)
        {
            return new ProcessStartInfo
            {
                FileName = FindCliPath(),
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        /// <summary>Node.js DeprecationWarning 필터링</summary>
        static string FilterDeprecationWarnings(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";

            var lines = error.Split('\n');
            var filtered = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.Contains("DeprecationWarning") || line.Contains("DEP0190") ||
                    line.Contains("--trace-deprecation"))
                    continue;
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    filtered.AppendLine(trimmed);
            }
            return filtered.ToString().Trim();
        }
    }
}
#endif
