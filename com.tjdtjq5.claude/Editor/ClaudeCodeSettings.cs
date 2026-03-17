using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Claude Code 런처 설정 (EditorPrefs 기반 영속)
    /// </summary>
    public static class ClaudeCodeSettings
    {
        const string Prefix = "ClaudeCode_";

        // ── 추가 인자 ──
        public static string AdditionalArgs
        {
            get => EditorPrefs.GetString(Prefix + "Args", "");
            set => EditorPrefs.SetString(Prefix + "Args", value);
        }

        // ── 탭 색상 ──
        public static Color MainTabColor
        {
            get => LoadColor("MainColor", new Color(0.42f, 0.36f, 0.91f)); // #6B5CE7
            set => SaveColor("MainColor", value);
        }

        public static Color WorktreeTabColor
        {
            get => LoadColor("WtColor", new Color(0.91f, 0.65f, 0.36f)); // #E7A55C
            set => SaveColor("WtColor", value);
        }

        // ── Windows Terminal 윈도우 이름 ──
        public static string WindowName
        {
            get => EditorPrefs.GetString(Prefix + "WinName", "Claude");
            set => EditorPrefs.SetString(Prefix + "WinName", value);
        }

        // ── 워크트리 자동 실행 ──
        public static bool AutoLaunch
        {
            get => EditorPrefs.GetBool(Prefix + "AutoLaunch", false);
            set => EditorPrefs.SetBool(Prefix + "AutoLaunch", value);
        }

        // ── 유틸 ──
        public static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }

        static Color LoadColor(string key, Color fallback)
        {
            var hex = EditorPrefs.GetString(Prefix + key, "");
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        static void SaveColor(string key, Color value)
        {
            EditorPrefs.SetString(Prefix + key, "#" + ColorUtility.ToHtmlStringRGB(value));
        }
    }
}
