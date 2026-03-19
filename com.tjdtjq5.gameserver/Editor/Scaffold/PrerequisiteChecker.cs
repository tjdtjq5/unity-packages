using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Tjdtjq5.GameServer.Editor
{
    public static class PrerequisiteChecker
    {
        public struct ToolStatus
        {
            public bool Installed;
            public string Version;
            public bool LoggedIn;
            public string Account;
            public string Project;
        }

        static ToolStatus? _gcloud, _gh;
        static double _cacheTime;
        const double CACHE_SEC = 30;

        public static void InvalidateCache()
        {
            _gcloud = _gh = null;
        }

        static bool IsCacheValid() =>
            EditorApplication.timeSinceStartup - _cacheTime < CACHE_SEC;

        public static ToolStatus CheckGcloud()
        {
            if (_gcloud.HasValue && IsCacheValid()) return _gcloud.Value;
            _cacheTime = EditorApplication.timeSinceStartup;

            var s = new ToolStatus();
            var (code, output) = Run("gcloud", "--version");
            s.Installed = code == 0;
            if (s.Installed)
            {
                var m = Regex.Match(output, @"Google Cloud SDK (\S+)");
                s.Version = m.Success ? m.Groups[1].Value : "unknown";

                var (c2, o2) = Run("gcloud", "auth list --format=value(account,status)");
                if (c2 == 0 && !string.IsNullOrEmpty(o2))
                {
                    foreach (var line in o2.Split('\n'))
                    {
                        if (line.Contains("ACTIVE"))
                        {
                            s.LoggedIn = true;
                            s.Account = line.Split('\t', ' ')[0].Trim();
                            break;
                        }
                    }
                }

                var (c3, o3) = Run("gcloud", "config get-value project");
                if (c3 == 0 && !string.IsNullOrEmpty(o3.Trim()) && o3.Trim() != "(unset)")
                    s.Project = o3.Trim();
            }

            _gcloud = s;
            return s;
        }

        public static ToolStatus CheckGh()
        {
            if (_gh.HasValue && IsCacheValid()) return _gh.Value;
            _cacheTime = EditorApplication.timeSinceStartup;

            var s = new ToolStatus();
            var (code, output) = Run("gh", "--version");
            s.Installed = code == 0;
            if (s.Installed)
            {
                var m = Regex.Match(output, @"gh version (\S+)");
                s.Version = m.Success ? m.Groups[1].Value : "unknown";

                var (c2, o2) = Run("gh", "auth status");
                s.LoggedIn = c2 == 0;
                if (s.LoggedIn)
                {
                    var m2 = Regex.Match(o2, @"Logged in to .+ as (\S+)");
                    s.Account = m2.Success ? m2.Groups[1].Value : "";
                }
            }

            _gh = s;
            return s;
        }

        // ── CLI 실행 헬퍼 (배포용) ──

        public static void RunGcloudLogin()
        {
            Process.Start(new ProcessStartInfo("gcloud", "auth login") { UseShellExecute = true });
            InvalidateCache();
        }

        public static void RunGhLogin()
        {
            Process.Start(new ProcessStartInfo("gh", "auth login --web") { UseShellExecute = true });
            InvalidateCache();
        }

        public static void SetGcloudProject(string projectId)
        {
            Run("gcloud", $"config set project {projectId}");
            InvalidateCache();
        }

        public static (int exitCode, string output) Run(string cmd, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "");

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);

                return (p.ExitCode, string.IsNullOrEmpty(stdout) ? stderr : stdout);
            }
            catch
            {
                return (-1, "");
            }
        }
    }
}
