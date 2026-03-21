#if UNITY_EDITOR
namespace Tjdtjq5.CICD.Editor
{
    /// <summary>git tag 기반 버전 추출 (캐싱 포함)</summary>
    public static class GitVersionResolver
    {
        // ── 캐시 ──
        static string _cachedVersion;
        static string _cachedDetailedVersion;
        static string[] _cachedTags;
        static bool _cacheInitialized;

        /// <summary>캐시 초기화. 새로고침 시 호출.</summary>
        public static void InvalidateCache()
        {
            _cachedVersion = null;
            _cachedDetailedVersion = null;
            _cachedTags = null;
            _cacheInitialized = false;
        }

        static void EnsureCache()
        {
            if (_cacheInitialized) return;
            _cacheInitialized = true;

            string tag = GitHelper.RunGit("describe --tags --abbrev=0");
            _cachedVersion = string.IsNullOrEmpty(tag) ? "0.1.0" : tag.TrimStart('v').Trim();

            string desc = GitHelper.RunGit("describe --tags --long");
            _cachedDetailedVersion = ParseDetailedVersion(desc);
        }

        /// <summary>가장 가까운 git tag에서 버전 추출. 없으면 "0.1.0" (캐싱)</summary>
        public static string GetVersion()
        {
            EnsureCache();
            return _cachedVersion;
        }

        /// <summary>태그 이후 커밋 수 포함 버전 (예: "0.2.0+14") (캐싱)</summary>
        public static string GetDetailedVersion()
        {
            EnsureCache();
            return _cachedDetailedVersion;
        }

        /// <summary>MAJOR * 10000 + MINOR * 100 + PATCH</summary>
        public static int ComputeBuildCode(string version)
        {
            var cleanVersion = version.Split('+')[0].Split('-')[0];
            var parts = cleanVersion.Split('.');
            if (parts.Length < 3) return 1;

            int.TryParse(parts[0], out int major);
            int.TryParse(parts[1], out int minor);
            int.TryParse(parts[2], out int patch);

            return major * 10000 + minor * 100 + patch;
        }

        /// <summary>최근 태그 목록 (최대 count개) (캐싱)</summary>
        public static string[] GetRecentTags(int count = 5)
        {
            if (_cachedTags != null) return _cachedTags;

            string output = GitHelper.RunGit($"tag --sort=-version:refname -l \"v*\" --format=\"%(refname:short)\"");
            if (string.IsNullOrEmpty(output))
            {
                _cachedTags = System.Array.Empty<string>();
                return _cachedTags;
            }

            var tags = output.Split('\n');
            int len = System.Math.Min(tags.Length, count);
            _cachedTags = new string[len];
            System.Array.Copy(tags, _cachedTags, len);
            return _cachedTags;
        }

        static string ParseDetailedVersion(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "0.1.0+0";

            var match = System.Text.RegularExpressions.Regex.Match(
                desc.Trim(), @"^v?([\d]+\.[\d]+\.[\d]+)(?:-.+)?-(\d+)-g[0-9a-f]+$");
            if (match.Success)
            {
                string version = match.Groups[1].Value;
                string commits = match.Groups[2].Value;
                return commits == "0" ? version : $"{version}+{commits}";
            }
            return desc.TrimStart('v').Trim();
        }
    }
}
#endif
