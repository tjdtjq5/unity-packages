using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class PrerequisiteChecker
    {
        public struct ToolStatus
        {
            public bool Installed;
            public bool PathOk;        // PATH에서 찾았는지
            public string FullPath;    // 실행 파일 전체 경로
            public string Version;
            public bool LoggedIn;
            public string Account;
            public string Project;
        }

        static ToolStatus? _gcloud, _gh;
        static double _gcloudCacheTime, _ghCacheTime;
        const double CACHE_SEC = 30;

        // 찾은 CLI 전체 경로 캐시
        static string _gcloudPath;
        static string _ghPath;
        static string _dotnetPath;

        // 플랫폼 — Mac/Linux GUI 에디터는 셸 PATH를 상속하지 않을 수 있으므로
        // 명령은 가능한 한 절대 경로로 실행한다.
        static bool IsWindows => System.Environment.OSVersion.Platform == System.PlatformID.Win32NT;

        public static void InvalidateCache()
        {
            _gcloud = _gh = null;
            _dotnetMajor = null;
            _gcloudPath = _ghPath = _dotnetPath = null;
        }

        /// <summary>백그라운드에서 CLI 체크 후 메인 스레드에서 캐시 저장.</summary>
        public static void WarmCacheAsync()
        {
            if (_gcloud.HasValue && _gh.HasValue) return; // 이미 캐시 있음
            System.Threading.Tasks.Task.Run(() =>
            {
                var g = DoCheckGcloud();
                var h = DoCheckGh();
                EditorApplication.delayCall += () =>
                {
                    if (!_gcloud.HasValue)
                    {
                        _gcloud = g;
                        _gcloudCacheTime = EditorApplication.timeSinceStartup;
                    }
                    if (!_gh.HasValue)
                    {
                        _gh = h;
                        _ghCacheTime = EditorApplication.timeSinceStartup;
                    }
                };
            });
        }

        static bool IsGcloudCacheValid() =>
            EditorApplication.timeSinceStartup - _gcloudCacheTime < CACHE_SEC;
        static bool IsGhCacheValid() =>
            EditorApplication.timeSinceStartup - _ghCacheTime < CACHE_SEC;

        // ── dotnet SDK ──

        static int? _dotnetMajor;

        /// <summary>dotnet SDK 메이저 버전 반환. 미설치면 0.</summary>
        public static int GetDotnetMajorVersion()
        {
            if (_dotnetMajor.HasValue) return _dotnetMajor.Value;
            var dotnet = FindDotnet() ?? "dotnet";
            var (code, output) = Run(dotnet, "--version");
            if (code == 0 && !string.IsNullOrEmpty(output))
            {
                var parts = output.Trim().Split('.');
                if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
                {
                    _dotnetMajor = major;
                    return major;
                }
            }
            _dotnetMajor = 0;
            return 0;
        }

        public static bool IsDotnetInstalled() => GetDotnetMajorVersion() > 0;

        /// <summary>dotnet SDK 실행 파일 전체 경로. PATH → 알려진 설치 경로 순으로 탐색.</summary>
        public static string FindDotnet()
        {
            if (_dotnetPath != null) return _dotnetPath;

            // 1. PATH에서 찾기
            var (code, output) = IsWindows
                ? RunDirect("cmd.exe", "/c where dotnet")
                : RunDirect("/bin/sh", "-lc \"command -v dotnet\"");
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var first = output.Split('\n', '\r')[0].Trim();
                if (!string.IsNullOrEmpty(first) && File.Exists(first))
                {
                    _dotnetPath = first;
                    return _dotnetPath;
                }
            }

            // 2. 알려진 설치 경로 fallback
            string[] knownPaths;
            if (IsWindows)
            {
                var pf = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
                knownPaths = new[]
                {
                    Path.Combine(pf, "dotnet", "dotnet.exe"),
                    @"C:\Program Files\dotnet\dotnet.exe",
                    @"C:\Program Files (x86)\dotnet\dotnet.exe",
                };
            }
            else
            {
                var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                knownPaths = new[]
                {
                    "/usr/local/share/dotnet/dotnet",       // macOS PKG installer (x64)
                    "/usr/local/share/dotnet/x64/dotnet",   // macOS Apple Silicon + x64 SDK
                    "/opt/homebrew/bin/dotnet",             // Homebrew (Apple Silicon)
                    "/usr/local/bin/dotnet",                // Homebrew (Intel) / Linux
                    Path.Combine(home, ".dotnet", "dotnet"),// 사용자 수동 설치
                };
            }

            foreach (var p in knownPaths)
            {
                if (File.Exists(p))
                {
                    _dotnetPath = p;
                    return _dotnetPath;
                }
            }

            return null;
        }

        // ── CLI 경로 찾기 ──

        static string FindGcloud()
        {
            if (_gcloudPath != null) return _gcloudPath;

            // 1. PATH에서 찾기
            var (code, output) = IsWindows
                ? RunDirect("cmd.exe", "/c where gcloud")
                : RunDirect("/bin/sh", "-lc \"command -v gcloud\"");
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var first = output.Split('\n', '\r')[0].Trim();
                if (!string.IsNullOrEmpty(first) && File.Exists(first))
                {
                    _gcloudPath = first;
                    return _gcloudPath;
                }
            }

            // 2. 알려진 설치 경로에서 찾기
            string[] knownPaths;
            if (IsWindows)
            {
                var user = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                knownPaths = new[]
                {
                    Path.Combine(user, "Google", "Cloud SDK", "google-cloud-sdk", "bin", "gcloud.cmd"),
                    @"C:\Program Files (x86)\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd",
                    @"C:\Program Files\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd",
                };
            }
            else
            {
                var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                knownPaths = new[]
                {
                    "/opt/homebrew/bin/gcloud",                  // Homebrew (Apple Silicon)
                    "/usr/local/bin/gcloud",                     // Homebrew (Intel) / Linux
                    Path.Combine(home, "google-cloud-sdk", "bin", "gcloud"),
                    "/usr/local/google-cloud-sdk/bin/gcloud",
                };
            }

            foreach (var p in knownPaths)
            {
                if (File.Exists(p))
                {
                    _gcloudPath = p;
                    return _gcloudPath;
                }
            }

            return null;
        }

        static string FindGh()
        {
            if (_ghPath != null) return _ghPath;

            // 1. PATH에서 찾기
            var (code, output) = IsWindows
                ? RunDirect("cmd.exe", "/c where gh")
                : RunDirect("/bin/sh", "-lc \"command -v gh\"");
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var first = output.Split('\n', '\r')[0].Trim();
                if (!string.IsNullOrEmpty(first) && File.Exists(first))
                {
                    _ghPath = first;
                    return _ghPath;
                }
            }

            // 2. 알려진 설치 경로
            string[] knownPaths;
            if (IsWindows)
            {
                var programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
                knownPaths = new[]
                {
                    Path.Combine(programFiles, "GitHub CLI", "gh.exe"),
                    @"C:\Program Files\GitHub CLI\gh.exe",
                    @"C:\Program Files (x86)\GitHub CLI\gh.exe",
                };
            }
            else
            {
                knownPaths = new[]
                {
                    "/opt/homebrew/bin/gh",   // Homebrew (Apple Silicon)
                    "/usr/local/bin/gh",      // Homebrew (Intel) / Linux
                };
            }

            foreach (var p in knownPaths)
            {
                if (File.Exists(p))
                {
                    _ghPath = p;
                    return _ghPath;
                }
            }

            return null;
        }

        // ── 상태 체크 ──

        public static ToolStatus CheckGcloud()
        {
            if (_gcloud.HasValue && IsGcloudCacheValid()) return _gcloud.Value;
            if (_gcloud.HasValue && !IsGcloudCacheValid())
            {
                // 캐시 만료 → 백그라운드 갱신, 기존 값 즉시 반환
                RefreshGcloudAsync();
                return _gcloud.Value;
            }
            // 최초 1회만 동기 실행
            var s = DoCheckGcloud();
            _gcloud = s;
            _gcloudCacheTime = EditorApplication.timeSinceStartup;
            return s;
        }

        static bool _gcloudRefreshing;
        static void RefreshGcloudAsync()
        {
            if (_gcloudRefreshing) return;
            _gcloudRefreshing = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                var s = DoCheckGcloud();
                EditorApplication.delayCall += () =>
                {
                    _gcloud = s;
                    _gcloudCacheTime = EditorApplication.timeSinceStartup;
                    _gcloudRefreshing = false;
                };
            });
        }

        static ToolStatus DoCheckGcloud()
        {
            var s = new ToolStatus();
            var path = FindGcloud();
            if (path == null) return s;

            var (code, output) = Run(path, "--version");
            s.Installed = code == 0;
            s.PathOk = (path == "gcloud");
            s.FullPath = path;

            if (s.Installed)
            {
                var m = Regex.Match(output, @"Google Cloud SDK (\S+)");
                s.Version = m.Success ? m.Groups[1].Value : "unknown";

                var (c2, o2) = Run(path, "auth list --format=value(account,status)");
                if (c2 == 0 && !string.IsNullOrEmpty(o2))
                {
                    foreach (var line in o2.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        if (trimmed.Contains("*") || trimmed.Contains("ACTIVE"))
                        {
                            s.LoggedIn = true;
                            s.Account = trimmed.Split('\t', ' ', '*')[0].Trim();
                            break;
                        }
                        if (trimmed.Contains("@"))
                        {
                            s.LoggedIn = true;
                            s.Account = trimmed.Split('\t', ' ')[0].Trim();
                            break;
                        }
                    }
                }

                var (c3, o3) = Run(path, "config get-value project");
                if (c3 == 0 && !string.IsNullOrEmpty(o3.Trim()) && o3.Trim() != "(unset)")
                    s.Project = o3.Trim();
            }
            return s;
        }

        static bool _ghRefreshing;
        static void RefreshGhAsync()
        {
            if (_ghRefreshing) return;
            _ghRefreshing = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                var s = DoCheckGh();
                EditorApplication.delayCall += () =>
                {
                    _gh = s;
                    _ghCacheTime = EditorApplication.timeSinceStartup;
                    _ghRefreshing = false;
                };
            });
        }

        static ToolStatus DoCheckGh()
        {
            var s = new ToolStatus();
            var path = FindGh();
            if (path == null) return s;

            var (code, output) = Run(path, "--version");
            s.Installed = code == 0;
            s.PathOk = (path == "gh");
            s.FullPath = path;

            if (s.Installed)
            {
                var m = Regex.Match(output, @"gh version (\S+)");
                s.Version = m.Success ? m.Groups[1].Value : "unknown";

                var (c2, o2) = Run(path, "auth status");
                s.LoggedIn = c2 == 0;
                if (s.LoggedIn)
                {
                    var m2 = Regex.Match(o2, @"Logged in to \S+ (?:account|as) (\S+)");
                    s.Account = m2.Success ? m2.Groups[1].Value : "";
                }
            }
            return s;
        }

        public static ToolStatus CheckGh()
        {
            if (_gh.HasValue && IsGhCacheValid()) return _gh.Value;
            if (_gh.HasValue && !IsGhCacheValid())
            {
                RefreshGhAsync();
                return _gh.Value;
            }
            var s = DoCheckGh();
            _gh = s;
            _ghCacheTime = EditorApplication.timeSinceStartup;
            return s;
        }

        // ── CLI 실행 ──

        public static void RunGcloudLogin()
        {
            var path = FindGcloud() ?? "gcloud";
            Process.Start(new ProcessStartInfo(path, "auth login")
            {
                UseShellExecute = false,
                CreateNoWindow = false
            });
            InvalidateCache();
        }

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

        /// <summary>
        /// gh CLI 실행. FindGh()로 절대 경로 자동 탐색하여 macOS GUI 앱 PATH 문제 회피.
        /// (Apple Silicon brew 설치 시 /opt/homebrew/bin이 GUI PATH에 없어 "gh: command not found" 발생)
        /// </summary>
        public static (int code, string output) RunGh(string args)
        {
            var path = FindGh() ?? "gh";
            return Run(path, args);
        }

        public static void SetGcloudProject(string projectId)
        {
            var path = FindGcloud() ?? "gcloud";
            Run(path, $"config set project {projectId}");
            InvalidateCache();
        }

        // ── GCP 자동화 ──

        public static (bool success, string error) EnableCloudRunApi(string projectId)
        {
            var path = FindGcloud() ?? "gcloud";
            var (code, output) = Run(path, $"services enable run.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com --project={projectId}");
            if (code == 0)
                return (true, null);

            if (output.Contains("billing") || output.Contains("BILLING"))
                return (false, $"결제가 활성화되지 않았습니다.\nGCP 콘솔에서 결제를 먼저 활성화하세요.\nhttps://console.cloud.google.com/billing/linkedaccount?project={projectId}");

            return (false, $"API 활성화 실패:\n{output}");
        }

        public static (bool success, string email, string error) CreateServiceAccountAndSetSecret(
            string projectId, string ghRepo)
        {
            var gcloudPath = FindGcloud() ?? "gcloud";
            var ghPath = FindGh() ?? "gh";

            var saName = "gameserver-deployer";
            var saEmail = $"{saName}@{projectId}.iam.gserviceaccount.com";

            // 1. Service Account 생성
            var (code1, out1) = Run(gcloudPath, $"iam service-accounts create {saName} --project={projectId} --display-name=\"SupaRun Deployer\"");
            if (code1 != 0 && !out1.Contains("already exists"))
                return (false, null, $"Service Account 생성 실패: {out1}");

            // 2. 권한 부여
            string[] roles = {
                "roles/run.admin",
                "roles/storage.admin",
                "roles/iam.serviceAccountUser",
                "roles/artifactregistry.admin",
                "roles/cloudbuild.builds.editor",
                "roles/cloudbuild.builds.builder",
                "roles/serviceusage.serviceUsageConsumer"
            };
            foreach (var role in roles)
            {
                var (code2, out2) = Run(gcloudPath, $"projects add-iam-policy-binding {projectId} --member=serviceAccount:{saEmail} --role={role} --quiet");
                if (code2 != 0)
                    return (false, null, $"권한 부여 실패 ({role}): {out2}");
            }

            // 3. JSON 키 생성
            var keyPath = Path.Combine(Path.GetTempPath(), $"gcp_key_{System.Guid.NewGuid():N}.json");
            var (code3, out3) = Run(gcloudPath, $"iam service-accounts keys create \"{keyPath}\" --iam-account={saEmail}");
            if (code3 != 0)
                return (false, null, $"키 생성 실패: {out3}");

            // 4. GitHub Secret 등록 (stdin)
            int code4;
            string out4;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ghPath,
                    Arguments = $"secret set GCP_SA_KEY --repo {ghRepo}",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (false, null, "gh 프로세스 시작 실패");
                p.StandardInput.Write(File.ReadAllText(keyPath));
                p.StandardInput.Close();
                out4 = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                code4 = p.ExitCode;
            }
            catch (System.Exception ex)
            {
                return (false, null, $"gh secret set 실패: {ex.Message}");
            }

            // 5. 임시 키 삭제
            try { File.Delete(keyPath); } catch { }

            if (code4 != 0)
                return (false, null, $"GitHub Secret 등록 실패: {out4}");

            return (true, saEmail, null);
        }

        public static bool IsCloudRunApiEnabled(string projectId)
        {
            var path = FindGcloud() ?? "gcloud";
            var (code, output) = Run(path, $"services list --project={projectId} --filter=config.name:run.googleapis.com --format=value(config.name)");
            return code == 0 && output.Contains("run.googleapis.com");
        }

        // ── 목록 조회 ──

        static (string id, string name)[] _gcpProjectsCache;
        static string[] _ghReposCache;
        static double _projectsCacheTime, _reposCacheTime;

        public static (string id, string name)[] GetGcpProjects()
        {
            if (_gcpProjectsCache != null && EditorApplication.timeSinceStartup - _projectsCacheTime < 60)
                return _gcpProjectsCache;

            var path = FindGcloud();
            if (path == null) return new (string, string)[0];

            var (code, output) = Run(path, "projects list --format=csv[no-heading](projectId,name)");
            if (code != 0) return new (string, string)[0];

            _gcpProjectsCache = output.Split('\n')
                .Where(line => !string.IsNullOrEmpty(line.Trim()))
                .Select(line =>
                {
                    var parts = line.Split(',');
                    return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
                })
                .ToArray();
            _projectsCacheTime = EditorApplication.timeSinceStartup;
            return _gcpProjectsCache;
        }

        public static string[] GetGhRepos()
        {
            if (_ghReposCache != null && EditorApplication.timeSinceStartup - _reposCacheTime < 60)
                return _ghReposCache;

            var path = FindGh();
            if (path == null) return new string[0];

            var (code, output) = Run(path, "repo list --json name --jq \".[].name\"");
            if (code != 0) return new string[0];

            _ghReposCache = output.Split('\n')
                .Where(line => !string.IsNullOrEmpty(line.Trim()))
                .Select(line => line.Trim())
                .ToArray();
            _reposCacheTime = EditorApplication.timeSinceStartup;
            return _ghReposCache;
        }

        /// <summary>GitHub 레포 존재 여부 확인.</summary>
        public static bool RepoExists(string ghRepo)
        {
            var path = FindGh();
            if (path == null) return false;
            var (code, _) = Run(path, $"repo view {ghRepo} --json name");
            return code == 0;
        }

        /// <summary>GitHub private 레포 생성. 이미 있으면 alreadyExisted=true.</summary>
        public static (bool success, bool alreadyExisted, string error) EnsureRepoExists(string ghRepo)
        {
            if (RepoExists(ghRepo)) return (true, true, null);

            var path = FindGh();
            if (path == null) return (false, false, "gh CLI를 찾을 수 없습니다.");

            var (code, output) = Run(path, $"repo create {ghRepo} --private --confirm");
            if (code != 0)
                return (false, false, $"레포 생성 실패: {output}");

            _ghReposCache = null;
            return (true, false, null);
        }

        // ── 통합 자동 설정 ──

        public static (bool success, string saEmail, string error) AutoSetupCloudRun(
            string projectId, string region, string serviceName, string ghRepo)
        {
            // 1. API 활성화
            var (apiOk, apiErr) = EnableCloudRunApi(projectId);
            if (!apiOk) return (false, null, apiErr);

            // 2. SA + 키 + Secret
            var (saOk, email, saErr) = CreateServiceAccountAndSetSecret(projectId, ghRepo);
            if (!saOk) return (false, null, saErr);

            // 3. Cloud Run 관련 Secret
            var ghPath = FindGh() ?? "gh";
            Run(ghPath, $"secret set CLOUD_RUN_SERVICE --repo {ghRepo} --body {serviceName?.ToLower()}");
            Run(ghPath, $"secret set CLOUD_RUN_REGION --repo {ghRepo} --body {region}");

            return (true, email, null);
        }

        // ── 실행 ──

        /// <summary>CLI 명령 실행. cmd에 전체 경로 또는 명령 이름.</summary>
        public static (int exitCode, string output) Run(string cmd, string args)
        {
            try
            {
                ProcessStartInfo psi;

                // 전체 경로(공백 포함 가능)면 직접 실행, 아니면 셸로 PATH 검색
                if (cmd.Contains(Path.DirectorySeparatorChar.ToString()) || cmd.Contains("/"))
                {
                    // 전체 경로 → 직접 실행 (공백 문제 없음)
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
                else if (IsWindows)
                {
                    // Windows: cmd.exe /c로 PATH에서 찾기
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {cmd} {args}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                }
                else
                {
                    // macOS/Linux: /bin/sh -lc로 로그인 셸 PATH 사용
                    var combined = $"{cmd} {args}".Replace("\\", "\\\\").Replace("\"", "\\\"");
                    psi = new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-lc \"{combined}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                }

                using var p = Process.Start(psi);
                if (p == null) return (-1, "");

                // 비동기 읽기로 데드락 방지 (stdout/stderr 버퍼 풀 차면 프로세스가 멈추는 문제)
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit(30000);

                var stdout = stdoutTask.Result;
                var stderr = stderrTask.Result;

                return (p.ExitCode, string.IsNullOrEmpty(stdout) ? stderr : stdout);
            }
            catch
            {
                return (-1, "");
            }
        }

        /// <summary>Run과 동일하지만 경로 탐색용 (where 등)</summary>
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
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                p.WaitForExit(5000);
                return (p.ExitCode, stdoutTask.Result);
            }
            catch
            {
                return (-1, "");
            }
        }
    }
}
