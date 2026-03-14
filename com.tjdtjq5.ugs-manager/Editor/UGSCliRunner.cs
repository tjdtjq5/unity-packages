#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;

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
