using System;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunSettings : ScriptableObject
    {
        public const string VERSION = "0.2.0";
        const string AssetPath = "Assets/Editor/SupaRunSettings.asset";
        const string PREF = "SupaRun_";

        [Header("Supabase")]
        public string supabaseUrl;

        [Header("Google Cloud")]
        public string gcpProjectId;
        public string gcpRegion = "asia-northeast3";
        public string gcpServiceName;
        public int gcpMinInstances;

        [Header("GitHub")]
        public string githubRepoName;

        [Header("GCP 상태")]
        public bool gcpCloudRunApiEnabled;
        public string gcpServiceAccountEmail;

        [Header("Auth")]
        public System.Collections.Generic.List<string> enabledAuthProviders = new() { "Guest" };

        [Header("스케일링")]
        public int supabaseMaxConnections = 60;
        public int gcpMaxInstances = 3;
        public int dbPoolSize = 20;

        [Header("배포 캐시")]
        public System.Collections.Generic.List<string> enabledServerCaches = new() { "nuget", "docker" };

        [Header("서버 로그")]
        public bool serverLogToConsole = true;

        [Header("상태")]
        public bool setupCompleted;
        /// <summary>Cloud Run 서버 URL. 배포 후 자동 설정. 비어있으면 개발 모드 (LocalGameDB).</summary>
        public string cloudRunUrl;

        // ── 민감 정보 (EditorPrefs, git에 안 올라감) ──

        public static string SupabaseAnonKey
        {
            get => EditorPrefs.GetString(PREF + "SupabaseAnonKey", "");
            set => EditorPrefs.SetString(PREF + "SupabaseAnonKey", value);
        }

        public static string SupabaseDbPassword
        {
            get => EditorPrefs.GetString(PREF + "SupabaseDbPassword", "");
            set => EditorPrefs.SetString(PREF + "SupabaseDbPassword", value);
        }

        public static string GithubToken
        {
            get => EditorPrefs.GetString(PREF + "GithubToken", "");
            set => EditorPrefs.SetString(PREF + "GithubToken", value);
        }

        /// <summary>Supabase Management API용 Access Token. supabase.com/dashboard/account/tokens에서 발급.</summary>
        public static string SupabaseAccessToken
        {
            get => EditorPrefs.GetString(PREF + "SupabaseAccessToken", "");
            set => EditorPrefs.SetString(PREF + "SupabaseAccessToken", value);
        }

        public static string CronSecret
        {
            get => EditorPrefs.GetString(PREF + "CronSecret", "");
            set => EditorPrefs.SetString(PREF + "CronSecret", value);
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

        // ── 싱글톤 ──

        static SupaRunSettings _instance;

        public static SupaRunSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = AssetDatabase.LoadAssetAtPath<SupaRunSettings>(AssetPath);
                    if (_instance == null)
                    {
                        _instance = CreateInstance<SupaRunSettings>();
                        var dir = System.IO.Path.GetDirectoryName(AssetPath);
                        if (!System.IO.Directory.Exists(dir))
                            System.IO.Directory.CreateDirectory(dir!);
                        AssetDatabase.CreateAsset(_instance, AssetPath);
                        AssetDatabase.SaveAssets();
                    }
                }
                return _instance;
            }
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
