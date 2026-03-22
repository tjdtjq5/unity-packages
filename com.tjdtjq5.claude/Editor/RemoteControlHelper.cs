using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Remote Control 사전 요건 검사 + 세션 이름 생성 헬퍼.
    /// </summary>
    public static class RemoteControlHelper
    {
        public struct PrereqResult
        {
            public bool ClaudeInstalled;
            public string Version;
            public bool VersionOk;
        }

        const string MinVersion = "2.1.51";

        /// <summary>Claude CLI 설치 + 버전 확인 (비동기)</summary>
        public static void CheckPrerequisites(Action<PrereqResult> callback)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var result = new PrereqResult();

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "claude",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    string stderr = null;
                    var stderrTask = System.Threading.Tasks.Task.Run(
                        () => stderr = p!.StandardError.ReadToEnd());
                    var output = p!.StandardOutput.ReadToEnd().Trim();
                    stderrTask.Wait(5000);
                    p.WaitForExit(5000);

                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        result.ClaudeInstalled = true;
                        var match = Regex.Match(output, @"(\d+\.\d+\.\d+)");
                        if (match.Success)
                        {
                            result.Version = match.Groups[1].Value;
                            result.VersionOk = CompareVersions(result.Version, MinVersion) >= 0;
                        }
                    }
                }
                catch
                {
                    result.ClaudeInstalled = false;
                }

                EditorApplication.delayCall += () => callback?.Invoke(result);
            });
        }

        /// <summary>RC 세션 이름 생성 (프로젝트명 + 워크트리)</summary>
        public static string GetSessionName(string label = null)
        {
            var projectName = Path.GetFileName(
                Path.GetDirectoryName(Application.dataPath));

            // 셸 안전을 위해 싱글 쿼트 제거
            projectName = projectName?.Replace("'", "") ?? "Unity";

            if (!string.IsNullOrEmpty(label))
            {
                var clean = label.Replace("'", "");
                // "Claude wt-1" → "wt-1"
                if (clean.StartsWith("Claude "))
                    clean = clean.Substring(7);
                return $"{projectName}/{clean}";
            }

            return projectName;
        }

        static int CompareVersions(string a, string b)
        {
            var va = a.Split('.');
            var vb = b.Split('.');
            for (int i = 0; i < Math.Max(va.Length, vb.Length); i++)
            {
                int na = i < va.Length && int.TryParse(va[i], out var x) ? x : 0;
                int nb = i < vb.Length && int.TryParse(vb[i], out var y) ? y : 0;
                if (na != nb) return na.CompareTo(nb);
            }
            return 0;
        }
    }
}
