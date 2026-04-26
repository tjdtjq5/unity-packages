using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// 터미널 실행의 플랫폼별 분기 헬퍼.
    /// Windows: Windows Terminal (wt) → PowerShell 폴백
    /// macOS:   cmux → iTerm2 → Terminal.app 폴백
    /// </summary>
    public static class PlatformTerminalLauncher
    {
        public enum TabRole { Main, Worktree }

        static bool IsMac => Application.platform == RuntimePlatform.OSXEditor;

        // 터미널 존재 감지 — 에디터 세션당 1회 캐시
        static bool? _hasWt;
        static bool? _hasITerm;
        static bool? _hasCmux;
        static string _cmuxBinPath;

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

        // cmux: 앱(.app)과 CLI 둘 다 있을 때만 사용. CLI는 Unix 소켓으로 데몬 제어.
        static bool HasCmux
        {
            get
            {
                if (_hasCmux.HasValue) return _hasCmux.Value;
                if (!Directory.Exists("/Applications/cmux.app")) { _hasCmux = false; return false; }
                _cmuxBinPath = ResolveCmuxBinary();
                _hasCmux = !string.IsNullOrEmpty(_cmuxBinPath);
                return _hasCmux.Value;
            }
        }

        static string ResolveCmuxBinary()
        {
            // Unity Editor 프로세스의 PATH에는 Homebrew bin이 없을 수 있어 절대경로 우선.
            if (File.Exists("/opt/homebrew/bin/cmux")) return "/opt/homebrew/bin/cmux";
            if (File.Exists("/usr/local/bin/cmux")) return "/usr/local/bin/cmux";
            return RunAndCheckExit("which", "cmux") ? "cmux" : null;
        }

        /// <summary>
        /// 런처 진입점. workDir에서 command를 실행하는 새 터미널(창/탭)을 연다.
        /// role=Main: 새 창, role=Worktree: 기존 창의 새 탭.
        /// </summary>
        public static void Launch(TabRole role, string workDir, string title, Color tabColor, string command)
        {
            if (IsMac)
            {
                // cmux가 설치돼 있으면 cmux만 사용 — 실패해도 다른 터미널을 추가로 띄우지 않는다.
                // 사용자가 명시적으로 cmux를 설치한 만큼 의도와 다른 터미널이 같이 뜨는 일을 막기 위함.
                if (HasCmux) { LaunchMacCmux(role, workDir, title, tabColor, command); return; }
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

        // ── macOS: cmux ──
        // cmux는 workspace 단위로 탭/창을 관리한다. CLI(`cmux new-workspace`)는
        // Unix 소켓으로 데몬과 통신하므로, 앱이 떠 있어야 한다. 데몬이 없으면
        // `open -a cmux`로 부팅 후 ping 폴링으로 준비를 기다린다.
        // 실패 시 false를 반환하여 호출자가 iTerm2/Terminal로 폴백할 수 있게 한다.
        // 색상은 CLI에 직접 노출된 옵션이 없어 제목 prefix로 역할을 표시한다.
        // 세션당 1회만 자동 설정/재시작을 시도한다.
        static bool _autoSetupAttempted;

        static void LaunchMacCmux(TabRole role, string workDir, string title, Color tabColor, string command)
        {
            var icon = role == TabRole.Main ? "🟣" : "🟠";
            var prefixedTitle = $"{icon} {title}";
            var bin = _cmuxBinPath ?? "cmux";
            var wrappedCommand = WrapForInteractiveShell(command);

            // 1차: 데몬 활성화 + 워크스페이스 시도.
            if (TryWakeAndCreateWorkspace(bin, workDir, prefixedTitle, wrappedCommand, out var stderr))
                return;

            // 실패 원인이 socketControlMode = cmuxOnly(기본값)면 외부 CLI가 막혀 있다.
            // settings.json을 수정하고 cmux를 재시작해서 자동화 모드로 전환한다.
            if (!_autoSetupAttempted &&
                (stderr?.Contains("Broken pipe") == true || stderr?.Contains("Socket not found") == true))
            {
                _autoSetupAttempted = true;

                if (TryAutoEnableCmuxAutomation(out var setupMessage))
                {
                    var proceed = EditorUtility.DisplayDialog(
                        "cmux 자동 설정",
                        "cmux 외부 CLI 접근이 비활성화돼 있어 자동화 모드로 전환합니다.\n" +
                        "변경 적용을 위해 cmux를 재시작해야 합니다 (열려있는 작업은 닫힙니다).\n\n" +
                        $"세부: {setupMessage}",
                        "재시작 후 진행", "취소");
                    if (proceed)
                    {
                        RestartCmuxApp();
                        if (TryWakeAndCreateWorkspace(bin, workDir, prefixedTitle, wrappedCommand, out stderr))
                            return;
                    }
                }
                else
                {
                    Debug.LogWarning($"[Claude Code] cmux 자동 설정 실패: {setupMessage}");
                }
            }

            Debug.LogError($"[Claude Code] cmux new-workspace 실패: {stderr}");
        }

        static bool TryWakeAndCreateWorkspace(string bin, string workDir, string title, string command, out string lastStderr)
        {
            lastStderr = null;

            // 데몬이 이미 응답하면 open -a cmux를 호출하지 않는다.
            // 이미 떠있는 cmux에 open을 다시 때리면 일부 환경(워크스페이스 placement 설정,
            // 메인 창이 닫혀 있고 데몬만 떠있는 상태 등)에서 워크스페이스가 attach되지 않은
            // 빈 윈도우가 추가로 뜨는 케이스를 본다. 사용자가 본 "입력 안 되는 빈 창"이 그것.
            bool pingOk = RunAndCheckExit(bin, "ping");
            if (!pingOk)
            {
                StartShellProcess("open", "-a cmux", null);
                for (int i = 0; i < 60; i++)
                {
                    System.Threading.Thread.Sleep(200);
                    if (RunAndCheckExit(bin, "ping")) { pingOk = true; break; }
                }
                // 콜드 스타트 직후 UI 초기화가 끝날 시간을 잠깐 준다.
                if (pingOk) System.Threading.Thread.Sleep(700);
            }

            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (TryCmuxNewWorkspace(bin, workDir, title, command, out var workspaceRef, out lastStderr))
                {
                    // 새 워크스페이스가 만들어진 창을 명시적으로 포커스. 새 워크스페이스가
                    // 사용자가 보고 있는 창이 아닌 다른 창에 추가될 경우(또는 cmux가 백그라운드
                    // 상태일 경우) 사용자가 그 워크스페이스를 못 보는 문제를 막는다. 실패는 무시.
                    TryFocusWorkspace(bin, workspaceRef);
                    return true;
                }
                System.Threading.Thread.Sleep(400);
            }
            if (string.IsNullOrEmpty(lastStderr) && !pingOk)
                lastStderr = "데몬 응답 없음 (ping timeout)";
            return false;
        }

        // 워크스페이스가 속한 윈도우를 찾아 활성화한다.
        // identify는 JSON을 반환하므로 정규식 한 줄로 window_ref만 뽑는다.
        static void TryFocusWorkspace(string bin, string workspaceRef)
        {
            if (string.IsNullOrEmpty(workspaceRef)) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = bin,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                psi.ArgumentList.Add("identify");
                psi.ArgumentList.Add("--workspace");
                psi.ArgumentList.Add(workspaceRef);
                psi.ArgumentList.Add("--no-caller");
                using var p = Process.Start(psi);
                if (p == null) return;
                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(2000);
                if (p.ExitCode != 0) return;

                var match = System.Text.RegularExpressions.Regex.Match(
                    stdout, "\"window_ref\"\\s*:\\s*\"(window:[^\"]+)\"");
                if (!match.Success) return;
                var windowRef = match.Groups[1].Value;

                using var fp = Process.Start(new ProcessStartInfo
                {
                    FileName = bin,
                    Arguments = $"focus-window --window {windowRef}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });
                fp?.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Claude Code] cmux focus-window 실패(무시): {ex.Message}");
            }
        }

        // ~/.config/cmux/settings.json의 주석 처리된 automation 블록을 활성 블록으로 치환한다.
        // 이미 활성 블록이 있으면 손대지 않는다. 백업 파일(.bak)을 함께 만든다.
        static bool TryAutoEnableCmuxAutomation(out string message)
        {
            try
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home))
                {
                    message = "HOME 환경변수가 비어있음.";
                    return false;
                }
                var path = Path.Combine(home, ".config/cmux/settings.json");
                if (!File.Exists(path))
                {
                    message = $"settings.json 없음: {path}";
                    return false;
                }
                var content = File.ReadAllText(path);

                // 이미 활성화된 automation 블록이 있는지 — 주석이 아닌 라인에서 "automation" 키 검색.
                foreach (var rawLine in content.Split('\n'))
                {
                    var trimmed = rawLine.TrimStart();
                    if (!trimmed.StartsWith("//") && trimmed.StartsWith("\"automation\""))
                    {
                        message = "automation 블록이 이미 활성화돼 있음 — 다른 원인일 가능성.";
                        return false;
                    }
                }

                // 주석 템플릿 블록 찾기.
                const string templateStart = "//   \"automation\" : {";
                var startIdx = content.IndexOf(templateStart);
                if (startIdx < 0)
                {
                    message = "automation 템플릿 블록을 찾지 못함.";
                    return false;
                }
                const string templateEnd = "//   },";
                var endIdx = content.IndexOf(templateEnd, startIdx);
                if (endIdx < 0)
                {
                    message = "automation 템플릿의 닫는 괄호를 찾지 못함.";
                    return false;
                }
                endIdx += templateEnd.Length;

                const string activeBlock =
                    "\"automation\" : {\n" +
                    "    \"socketControlMode\" : \"automation\"\n" +
                    "  },";

                var newContent = content.Substring(0, startIdx) + activeBlock + content.Substring(endIdx);

                File.Copy(path, path + ".bak", true);
                File.WriteAllText(path, newContent);

                message = $"automation 모드 활성화 ({path}). 백업: {path}.bak";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        static void RestartCmuxApp()
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "pkill",
                    Arguments = "-i cmux",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }))
                {
                    p?.WaitForExit(3000);
                }
                System.Threading.Thread.Sleep(800);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Claude Code] pkill 실패(무시): {ex.Message}");
            }
        }

        // cmux의 --command는 비대화형 셸에서 실행되어 .zshrc/.bash_profile이 로드되지 않는다.
        // 사용자가 rc 파일에서 PATH를 추가하는 경우(claude를 ~/.local/bin 등에 둔 케이스) 명령을 못 찾는다.
        // 대화형 로그인 셸로 감싸고, 명령 종료 후에도 셸 프롬프트가 남도록 exec로 셸을 다시 띄운다.
        static string WrapForInteractiveShell(string command)
        {
            if (string.IsNullOrEmpty(command)) return command;
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell) || !File.Exists(shell)) shell = "/bin/zsh";
            // POSIX 싱글쿼트 안전 이스케이프: ' → '\''
            var safe = command.Replace("'", "'\\''");
            return $"{shell} -ilc '{safe}; exec {shell} -il'";
        }

        // new-workspace의 stdout 형식: "OK workspace:N"
        static bool TryCmuxNewWorkspace(string bin, string workDir, string title, string command, out string workspaceRef, out string stderr)
        {
            stderr = string.Empty;
            workspaceRef = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = bin,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                psi.ArgumentList.Add("new-workspace");
                if (!string.IsNullOrEmpty(title))
                {
                    psi.ArgumentList.Add("--name");
                    psi.ArgumentList.Add(title);
                }
                if (!string.IsNullOrEmpty(workDir))
                {
                    psi.ArgumentList.Add("--cwd");
                    psi.ArgumentList.Add(workDir);
                }
                if (!string.IsNullOrEmpty(command))
                {
                    psi.ArgumentList.Add("--command");
                    psi.ArgumentList.Add(command);
                }

                using var p = Process.Start(psi);
                var stdout = p!.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                if (p.ExitCode != 0) return false;

                var refMatch = System.Text.RegularExpressions.Regex.Match(stdout, "workspace:[A-Za-z0-9-]+");
                if (refMatch.Success) workspaceRef = refMatch.Value;
                return true;
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return false;
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
