using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunSettings
    {
        public const string VERSION = "0.3.0";
        const string SettingsPath = "UserSettings/SupaRunSettings.json";
        const string LegacyAssetPath = "Assets/Editor/SupaRunSettings.asset";

        // ── 전체 데이터 (UserSettings/SupaRunSettings.json) ──

        [Serializable]
        class Data
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

            // 서버 로그
            public bool serverLogToConsole = true;

            // 상태
            public bool setupCompleted;
            public string cloudRunUrl = "";
            public string cronSecret = "";
        }

        static Data _data;
        static SupaRunSettings _instance;

        static Data D
        {
            get
            {
                if (_data == null)
                {
                    MigrateIfNeeded();
                    _data = LoadData();
                }
                return _data;
            }
        }

        static Data LoadData()
        {
            if (File.Exists(SettingsPath))
            {
                try { return JsonUtility.FromJson<Data>(File.ReadAllText(SettingsPath)); }
                catch (Exception ex) { Debug.LogWarning($"[SupaRun] {SettingsPath} 파싱 실패 — 초기화합니다: {ex.Message}"); }
            }
            return new Data();
        }

        static void SaveData()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(D, true));
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

        public void Save() => SaveData();

        // ── Supabase ──

        public string supabaseUrl
        {
            get => D.supabaseUrl;
            set { D.supabaseUrl = value; }
        }

        public string SupabaseAnonKey
        {
            get => D.supabaseAnonKey;
            set { D.supabaseAnonKey = value; SaveData(); }
        }

        public string SupabaseDbPassword
        {
            get => D.supabaseDbPassword;
            set { D.supabaseDbPassword = value; SaveData(); }
        }

        public string SupabaseAccessToken
        {
            get => D.supabaseAccessToken;
            set { D.supabaseAccessToken = value; SaveData(); }
        }

        // ── Google Cloud ──

        public string gcpProjectId
        {
            get => D.gcpProjectId;
            set { D.gcpProjectId = value; }
        }

        public string gcpRegion
        {
            get => D.gcpRegion;
            set { D.gcpRegion = value; }
        }

        public string gcpServiceName
        {
            get => D.gcpServiceName;
            set { D.gcpServiceName = value; }
        }

        public int gcpMinInstances
        {
            get => D.gcpMinInstances;
            set { D.gcpMinInstances = value; }
        }

        public bool gcpCloudRunApiEnabled
        {
            get => D.gcpCloudRunApiEnabled;
            set { D.gcpCloudRunApiEnabled = value; }
        }

        public string gcpServiceAccountEmail
        {
            get => D.gcpServiceAccountEmail;
            set { D.gcpServiceAccountEmail = value; }
        }

        // ── GitHub ──

        public string githubRepoName
        {
            get => D.githubRepoName;
            set { D.githubRepoName = value; }
        }

        public string GithubToken
        {
            get => D.githubToken;
            set { D.githubToken = value; SaveData(); }
        }

        // ── Auth ──

        static System.Collections.Generic.List<string> _authProvidersCache;
        static string _authProvidersCacheKey;

        public System.Collections.Generic.List<string> enabledAuthProviders
        {
            get
            {
                var raw = D.enabledAuthProviders;
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
                D.enabledAuthProviders = string.Join(",", value);
                _authProvidersCacheKey = null;
            }
        }

        // ── 스케일링 ──

        public int supabaseMaxConnections
        {
            get => D.supabaseMaxConnections;
            set { D.supabaseMaxConnections = value; }
        }

        public int gcpMaxInstances
        {
            get => D.gcpMaxInstances;
            set { D.gcpMaxInstances = value; }
        }

        public int dbPoolSize
        {
            get => D.dbPoolSize;
            set { D.dbPoolSize = value; }
        }

        // ── 배포 캐시 ──

        static System.Collections.Generic.List<string> _serverCachesCache;
        static string _serverCachesCacheKey;

        public System.Collections.Generic.List<string> enabledServerCaches
        {
            get
            {
                var raw = D.enabledServerCaches;
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
                D.enabledServerCaches = string.Join(",", value);
                _serverCachesCacheKey = null;
            }
        }

        // ── 기타 ──

        public bool serverLogToConsole
        {
            get => D.serverLogToConsole;
            set { D.serverLogToConsole = value; }
        }

        public bool setupCompleted
        {
            get => D.setupCompleted;
            set { D.setupCompleted = value; }
        }

        public string cloudRunUrl
        {
            get => D.cloudRunUrl;
            set { D.cloudRunUrl = value; }
        }

        public string CronSecret
        {
            get => D.cronSecret;
            set { D.cronSecret = value; SaveData(); }
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

        /// <summary>레거시 .asset + EditorPrefs → JSON 일회성 마이그레이션.</summary>
        static void MigrateIfNeeded()
        {
            if (File.Exists(SettingsPath)) return;

            // 레거시 시크릿 JSON (이전 버전에서 생성된 것)
            const string LegacySecretsPath = "UserSettings/SupaRunSecrets.json";

            var data = new Data();
            var migrated = false;

            // 1. 레거시 .asset에서 YAML 텍스트 파싱
            var assetPath = File.Exists(LegacyAssetPath) ? LegacyAssetPath : null;
            if (assetPath == null)
            {
                const string oldAsset = "Assets/Editor/GameServerSettings.asset";
                if (File.Exists(oldAsset)) assetPath = oldAsset;
            }

            if (assetPath != null)
            {
                var yaml = File.ReadAllText(assetPath);
                data.supabaseUrl = ParseYaml(yaml, "supabaseUrl");
                data.gcpProjectId = ParseYaml(yaml, "gcpProjectId");
                data.gcpRegion = ParseYaml(yaml, "gcpRegion", "asia-northeast3");
                data.gcpServiceName = ParseYaml(yaml, "gcpServiceName");
                data.gcpMinInstances = int.TryParse(ParseYaml(yaml, "gcpMinInstances"), out var mi) ? mi : 0;
                data.githubRepoName = ParseYaml(yaml, "githubRepoName");
                data.gcpCloudRunApiEnabled = ParseYaml(yaml, "gcpCloudRunApiEnabled") == "1";
                data.gcpServiceAccountEmail = ParseYaml(yaml, "gcpServiceAccountEmail");
                data.setupCompleted = ParseYaml(yaml, "setupCompleted") == "1";
                data.cloudRunUrl = ParseYaml(yaml, "cloudRunUrl");
                data.serverLogToConsole = ParseYaml(yaml, "serverLogToConsole", "1") == "1";
                data.supabaseMaxConnections = int.TryParse(ParseYaml(yaml, "supabaseMaxConnections"), out var mc) ? mc : 60;
                data.gcpMaxInstances = int.TryParse(ParseYaml(yaml, "gcpMaxInstances"), out var mx) ? mx : 3;
                data.dbPoolSize = int.TryParse(ParseYaml(yaml, "dbPoolSize"), out var dp) ? dp : 20;
                migrated = true;
            }

            // 2. 레거시 시크릿 JSON에서 읽기
            if (File.Exists(LegacySecretsPath))
            {
                try
                {
                    var json = File.ReadAllText(LegacySecretsPath);
                    var secrets = JsonUtility.FromJson<Data>(json); // 필드명 호환
                    if (!string.IsNullOrEmpty(secrets.supabaseAnonKey)) data.supabaseAnonKey = secrets.supabaseAnonKey;
                    if (!string.IsNullOrEmpty(secrets.supabaseDbPassword)) data.supabaseDbPassword = secrets.supabaseDbPassword;
                    if (!string.IsNullOrEmpty(secrets.githubToken)) data.githubToken = secrets.githubToken;
                    if (!string.IsNullOrEmpty(secrets.supabaseAccessToken)) data.supabaseAccessToken = secrets.supabaseAccessToken;
                    if (!string.IsNullOrEmpty(secrets.cronSecret)) data.cronSecret = secrets.cronSecret;
                    migrated = true;
                }
                catch { /* 파싱 실패 무시 */ }
                File.Delete(LegacySecretsPath);
            }

            // 3. EditorPrefs에서 시크릿 읽기
            var projectPrefix = EditorPrefUtils.ProjectPrefix;
            var legacyPrefixes = new[] { projectPrefix, "SupaRun_", "GameServer_" };

            var secretMap = new (string key, Action<string> setter)[]
            {
                ("SupabaseAnonKey", v => data.supabaseAnonKey = v),
                ("SupabaseDbPassword", v => data.supabaseDbPassword = v),
                ("GithubToken", v => data.githubToken = v),
                ("SupabaseAccessToken", v => data.supabaseAccessToken = v),
                ("CronSecret", v => data.cronSecret = v),
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

            if (migrated)
            {
                _data = data;
                SaveData();
                Debug.Log($"[SupaRun] 설정 마이그레이션 완료 → {SettingsPath}");
            }
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
