#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>gh CLI 감지 + 캐싱 + 실행 유틸리티</summary>
    public static class GhChecker
    {
        public struct GhStatus
        {
            public bool Installed;
            public string Version;
            public bool LoggedIn;
            public string Account;
        }

        // ── 캐시 ──
        static GhStatus? _cached;
        static double _cacheTime;
        const double CACHE_SEC = 30;
        static string _ghPath;
        static bool _refreshing;

        public static void InvalidateCache()
        {
            _cached = null;
            _ghPath = null;
        }

        /// <summary>백그라운드에서 gh CLI 체크 후 캐시 저장</summary>
        public static void WarmCacheAsync()
        {
            if (_cached.HasValue) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                var s = DoCheck();
                EditorApplication.delayCall += () =>
                {
                    if (!_cached.HasValue)
                    {
                        _cached = s;
                        _cacheTime = EditorApplication.timeSinceStartup;
                    }
                };
            });
        }

        /// <summary>gh CLI 상태 확인 (캐싱)</summary>
        public static GhStatus Check()
        {
            if (_cached.HasValue && EditorApplication.timeSinceStartup - _cacheTime < CACHE_SEC)
                return _cached.Value;

            if (_cached.HasValue)
            {
                // 캐시 만료 → 백그라운드 갱신, 기존 값 즉시 반환
                RefreshAsync();
                return _cached.Value;
            }

            // 최초 1회만 동기 실행
            var s = DoCheck();
            _cached = s;
            _cacheTime = EditorApplication.timeSinceStartup;
            return s;
        }

        static void RefreshAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                var s = DoCheck();
                EditorApplication.delayCall += () =>
                {
                    _cached = s;
                    _cacheTime = EditorApplication.timeSinceStartup;
                    _refreshing = false;
                };
            });
        }

        static GhStatus DoCheck()
        {
            var s = new GhStatus();
            var path = FindGh();
            if (path == null) return s;

            var (code, output) = Run(path, "--version");
            s.Installed = code == 0;

            if (!s.Installed) return s;

            var m = Regex.Match(output, @"gh version (\S+)");
            s.Version = m.Success ? m.Groups[1].Value : "unknown";

            var (c2, o2) = Run(path, "auth status");
            s.LoggedIn = c2 == 0;
            if (s.LoggedIn)
            {
                var m2 = Regex.Match(o2, @"Logged in to \S+ (?:account|as) (\S+)");
                s.Account = m2.Success ? m2.Groups[1].Value : "";
            }

            return s;
        }

        // ── CLI 경로 찾기 ──

        static string FindGh()
        {
            if (_ghPath != null) return _ghPath;

#if UNITY_EDITOR_WIN
            var (code, _) = RunDirect("cmd.exe", "/c where gh");
            if (code == 0)
            {
                _ghPath = "gh";
                return _ghPath;
            }

            var programFiles = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.ProgramFiles);
            string[] knownPaths =
            {
                Path.Combine(programFiles, "GitHub CLI", "gh.exe"),
                @"C:\Program Files\GitHub CLI\gh.exe",
                @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            };
            foreach (var p in knownPaths)
            {
                if (File.Exists(p))
                {
                    _ghPath = p;
                    return _ghPath;
                }
            }
#else
            var (code, _) = RunDirect("which", "gh");
            if (code == 0)
            {
                _ghPath = "gh";
                return _ghPath;
            }
#endif
            return null;
        }

        // ── 실행 ──

        /// <summary>gh auth login --web 실행</summary>
        public static void RunGhLogin()
        {
            var path = FindGh() ?? "gh";
            Process.Start(new ProcessStartInfo(path, "auth login --web")
            {
                UseShellExecute = false,
                CreateNoWindow = false
            });
            InvalidateCache();
        }

        /// <summary>gh CLI 명령 실행. 반환: (exitCode, output)</summary>
        public static (int exitCode, string output) RunGh(string args)
        {
            var path = FindGh() ?? "gh";
            return Run(path, args);
        }

        static (int exitCode, string output) Run(string cmd, string args)
        {
            try
            {
                ProcessStartInfo psi;

                if (cmd.Contains(Path.DirectorySeparatorChar.ToString()) || cmd.Contains("/"))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                }
                else
                {
#if UNITY_EDITOR_WIN
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {cmd} {args}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
#else
                    psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
#endif
                }

                using var p = Process.Start(psi);
                if (p == null) return (-1, "");
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return (p.ExitCode, string.IsNullOrEmpty(stdout) ? stderr : stdout);
            }
            catch
            {
                return (-1, "");
            }
        }

        static (int exitCode, string output) RunDirect(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "");
                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return (p.ExitCode, stdout);
            }
            catch
            {
                return (-1, "");
            }
        }
    }
}
#endif
