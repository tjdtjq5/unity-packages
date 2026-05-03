#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>에디터에서 Release (사전 검증 + yml 재생성 + tag + push)</summary>
    public static class ReleaseManager
    {
        // ── 사전 검증 ──

        /// <summary>Release 전 사전 검증. 실패 시 에러 메시지 목록 반환.</summary>
        /// <param name="checkDirty">미커밋 변경사항 검사 포함 여부. CreateRelease는 자동 commit 흐름이 있어 false로 호출.</param>
        public static List<string> PreflightCheck(string version, bool checkDirty = true)
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

            // 3. 미커밋 변경사항 — manifest.json은 자동 swap이 처리하므로 제외
            if (checkDirty)
            {
                var dirty = GetDirtyFilesExceptManifest();
                if (dirty.Count > 0)
                    errors.Add("커밋되지 않은 변경사항이 있습니다. 커밋 후 Release하세요.");
            }

            // 4. manifest.json file: 검사 — 백업(_xxx_remote) 없는 file:만 차단
            var localPkgs = ManifestModeSwapper.DetectLocalPackages();
            foreach (var pkg in localPkgs)
            {
                if (!pkg.HasBackup)
                    errors.Add($"manifest.json: '{pkg.PackageName}' 패키지가 file: 경로지만 " +
                               $"백업 URL ('_{pkg.ShortName}_remote') 필드가 없습니다. " +
                               "수동으로 git URL로 복원하세요.");
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
            // 0. file: 자동 swap (백업 있는 패키지만) — 워킹트리를 git URL로 변경
            var localPkgs = ManifestModeSwapper.DetectLocalPackages();
            var swappablePkgs = localPkgs.Where(p => p.HasBackup).ToList();
            bool swapped = swappablePkgs.Count > 0;
            if (swapped)
                ManifestModeSwapper.SwapToRemote(swappablePkgs);

            try
            {
                // 1. 사전 검증 (dirty 검사는 별도 흐름에서 처리)
                var errors = PreflightCheck(version, checkDirty: false);
                if (errors.Count > 0)
                    return (false, string.Join("\n", errors));

                // 2. v 접두사 보장
                if (!version.StartsWith("v")) version = $"v{version}";

                // 2.5. 미커밋 변경사항 → 다이얼로그로 확인 후 자동 commit
                var dirtyFiles = GetDirtyFilesExceptManifest();
                if (dirtyFiles.Count > 0)
                {
                    const int previewMax = 10;
                    var preview = string.Join("\n  ", dirtyFiles.Take(previewMax));
                    if (dirtyFiles.Count > previewMax)
                        preview += $"\n  ... 외 {dirtyFiles.Count - previewMax}개";

                    var ok = EditorUtility.DisplayDialog(
                        "커밋되지 않은 변경사항",
                        $"미커밋 변경사항 {dirtyFiles.Count}개:\n\n  {preview}\n\n" +
                        $"자동으로 커밋 후 Release ({version})를 진행할까요?",
                        "커밋 후 진행",
                        "취소");

                    if (!ok)
                        return (false, "사용자가 Release를 취소했습니다.");

                    GitHelper.RunGit("add -A");
                    var (commitCode, commitOutput) = GitHelper.RunGitWithCode(
                        $"commit -m \"WIP: pre-release {version}\"");
                    if (commitCode != 0)
                        return (false, $"자동 커밋 실패: {commitOutput}");
                }

                // 3. yml 재생성
                var settings = BuildAutomationSettings.Instance;
                var yml = WorkflowGenerator.Generate(settings);
                WorkflowGenerator.SaveToProject(yml);

                // 4. 원격 최신화 + commit + push
                var releaseBranch = settings.releaseBranch;
                GitHelper.RunGitWithCode($"pull origin {releaseBranch} --rebase");

                if (swapped)
                    GitHelper.RunGit("add Packages/manifest.json");
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

                // 7. 캐시 헬스 체크 스냅샷 저장
                CacheHealthChecker.SaveBuildSnapshot(version);

                return (true, null);
            }
            finally
            {
                // 8. 워킹트리만 file:로 복원 (commit 안 함) — 로컬 개발 모드 유지
                if (swapped)
                    ManifestModeSwapper.SwapToLocal(swappablePkgs);
            }
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

        // ── 헬퍼 ──

        /// <summary>git status에서 manifest.json을 제외한 변경 파일 목록.</summary>
        static List<string> GetDirtyFilesExceptManifest()
        {
            var status = GitHelper.RunGit("status --porcelain");
            if (string.IsNullOrEmpty(status)) return new List<string>();

            return status.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Where(l => !l.Contains("Packages/manifest.json"))
                .Select(l => l.Length > 3 ? l.Substring(3).Trim() : l.Trim())
                .ToList();
        }
    }
}
#endif
