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

        static string _cachedRoot;

        static void OnProjectWindowItem(string guid, Rect rect)
        {
            // 아이콘 모드/큰 행에서는 스킵 (가장 빈번한 조기 탈출)
            if (rect.height > 20f) return;

            var rules = AddrXSetupRules.Instance;
            if (rules == null) return;

            _cachedRoot ??= rules.RootPath + "/";

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;
            if (!path.StartsWith(_cachedRoot)) return;
            if (!AssetDatabase.IsValidFolder(path)) return;

            var relative = path.Substring(_cachedRoot.Length);
            if (relative.Contains("/")) return;

            Color color = rules.IsGroupRemote(relative) ? COL_REMOTE : COL_LOCAL;

            float bx = rect.xMax - 14f;
            float by = rect.y + (rect.height - 8f) * 0.5f;
            EditorGUI.DrawRect(new Rect(bx, by, 8f, 8f), color);

            // 모서리 픽셀을 배경색으로 덮어 원형 느낌
            var bg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f)
                : new Color(0.76f, 0.76f, 0.76f);
            EditorGUI.DrawRect(new Rect(bx, by, 1f, 1f), bg);
            EditorGUI.DrawRect(new Rect(bx + 7f, by, 1f, 1f), bg);
            EditorGUI.DrawRect(new Rect(bx, by + 7f, 1f, 1f), bg);
            EditorGUI.DrawRect(new Rect(bx + 7f, by + 7f, 1f, 1f), bg);
        }
    }
}
#endif
