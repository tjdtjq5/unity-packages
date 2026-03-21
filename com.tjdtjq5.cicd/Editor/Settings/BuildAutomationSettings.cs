#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    public enum AndroidBuildFormat { APK, AAB }
    public enum NotifyChannel { None, Discord, Slack, Custom }

    public class BuildAutomationSettings : ScriptableObject
    {
        const string AssetPath = "Assets/Editor/BuildAutomationSettings.asset";
        const string PREF = "BuildAuto_";

        // ── 플랫폼 ──

        [Header("플랫폼")]
        public bool enableAndroid = true;
        public bool enableIOS;
        public bool enableWindows;
        public bool enableWebGL;
        public AndroidBuildFormat androidBuildFormat = AndroidBuildFormat.AAB;
        public string keystorePath;
        public string keyAlias;
        public string iosTeamId;

        // ── 배포 대상 ──

        [Header("배포")]
        public bool deployGitHubReleases = true;
        public bool deployGooglePlay;
        public bool deployAppStore;
        public bool deploySteam;
        public string steamAppId;
        public string steamDepotId;

        // ── 알림 ──

        [Header("알림")]
        public NotifyChannel notifyChannel;
        public string webhookUrl;

        // ── 릴리스 ──

        [Header("릴리스")]
        public string releaseBranch = "main";

        // ── 상태 ──

        [Header("상태")]
        public bool setupCompleted;

        // ── 민감 정보 (EditorPrefs, Git 미추적) ──

        public static string KeystorePass
        {
            get => EditorPrefs.GetString(PREF + "KeystorePass", "");
            set => EditorPrefs.SetString(PREF + "KeystorePass", value);
        }

        public static string KeyPass
        {
            get => EditorPrefs.GetString(PREF + "KeyPass", "");
            set => EditorPrefs.SetString(PREF + "KeyPass", value);
        }

        public static string WebhookUrlSecret
        {
            get => EditorPrefs.GetString(PREF + "WebhookUrl", "");
            set => EditorPrefs.SetString(PREF + "WebhookUrl", value);
        }

        public static string UnityEmail
        {
            get => EditorPrefs.GetString(PREF + "UnityEmail", "");
            set => EditorPrefs.SetString(PREF + "UnityEmail", value);
        }

        public static string UnityPassword
        {
            get => EditorPrefs.GetString(PREF + "UnityPassword", "");
            set => EditorPrefs.SetString(PREF + "UnityPassword", value);
        }

        public static string UlfContent
        {
            get => EditorPrefs.GetString(PREF + "UlfContent", "");
            set => EditorPrefs.SetString(PREF + "UlfContent", value);
        }

        // ── 판단 헬퍼 ──

        public bool HasAnyPlatform =>
            enableAndroid || enableIOS || enableWindows || enableWebGL;

        public bool HasAnyDeploy =>
            deployGitHubReleases || deployGooglePlay || deployAppStore || deploySteam;

        public bool IsKeystoreConfigured =>
            !string.IsNullOrEmpty(keystorePath) &&
            System.IO.File.Exists(keystorePath) &&
            !string.IsNullOrEmpty(KeystorePass) &&
            !string.IsNullOrEmpty(keyAlias) &&
            !string.IsNullOrEmpty(KeyPass);

        // ── 싱글톤 ──

        static BuildAutomationSettings _instance;

        public static BuildAutomationSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = AssetDatabase.LoadAssetAtPath<BuildAutomationSettings>(AssetPath);
                if (_instance == null)
                {
                    _instance = CreateInstance<BuildAutomationSettings>();
                    var dir = System.IO.Path.GetDirectoryName(AssetPath);
                    if (!System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir!);
                    AssetDatabase.CreateAsset(_instance, AssetPath);
                    AssetDatabase.SaveAssets();
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
#endif
