using System;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// Supabase Auth config JSON에서 provider 필드를 읽는 순수 파서.
    /// 이전엔 SettingsView의 IsProviderConfigured / GetProviderSupabaseStatus / nonce-skip 표시가
    /// 동일한 손수 IndexOf/Substring 파싱을 각각 중복 구현했다 — 단일 테스트 가능 모듈로 통합.
    /// (Supabase Management API 응답은 항상 유효 JSON이라 키-콜론 누락 같은 malformed 케이스는 Missing으로 본다.)
    /// </summary>
    public static class AuthConfigParser
    {
        public enum FieldState { Missing, Empty, Set }

        /// <summary>boolean 필드가 true인지. (예: "google_enabled": true)</summary>
        public static bool IsFieldTrue(string json, string fieldName)
        {
            var after = ValueAfterKey(json, fieldName, 10);
            return after != null && after.StartsWith("true");
        }

        /// <summary>string 필드 상태: 키 없음(Missing) / 빈값 ""·null(Empty) / 설정됨(Set).</summary>
        public static FieldState GetStringFieldState(string json, string fieldName)
        {
            var after = ValueAfterKey(json, fieldName, 20);
            if (after == null) return FieldState.Missing;
            if (after.StartsWith("\"\"") || after.StartsWith("null")) return FieldState.Empty;
            return FieldState.Set;
        }

        /// <summary>"fieldName": 뒤 값의 앞부분(maxLen)을 Trim해 반환. 키/콜론 없으면 null.</summary>
        static string ValueAfterKey(string json, string fieldName, int maxLen)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName)) return null;
            var quotedKey = $"\"{fieldName}\"";
            var idx = json.IndexOf(quotedKey, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx + quotedKey.Length);
            if (colon < 0) return null;
            return json.Substring(colon + 1, Math.Min(maxLen, json.Length - colon - 1)).Trim();
        }
    }
}
