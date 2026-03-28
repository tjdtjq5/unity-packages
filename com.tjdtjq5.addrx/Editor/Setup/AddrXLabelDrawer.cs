#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>모든 에셋 Inspector에 AddrX Label Category 드롭다운을 표시.</summary>
    [InitializeOnLoad]
    public static class AddrXLabelDrawer
    {
        static AddrXLabelDrawer()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1) return;

            var path = AssetDatabase.GetAssetPath(editor.target);
            if (string.IsNullOrEmpty(path)) return;

            var rules = AddrXSetupRules.Instance;
            if (rules == null || rules.LabelCategories.Count == 0) return;

            if (!path.StartsWith(rules.RootPath + "/")) return;
            if (AssetDatabase.IsValidFolder(path)) return;

            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("AddrX Labels", EditorStyles.boldLabel);

            bool changed = false;

            foreach (var cat in rules.LabelCategories)
            {
                var currentValue = rules.GetLabelForCategory(guid, cat.categoryName);
                int currentIdx = cat.options.IndexOf(currentValue);
                if (currentIdx < 0) currentIdx = cat.options.IndexOf(cat.defaultValue);
                if (currentIdx < 0) currentIdx = 0;

                bool isOverridden = currentValue != cat.defaultValue;

                var style = isOverridden
                    ? new GUIStyle(EditorStyles.popup) { fontStyle = FontStyle.Bold }
                    : EditorStyles.popup;

                int newIdx = EditorGUILayout.Popup(cat.categoryName, currentIdx,
                    cat.options.ToArray(), style);

                if (newIdx != currentIdx)
                {
                    rules.SetLabelOverride(guid, cat.categoryName, cat.options[newIdx]);
                    changed = true;
                }
            }

            if (changed)
                ApplyLabelsToAddressables(guid, rules);
        }

        static void ApplyLabelsToAddressables(string guid, AddrXSetupRules rules)
        {
            var settings = UnityEditor.AddressableAssets
                .AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var entry = settings.FindAssetEntry(guid);
            if (entry == null) return;

            foreach (var cat in rules.LabelCategories)
                foreach (var option in cat.options)
                    entry.SetLabel(option, false);

            var labels = rules.GetLabelsForAsset(guid);
            foreach (var label in labels)
            {
                settings.AddLabel(label);
                entry.SetLabel(label, true);
            }

            settings.SetDirty(
                UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                    .ModificationEvent.EntryModified, entry, true);
        }
    }
}
#endif
