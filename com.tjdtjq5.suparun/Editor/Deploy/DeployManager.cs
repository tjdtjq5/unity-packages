using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class DeployManager
    {
        // ── 파일 생성 (배포 + 빌드 테스트 공용) ──

        public static (List<GeneratedFile> files, Type[] logicTypes, string error) GenerateFiles(
            SupaRunSettings settings, Action<string> onProgress = null)
        {
            onProgress?.Invoke("코드 스캔 중...");

            var tableTypes = ScanTypes<TableAttribute>();
            var specTypes = ScanTypes<ConfigAttribute>();
            var logicTypes = ScanTypes<ServiceAttribute>();

            if (tableTypes.Length == 0 && specTypes.Length == 0 && logicTypes.Length == 0)
                return (null, null, "[Table], [Config], [Service] 클래스가 하나도 없습니다.");

            Debug.Log($"[SupaRun:Deploy] 스캔 완료 — Table: {tableTypes.Length}, Config: {specTypes.Length}, Service: {logicTypes.Length}");

            onProgress?.Invoke("서버 코드 생성 중...");
            var files = ServerCodeGenerator.Generate(tableTypes, specTypes, logicTypes, settings);
            files.AddRange(GetTemplateFiles(settings));
            files.AddRange(GetSharedFiles(tableTypes, specTypes, logicTypes));

            Debug.Log($"[SupaRun:Deploy] 파일 {files.Count}개 생성");
            return (files, logicTypes, null);
        }

        // ── 배포 ──

        public static void Deploy(SupaRunSettings settings,
            Action<string> onProgress, Action onSuccess, Action<string> onFailed,
            Action onSkipped = null)
        {
            if (!settings.IsGitHubConfigured)
            {
                onFailed?.Invoke("GitHub 설정이 필요합니다. Settings에서 설정하세요.");
                return;
            }

            var (files, logicTypes, genError) = GenerateFiles(settings, onProgress);
            if (files == null)
            {
                onFailed?.Invoke(genError);
                return;
            }

            // 변경 감지 스킵
            if (settings.HasCache(ServerCacheTypes.Skip) && !ServerCacheHealthChecker.IsCodeChanged(files))
            {
                onSkipped?.Invoke();
                return;
            }

            onProgress?.Invoke("GitHub에 push 중...");
            GitHubPusher.Push(settings, files,
                onSuccess: () =>
                {
                    // 배포 스냅샷 저장
                    ServerCacheHealthChecker.SaveDeploySnapshot(files);

                    var endpoints = new List<string>();
                    foreach (var type in logicTypes)
                    {
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Where(m => !m.IsSpecialName);
                        foreach (var m in methods)
                            endpoints.Add($"{type.Name}/{m.Name}");
                    }
                    DeployRegistry.MarkDeployed(endpoints.ToArray());

                    Debug.Log($"[SupaRun:Deploy] 배포 목록 업데이트: {endpoints.Count}개 엔드포인트");

                    // autoconfirm 보장 (어드민 회원가입 즉시 로그인용)
                    EnsureAutoConfirm(settings);

                    onSuccess?.Invoke();
                },
                onFailed: error =>
                {
                    ServerCacheHealthChecker.MarkDeployFailed();
                    onFailed?.Invoke(error);
                });
        }

        // ── 로컬 빌드 테스트 ──

        public static bool IsDotnetAvailable() => PrerequisiteChecker.IsDotnetInstalled();

        /// <summary>메인 스레드: 코드 생성 + temp 폴더에 쓰기. 경로를 반환.</summary>
        public static (string tempDir, string error) PrepareBuildTest(SupaRunSettings settings)
        {
            var (files, _, genError) = GenerateFiles(settings);
            if (files == null)
                return (null, genError);

            var tempDir = Path.Combine(Path.GetTempPath(),
                "suparun_buildtest_" + Guid.NewGuid().ToString("N")[..8]);

            foreach (var file in files)
            {
                if (file.Path.StartsWith(".github")) continue;
                if (file.Path.EndsWith(".sql")) continue;

                var fullPath = Path.Combine(tempDir, file.Path);
                var dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);
                File.WriteAllText(fullPath, file.Content);
            }

            return (tempDir, null);
        }

        /// <summary>백그라운드 OK: dotnet build 실행. 완료 후 temp 폴더 삭제.</summary>
        public static (bool success, string output) RunDotnetBuild(string tempDir)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{tempDir}\" --nologo -v q",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // 영어 출력 강제 (한글 깨짐 방지)
                psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return (false, "dotnet 프로세스 시작 실패");
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);

                var output = string.IsNullOrEmpty(stdout) ? stderr : stdout;
                return (p.ExitCode == 0, output);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* ignore */ }
            }
        }

        /// <summary>어드민 회원가입 즉시 로그인을 위해 autoconfirm 보장.</summary>
        static async void EnsureAutoConfirm(SupaRunSettings settings)
        {
            var token = SupaRunSettings.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;
            var (ok, _) = await SupabaseManagementApi.PatchAuthConfig(
                settings.SupabaseProjectId, token, "{\"mailer_autoconfirm\":true}");
            if (ok) Debug.Log("[SupaRun:Deploy] autoconfirm 설정 완료");
        }

        /// <summary>pg_cron 잡 관리. [Cron] 있으면 활성화+등록, 없으면 기존 잡 삭제.</summary>
        public static async void RegisterCronJobs()
        {
            var settings = SupaRunSettings.Instance;
            var accessToken = SupaRunSettings.SupabaseAccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.Log("[SupaRun:Deploy] Supabase Access Token 없음 — pg_cron 스킵");
                return;
            }

            var projectId = settings.SupabaseProjectId;
            var logicTypes = ScanTypes<ServiceAttribute>();

            var cronSecret = SupaRunSettings.CronSecret;
            if (string.IsNullOrEmpty(cronSecret))
            {
                cronSecret = Guid.NewGuid().ToString("N");
                SupaRunSettings.CronSecret = cronSecret;
            }

            var scheduleSqls = ServerCodeGenerator.GenerateCronScheduleSqls(
                logicTypes, settings.cloudRunUrl ?? "", cronSecret);

            try
            {
                if (scheduleSqls != null)
                {
                    // [Cron] 있음 → extensions 활성화 + 기존 잡 삭제 + 새 잡 등록
                    await RunCronQuery(projectId, accessToken, ServerCodeGenerator.GenerateCronExtensionsSql_PgCron());
                    await RunCronQuery(projectId, accessToken, ServerCodeGenerator.GenerateCronExtensionsSql_PgNet());
                    await RunCronQuery(projectId, accessToken, ServerCodeGenerator.GenerateCronCleanupSql());

                    var allOk = true;
                    foreach (var sql in scheduleSqls)
                    {
                        var (ok, _, error) = await SupabaseManagementApi.RunQuery(projectId, accessToken, sql);
                        if (!ok)
                        {
                            Debug.LogWarning($"[SupaRun:Deploy] pg_cron 잡 등록 실패: {error}\nSQL: {sql}");
                            allOk = false;
                        }
                    }

                    Debug.Log(allOk
                        ? $"[SupaRun:Deploy] pg_cron {scheduleSqls.Count}개 잡 등록 완료"
                        : "[SupaRun:Deploy] pg_cron 일부 잡 등록 실패 (위 로그 확인)");
                }
                else
                {
                    // [Cron] 없음 → 기존 gs_ 잡만 삭제 (extensions는 유지)
                    await RunCronQuery(projectId, accessToken, ServerCodeGenerator.GenerateCronCleanupSql());
                    Debug.Log("[SupaRun:Deploy] [Cron] 메서드 없음 — 기존 잡 정리 완료");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:Deploy] pg_cron 예외: {ex.Message}");
            }
        }

        static async System.Threading.Tasks.Task RunCronQuery(string projectId, string token, string sql)
        {
            var (ok, _, error) = await SupabaseManagementApi.RunQuery(projectId, token, sql);
            if (!ok) Debug.LogWarning($"[SupaRun:Deploy] pg_cron SQL 실패: {error}");
        }

        static Type[] ScanTypes<T>() where T : Attribute
        {
            var result = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("Assembly-CSharp")) continue;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute<T>() != null)
                        result.Add(type);
                }
            }
            return result.ToArray();
        }

        static List<GeneratedFile> GetTemplateFiles(SupaRunSettings settings)
        {
            var files = new List<GeneratedFile>();
            var templateRoot = GetTemplateRoot();

            // ASP.NET 템플릿
            files.Add(LoadTemplate(templateRoot, "AspNetTemplate~/Program.cs.template", "Program.cs", settings));
            files.Add(LoadTemplate(templateRoot, "AspNetTemplate~/SupaRun.csproj.template", "SupaRun.csproj", settings));
            files.Add(LoadTemplate(templateRoot, "AspNetTemplate~/Dockerfile.template", "Dockerfile", settings));
            files.Add(LoadTemplate(templateRoot, "AspNetTemplate~/appsettings.json.template", "appsettings.json", settings));

            // Workflow
            files.Add(LoadTemplate(templateRoot, "WorkflowTemplate~/deploy.yml.template", ".github/workflows/deploy.yml", settings));

            // Admin 웹 페이지
            var adminTemplatePath = Path.Combine(templateRoot, "AdminTemplate~/index.html");
            if (File.Exists(adminTemplatePath))
                files.Add(LoadTemplate(templateRoot, "AdminTemplate~/index.html", "admin/index.html", settings));

            // Auth 플랫폼 (GPGS/GameCenter 활성화 시)
            if (settings.enabledAuthProviders != null)
            {
                if (settings.enabledAuthProviders.Contains("GPGS") || settings.enabledAuthProviders.Contains("GameCenter"))
                {
                    files.Add(LoadTemplate(templateRoot, "AuthTemplate~/PlatformAuthRequest.cs.template",
                        "Generated/Auth/PlatformAuthRequest.cs", settings));
                }
                if (settings.enabledAuthProviders.Contains("GPGS"))
                {
                    files.Add(LoadTemplate(templateRoot, "AuthTemplate~/AuthGPGSController.cs.template",
                        "Generated/Auth/AuthGPGSController.cs", settings));
                }
                if (settings.enabledAuthProviders.Contains("GameCenter"))
                {
                    files.Add(LoadTemplate(templateRoot, "AuthTemplate~/AuthGameCenterController.cs.template",
                        "Generated/Auth/AuthGameCenterController.cs", settings));
                }
            }

            return files;
        }

        static GeneratedFile LoadTemplate(string root, string templatePath, string outputPath, SupaRunSettings settings)
        {
            var fullPath = Path.Combine(root, templatePath);
            var content = File.ReadAllText(fullPath);

            // SDK 버전 감지
            var dotnetMajor = PrerequisiteChecker.GetDotnetMajorVersion();
            if (dotnetMajor < 8) dotnetMajor = 8; // 최소 8

            // 변수 치환
            content = content.Replace("{{SUPABASE_PROJECT_ID}}", settings.SupabaseProjectId);
            content = content.Replace("{{DOTNET_MAJOR}}", dotnetMajor.ToString());
            content = content.Replace("{{SUPABASE_URL}}", settings.supabaseUrl ?? "");
            content = content.Replace("{{SUPABASE_ANON_KEY}}", SupaRunSettings.SupabaseAnonKey ?? "");

            // OAuth provider 목록 (Guest, GPGS, GameCenter 제외 = 웹 OAuth만)
            var q = '"';
            var oauthProviders = settings.enabledAuthProviders?
                .Where(p => p != "Guest" && p != "GPGS" && p != "GameCenter")
                .Select(p => q + p.ToLower() + q) ?? Enumerable.Empty<string>();
            content = content.Replace("{{AUTH_PROVIDERS_JSON}}", "[" + string.Join(",", oauthProviders) + "]");

            return new GeneratedFile(outputPath, content);
        }

        static string GetTemplateRoot()
        {
            // 패키지 경로에서 Templates 폴더 찾기
            var guids = AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Packages/com.tjdtjq5.suparun/Templates" });
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return Path.GetDirectoryName(Path.GetDirectoryName(path));
            }
            // 폴백: 직접 경로
            return "Packages/com.tjdtjq5.suparun/Templates";
        }

        static List<GeneratedFile> GetSharedFiles(Type[] tableTypes, Type[] specTypes, Type[] logicTypes)
        {
            var files = new List<GeneratedFile>();
            var allTypes = tableTypes.Concat(specTypes).Concat(logicTypes);

            foreach (var type in allTypes)
            {
                var sourceFile = FindSourceFile(type);
                if (sourceFile == null) continue;

                var content = StripUnityUsings(File.ReadAllText(sourceFile));
                var category = type.GetCustomAttribute<TableAttribute>() != null ? "Table"
                    : type.GetCustomAttribute<ConfigAttribute>() != null ? "Config"
                    : "Service";

                files.Add(new GeneratedFile($"Shared/{category}/{type.Name}.cs", content));
            }

            // DTO 클래스도 찾기 (Service 메서드의 반환 타입)
            foreach (var type in logicTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var m in methods)
                {
                    var returnType = m.ReturnType;
                    if (returnType.IsGenericType)
                        returnType = returnType.GetGenericArguments()[0];

                    // 기본 타입이 아니고, 이미 추가하지 않은 경우
                    if (!returnType.IsPrimitive && returnType != typeof(string) && returnType != typeof(void)
                        && !allTypes.Contains(returnType))
                    {
                        var src = FindSourceFile(returnType);
                        if (src != null)
                            files.Add(new GeneratedFile($"Shared/Models/{returnType.Name}.cs", StripUnityUsings(File.ReadAllText(src))));
                    }
                }
            }

            return files;
        }

        static string StripUnityUsings(string content)
        {
            var lines = content.Split('\n');
            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("using UnityEngine") ||
                    trimmed.StartsWith("using UnityEditor") ||
                    trimmed.StartsWith("using Unity."))
                    continue;
                // #if UNITY_EDITOR / #endif 전처리기 지시문 제거
                if (trimmed.StartsWith("#if UNITY_EDITOR") ||
                    trimmed.StartsWith("#endif // UNITY_EDITOR"))
                    continue;
                sb.AppendLine(line.TrimEnd('\r'));
            }
            return sb.ToString().TrimEnd();
        }

        static string FindSourceFile(Type type)
        {
            // MonoScript로 소스 파일 경로 찾기
            var guids = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                    return path;
            }
            return null;
        }
    }
}
