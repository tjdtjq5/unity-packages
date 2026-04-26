using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class GitHubPusher
    {
        public static void Push(SupaRunSettings settings, List<GeneratedFile> files,
            Action onSuccess, Action<string> onFailed)
        {
            var gh = PrerequisiteChecker.CheckGh();
            if (!gh.Installed || !gh.LoggedIn)
            {
                onFailed?.Invoke("gh CLI에 로그인되어 있지 않습니다.");
                return;
            }

            var repoName = settings.githubRepoName;
            if (string.IsNullOrEmpty(repoName))
            {
                onFailed?.Invoke("GitHub Repo Name이 설정되지 않았습니다.");
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "suparun_deploy_" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                // 1. clone
                Debug.Log($"[SupaRun:Deploy] Cloning {gh.Account}/{repoName}...");
                var (cloneCode, cloneOut) = PrerequisiteChecker.RunGh(
                    $"repo clone {gh.Account}/{repoName} \"{tempDir}\" -- --depth 1");
                if (cloneCode != 0)
                {
                    onFailed?.Invoke($"GitHub clone 실패:\n{cloneOut}");
                    return;
                }

                // 2. 기존 Generated/ + Shared/ 삭제 + 레거시 파일 정리
                foreach (var dir in new[] { "Generated", "Shared", "admin" })
                {
                    var d = Path.Combine(tempDir, dir);
                    if (Directory.Exists(d))
                        Directory.Delete(d, true);
                }
                // 레거시 GameServer.csproj 삭제 (리네임 전 잔여물)
                var legacyCsproj = Path.Combine(tempDir, "GameServer.csproj");
                if (File.Exists(legacyCsproj))
                    File.Delete(legacyCsproj);

                // 3. 파일 쓰기
                foreach (var file in files)
                {
                    var fullPath = Path.Combine(tempDir, file.Path);
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir!);
                    File.WriteAllText(fullPath, file.Content);
                }

                Debug.Log($"[SupaRun:Deploy] {files.Count} files written");

                // 4. git add + commit + push
                var (addCode, _) = PrerequisiteChecker.Run("git", $"-C \"{tempDir}\" add -A");
                if (addCode != 0)
                {
                    onFailed?.Invoke("git add 실패");
                    return;
                }

                var (commitCode, commitOut) = PrerequisiteChecker.Run("git",
                    $"-C \"{tempDir}\" commit -m \"deploy from Unity SupaRun\"");
                if (commitCode != 0 && !commitOut.Contains("nothing to commit"))
                {
                    onFailed?.Invoke($"git commit 실패:\n{commitOut}");
                    return;
                }

                if (commitOut.Contains("nothing to commit"))
                {
                    // 코드 변경은 없지만 시크릿 갱신 + 재배포
                    SetSecrets(settings, gh);
                    Debug.Log("[SupaRun:Deploy] 코드 변경 없음, 시크릿 갱신 후 재배포");

                    var repo = $"{gh.Account}/{repoName}";

                    // 빈 커밋으로 push 트리거 (가장 확실한 방법)
                    var (emptyCode, emptyOut) = PrerequisiteChecker.Run("git",
                        $"-C \"{tempDir}\" commit --allow-empty -m \"redeploy: update secrets\"");
                    if (emptyCode != 0)
                    {
                        onFailed?.Invoke($"빈 커밋 실패:\n{emptyOut}");
                        return;
                    }

                    var (pushCode2, pushOut2) = PrerequisiteChecker.Run("git", $"-C \"{tempDir}\" push");
                    if (pushCode2 != 0)
                    {
                        onFailed?.Invoke($"push 실패:\n{pushOut2}");
                        return;
                    }

                    onSuccess?.Invoke();
                    return;
                }

                var (pushCode, pushOut) = PrerequisiteChecker.Run("git", $"-C \"{tempDir}\" push");
                if (pushCode != 0)
                {
                    onFailed?.Invoke($"git push 실패:\n{pushOut}");
                    return;
                }

                // 6. GitHub Secrets 설정
                SetSecrets(settings, gh);

                Debug.Log("[SupaRun:Deploy] Push 완료!");
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                onFailed?.Invoke($"예외 발생: {ex.Message}");
            }
            finally
            {
                // 5. 정리
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { /* ignore cleanup errors */ }
            }
        }

        static void SetSecrets(SupaRunSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            var repo = $"{gh.Account}/{settings.githubRepoName}";
            var projectId = settings.SupabaseProjectId;
            var dbPassword = SupaRunSettings.Instance.SupabaseDbPassword;

            // Supabase 연결 문자열
            var poolSize = settings.dbPoolSize > 0 ? settings.dbPoolSize : 20;
            var connStr = $"Host=db.{projectId}.supabase.co;Port=5432;Database=postgres;Username=postgres;Password={dbPassword};Maximum Pool Size={poolSize}";
            SetSecret(repo, "SUPABASE_CONNECTION_STRING", connStr);
            SetSecret(repo, "SUPABASE_AUTH_URL", $"https://{projectId}.supabase.co/auth/v1");

            // GCP (설정되어 있으면)
            if (settings.IsGcpConfigured)
            {
                SetSecret(repo, "CLOUD_RUN_SERVICE", settings.gcpServiceName?.ToLower());
                SetSecret(repo, "CLOUD_RUN_REGION", settings.gcpRegion);
                SetSecret(repo, "CLOUD_RUN_MIN_INSTANCES", settings.gcpMinInstances.ToString());
                SetSecret(repo, "CLOUD_RUN_MAX_INSTANCES", (settings.gcpMaxInstances > 0 ? settings.gcpMaxInstances : 3).ToString());
            }

            // Cron Secret (없으면 자동 생성)
            var cronSecret = SupaRunSettings.Instance.CronSecret;
            if (string.IsNullOrEmpty(cronSecret))
            {
                cronSecret = Guid.NewGuid().ToString("N");
                SupaRunSettings.Instance.CronSecret = cronSecret;
            }
            SetSecret(repo, "CRON_SECRET", cronSecret);

            Debug.Log("[SupaRun:Deploy] GitHub Secrets 설정 완료");
        }

        static void SetSecret(string repo, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            PrerequisiteChecker.RunGh($"secret set {name} --repo {repo} --body \"{value}\"");
        }
    }
}
