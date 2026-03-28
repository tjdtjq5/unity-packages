#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>폴더 + Addressables 그룹 생성 유틸. Label Category 라벨도 등록.</summary>
    public static class FolderTemplateGenerator
    {
        /// <summary>전체 규칙 기반 일괄 생성.</summary>
        static readonly string[] DefaultFolders =
        {
            "Common", "Title", "Lobby", "Chapter1", "Chapter2", "Chapter3",
            "Audio_BGM", "Audio_SFX", "Font"
        };

        public static bool Generate()
        {
            try
            {
                var rules = AddrXSetupRules.GetOrCreate();
                var settings = EnsureAddressablesSettings();
                EnsureFolder(rules.RootPath);

                // 기본 폴더 생성
                foreach (var folderName in DefaultFolders)
                    EnsureGroup(rules, folderName, rules.IsGroupRemote(folderName));

                // Label Category의 모든 옵션을 Addressables 라벨로 등록
                if (settings != null)
                {
                    foreach (var cat in rules.LabelCategories)
                        foreach (var option in cat.options)
                            settings.AddLabel(option);
                }

                AddrXLog.Info("Setup", $"템플릿 생성 완료: {DefaultFolders.Length}개 그룹");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[AddrX][Setup] Generate 실패: {ex}");
                return false;
            }
        }

        /// <summary>그룹 폴더 + Addressables 그룹 생성 (로컬/원격 스키마 적용).</summary>
        public static void EnsureGroup(AddrXSetupRules rules, string groupName, bool isRemote)
        {
            EnsureFolder(rules.RootPath);
            EnsureFolder($"{rules.RootPath}/{groupName}");

            var settings = EnsureAddressablesSettings();
            if (settings == null) return;

            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                group = settings.CreateGroup(groupName, false, false, true,
                    null, typeof(BundledAssetGroupSchema));
            }

            AddrXAutoRegister.ApplyGroupSchema(group, isRemote);
        }

        static AddressableAssetSettings EnsureAddressablesSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null) return settings;

            return AddressableAssetSettings.Create(
                AddressableAssetSettingsDefaultObject.kDefaultConfigFolder,
                AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName,
                true, true);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var folderName = Path.GetFileName(path);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif
