using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        // SessionState: 에디터 세션 동안만 유지, 재시작 시 리셋
        const string MainLaunchedKey = "ClaudeCode_MainLaunched";
        const string WorktreeCountKey = "ClaudeCode_WtCount";

        static bool MainLaunched
        {
            get => SessionState.GetBool(MainLaunchedKey, false);
            set => SessionState.SetBool(MainLaunchedKey, value);
        }

        static int WorktreeCount
        {
            get => SessionState.GetInt(WorktreeCountKey, 0);
            set => SessionState.SetInt(WorktreeCountKey, value);
        }

        static string ProjectPath =>
            Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');

        static bool HasWindowsTerminal
        {
            get
            {
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
                    p!.WaitForExit(3000);
                    return p.ExitCode == 0;
                }
                catch { return false; }
            }
        }

        // ── 메뉴 아이템 ──

        [MenuItem("Tools/Claude Code/Open")]
        public static void Open()
        {
            if (!MainLaunched)
                LaunchMain();
            else
                LaunchWorktree();
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

        // ── 워크트리 실행 ──

        static void LaunchWorktree()
        {
            // 기존 worktree 목록을 확인하여 사용 가능한 번호 찾기
            var count = FindNextAvailableWorktreeNumber();
            WorktreeCount = count;

            var wtName = $"wt-{count}";
            var wtPath = Path.GetFullPath(
                Path.Combine(ProjectPath, "..", $"{Path.GetFileName(ProjectPath)}-{wtName}"))
                .Replace('\\', '/');
            var branchName = $"wt/{count}";

            // 이미 존재하는 worktree면 그대로 재사용 (새 터미널만 열기)
            bool worktreeExists = Directory.Exists(wtPath);
            if (!worktreeExists)
            {
                var gitResult = RunGit($"worktree add \"{wtPath}\" -b \"{branchName}\"", ProjectPath);
                if (gitResult.exitCode != 0)
                {
                    // 브랜치가 이미 있으면 브랜치 없이 재시도
                    if (gitResult.output.Contains("already exists"))
                        gitResult = RunGit($"worktree add \"{wtPath}\"", ProjectPath);

                    if (gitResult.exitCode != 0)
                    {
                        Debug.LogError($"[Claude Code] 워크트리 생성 실패: {gitResult.output}");
                        wtPath = ProjectPath;
                    }
                }
            }

            var args = BuildClaudeCommand();
            var colorHex = ClaudeCodeSettings.ColorToHex(ClaudeCodeSettings.WorktreeTabColor);

            if (HasWindowsTerminal)
            {
                var wtArgs = $"-w {ClaudeCodeSettings.WindowName} new-tab " +
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

        // ── 헬퍼 ──

        /// <summary>git worktree list를 파싱하여 다음 사용 가능한 번호를 반환</summary>
        static int FindNextAvailableWorktreeNumber()
        {
            var result = RunGit("worktree list --porcelain", ProjectPath);
            int maxNum = 0;
            if (result.exitCode == 0)
            {
                var projectName = Path.GetFileName(ProjectPath);
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
                var stdout = p!.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                return (p.ExitCode, stdout + stderr);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }
    }
}
