using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Claude
{
    public struct WorktreeInfo
    {
        public string Name;     // "wt-1"
        public string Path;     // "C:/Workspace/Card-wt-1"
        public string Branch;   // "wt/1"
        public bool IsDirty;    // 미커밋 변경 여부
    }

    /// <summary>
    /// Claude Code를 Windows Terminal / PowerShell로 실행하는 런처.
    /// 워크트리 생성·관리·실행 API 제공.
    /// </summary>
    public static class ClaudeCodeLauncher
    {
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
                    p!.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    _hasWt = p.ExitCode == 0;
                }
                catch { _hasWt = false; }
                return _hasWt.Value;
            }
        }

        // ── 메뉴 아이템 ──

        [MenuItem("Tools/Claude Code/Settings")]
        static void OpenSettings()
        {
            ClaudeCodeSettingsWindow.Open();
        }

        // ── 공개 API ──

        public static void LaunchMain()
        {
            var cmd = BuildClaudeCommand();
            var colorHex = ClaudeCodeSettings.ColorToHex(ClaudeCodeSettings.MainTabColor);

            if (HasWindowsTerminal)
            {
                var wtArgs = $"-w {ClaudeCodeSettings.WindowName} " +
                             $"-d \"{ProjectPath}\" " +
                             $"--tabColor \"{colorHex}\" " +
                             $"--title \"Claude Main\" " +
                             $"powershell -NoExit -Command \"{cmd}\"";
                StartProcess("wt", wtArgs);
            }
            else
            {
                StartProcess("powershell", $"-NoExit -Command \"{cmd}\"", ProjectPath);
            }

            // Discord 모드 활성이면 Pipe 연결 + Discord 설정 전송
            if (ClaudeCodeSettings.DiscordEnabled)
            {
                ChannelBridge.Connect();
                // 중복 등록 방지 후 콜백 등록
                ChannelBridge.OnStateChanged -= OnBridgeConnectedSendConfig;
                ChannelBridge.OnStateChanged += OnBridgeConnectedSendConfig;
            }

            Debug.Log($"[Claude Code] 메인 실행 — {ProjectPath}");
        }

        public static void LaunchClaudeAt(string path, string title)
        {
            var cmd = BuildClaudeCommand(title);
            var colorHex = ClaudeCodeSettings.ColorToHex(ClaudeCodeSettings.WorktreeTabColor);

            if (HasWindowsTerminal)
            {
                var wtArgs = $"-w {ClaudeCodeSettings.WindowName} new-tab " +
                             $"-d \"{path}\" " +
                             $"--tabColor \"{colorHex}\" " +
                             $"--title \"{title}\" " +
                             $"powershell -NoExit -Command \"{cmd}\"";
                StartProcess("wt", wtArgs);
            }
            else
            {
                StartProcess("powershell", $"-NoExit -Command \"{cmd}\"", path);
            }

            Debug.Log($"[Claude Code] 실행 — {path}");
        }

        public static void CreateWorktreeAsync(Action<WorktreeInfo> onSuccess, Action<string> onError)
        {
            var projectPath = ProjectPath;

            Task.Run(() =>
            {
                try
                {
                    var count = FindNextAvailableWorktreeNumber(projectPath);
                    var wtName = $"wt-{count}";
                    var wtPath = Path.GetFullPath(
                        Path.Combine(projectPath, "..", $"{Path.GetFileName(projectPath)}-{wtName}"))
                        .Replace('\\', '/');
                    var branchName = $"wt-{count}";

                    var gitResult = RunGit($"worktree add \"{wtPath}\" -b \"{branchName}\"", projectPath);
                    if (gitResult.exitCode != 0)
                    {
                        // 브랜치가 이미 존재하면 기존 브랜치를 재사용
                        if (gitResult.output.Contains("already exists"))
                            gitResult = RunGit($"worktree add \"{wtPath}\" \"{branchName}\"", projectPath);

                        if (gitResult.exitCode != 0)
                        {
                            EditorApplication.delayCall += () => onError?.Invoke(gitResult.output);
                            return;
                        }
                    }

                    var info = new WorktreeInfo
                    {
                        Name = wtName,
                        Path = wtPath,
                        Branch = branchName,
                        IsDirty = false
                    };

                    EditorApplication.delayCall += () => onSuccess?.Invoke(info);
                }
                catch (Exception ex)
                {
                    EditorApplication.delayCall += () => onError?.Invoke(ex.Message);
                }
            });
        }

        public static void GetActiveWorktreesAsync(Action<List<WorktreeInfo>> callback)
        {
            var projectPath = ProjectPath;

            Task.Run(() =>
            {
                var list = new List<WorktreeInfo>();
                var result = RunGit("worktree list --porcelain", projectPath);
                if (result.exitCode != 0)
                {
                    EditorApplication.delayCall += () => callback?.Invoke(list);
                    return;
                }

                var projectName = Path.GetFileName(projectPath);
                var prefix = $"{projectName}-wt-";

                var blocks = result.output.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var block in blocks)
                {
                    string wtPath = null;
                    string branch = null;

                    foreach (var line in block.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("worktree "))
                            wtPath = trimmed.Substring(9).Trim().Replace('\\', '/');
                        else if (trimmed.StartsWith("branch "))
                            branch = trimmed.Substring(7).Trim().Replace("refs/heads/", "");
                    }

                    if (wtPath == null) continue;
                    var dirName = Path.GetFileName(wtPath);
                    if (!dirName.StartsWith(prefix)) continue;

                    // dirty 체크
                    var statusResult = RunGit($"-C \"{wtPath}\" status --porcelain", projectPath);
                    bool isDirty = statusResult.exitCode == 0 && !string.IsNullOrWhiteSpace(statusResult.output);

                    list.Add(new WorktreeInfo
                    {
                        Name = dirName.Substring(projectName.Length + 1), // "wt-1"
                        Path = wtPath,
                        Branch = branch ?? "unknown",
                        IsDirty = isDirty
                    });
                }

                EditorApplication.delayCall += () => callback?.Invoke(list);
            });
        }

        public static void RemoveWorktreeAsync(WorktreeInfo wt, Action onSuccess, Action<string> onError)
        {
            var projectPath = ProjectPath;

            Task.Run(() =>
            {
                // 1. 워크트리 제거
                var result = RunGit($"worktree remove \"{wt.Path}\" --force", projectPath);
                if (result.exitCode != 0)
                {
                    EditorApplication.delayCall += () => onError?.Invoke(result.output);
                    return;
                }

                // 2. 잔여 참조 정리 (prune 먼저 해야 브랜치 삭제 가능)
                RunGit("worktree prune", projectPath);

                // 3. 로컬 브랜치 삭제
                if (!string.IsNullOrEmpty(wt.Branch) && wt.Branch != "unknown")
                    RunGit($"branch -D \"{wt.Branch}\"", projectPath);

                // 4. 원격 브랜치 삭제 (실패 무시)
                if (!string.IsNullOrEmpty(wt.Branch) && wt.Branch != "unknown")
                    RunGit($"push origin --delete \"{wt.Branch}\"", projectPath);

                EditorApplication.delayCall += () => onSuccess?.Invoke();
            });
        }

        public static void RemoveAllWorktreesAsync(Action onSuccess, Action<string> onError)
        {
            var projectPath = ProjectPath;

            Task.Run(() =>
            {
                // 먼저 활성 워크트리 목록 수집
                var result = RunGit("worktree list --porcelain", projectPath);
                if (result.exitCode != 0)
                {
                    EditorApplication.delayCall += () => onError?.Invoke(result.output);
                    return;
                }

                var projectName = Path.GetFileName(projectPath);
                var prefix = $"{projectName}-wt-";
                var errors = new List<string>();

                // 워크트리 정보 수집
                var targets = new List<(string path, string branch)>();
                var blocks = result.output.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var block in blocks)
                {
                    string wtPath = null;
                    string branch = null;
                    foreach (var line in block.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("worktree "))
                            wtPath = trimmed.Substring(9).Trim();
                        else if (trimmed.StartsWith("branch "))
                            branch = trimmed.Substring(7).Trim().Replace("refs/heads/", "");
                    }
                    if (wtPath == null) continue;
                    if (!Path.GetFileName(wtPath).StartsWith(prefix)) continue;
                    targets.Add((wtPath, branch));
                }

                // 1. 전체 워크트리 제거
                foreach (var (wtPath, _) in targets)
                {
                    var removeResult = RunGit($"worktree remove \"{wtPath}\" --force", projectPath);
                    if (removeResult.exitCode != 0)
                        errors.Add($"{wtPath}: {removeResult.output}");
                }

                // 2. 잔여 참조 정리 (prune 먼저 해야 브랜치 삭제 가능)
                RunGit("worktree prune", projectPath);

                // 3. 로컬 + 원격 브랜치 삭제
                foreach (var (_, branch) in targets)
                {
                    if (string.IsNullOrEmpty(branch)) continue;
                    RunGit($"branch -D \"{branch}\"", projectPath);
                    RunGit($"push origin --delete \"{branch}\"", projectPath);
                }

                if (errors.Count > 0)
                    EditorApplication.delayCall += () => onError?.Invoke(string.Join("\n", errors));
                else
                    EditorApplication.delayCall += () => onSuccess?.Invoke();
            });
        }

        // ── 헬퍼 ──

        /// <summary>git worktree list를 파싱하여 다음 사용 가능한 번호를 반환</summary>
        internal static int FindNextAvailableWorktreeNumber(string projectPath)
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

        internal static string BuildClaudeCommand(string sessionLabel = null)
        {
            var sb = new StringBuilder("claude");

            // Effort 레벨 (별도 인자로 전달)
            var effort = ClaudeCodeSettings.DefaultEffortLevel;
            if (!string.IsNullOrEmpty(effort))
                sb.Append($" --effort {effort}");

            var extra = ClaudeCodeSettings.AdditionalArgs.Trim();
            if (!string.IsNullOrEmpty(extra))
                sb.Append(' ').Append(extra);

            // Channel — Discord 모드가 활성이면 Bridge 연결
            if (ClaudeCodeSettings.DiscordEnabled)
            {
                EnsureMcpConfig();
                // 워크트리에서도 Bridge를 찾을 수 있도록 .mcp.json 경로를 명시적으로 전달
                var mcpJsonPath = Path.Combine(ProjectPath, ".mcp.json").Replace('\\', '/');
                sb.Append($" --mcp-config \"{mcpJsonPath}\"");
                sb.Append(" --dangerously-load-development-channels server:claude-unity-bridge");
            }

            // Remote Control — 세션 이름 포함
            if (ClaudeCodeSettings.RemoteControlEnabled)
            {
                var sessionName = RemoteControlHelper.GetSessionName(sessionLabel);
                sb.Append($" --remote-control '{sessionName}'");
            }

            return sb.ToString();
        }

        /// <summary>Bridge~/src/index.js 경로를 찾는다</summary>
        static string GetBridgePath()
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine("Packages", "com.tjdtjq5.claude", "Bridge~", "src", "index.js")),
                Path.GetFullPath(Path.Combine(ProjectPath, "Packages", "com.tjdtjq5.claude", "Bridge~", "src", "index.js")),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate.Replace('\\', '/');
            }

            Debug.LogWarning("[Claude Code] Bridge~/src/index.js를 찾을 수 없습니다");
            return null;
        }

        /// <summary>
        /// [C3][N2] 프로젝트 루트에 .mcp.json을 생성/갱신하여
        /// Claude Code가 Bridge를 Channel 서버로 인식하게 한다.
        /// 기존 .mcp.json이 있으면 다른 서버 설정을 보존하면서 머지한다.
        /// </summary>
        static void EnsureMcpConfig()
        {
            var bridgePath = GetBridgePath();
            if (string.IsNullOrEmpty(bridgePath)) return;

            var mcpJsonPath = Path.Combine(ProjectPath, ".mcp.json");
            var escapedPath = bridgePath.Replace("\\", "\\\\");
            var bridgeEntry =
                $"\"claude-unity-bridge\": {{\n" +
                $"      \"command\": \"node\",\n" +
                $"      \"args\": [\"{escapedPath}\"],\n" +
                $"      \"env\": {{\n" +
                $"        \"CLAUDE_UNITY_PIPE_HASH\": \"{ChannelBridge.PipeHash}\"\n" +
                $"      }}\n" +
                $"    }}";

            if (File.Exists(mcpJsonPath))
            {
                var current = File.ReadAllText(mcpJsonPath);
                if (current.Contains("claude-unity-bridge")) return; // 이미 등록됨

                // 기존 파일에 머지: "mcpServers": { 뒤에 삽입
                var insertPos = current.IndexOf("\"mcpServers\"");
                if (insertPos >= 0)
                {
                    // "mcpServers": { 다음 위치를 찾아서 엔트리 삽입
                    var bracePos = current.IndexOf('{', insertPos + 12);
                    if (bracePos >= 0)
                    {
                        var merged = current.Substring(0, bracePos + 1) +
                                     "\n    " + bridgeEntry + "," +
                                     current.Substring(bracePos + 1);
                        File.WriteAllText(mcpJsonPath, merged);
                        Debug.Log("[Claude Code] .mcp.json에 Channel Bridge 추가됨");
                        return;
                    }
                }

                // mcpServers 구조가 없으면 새로 작성 (기존 파일 백업 후 덮어쓰기)
                Debug.LogWarning("[Claude Code] 기존 .mcp.json 구조를 파싱할 수 없어 새로 생성합니다");
            }

            // 새 파일 생성
            var content =
                "{\n" +
                "  \"mcpServers\": {\n" +
                "    " + bridgeEntry + "\n" +
                "  }\n" +
                "}";
            File.WriteAllText(mcpJsonPath, content);
            Debug.Log("[Claude Code] .mcp.json 생성됨 (Channel Bridge 등록)");
        }

        /// <summary>Bridge 연결 성공 시 Discord 설정을 자동 전송 (1회)</summary>
        static void OnBridgeConnectedSendConfig(ChannelBridge.State state)
        {
            if (state == ChannelBridge.State.Connected)
            {
                ChannelBridge.OnStateChanged -= OnBridgeConnectedSendConfig;
                ChannelBridge.SendConfig();
                Debug.Log("[Claude Code] Discord 설정 자동 전송됨");
            }
        }

        internal static void StartProcess(string fileName, string arguments, string workDir = null)
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
        internal static (int exitCode, string output) RunGit(string arguments, string workDir)
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
