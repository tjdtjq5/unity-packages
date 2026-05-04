#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace Tjdtjq5.Codemagic.Editor.Util
{
    /// <summary>Android Keystore 유틸리티 — 파일 ↔ base64 변환 + 클립보드 복사.</summary>
    public static class KeystoreHelper
    {
        /// <summary>Keystore 파일을 base64 문자열로 변환. 파일 없거나 경로 비어있으면 null.</summary>
        public static string ToBase64(string keystorePath)
        {
            if (string.IsNullOrEmpty(keystorePath) || !File.Exists(keystorePath))
                return null;

            byte[] bytes = File.ReadAllBytes(keystorePath);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Keystore 파일의 base64 문자열을 시스템 클립보드에 복사.</summary>
        public static bool CopyBase64ToClipboard(string keystorePath)
        {
            var base64 = ToBase64(keystorePath);
            if (base64 == null) return false;

            EditorGUIUtility.systemCopyBuffer = base64;
            return true;
        }
    }
}
#endif
