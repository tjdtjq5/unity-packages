#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Settings
{
    /// <summary>
    /// Layer 3 — 팀 공유 프로젝트 설정 (ScriptableObject, git 추적).
    /// Codemagic 앱 메타 / 팀 알림 수신자 / keystore alias 등.
    /// 시크릿(토큰/비번)은 SecretStore가 담당.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CodemagicProjectSettings",
        menuName = "Codemagic/Project Settings")]
    public sealed class CodemagicProjectSettings : ScriptableObject
    {
        // ── Codemagic 앱 메타 (프로젝트 = 1 앱) ──

        [Header("Codemagic 앱")]
        public string CodemagicAppId;
        public string CodemagicAppName;
        public string CodemagicAppRepoUrl;

        // ── Keystore 메타 (비번은 EditorPrefs/SecretStore) ──

        [Header("Keystore")]
        public string KeyAlias;

        // ── 팀 알림 (git 추적되어 모든 멤버가 동일 수신자 사용) ──

        [Header("알림")]
        public List<string> NotificationRecipients = new();
        public bool NotifyOnSuccess = true;
        public bool NotifyOnFailure = false;

        // ── 팀 메모 / 컨벤션 ──

        [Header("메모")]
        [TextArea(3, 10)]
        public string Notes;

        // ── 싱글톤 ──

        const string AssetPath = "Assets/Editor/CodemagicProjectSettings.asset";

        static CodemagicProjectSettings _instance;

        public static CodemagicProjectSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = AssetDatabase.LoadAssetAtPath<CodemagicProjectSettings>(AssetPath);
                if (_instance == null)
                {
                    _instance = CreateInstance<CodemagicProjectSettings>();
                    var dir = Path.GetDirectoryName(AssetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
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
