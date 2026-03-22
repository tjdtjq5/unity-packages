#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>에디터에서 Release (사전 검증 + yml 재생성 + tag + push)</summary>
    public static class ReleaseManager
    {
        // ── 사전 검증 ──

        /// <summary>Release 전 사전 검증. 실패 시 에러 메시지 목록 반환.</summary>
        public static List<string> PreflightCheck(string version)
        {
            var errors = new List<string>();

            // 1. 버전 형식
            if (!version.StartsWith("v")) version = $"v{version}";
            var clean = version.TrimStart('v');
            var parts = clean.Split('.');
            if (parts.Length < 3)
                errors.Add("버전 형식이 올바르지 않습니다. 예: 0.2.0");

            // 2. 태그 중복
            var existing = GitHelper.RunGit($"tag -l {version}");
            if (!string.IsNullOrEmpty(existing))
                errors.Add($"태그 {version}이 이미 존재합니다.");

            // 3. 미커밋 변경사항
            var status = GitHelper.RunGit("status --porcelain");
            if (!string.IsNullOrEmpty(status))
                errors.Add("커밋되지 않은 변경사항이 있습니다. 커밋 후 Release하세요.");

            // 4. manifest.json에 file: 로컬 경로 체크
            var manifestPath = System.IO.Path.Combine(
                System.IO.Directory.GetParent(UnityEngine.Application.dataPath)!.FullName,
                "Packages", "manifest.json");
            if (System.IO.File.Exists(manifestPath))
            {
                var content = System.IO.File.ReadAllText(manifestPath);
                if (content.Contains("\"file:"))
                    errors.Add("manifest.json에 로컬 경로(file:)가 포함되어 있습니다.\n" +
                        "  패키지 dev 모드를 종료하세요. (/pkg-dev --end)");
            }

            // 5. gh CLI 로그인
            var gh = GhChecker.Check();
            if (!gh.LoggedIn)
                errors.Add("gh CLI에 로그인되어 있지 않습니다.");

            // 6. GitHub 리포 연결
            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo))
                errors.Add("GitHub 리포가 연결되어 있지 않습니다.");

            // 7. yml 파일 존재
            if (!WorkflowGenerator.WorkflowExists())
                errors.Add("워크플로우 파일이 없습니다. 먼저 생성하세요.");

            return errors;
        }

        // ── Release 실행 ──

        /// <summary>yml 재생성 + commit + push + tag → 빌드 트리거</summary>
        public static (bool success, string error) CreateRelease(string version)
        {
            // 1. 사전 검증
            var errors = PreflightCheck(version);
            if (errors.Count > 0)
                return (false, string.Join("\n", errors));

            // 2. v 접두사 보장
            if (!version.StartsWith("v")) version = $"v{version}";

            // 3. yml 재생성
            var settings = BuildAutomationSettings.Instance;
            var yml = WorkflowGenerator.Generate(settings);
            WorkflowGenerator.SaveToProject(yml);

            // 4. 원격 최신화 + commit + push
            var releaseBranch = settings.releaseBranch;
            GitHelper.RunGitWithCode($"pull origin {releaseBranch} --rebase");

            GitHelper.RunGit("add .github/workflows/build-and-deploy.yml");
            GitHelper.RunGitWithCode(
                $"commit -m \"Update CI/CD workflow for {version}\" --allow-empty");

            var (pushCode, pushOutput) = GitHelper.RunGitWithCode($"push origin {releaseBranch}");
            if (pushCode != 0)
                return (false, $"Push 실패: {pushOutput}");

            // 5. tag 생성 + push
            GitHelper.RunGit($"tag {version}");
            var verify = GitHelper.RunGit($"tag -l {version}");
            if (string.IsNullOrEmpty(verify))
                return (false, "태그 생성에 실패했습니다.");

            var (tagPushCode, tagPushOutput) = GitHelper.RunGitWithCode($"push origin {version}");
            if (tagPushCode != 0)
                return (false, $"태그 Push 실패: {tagPushOutput}");

            // 6. 캐시 갱신
            GitVersionResolver.InvalidateCache();
            WorkflowGenerator.InvalidateCache();

            // 7. 캐시 헬스 체크 스냅샷 저장 + 클린 빌드 플래그 리셋
            CacheHealthChecker.SaveBuildSnapshot(version);
            BuildAutomationSettings.Instance.forceCleanBuild = false;

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
