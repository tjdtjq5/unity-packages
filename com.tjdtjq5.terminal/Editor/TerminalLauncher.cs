using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Terminal
{
    /// <summary>
    /// 선택된 터미널 프로필로 프로젝트 루트에서 터미널을 연다.
    /// </summary>
    public static class TerminalLauncher
    {
        static string ProjectPath =>
            Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');

        [MenuItem("Tjdtjq/Terminal/터미널 열기")]
        public static void Open()
        {
            var profile = TerminalProfiles.GetSelected();
            if (profile == null)
            {
                Debug.LogWarning("[Terminal] 터미널 프로필이 없습니다. 목록 편집에서 추가하세요.");
                TerminalListWindow.Open();
                return;
            }
            Launch(profile);
        }

        public static void Launch(TerminalProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.command))
            {
                Debug.LogError("[Terminal] 실행할 명령이 비어있습니다.");
                return;
            }

            var dir = ProjectPath;
            var command = profile.command.Trim()
                .Replace("{dirUri}", Uri.EscapeDataString(dir))
                .Replace("{dir}", dir);

            try
            {
                ProcessStartInfo psi;
                if (Regex.IsMatch(command, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://"))
                {
                    // URI 스킴 (warp:// 등) — 셸이 등록된 핸들러로 연결
                    psi = new ProcessStartInfo { FileName = command, UseShellExecute = true };
                }
                else
                {
                    SplitCommand(command, out var file, out var args);
                    psi = new ProcessStartInfo
                    {
                        FileName = file,
                        Arguments = args,
                        UseShellExecute = true,
                        WorkingDirectory = dir, // 인자 없는 셸(powershell 등)도 프로젝트 루트에서 열리게
                    };
                }
                Process.Start(psi);
                Debug.Log($"[Terminal] {profile.name} 실행 — {dir}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Terminal] {profile.name} 실행 실패: {ex.Message}\n명령: {command}");
            }
        }

        /// <summary>첫 토큰(따옴표 지원)을 실행 파일로, 나머지를 인자로 분리한다.</summary>
        internal static void SplitCommand(string command, out string file, out string args)
        {
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                var end = command.IndexOf('"', 1);
                if (end > 0)
                {
                    file = command.Substring(1, end - 1);
                    args = command.Substring(end + 1).TrimStart();
                    return;
                }
            }
            var space = command.IndexOf(' ');
            if (space < 0) { file = command; args = ""; return; }
            file = command.Substring(0, space);
            args = command.Substring(space + 1).TrimStart();
        }
    }
}
