#if UNITY_EDITOR
namespace Tjdtjq5.CICD.Editor
{
    /// <summary>에디터에서 Release (git tag + push → 빌드 트리거)</summary>
    public static class ReleaseManager
    {
        /// <summary>yml 재생성 + commit + push + tag → 빌드 트리거</summary>
        public static (bool success, string error) CreateRelease(string version)
        {
            // 1. v 접두사 보장
            if (!version.StartsWith("v")) version = $"v{version}";

            // 2. 버전 형식 검증
            var clean = version.TrimStart('v');
            var parts = clean.Split('.');
            if (parts.Length < 3)
                return (false, "버전 형식이 올바르지 않습니다. 예: 0.2.0");

            // 3. 태그 중복 확인
            var existing = GitHelper.RunGit($"tag -l {version}");
            if (!string.IsNullOrEmpty(existing))
                return (false, $"태그 {version}이 이미 존재합니다.");

            // 4. yml 재생성 (항상 최신 설정 반영)
            var settings = BuildAutomationSettings.Instance;
            var yml = WorkflowGenerator.Generate(settings);
            WorkflowGenerator.SaveToProject(yml);

            // 5. 변경사항 commit + push
            GitHelper.RunGit("add .github/workflows/build-and-deploy.yml");
            var (commitCode, commitOutput) = GitHelper.RunGitWithCode(
                $"commit -m \"Update CI/CD workflow for {version}\" --allow-empty");
            // commit 실패해도 진행 (nothing to commit일 수 있음)

            var (pushCode, pushOutput) = GitHelper.RunGitWithCode("push");
            if (pushCode != 0)
                return (false, $"Push 실패: {pushOutput}");

            // 6. git tag 생성 + push
            GitHelper.RunGit($"tag {version}");
            var verify = GitHelper.RunGit($"tag -l {version}");
            if (string.IsNullOrEmpty(verify))
                return (false, "태그 생성에 실패했습니다.");

            var (tagPushCode, tagPushOutput) = GitHelper.RunGitWithCode($"push origin {version}");
            if (tagPushCode != 0)
                return (false, $"태그 Push 실패: {tagPushOutput}");

            // 7. 캐시 갱신
            GitVersionResolver.InvalidateCache();
            WorkflowGenerator.InvalidateCache();

            return (true, null);
        }

        /// <summary>다음 버전 제안 (현재 patch + 1)</summary>
        public static string SuggestNextVersion()
        {
            var current = GitVersionResolver.GetVersion();
            var parts = current.Split('.');
            if (parts.Length < 3) return "0.1.0";

            int.TryParse(parts[2], out int patch);
            return $"{parts[0]}.{parts[1]}.{patch + 1}";
        }

        /// <summary>다음 마이너 버전 제안</summary>
        public static string SuggestNextMinor()
        {
            var current = GitVersionResolver.GetVersion();
            var parts = current.Split('.');
            if (parts.Length < 3) return "0.1.0";

            int.TryParse(parts[1], out int minor);
            return $"{parts[0]}.{minor + 1}.0";
        }
    }
}
#endif
