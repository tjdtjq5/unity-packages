using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunSettings
    {
        public const string VERSION = "0.4.0";

        // 신규 분리 경로 — 공유 데이터는 ProjectSettings/, 개인 환경은 UserSettings/
        const string ProjectSettingsPath = "ProjectSettings/SupaRunProjectSettings.json";
        const string UserSettingsPath = "UserSettings/SupaRunUserSettings.json";

        // 레거시 경로 — 마이그레이션 입력 (단일 JSON / .asset / 시크릿 JSON)
        const string LegacyUnifiedJsonPath = "UserSettings/SupaRunSettings.json";
        const string LegacyAssetPath = "Assets/Editor/SupaRunSettings.asset";
        const string LegacySecretsJsonPath = "UserSettings/SupaRunSecrets.json";

        // ── 데이터 클래스 (분리) ──

        [Serializable]
        class ProjectData
        {
            // Supabase
            public string supabaseUrl = "";
            public string supabaseAnonKey = "";
            public string supabaseDbPassword = "";
            public string supabaseAccessToken = "";

            // Google Cloud
            public string gcpProjectId = "";
            public string gcpRegion = "asia-northeast3";
            public string gcpServiceName = "";
            public int gcpMinInstances;
            public bool gcpCloudRunApiEnabled;
            public string gcpServiceAccountEmail = "";

            // GitHub
            public string githubRepoName = "";
            public string githubToken = "";

            // Auth
            public string enabledAuthProviders = "Guest";

            // 스케일링
            public int supabaseMaxConnections = 60;
            public int gcpMaxInstances = 3;
            public int dbPoolSize = 20;

            // 배포 캐시
            public string enabledServerCaches = "nuget,docker";

            // 상태 (배포 결과)
            public string cloudRunUrl = "";
            public string cronSecret = "";
        }

        [Serializable]
        class UserData
        {
            public bool serverLogToConsole = true;
            public bool setupCompleted;
        }

        // ── 레거시 통합 데이터 (마이그레이션 입력) ──

        [Serializable]
        class LegacyData
        {
            public string supabaseUrl = "";
            public string supabaseAnonKey = "";
            public string supabaseDbPassword = "";
            public string supabaseAccessToken = "";
            public string gcpProjectId = "";
            public string gcpRegion = "asia-northeast3";
            public string gcpServiceName = "";
            public int gcpMinInstances;
            public bool gcpCloudRunApiEnabled;
            public string gcpServiceAccountEmail = "";
            public string githubRepoName = "";
            public string githubToken = "";
            public string enabledAuthProviders = "Guest";
            public int supabaseMaxConnections = 60;
            public int gcpMaxInstances = 3;
            public int dbPoolSize = 20;
            public string enabledServerCaches = "nuget,docker";
            public bool serverLogToConsole = true;
            public bool setupCompleted;
            public string cloudRunUrl = "";
            public string cronSecret = "";
        }

        // ── 메모리 캐시 + 로드 ──

        static ProjectData _project;
        static UserData _user;
        static SupaRunSettings _instance;

        static ProjectData P
        {
            get
            {
                if (_project == null)
                {
                    MigrateIfNeeded();
                    _project ??= LoadProject();
                }
                return _project;
            }
        }

        static UserData U
        {
            get
            {
                if (_user == null)
                {
                    MigrateIfNeeded();
                    _user ??= LoadUser();
                }
                return _user;
            }
        }

        static ProjectData LoadProject()
        {
            if (File.Exists(ProjectSettingsPath))
            {
                try { return JsonUtility.FromJson<ProjectData>(File.ReadAllText(ProjectSettingsPath)); }
                catch (Exception ex) { Debug.LogWarning($"[SupaRun] {ProjectSettingsPath} 파싱 실패 — 초기화합니다: {ex.Message}"); }
            }
            return new ProjectData();
        }

        static UserData LoadUser()
        {
            if (File.Exists(UserSettingsPath))
            {
                try { return JsonUtility.FromJson<UserData>(File.ReadAllText(UserSettingsPath)); }
                catch (Exception ex) { Debug.LogWarning($"[SupaRun] {UserSettingsPath} 파싱 실패 — 초기화합니다: {ex.Message}"); }
            }
            return new UserData();
        }

        static void SaveProject()
        {
            EnsureDirFor(ProjectSettingsPath);
            File.WriteAllText(ProjectSettingsPath, JsonUtility.ToJson(P, true));
        }

        static void SaveUser()
        {
            EnsureDirFor(UserSettingsPath);
            File.WriteAllText(UserSettingsPath, JsonUtility.ToJson(U, true));
        }

        static void EnsureDirFor(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ── 싱글톤 ──

        public static SupaRunSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new SupaRunSettings();
                return _instance;
            }
        }

        public void Save()
        {
            SaveProject();
            SaveUser();
        }

        // ── Supabase ──

        public string supabaseUrl
        {
            get => P.supabaseUrl;
            set { P.supabaseUrl = value; }
        }

        public string SupabaseAnonKey
        {
            get => P.supabaseAnonKey;
            set { P.supabaseAnonKey = value; SaveProject(); }
        }

        public string SupabaseDbPassword
        {
            get => P.supabaseDbPassword;
            set { P.supabaseDbPassword = value; SaveProject(); }
        }

        public string SupabaseAccessToken
        {
            get => P.supabaseAccessToken;
            set { P.supabaseAccessToken = value; SaveProject(); }
        }

        // ── Google Cloud ──

        public string gcpProjectId
        {
            get => P.gcpProjectId;
            set { P.gcpProjectId = value; }
        }

        public string gcpRegion
        {
            get => P.gcpRegion;
            set { P.gcpRegion = value; }
        }

        public string gcpServiceName
        {
            get => P.gcpServiceName;
            set { P.gcpServiceName = value; }
        }

        public int gcpMinInstances
        {
            get => P.gcpMinInstances;
            set { P.gcpMinInstances = value; }
        }

        public bool gcpCloudRunApiEnabled
        {
            get => P.gcpCloudRunApiEnabled;
            set { P.gcpCloudRunApiEnabled = value; }
        }

        public string gcpServiceAccountEmail
        {
            get => P.gcpServiceAccountEmail;
            set { P.gcpServiceAccountEmail = value; }
        }

        // ── GitHub ──

        public string githubRepoName
        {
            get => P.githubRepoName;
            set { P.githubRepoName = value; }
        }

        public string GithubToken
        {
            get => P.githubToken;
            set { P.githubToken = value; SaveProject(); }
        }

        // ── Auth ──

        static System.Collections.Generic.List<string> _authProvidersCache;
        static string _authProvidersCacheKey;

        public System.Collections.Generic.List<string> enabledAuthProviders
        {
            get
            {
                var raw = P.enabledAuthProviders;
                if (_authProvidersCacheKey != raw)
                {
                    _authProvidersCacheKey = raw;
                    _authProvidersCache = string.IsNullOrEmpty(raw)
                        ? new System.Collections.Generic.List<string>()
                        : new System.Collections.Generic.List<string>(raw.Split(','));
                }
                return _authProvidersCache;
            }
            set
            {
                P.enabledAuthProviders = string.Join(",", value);
                _authProvidersCacheKey = null;
            }
        }

        // ── 스케일링 ──

        public int supabaseMaxConnections
        {
            get => P.supabaseMaxConnections;
            set { P.supabaseMaxConnections = value; }
        }

        public int gcpMaxInstances
        {
            get => P.gcpMaxInstances;
            set { P.gcpMaxInstances = value; }
        }

        public int dbPoolSize
        {
            get => P.dbPoolSize;
            set { P.dbPoolSize = value; }
        }

        // ── 배포 캐시 ──

        static System.Collections.Generic.List<string> _serverCachesCache;
        static string _serverCachesCacheKey;

        public System.Collections.Generic.List<string> enabledServerCaches
        {
            get
            {
                var raw = P.enabledServerCaches;
                if (_serverCachesCacheKey != raw)
                {
                    _serverCachesCacheKey = raw;
                    _serverCachesCache = string.IsNullOrEmpty(raw)
                        ? new System.Collections.Generic.List<string>()
                        : new System.Collections.Generic.List<string>(raw.Split(','));
                }
                return _serverCachesCache;
            }
            set
            {
                P.enabledServerCaches = string.Join(",", value);
                _serverCachesCacheKey = null;
            }
        }

        // ── 기타 ──

        public bool serverLogToConsole
        {
            get => U.serverLogToConsole;
            set { U.serverLogToConsole = value; }
        }

        public bool setupCompleted
        {
            get => U.setupCompleted;
            set { U.setupCompleted = value; }
        }

        public string cloudRunUrl
        {
            get => P.cloudRunUrl;
            set { P.cloudRunUrl = value; }
        }

        public string CronSecret
        {
            get => P.cronSecret;
            set { P.cronSecret = value; SaveProject(); }
        }

        // ── 설정 완료 판단 ──

        public bool IsSupabaseConfigured =>
            !string.IsNullOrEmpty(supabaseUrl) &&
            !string.IsNullOrEmpty(SupabaseAnonKey) &&
            !string.IsNullOrEmpty(SupabaseDbPassword);

        public bool IsGcpConfigured =>
            !string.IsNullOrEmpty(gcpProjectId);

        public bool IsGitHubConfigured =>
            !string.IsNullOrEmpty(GithubToken) &&
            !string.IsNullOrEmpty(githubRepoName);

        public bool IsDeployConfigured =>
            IsGcpConfigured && IsGitHubConfigured;

        public bool HasCache(string cacheId) => enabledServerCaches.Contains(cacheId);

        // ── Supabase 프로젝트 ID 추출 ──

        public string SupabaseProjectId
        {
            get
            {
                if (string.IsNullOrEmpty(supabaseUrl)) return "";
                try
                {
                    var uri = new Uri(supabaseUrl);
                    return uri.Host.Split('.')[0];
                }
                catch { return ""; }
            }
        }

        public string SupabaseApiSettingsUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}/settings/api-keys";
        public string SupabaseDatabaseSettingsUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}/settings/database";
        public string SupabaseDashboardUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}";

        // ── 마이그레이션 ──

        /// <summary>
        /// 마이그레이션 진입점. 멱등 — ProjectSettings/SupaRunProjectSettings.json이 있으면 스킵.
        ///
        /// 흐름:
        /// 1. 새 분리 파일이 이미 있으면 종료 (마이그레이션 완료 상태)
        /// 2. 레거시 단일 JSON(UserSettings/SupaRunSettings.json) 발견 → 2개 파일로 분배 + .bak 백업
        /// 3. 단일 JSON도 없으면 → 더 오래된 .asset/시크릿/EditorPrefs 마이그레이션 시도
        /// </summary>
        static void MigrateIfNeeded()
        {
            if (File.Exists(ProjectSettingsPath)) return;

            var projectData = new ProjectData();
            var userData = new UserData();
            var migrated = false;

            // Step A: 레거시 단일 JSON (v0.3 형식) → 분리
            if (File.Exists(LegacyUnifiedJsonPath))
            {
                try
                {
                    var legacy = JsonUtility.FromJson<LegacyData>(File.ReadAllText(LegacyUnifiedJsonPath));
                    if (legacy != null)
                    {
                        ApplyLegacyToSplit(legacy, projectData, userData);
                        migrated = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] {LegacyUnifiedJsonPath} 파싱 실패 — 빈 설정으로 시작: {ex.Message}");
                }

                // 레거시 파일 .bak 백업 (실패해도 무시)
                try
                {
                    var bakPath = LegacyUnifiedJsonPath + ".bak";
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Move(LegacyUnifiedJsonPath, bakPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] 레거시 파일 백업 실패: {ex.Message}");
                }
            }
            else
            {
                // Step B: v0.2 이전 마이그레이션 (.asset / EditorPrefs / SupaRunSecrets.json)
                migrated = MigrateFromV02(projectData, userData) || migrated;
            }

            if (migrated)
            {
                _project = projectData;
                _user = userData;
                SaveProject();
                SaveUser();
                Debug.Log($"[SupaRun] 설정 마이그레이션 완료 → {ProjectSettingsPath} + {UserSettingsPath}");
            }
        }

        /// <summary>레거시 통합 데이터 → 분리된 ProjectData/UserData로 복사.</summary>
        static void ApplyLegacyToSplit(LegacyData legacy, ProjectData project, UserData user)
        {
            // ProjectData
            project.supabaseUrl = legacy.supabaseUrl;
            project.supabaseAnonKey = legacy.supabaseAnonKey;
            project.supabaseDbPassword = legacy.supabaseDbPassword;
            project.supabaseAccessToken = legacy.supabaseAccessToken;
            project.gcpProjectId = legacy.gcpProjectId;
            project.gcpRegion = string.IsNullOrEmpty(legacy.gcpRegion) ? "asia-northeast3" : legacy.gcpRegion;
            project.gcpServiceName = legacy.gcpServiceName;
            project.gcpMinInstances = legacy.gcpMinInstances;
            project.gcpCloudRunApiEnabled = legacy.gcpCloudRunApiEnabled;
            project.gcpServiceAccountEmail = legacy.gcpServiceAccountEmail;
            project.githubRepoName = legacy.githubRepoName;
            project.githubToken = legacy.githubToken;
            project.enabledAuthProviders = string.IsNullOrEmpty(legacy.enabledAuthProviders) ? "Guest" : legacy.enabledAuthProviders;
            project.supabaseMaxConnections = legacy.supabaseMaxConnections > 0 ? legacy.supabaseMaxConnections : 60;
            project.gcpMaxInstances = legacy.gcpMaxInstances > 0 ? legacy.gcpMaxInstances : 3;
            project.dbPoolSize = legacy.dbPoolSize > 0 ? legacy.dbPoolSize : 20;
            project.enabledServerCaches = string.IsNullOrEmpty(legacy.enabledServerCaches) ? "nuget,docker" : legacy.enabledServerCaches;
            project.cloudRunUrl = legacy.cloudRunUrl;
            project.cronSecret = legacy.cronSecret;

            // UserData
            user.serverLogToConsole = legacy.serverLogToConsole;
            user.setupCompleted = legacy.setupCompleted;
        }

        /// <summary>v0.2 이전 마이그레이션: .asset YAML + 시크릿 JSON + EditorPrefs.</summary>
        static bool MigrateFromV02(ProjectData project, UserData user)
        {
            var migrated = false;

            // 1. 레거시 .asset YAML 파싱
            var assetPath = File.Exists(LegacyAssetPath) ? LegacyAssetPath : null;
            if (assetPath == null)
            {
                const string oldAsset = "Assets/Editor/GameServerSettings.asset";
                if (File.Exists(oldAsset)) assetPath = oldAsset;
            }

            if (assetPath != null)
            {
                var yaml = File.ReadAllText(assetPath);
                project.supabaseUrl = ParseYaml(yaml, "supabaseUrl");
                project.gcpProjectId = ParseYaml(yaml, "gcpProjectId");
                project.gcpRegion = ParseYaml(yaml, "gcpRegion", "asia-northeast3");
                project.gcpServiceName = ParseYaml(yaml, "gcpServiceName");
                project.gcpMinInstances = int.TryParse(ParseYaml(yaml, "gcpMinInstances"), out var mi) ? mi : 0;
                project.githubRepoName = ParseYaml(yaml, "githubRepoName");
                project.gcpCloudRunApiEnabled = ParseYaml(yaml, "gcpCloudRunApiEnabled") == "1";
                project.gcpServiceAccountEmail = ParseYaml(yaml, "gcpServiceAccountEmail");
                project.cloudRunUrl = ParseYaml(yaml, "cloudRunUrl");
                project.supabaseMaxConnections = int.TryParse(ParseYaml(yaml, "supabaseMaxConnections"), out var mc) ? mc : 60;
                project.gcpMaxInstances = int.TryParse(ParseYaml(yaml, "gcpMaxInstances"), out var mx) ? mx : 3;
                project.dbPoolSize = int.TryParse(ParseYaml(yaml, "dbPoolSize"), out var dp) ? dp : 20;

                user.setupCompleted = ParseYaml(yaml, "setupCompleted") == "1";
                user.serverLogToConsole = ParseYaml(yaml, "serverLogToConsole", "1") == "1";

                migrated = true;
            }

            // 2. 레거시 시크릿 JSON
            if (File.Exists(LegacySecretsJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(LegacySecretsJsonPath);
                    var secrets = JsonUtility.FromJson<LegacyData>(json);
                    if (!string.IsNullOrEmpty(secrets.supabaseAnonKey)) project.supabaseAnonKey = secrets.supabaseAnonKey;
                    if (!string.IsNullOrEmpty(secrets.supabaseDbPassword)) project.supabaseDbPassword = secrets.supabaseDbPassword;
                    if (!string.IsNullOrEmpty(secrets.githubToken)) project.githubToken = secrets.githubToken;
                    if (!string.IsNullOrEmpty(secrets.supabaseAccessToken)) project.supabaseAccessToken = secrets.supabaseAccessToken;
                    if (!string.IsNullOrEmpty(secrets.cronSecret)) project.cronSecret = secrets.cronSecret;
                    migrated = true;
                }
                catch { /* 파싱 실패 무시 */ }
                File.Delete(LegacySecretsJsonPath);
            }

            // 3. EditorPrefs 시크릿
            var projectPrefix = EditorPrefUtils.ProjectPrefix;
            var legacyPrefixes = new[] { projectPrefix, "SupaRun_", "GameServer_" };

            var secretMap = new (string key, Action<string> setter)[]
            {
                ("SupabaseAnonKey", v => project.supabaseAnonKey = v),
                ("SupabaseDbPassword", v => project.supabaseDbPassword = v),
                ("GithubToken", v => project.githubToken = v),
                ("SupabaseAccessToken", v => project.supabaseAccessToken = v),
                ("CronSecret", v => project.cronSecret = v),
            };

            foreach (var (key, setter) in secretMap)
            {
                foreach (var prefix in legacyPrefixes)
                {
                    var val = EditorPrefs.GetString(prefix + key, "");
                    if (!string.IsNullOrEmpty(val))
                    {
                        setter(val);
                        EditorPrefs.DeleteKey(prefix + key);
                        migrated = true;
                        break;
                    }
                }
            }

            return migrated;
        }

        /// <summary>간단한 YAML "key: value" 파서.</summary>
        static string ParseYaml(string yaml, string key, string fallback = "")
        {
            var prefix = $"  {key}: ";
            foreach (var line in yaml.Split('\n'))
            {
                if (line.StartsWith(prefix))
                    return line.Substring(prefix.Length).Trim();
            }
            return fallback;
        }
    }
}
