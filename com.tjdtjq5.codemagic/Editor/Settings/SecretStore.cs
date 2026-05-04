#if UNITY_EDITOR
using UnityEditor;

namespace Tjdtjq5.Codemagic.Editor.Settings
{
    /// <summary>
    /// Layer 1 — 시크릿 영속화 (EditorPrefs, per-user, OS-level).
    /// 토큰 / 비밀번호 / .ulf content 등 git에 노출되면 안 되는 값.
    /// </summary>
    public static class SecretStore
    {
        // 모든 키는 "Codemagic." prefix 사용 (cicd 패키지와 충돌 방지).
        const string K_CodemagicToken    = "Codemagic.Token";
        const string K_UnityEmail        = "Codemagic.UnityEmail";
        const string K_UnityPassword     = "Codemagic.UnityPassword";
        const string K_UlfContent        = "Codemagic.UlfContent";
        const string K_KeystorePassword  = "Codemagic.KeystorePassword";
        const string K_KeyPassword       = "Codemagic.KeyPassword";

        // ── Step 2: Codemagic API 토큰 ──

        public static string CodemagicToken
        {
            get => EditorPrefs.GetString(K_CodemagicToken, "");
            set => EditorPrefs.SetString(K_CodemagicToken, value);
        }

        // ── Step 4: Unity 라이선스 / .ulf ──

        public static string UnityEmail
        {
            get => EditorPrefs.GetString(K_UnityEmail, "");
            set => EditorPrefs.SetString(K_UnityEmail, value);
        }

        public static string UnityPassword
        {
            get => EditorPrefs.GetString(K_UnityPassword, "");
            set => EditorPrefs.SetString(K_UnityPassword, value);
        }

        public static string UlfContent
        {
            get => EditorPrefs.GetString(K_UlfContent, "");
            set => EditorPrefs.SetString(K_UlfContent, value);
        }

        // ── Step 5: Keystore ──

        public static string KeystorePassword
        {
            get => EditorPrefs.GetString(K_KeystorePassword, "");
            set => EditorPrefs.SetString(K_KeystorePassword, value);
        }

        public static string KeyPassword
        {
            get => EditorPrefs.GetString(K_KeyPassword, "");
            set => EditorPrefs.SetString(K_KeyPassword, value);
        }

        /// <summary>
        /// 모든 시크릿 키 삭제. "처음부터 다시" 액션 또는 사용자 변경 시 호출.
        /// </summary>
        public static void ClearAll()
        {
            EditorPrefs.DeleteKey(K_CodemagicToken);
            EditorPrefs.DeleteKey(K_UnityEmail);
            EditorPrefs.DeleteKey(K_UnityPassword);
            EditorPrefs.DeleteKey(K_UlfContent);
            EditorPrefs.DeleteKey(K_KeystorePassword);
            EditorPrefs.DeleteKey(K_KeyPassword);
        }
    }
}
#endif
