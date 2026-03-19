using System;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class GameServerSettings : ScriptableObject
    {
        const string AssetPath = "Assets/Editor/GameServerSettings.asset";
        const string PREF = "GameServer_";

        [Header("Supabase")]
        public string supabaseUrl;

        [Header("Google Cloud")]
        public string gcpProjectId;
        public string gcpRegion = "asia-northeast3";
        public string gcpServiceName;
        public int gcpMinInstances;

        [Header("GitHub")]
        public string githubRepoName;

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
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}/settings/api";
        public string SupabaseDatabaseSettingsUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}/settings/database";
        public string SupabaseDashboardUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}";

        // ── 싱글톤 ──

        static GameServerSettings _instance;

        public static GameServerSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = AssetDatabase.LoadAssetAtPath<GameServerSettings>(AssetPath);
                    if (_instance == null)
                    {
                        _instance = CreateInstance<GameServerSettings>();
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
