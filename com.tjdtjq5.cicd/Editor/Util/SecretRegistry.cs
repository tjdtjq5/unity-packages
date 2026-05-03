#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>GitHub Repository Secrets 등록 상태 캐시. 비동기 로드 + 60초 캐시.</summary>
    public static class SecretRegistry
    {
        static HashSet<string> _registered;
        static bool _loading;
        static double _lastLoadTime;
        const double CACHE_SEC = 60;

        public static bool IsLoaded => _registered != null;
        public static bool IsLoading => _loading;

        /// <summary>특정 secret이 등록되어 있는지. 캐시가 없으면 자동으로 로드 시작.</summary>
        public static bool IsRegistered(string name)
        {
            if (_registered == null)
            {
                Load();
                return false;
            }
            return _registered.Contains(name);
        }

        /// <summary>여러 secret 중 등록된 개수.</summary>
        public static int RegisteredCount(IEnumerable<string> names)
        {
            if (_registered == null) Load();
            if (_registered == null) return 0;

            int n = 0;
            foreach (var name in names)
                if (_registered.Contains(name)) n++;
            return n;
        }

        /// <summary>모두 등록되어 있는지.</summary>
        public static bool AllRegistered(params string[] names)
        {
            if (_registered == null) Load();
            if (_registered == null) return false;

            foreach (var name in names)
                if (!_registered.Contains(name)) return false;
            return true;
        }

        /// <summary>캐시 무효화 후 다음 호출 시 재로드.</summary>
        public static void Invalidate()
        {
            _registered = null;
            _lastLoadTime = 0;
        }

        /// <summary>비동기 로드 시작. 이미 로딩 중이거나 캐시가 유효하면 무시.</summary>
        public static void Load()
        {
            if (_loading) return;
            if (_registered != null &&
                EditorApplication.timeSinceStartup - _lastLoadTime < CACHE_SEC) return;

            var gh = GhChecker.Check();
            if (!gh.LoggedIn)
            {
                _registered = new HashSet<string>();
                _lastLoadTime = EditorApplication.timeSinceStartup;
                return;
            }

            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo))
            {
                _registered = new HashSet<string>();
                _lastLoadTime = EditorApplication.timeSinceStartup;
                return;
            }

            _loading = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                var (code, output) = GhChecker.RunGh($"secret list --repo {repo}");
                var result = new HashSet<string>();
                if (code == 0 && !string.IsNullOrEmpty(output))
                {
                    foreach (var line in output.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        var name = trimmed.Split('\t', ' ')[0].Trim();
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                }
                EditorApplication.delayCall += () =>
                {
                    _registered = result;
                    _lastLoadTime = EditorApplication.timeSinceStartup;
                    _loading = false;
                };
            });
        }
    }
}
#endif
