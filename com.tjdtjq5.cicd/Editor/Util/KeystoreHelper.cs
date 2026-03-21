#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Android Keystore 유틸리티</summary>
    public static class KeystoreHelper
    {
        /// <summary>Keystore 파일을 base64 문자열로 변환</summary>
        public static string ToBase64(string keystorePath)
        {
            if (string.IsNullOrEmpty(keystorePath) || !File.Exists(keystorePath))
                return null;

            byte[] bytes = File.ReadAllBytes(keystorePath);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>base64 문자열을 클립보드에 복사</summary>
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
