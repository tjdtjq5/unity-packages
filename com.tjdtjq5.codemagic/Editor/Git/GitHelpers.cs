#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Tjdtjq5.Codemagic.Editor.Git
{
    /// <summary>Git 리포 정보 유틸리티 — repo root, GitHub remote, dirty 파일 조회 (캐싱 포함).</summary>
    public static class GitHelpers
    {
        // ── 캐시 ──
        static string _cachedRepoRoot;
        static string _cachedGitHubRepo;
        static bool _cacheInitialized;

        /// <summary>캐시 초기화. OnEnable이나 새로고침 시 호출.</summary>
        public static void InvalidateCache()
        {
            _cachedRepoRoot = null;
            _cachedGitHubRepo = null;
            _cacheInitialized = false;
        }

        static void EnsureCache()
        {
            if (_cacheInitialized) return;
            _cacheInitialized = true;

            _cachedRepoRoot = RunGit("rev-parse --show-toplevel");

            string url = RunGit("remote get-url origin");
            _cachedGitHubRepo = ParseGitHubRepo(url);
        }

        /// <summary>git 리포 루트 경로 (캐싱).</summary>
        public static string GetRepoRoot()
        {
            EnsureCache();
            return _cachedRepoRoot;
        }

        /// <summary>GitHub remote URL에서 owner/repo 추출 (캐싱). 예: "user/repo".</summary>
        public static string GetGitHubRepo()
        {
            EnsureCache();
            return _cachedGitHubRepo;
        }

        /// <summary>GitHub Secrets 설정 페이지 URL.</summary>
        public static string GetSecretsPageUrl()
        {
            var repo = GetGitHubRepo();
            return repo != null
                ? $"https://github.com/{repo}/settings/secrets/actions"
                : "https://github.com";
        }

        /// <summary>GitHub 리포 URL (없으면 null).</summary>
        public static string GetRepoUrl()
        {
            var repo = GetGitHubRepo();
            return repo != null ? $"https://github.com/{repo}" : null;
        }

        /// <summary>git 명령 실행 (stdout 반환).</summary>
        public static string RunGit(string arguments)
        {
            var (_, output) = RunGitWithCode(arguments);
            return output;
        }

        /// <summary>git 명령 실행 (exit code + 출력).</summary>
        public static (int exitCode, string output) RunGitWithCode(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                string stdout = process!.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                string output = string.IsNullOrEmpty(stdout.Trim()) ? stderr.Trim() : stdout.Trim();
                return (process.ExitCode, output);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Codemagic] git {arguments} 실행 실패: {ex.Message}");
                return (-1, string.Empty);
            }
        }

        /// <summary>git status --porcelain 결과에서 excludeContains 안의 substring을 포함한 라인 제외.</summary>
        /// <remarks>
        /// 빈 라인 무시. status 출력이 비어있으면 빈 리스트 반환.
        /// 각 라인의 prefix 3자(상태 문자 + 공백 2자)를 떼어낸 파일 경로를 트리밍해 반환.
        /// </remarks>
        public static List<string> GetDirtyFiles(params string[] excludeContains)
        {
            var status = RunGit("status --porcelain");
            if (string.IsNullOrEmpty(status)) return new List<string>();

            return status.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Where(l => excludeContains == null || excludeContains.Length == 0 ||
                            !excludeContains.Any(ex => !string.IsNullOrEmpty(ex) && l.Contains(ex)))
                .Select(l => l.Length > 3 ? l.Substring(3).Trim() : l.Trim())
                .ToList();
        }

        static string ParseGitHubRepo(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            url = url.Replace(".git", "").Trim();

            int httpsIdx = url.IndexOf("github.com/");
            if (httpsIdx >= 0)
                return url.Substring(httpsIdx + 11);

            int sshIdx = url.IndexOf("github.com:");
            if (sshIdx >= 0)
                return url.Substring(sshIdx + 11);

            return null;
        }
    }
}
#endif
