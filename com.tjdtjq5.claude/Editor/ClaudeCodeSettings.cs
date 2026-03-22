using System;
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

        // ── Discord ──
        /// <summary>Discord에서 대화 기능 활성화 (Bot 연결)</summary>
        public static bool DiscordEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "DiscordEnabled", false);
            set => EditorPrefs.SetBool(Prefix + "DiscordEnabled", value);
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

        // ── Discord 표시용 (위자드에서 설정) ──
        public static string DiscordChannelName
        {
            get => EditorPrefs.GetString(Prefix + "DiscordChName", "");
            set => EditorPrefs.SetString(Prefix + "DiscordChName", value);
        }

        public static string DiscordServerName
        {
            get => EditorPrefs.GetString(Prefix + "DiscordServer", "");
            set => EditorPrefs.SetString(Prefix + "DiscordServer", value);
        }

        /// <summary>위자드 완료 여부</summary>
        public static bool DiscordSetupDone
        {
            get => EditorPrefs.GetBool(Prefix + "DiscordSetup", false);
            set => EditorPrefs.SetBool(Prefix + "DiscordSetup", value);
        }

        // ── Remote Control ──
        public static bool RemoteControlEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "RC", false);
            set => EditorPrefs.SetBool(Prefix + "RC", value);
        }

        /// <summary>첫 RC 설정 안내 완료 여부</summary>
        public static bool RemoteControlSetupDone
        {
            get => EditorPrefs.GetBool(Prefix + "RC_Setup", false);
            set => EditorPrefs.SetBool(Prefix + "RC_Setup", value);
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

        // ── 토큰 난독화 (Base64 + XOR) ──
        // EditorPrefs는 로컬 전용이므로 네트워크 노출 없음.
        // 평문 저장을 방지하는 수준의 난독화.
        static readonly byte[] ObfuscateKey = { 0xC1, 0xA0, 0xDE, 0x42, 0x7F, 0x3B, 0x91, 0x55 };

        static string EncryptToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            var bytes = Encoding.UTF8.GetBytes(token);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= ObfuscateKey[i % ObfuscateKey.Length];
            return Convert.ToBase64String(bytes);
        }

        static string DecryptToken(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] ^= ObfuscateKey[i % ObfuscateKey.Length];
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
    }
}
