using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Claude Code를 Windows Terminal / PowerShell로 실행하는 런처.
    /// 첫 실행 = 메인, 이후 = git worktree + 새 탭.
    /// </summary>
    public static class ClaudeCodeLauncher
    {
        // EditorPrefs: 에디터 재시작에도 유지 (SessionState는 세션 종료 시 리셋됨)
        internal const string MainLaunchedKey = "ClaudeCode_MainLaunched";

        static bool _launching;

        static bool MainLaunched
        {
            get => EditorPrefs.GetBool(MainLaunchedKey, false);
            set => EditorPrefs.SetBool(MainLaunchedKey, value);
        }

        static string ProjectPath =>
            Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');

        // wt 존재 여부 캐시 (에디터 세션당 1회만 확인)
        static bool? _hasWt;

        static bool HasWindowsTerminal
        {
            get
            {
                if (_hasWt.HasValue) return _hasWt.Value;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "wt",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p!.StandardOutput.ReadToEnd(); // 버퍼 소비하여 데드락 방지
                    p.WaitForExit(3000);
                    _hasWt = p.ExitCode == 0;
                }
                catch { _hasWt = false; }
                return _hasWt.Value;
            }
        }

        // ── 메뉴 아이템 ──

        [MenuItem("Tools/Claude Code/Open")]
        public static void Open()
        {
            if (_launching) return; // 연타 방지

            if (!MainLaunched)
                LaunchMain();
            else
                LaunchWorktreeAsync();
        }

        [MenuItem("Tools/Claude Code/Settings")]
        static void OpenSettings()
        {
            ClaudeCodeSettingsWindow.Open();
        }

        // ── 메인 실행 ──

        static void LaunchMain()
        {
            var args = BuildClaudeCommand();
            var colorHex = ClaudeCodeSettings.ColorToHex(ClaudeCodeSettings.MainTabColor);

            if (HasWindowsTerminal)
            {
                var wtArgs = $"-w {ClaudeCodeSettings.WindowName} " +
                             $"-d \"{ProjectPath}\" " +
                             $"--tabColor \"{colorHex}\" " +
                             $"--title \"Claude Main\" " +
                             $"powershell -NoExit -Command \"{args}\"";
                StartProcess("wt", wtArgs);
            }
            else
            {
                StartProcess("powershell", $"-NoExit -Command \"{args}\"", ProjectPath);
            }

            MainLaunched = true;
            Debug.Log($"[Claude Code] 메인 실행 — {ProjectPath}");
        }

        // ── 워크트리 실행 (백그라운드) ──

        static void LaunchWorktreeAsync()
        {
            _launching = true;

            // UI 스레드에서 필요한 값을 미리 캡처
            var projectPath = ProjectPath;
            var args = BuildClaudeCommand();
            var colorHex = ClaudeCodeSettings.ColorToHex(ClaudeCodeSettings.WorktreeTabColor);
            var windowName = ClaudeCodeSettings.WindowName;
            bool hasWt = HasWindowsTerminal;

            Debug.Log("[Claude Code] 워크트리 준비 중...");

            // git 작업을 백그라운드 스레드에서 실행하여 UI 차단 방지
            Task.Run(() =>
            {
                try
                {
                    var count = FindNextAvailableWorktreeNumber(projectPath);

                    var wtName = $"wt-{count}";
                    var wtPath = Path.GetFullPath(
                        Path.Combine(projectPath, "..", $"{Path.GetFileName(projectPath)}-{wtName}"))
                        .Replace('\\', '/');
                    var branchName = $"wt/{count}";

                    bool worktreeExists = Directory.Exists(wtPath);
                    if (!worktreeExists)
                    {
                        var gitResult = RunGit($"worktree add \"{wtPath}\" -b \"{branchName}\"", projectPath);
                        if (gitResult.exitCode != 0)
                        {
                            if (gitResult.output.Contains("already exists"))
                                gitResult = RunGit($"worktree add \"{wtPath}\"", projectPath);

                            if (gitResult.exitCode != 0)
                            {
                                Debug.LogError($"[Claude Code] 워크트리 생성 실패: {gitResult.output}");
                                wtPath = projectPath;
                            }
                        }
                    }

                    // 터미널 실행
                    if (hasWt)
                    {
                        var wtArgs = $"-w {windowName} new-tab " +
                                     $"-d \"{wtPath}\" " +
                                     $"--tabColor \"{colorHex}\" " +
                                     $"--title \"Claude {wtName}\" " +
                                     $"powershell -NoExit -Command \"{args}\"";
                        StartProcess("wt", wtArgs);
                    }
                    else
                    {
                        StartProcess("powershell", $"-NoExit -Command \"{args}\"", wtPath);
                    }

                    Debug.Log($"[Claude Code] 워크트리 실행 — {wtPath} (branch: {branchName})");
                }
                finally
                {
                    _launching = false;
                }
            });
        }

        // ── 헬퍼 ──

        /// <summary>git worktree list를 파싱하여 다음 사용 가능한 번호를 반환</summary>
        static int FindNextAvailableWorktreeNumber(string projectPath)
        {
            var result = RunGit("worktree list --porcelain", projectPath);
            int maxNum = 0;
            if (result.exitCode == 0)
            {
                var projectName = Path.GetFileName(projectPath);
                var prefix = $"{projectName}-wt-";
                foreach (var line in result.output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("worktree ")) continue;
                    var wtDir = Path.GetFileName(trimmed.Substring(9).Trim());
                    if (wtDir.StartsWith(prefix) &&
                        int.TryParse(wtDir.Substring(prefix.Length), out int num) &&
                        num > maxNum)
                    {
                        maxNum = num;
                    }
                }
            }
            return maxNum + 1;
        }

        static string BuildClaudeCommand()
        {
            var sb = new StringBuilder("claude");
            var extra = ClaudeCodeSettings.AdditionalArgs.Trim();
            if (!string.IsNullOrEmpty(extra))
                sb.Append(' ').Append(extra);
            return sb.ToString();
        }

        static void StartProcess(string fileName, string arguments, string workDir = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };
            if (!string.IsNullOrEmpty(workDir))
                psi.WorkingDirectory = workDir;

            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Claude Code] 프로세스 시작 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// git 명령 실행. stderr를 병렬로 읽어 데드락을 방지한다.
        /// </summary>
        static (int exitCode, string output) RunGit(string arguments, string workDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);

                // stderr를 병렬로 읽어 버퍼 풀 데드락 방지
                string stderr = null;
                var stderrTask = Task.Run(() => stderr = p!.StandardError.ReadToEnd());
                var stdout = p!.StandardOutput.ReadToEnd();
                stderrTask.Wait(30000);

                p.WaitForExit(30000);
                return (p.ExitCode, stdout + (stderr ?? ""));
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }
    }
}
