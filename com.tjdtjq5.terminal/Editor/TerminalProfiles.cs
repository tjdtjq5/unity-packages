using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Terminal
{
    [Serializable]
    public class TerminalProfile
    {
        public string name;
        public string command; // {dir}=프로젝트 경로, {dirUri}=URL 인코딩 경로. scheme:// 로 시작하면 URI로 실행
    }

    /// <summary>
    /// 터미널 프로필 목록 (EditorPrefs JSON, 머신별 저장).
    /// 목록 데이터가 항상 우선 — 자동 감지는 항목을 추가만 하고 절대 수정/삭제하지 않는다.
    /// </summary>
    public static class TerminalProfiles
    {
        const string ListKey = "Tjdtjq5Terminal_Profiles";
        const string SelectedKey = "Tjdtjq5Terminal_Selected";
        const string SeededKey = "Tjdtjq5Terminal_Seeded";

        [Serializable]
        class ListWrapper { public List<TerminalProfile> items = new(); }

        static bool IsMac => Application.platform == RuntimePlatform.OSXEditor;

        // ── 목록 ──

        public static List<TerminalProfile> Load()
        {
            EnsureSeeded();
            var json = EditorPrefs.GetString(ListKey, "");
            if (string.IsNullOrEmpty(json)) return new List<TerminalProfile>();
            try { return JsonUtility.FromJson<ListWrapper>(json)?.items ?? new List<TerminalProfile>(); }
            catch { return new List<TerminalProfile>(); }
        }

        public static void Save(List<TerminalProfile> list)
        {
            var json = JsonUtility.ToJson(new ListWrapper { items = list ?? new List<TerminalProfile>() });
            EditorPrefs.SetString(ListKey, json);
        }

        public static string SelectedName
        {
            get => EditorPrefs.GetString(SelectedKey, "");
            set => EditorPrefs.SetString(SelectedKey, value ?? "");
        }

        /// <summary>선택된 프로필. 이름 매칭 실패 시 첫 항목, 목록이 비면 null.</summary>
        public static TerminalProfile GetSelected()
        {
            var list = Load();
            if (list.Count == 0) return null;
            var selected = SelectedName;
            return list.FirstOrDefault(p => p.name == selected) ?? list[0];
        }

        // ── 알려진 터미널 & 자동 감지 ──

        struct KnownTerminal
        {
            public string Name;
            public string Command;
            public Func<bool> IsInstalled;
        }

        static KnownTerminal[] GetKnownTerminals()
        {
            if (IsMac)
            {
                return new[]
                {
                    new KnownTerminal { Name = "Terminal", Command = "open -a Terminal \"{dir}\"", IsInstalled = () => true },
                    new KnownTerminal { Name = "iTerm2", Command = "open -a iTerm \"{dir}\"", IsInstalled = () => Directory.Exists("/Applications/iTerm.app") },
                    new KnownTerminal { Name = "Warp", Command = "open -a Warp \"{dir}\"", IsInstalled = () => Directory.Exists("/Applications/Warp.app") },
                    new KnownTerminal { Name = "Ghostty", Command = "open -na Ghostty --args --working-directory=\"{dir}\"", IsInstalled = () => Directory.Exists("/Applications/Ghostty.app") },
                    new KnownTerminal { Name = "Alacritty", Command = "/Applications/Alacritty.app/Contents/MacOS/alacritty --working-directory \"{dir}\"", IsInstalled = () => Directory.Exists("/Applications/Alacritty.app") },
                    new KnownTerminal { Name = "WezTerm", Command = "/Applications/WezTerm.app/Contents/MacOS/wezterm-gui start --cwd \"{dir}\"", IsInstalled = () => Directory.Exists("/Applications/WezTerm.app") },
                    new KnownTerminal { Name = "Tabby", Command = "open -a Tabby \"{dir}\"", IsInstalled = () => Directory.Exists("/Applications/Tabby.app") },
                };
            }

            // 경로 계산은 싸고(폴더 경로 조합), 프로세스 실행은 IsInstalled 람다 안에서만 (lazy)
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var gitBash = Path.Combine(programFiles, "Git", "git-bash.exe");
            var tabby = Path.Combine(localAppData, "Programs", "Tabby", "Tabby.exe");
            var cmderRoot = Environment.GetEnvironmentVariable("CMDER_ROOT");

            return new[]
            {
                new KnownTerminal { Name = "Windows Terminal", Command = "wt -d \"{dir}\"", IsInstalled = () => RunExitOk("where", "wt") },
                new KnownTerminal { Name = "Warp", Command = "warp://action/new_window?path={dirUri}", IsInstalled = HasWarpWindows },
                new KnownTerminal { Name = "PowerShell", Command = "powershell", IsInstalled = () => true },
                new KnownTerminal { Name = "Git Bash", Command = $"\"{gitBash}\" --cd=\"{{dir}}\"", IsInstalled = () => File.Exists(gitBash) },
                new KnownTerminal { Name = "Alacritty", Command = "alacritty --working-directory \"{dir}\"",
                    IsInstalled = () => RunExitOk("where", "alacritty") || File.Exists(Path.Combine(programFiles, "Alacritty", "alacritty.exe")) },
                new KnownTerminal { Name = "WezTerm", Command = "wezterm start --cwd \"{dir}\"",
                    IsInstalled = () => RunExitOk("where", "wezterm") || File.Exists(Path.Combine(programFiles, "WezTerm", "wezterm-gui.exe")) },
                new KnownTerminal { Name = "Tabby", Command = $"\"{tabby}\" open \"{{dir}}\"", IsInstalled = () => File.Exists(tabby) },
                // cmder는 portable 앱 — CMDER_ROOT 환경변수 또는 PATH 등록 시에만 감지 가능
                new KnownTerminal { Name = "cmder",
                    Command = string.IsNullOrEmpty(cmderRoot) ? "cmder /START \"{dir}\"" : $"\"{Path.Combine(cmderRoot, "Cmder.exe")}\" /START \"{{dir}}\"",
                    IsInstalled = () => !string.IsNullOrEmpty(cmderRoot) || RunExitOk("where", "cmder") },
            };
        }

        /// <summary>첫 실행 시 설치된 터미널만 목록에 시드한다.</summary>
        static void EnsureSeeded()
        {
            if (EditorPrefs.GetBool(SeededKey, false)) return;
            EditorPrefs.SetBool(SeededKey, true);

            var list = new List<TerminalProfile>();
            foreach (var known in GetKnownTerminals())
            {
                if (known.IsInstalled())
                    list.Add(new TerminalProfile { name = known.Name, command = known.Command });
            }
            Save(list);
            if (list.Count > 0) SelectedName = list[0].name;
        }

        /// <summary>설치돼 있는데 목록에 없는 알려진 터미널을 추가한다 (이름 기준 중복 방지). 추가된 개수 반환.</summary>
        public static int AddMissingInstalled(List<TerminalProfile> list)
        {
            int added = 0;
            foreach (var known in GetKnownTerminals())
            {
                if (list.Any(p => p.name == known.Name)) continue;
                if (!known.IsInstalled()) continue;
                list.Add(new TerminalProfile { name = known.Name, command = known.Command });
                added++;
            }
            return added;
        }

        /// <summary>설치 여부 확인. true/false/null(판단 불가). 프로세스 실행이 있어 호출측에서 캐시할 것.</summary>
        public static bool? CheckInstalled(TerminalProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.command)) return false;

            // 알려진 터미널이면 전용 감지 사용
            foreach (var known in GetKnownTerminals())
            {
                if (known.Name == profile.name) return known.IsInstalled();
            }

            var command = profile.command.Trim();

            // URI: Windows는 레지스트리 핸들러 확인, Mac은 판단 불가
            var schemeMatch = System.Text.RegularExpressions.Regex.Match(command, @"^([a-zA-Z][a-zA-Z0-9+.\-]*)://");
            if (schemeMatch.Success)
            {
                if (IsMac) return null;
                var scheme = schemeMatch.Groups[1].Value;
                return RunExitOk("reg", $"query HKCU\\Software\\Classes\\{scheme}") ||
                       RunExitOk("reg", $"query HKCR\\{scheme}");
            }

            // 실행 파일: 절대경로면 존재 확인, 아니면 where/which
            TerminalLauncher.SplitCommand(command, out var file, out _);
            if (Path.IsPathRooted(file)) return File.Exists(file);
            return RunExitOk(IsMac ? "which" : "where", file);
        }

        static bool HasWarpWindows()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (File.Exists(Path.Combine(localAppData, "Programs", "Warp", "Warp.exe"))) return true;
            return RunExitOk("reg", "query HKCU\\Software\\Classes\\warp");
        }

        static bool RunExitOk(string fileName, string args)
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
    }
}
