using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// 터미널 실행의 플랫폼별 분기 헬퍼.
    /// Windows: Windows Terminal (wt) → PowerShell 폴백
    /// macOS:   iTerm2 → Terminal.app 폴백
    /// </summary>
    public static class PlatformTerminalLauncher
    {
        public enum TabRole { Main, Worktree }

        static bool IsMac => Application.platform == RuntimePlatform.OSXEditor;

        // 터미널 존재 감지 — 에디터 세션당 1회 캐시
        static bool? _hasWt;
        static bool? _hasITerm;

        static bool HasWindowsTerminal
        {
            get
            {
                if (_hasWt.HasValue) return _hasWt.Value;
                _hasWt = RunAndCheckExit("where", "wt");
                return _hasWt.Value;
            }
        }

        static bool HasITerm2
        {
            get
            {
                if (_hasITerm.HasValue) return _hasITerm.Value;
                _hasITerm = Directory.Exists("/Applications/iTerm.app");
                return _hasITerm.Value;
            }
        }

        /// <summary>
        /// 런처 진입점. workDir에서 command를 실행하는 새 터미널(창/탭)을 연다.
        /// role=Main: 새 창, role=Worktree: 기존 창의 새 탭.
        /// </summary>
        public static void Launch(TabRole role, string workDir, string title, Color tabColor, string command)
        {
            if (IsMac)
            {
                if (HasITerm2) LaunchMacITerm2(role, workDir, title, tabColor, command);
                else LaunchMacTerminal(role, workDir, title, tabColor, command);
            }
            else
            {
                LaunchWindows(role, workDir, title, tabColor, command);
            }
        }

        // ── Windows ──
        static void LaunchWindows(TabRole role, string workDir, string title, Color tabColor, string command)
        {
            var colorHex = ClaudeCodeSettings.ColorToHex(tabColor);
            if (HasWindowsTerminal)
            {
                var openVerb = role == TabRole.Main ? "" : "new-tab ";
                var wtArgs = $"-w {ClaudeCodeSettings.WindowName} {openVerb}" +
                             $"-d \"{workDir}\" " +
                             $"--tabColor \"{colorHex}\" " +
                             $"--title \"{title}\" " +
                             $"powershell -NoExit -Command \"{command}\"";
                StartShellProcess("wt", wtArgs, null);
            }
            else
            {
                StartShellProcess("powershell", $"-NoExit -Command \"{command}\"", workDir);
            }
        }

        // ── macOS: iTerm2 ──
        static void LaunchMacITerm2(TabRole role, string workDir, string title, Color tabColor, string command)
        {
            var shellCmd = BuildShellCommand(workDir, command);
            // iTerm AppleScript의 RGB는 16비트 (0-65535)
            var r = (int)(tabColor.r * 65535);
            var g = (int)(tabColor.g * 65535);
            var b = (int)(tabColor.b * 65535);

            string script;
            if (role == TabRole.Main)
            {
                script =
                    "tell application \"iTerm\"\n" +
                    "  activate\n" +
                    "  set newWindow to (create window with default profile)\n" +
                    "  tell current session of newWindow\n" +
                    $"    set name to \"{EscapeAppleScript(title)}\"\n" +
                    $"    write text \"{EscapeAppleScript(shellCmd)}\"\n" +
                    "  end tell\n" +
                    "  tell current tab of newWindow\n" +
                    $"    set tab color to {{{r}, {g}, {b}}}\n" +
                    "  end tell\n" +
                    "end tell";
            }
            else
            {
                script =
                    "tell application \"iTerm\"\n" +
                    "  activate\n" +
                    "  tell current window\n" +
                    "    set newTab to (create tab with default profile)\n" +
                    "    tell current session of newTab\n" +
                    $"      set name to \"{EscapeAppleScript(title)}\"\n" +
                    $"      write text \"{EscapeAppleScript(shellCmd)}\"\n" +
                    "    end tell\n" +
                    "    tell newTab\n" +
                    $"      set tab color to {{{r}, {g}, {b}}}\n" +
                    "    end tell\n" +
                    "  end tell\n" +
                    "end tell";
            }
            RunAppleScript(script);
        }

        // ── macOS: Terminal.app 폴백 ──
        // Terminal.app은 탭 색상 API가 없어, 제목 prefix로 역할을 시각화한다.
        static void LaunchMacTerminal(TabRole role, string workDir, string title, Color tabColor, string command)
        {
            var shellCmd = BuildShellCommand(workDir, command);
            var icon = role == TabRole.Main ? "🟣" : "🟠";
            var prefixedTitle = $"{icon} {title}";

            var script =
                "tell application \"Terminal\"\n" +
                "  activate\n" +
                $"  set newTab to do script \"{EscapeAppleScript(shellCmd)}\"\n" +
                $"  set custom title of newTab to \"{EscapeAppleScript(prefixedTitle)}\"\n" +
                "end tell";
            RunAppleScript(script);
        }

        // ── 헬퍼 ──

        static string BuildShellCommand(string workDir, string command)
        {
            if (string.IsNullOrEmpty(workDir)) return command;
            // POSIX 싱글쿼트 안전 이스케이프: ' → '\''
            var safeDir = workDir.Replace("'", "'\\''");
            return $"cd '{safeDir}' && {command}";
        }

        static string EscapeAppleScript(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static void RunAppleScript(string script)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                // ArgumentList로 직접 argv 전달 — 쉘 이스케이프 회피
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(script);

                using var p = Process.Start(psi);
                var stderr = p!.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                if (p.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                    Debug.LogError($"[Claude Code] AppleScript 실패: {stderr}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Claude Code] osascript 실행 실패: {ex.Message}");
            }
        }

        static bool RunAndCheckExit(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p!.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        static void StartShellProcess(string fileName, string arguments, string workDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(workDir))
                psi.WorkingDirectory = workDir;
            try { Process.Start(psi); }
            catch (Exception ex)
            {
                Debug.LogError($"[Claude Code] 프로세스 실행 실패: {ex.Message}");
            }
        }
    }
}
