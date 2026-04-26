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
                        var snakeName = ToSnakeCase(type.Name);
                        foreach (var m in methods)
                            endpoints.Add($"{snakeName}/{m.Name}");
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
                // macOS GUI Unity Editor는 셸 PATH를 상속하지 않을 수 있으므로
                // PrerequisiteChecker가 찾은 절대 경로를 사용한다.
                var dotnet = PrerequisiteChecker.FindDotnet() ?? "dotnet";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dotnet,
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
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;
            var (ok, _) = await SupabaseManagementApi.PatchAuthConfig(
                settings.SupabaseProjectId, token, "{\"mailer_autoconfirm\":true}");
            if (ok) Debug.Log("[SupaRun:Deploy] autoconfirm 설정 완료");
        }

        /// <summary>pg_cron 잡 관리. [Cron] 있으면 활성화+등록, 없으면 기존 잡 삭제.</summary>
        public static async void RegisterCronJobs()
        {
            var settings = SupaRunSettings.Instance;
            var accessToken = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.Log("[SupaRun:Deploy] Supabase Access Token 없음 — pg_cron 스킵");
                return;
            }

            var projectId = settings.SupabaseProjectId;
            var logicTypes = ScanTypes<ServiceAttribute>();

            var cronSecret = SupaRunSettings.Instance.CronSecret;
            if (string.IsNullOrEmpty(cronSecret))
            {
                cronSecret = Guid.NewGuid().ToString("N");
                SupaRunSettings.Instance.CronSecret = cronSecret;
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

        // [Table]/[Config]/[Service] 부착된 모든 user 타입을 수집한다.
        // TypeCache는 모든 어셈블리(asmdef 분리 포함)를 미리 인덱싱하므로
        // Assembly-CSharp 단일 가정 없이 안전하게 스캔한다.
        static Type[] ScanTypes<T>() where T : Attribute
            => TypeCache.GetTypesWithAttribute<T>().ToArray();

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
            content = content.Replace("{{SUPABASE_ANON_KEY}}", SupaRunSettings.Instance.SupabaseAnonKey ?? "");

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

                var content = StripForServer(File.ReadAllText(sourceFile));
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
                            files.Add(new GeneratedFile($"Shared/Models/{returnType.Name}.cs", StripForServer(File.ReadAllText(src))));
                    }
                }
            }

            return files;
        }

        static string StripForServer(string content)
        {
            // 1) [Json(typeof(...))] → [Json]  (타입 힌트는 클라이언트 Source Generator 전용)
            content = System.Text.RegularExpressions.Regex.Replace(
                content, @"\[Json\(typeof\(.+?\)\)\]", "[Json]");

            // 2) 어드민 전용 어트리뷰트 통째로 제거 (서버에서 불필요, [ \t]*로 개행 보존)
            content = System.Text.RegularExpressions.Regex.Replace(
                content, @"[ \t]*\[EnumType\(typeof\(.+?\)\)\]", "");
            content = System.Text.RegularExpressions.Regex.Replace(
                content, @"[ \t]*\[VisibleIf\(.+?\)\]", "");
            content = System.Text.RegularExpressions.Regex.Replace(
                content, @"[ \t]*\[HiddenIf\(.+?\)\]", "");

            // 3) #if UNITY 전처리기 블록 제거
            content = StripUnityPreprocessorBlocks(content);

            // 4) 서버 비호환 using 제거 (화이트리스트: System, Tjdtjq5, Newtonsoft, Microsoft)
            var lines = content.Split('\n');
            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("using ") && !IsServerSafeUsing(trimmed))
                    continue;
                sb.AppendLine(line.TrimEnd('\r'));
            }
            return sb.ToString().TrimEnd();
        }

        static bool IsServerSafeUsing(string usingLine)
        {
            // using alias (예: using SkillDef = Tjdtjq5.SupaRun.Defs.SkillDef;) → 우변 체크
            var checkTarget = usingLine;
            int eqIdx = usingLine.IndexOf('=');
            if (eqIdx > 0)
                checkTarget = "using " + usingLine.Substring(eqIdx + 1).TrimStart();

            return checkTarget.StartsWith("using System") ||
                   checkTarget.StartsWith("using Tjdtjq5") ||
                   checkTarget.StartsWith("using Newtonsoft") ||
                   checkTarget.StartsWith("using Microsoft");
        }

        static string StripUnityPreprocessorBlocks(string content)
        {
            var lines = content.Split('\n');
            var sb = new System.Text.StringBuilder();
            int stripDepth = 0;  // 내용까지 삭제하는 블록 깊이
            int unwrapDepth = 0; // 래핑만 벗기는 블록 깊이 (#if UNITY_EDITOR)

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                // #if UNITY_EDITOR → 래핑만 벗기기 (내용 유지, Service 코드 보존)
                if (trimmed.StartsWith("#if UNITY_EDITOR"))
                {
                    unwrapDepth++;
                    continue; // #if 줄만 제거
                }
                // #if UNITY (EDITOR 아닌 것) → 블록 통째 삭제
                if (trimmed.StartsWith("#if UNITY"))
                {
                    stripDepth++;
                    continue;
                }
                // 삭제 블록 내부의 중첩 #if
                if (stripDepth > 0 && trimmed.StartsWith("#if"))
                {
                    stripDepth++;
                    continue;
                }
                // 래핑 블록 내부의 중첩 #if (내용 유지 중이므로 그대로 출력)
                // → 별도 처리 불필요, 아래로 통과

                if (trimmed.StartsWith("#else"))
                {
                    if (stripDepth > 0) continue;    // 삭제 블록의 #else → 스킵
                    if (unwrapDepth > 0) continue;    // 래핑 블록의 #else → 스킵 (else 이후는 non-editor 코드)
                }
                if (trimmed.StartsWith("#endif"))
                {
                    if (stripDepth > 0) { stripDepth--; continue; }
                    if (unwrapDepth > 0) { unwrapDepth--; continue; } // #endif 줄만 제거
                }

                if (stripDepth > 0)
                    continue; // 삭제 블록 내부 → 스킵

                sb.AppendLine(line.TrimEnd('\r'));
            }
            return sb.ToString();
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

        static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }
    }
}
