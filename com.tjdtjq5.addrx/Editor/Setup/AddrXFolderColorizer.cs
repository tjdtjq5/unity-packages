#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>Assets/Addressables/ 하위 그룹 폴더에 로컬/원격 색상 아이콘을 표시.</summary>
    [InitializeOnLoad]
    public static class AddrXFolderColorizer
    {
        static readonly Color COL_LOCAL = new(0.4f, 0.7f, 0.95f, 0.8f);
        static readonly Color COL_REMOTE = new(0.95f, 0.6f, 0.3f, 0.8f);

        static AddrXFolderColorizer()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItem;
        }

        static void OnProjectWindowItem(string guid, Rect rect)
        {
            var rules = AddrXSetupRules.Instance;
            if (rules == null) return;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;
            if (!AssetDatabase.IsValidFolder(path)) return;

            var root = rules.RootPath + "/";
            if (!path.StartsWith(root)) return;

            var relative = path.Substring(root.Length);
            // 1뎁스 폴더만 (그룹 폴더)
            if (relative.Contains("/")) return;

            Color color = rules.IsGroupRemote(relative) ? COL_REMOTE : COL_LOCAL;

            // 폴더명 우측에 색상 원(●) 뱃지 표시
            if (rect.height > 20f) return; // 아이콘 모드에서는 스킵
            var badgeRect = new Rect(rect.xMax - 14f, rect.y + (rect.height - 8f) * 0.5f, 8f, 8f);
            EditorGUI.DrawRect(badgeRect, color);
            // 원형 느낌을 위해 모서리에 배경색 덮기
            var bg = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            EditorGUI.DrawRect(new Rect(badgeRect.x, badgeRect.y, 1f, 1f), bg);
            EditorGUI.DrawRect(new Rect(badgeRect.xMax - 1f, badgeRect.y, 1f, 1f), bg);
            EditorGUI.DrawRect(new Rect(badgeRect.x, badgeRect.yMax - 1f, 1f, 1f), bg);
            EditorGUI.DrawRect(new Rect(badgeRect.xMax - 1f, badgeRect.yMax - 1f, 1f, 1f), bg);
        }
    }
}
#endif
