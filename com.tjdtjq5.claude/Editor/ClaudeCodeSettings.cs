using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
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

        // ── Claude Code 글로벌 설정 (~/.claude/settings.json) ──

        static string SettingsJsonPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "settings.json");

        /// <summary>기본 모델. 빈 문자열이면 미지정(Claude 기본값 사용).</summary>
        public static string DefaultModel
        {
            get => ReadSettingsKey("model", "");
            set => WriteSettingsKey("model", string.IsNullOrEmpty(value) ? null : value);
        }

        /// <summary>기본 Effort 레벨. CLI 인자(--effort)로 전달되므로 EditorPrefs에 저장.</summary>
        public static string DefaultEffortLevel
        {
            get => EditorPrefs.GetString(Prefix + "Effort", "high");
            set => EditorPrefs.SetString(Prefix + "Effort", value);
        }

        static string ReadSettingsKey(string key, string fallback)
        {
            try
            {
                if (!File.Exists(SettingsJsonPath)) return fallback;
                var json = JObject.Parse(File.ReadAllText(SettingsJsonPath));
                var token = json[key];
                return token?.Type == JTokenType.String ? token.Value<string>() : fallback;
            }
            catch { return fallback; }
        }

        static void WriteSettingsKey(string key, string value)
        {
            try
            {
                var path = SettingsJsonPath;
                var dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                JObject json;
                if (File.Exists(path))
                    json = JObject.Parse(File.ReadAllText(path));
                else
                    json = new JObject();

                if (value == null)
                    json.Remove(key);
                else
                    json[key] = value;

                File.WriteAllText(path, json.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Claude Code] settings.json 쓰기 실패: {ex.Message}");
            }
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
