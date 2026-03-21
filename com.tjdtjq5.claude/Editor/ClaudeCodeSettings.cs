using System;
using System.Security.Cryptography;
using System.Text;
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

        // ── 모니터 ──
        public static bool MonitorEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "MonitorEnabled", false);
            set => EditorPrefs.SetBool(Prefix + "MonitorEnabled", value);
        }

        /// <summary>0=Error만, 1=Warning+Error, 2=All</summary>
        public static int MonitorSeverity
        {
            get => EditorPrefs.GetInt(Prefix + "MonitorSev", 0);
            set => EditorPrefs.SetInt(Prefix + "MonitorSev", value);
        }

        public static int CooldownSeconds
        {
            get => EditorPrefs.GetInt(Prefix + "Cooldown", 30);
            set => EditorPrefs.SetInt(Prefix + "Cooldown", value);
        }

        // ── Discord ──
        /// <summary>0=Off, 1=Notify, 2=Interactive</summary>
        public static int DiscordMode
        {
            get => EditorPrefs.GetInt(Prefix + "DiscordMode", 0);
            set => EditorPrefs.SetInt(Prefix + "DiscordMode", value);
        }

        public static string DiscordBotToken
        {
            get => DecryptToken(EditorPrefs.GetString(Prefix + "DiscordToken", ""));
            set => EditorPrefs.SetString(Prefix + "DiscordToken", EncryptToken(value));
        }

        public static string DiscordChannelId
        {
            get => EditorPrefs.GetString(Prefix + "DiscordChId", "");
            set => EditorPrefs.SetString(Prefix + "DiscordChId", value);
        }

        public static string DiscordAllowedUsers
        {
            get => EditorPrefs.GetString(Prefix + "DiscordUsers", "");
            set => EditorPrefs.SetString(Prefix + "DiscordUsers", value);
        }

        // ── Remote Control ──
        public static bool RemoteControlEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "RC", false);
            set => EditorPrefs.SetBool(Prefix + "RC", value);
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

        // ── 토큰 암호화 (DPAPI) ──
        static string EncryptToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(token);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch { return ""; }
        }

        static string DecryptToken(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch { return ""; }
        }
    }
}
