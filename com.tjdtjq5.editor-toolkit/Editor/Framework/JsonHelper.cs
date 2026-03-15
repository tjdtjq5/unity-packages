#if UNITY_EDITOR
namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>
    /// 경량 JSON 파서 유틸. 라이브러리 의존 없이 문자열 기반 JSON 필드 추출.
    /// 문자열 내 괄호/이스케이프 안전 처리.
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>문자열 필드 추출. "field": "value" → value</summary>
        public static string GetString(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, System.StringComparison.Ordinal);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return "";
            int s = ci + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            if (s >= json.Length) return "";
            if (json[s] == '"')
            {
                int qe = json.IndexOf('"', s + 1);
                return qe > s ? json.Substring(s + 1, qe - s - 1) : "";
            }
            int e = s;
            while (e < json.Length && json[e] != ',' && json[e] != '}' && json[e] != ']') e++;
            return json.Substring(s, e - s).Trim();
        }

        /// <summary>정수 필드 추출</summary>
        public static int GetInt(string json, string field)
        {
            string val = GetString(json, field);
            return int.TryParse(val, out int v) ? v : 0;
        }

        /// <summary>실수 필드 추출</summary>
        public static float GetFloat(string json, string field)
        {
            string val = GetString(json, field);
            return float.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0;
        }

        /// <summary>double 필드 추출</summary>
        public static double GetDouble(string json, string field)
        {
            string val = GetString(json, field);
            return double.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        /// <summary>중첩 객체 블록 추출. "field": {...} → {...}</summary>
        public static string GetObject(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, System.StringComparison.Ordinal);
            if (ki < 0) return "";
            int bs = json.IndexOf('{', ki + key.Length);
            if (bs < 0) return "";
            int be = FindBrace(json, bs);
            return json.Substring(bs, be - bs + 1);
        }

        /// <summary>배열 블록 추출. "field": [...] → [...]</summary>
        public static string GetArray(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, System.StringComparison.Ordinal);
            if (ki < 0) return "";
            int as_ = json.IndexOf('[', ki);
            if (as_ < 0) return "";
            int ae = FindBracket(json, as_);
            return json.Substring(as_, ae - as_ + 1);
        }

        /// <summary>값 추출 (타입 자동 감지: 문자열/숫자/bool/객체/배열)</summary>
        public static string GetValue(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, System.StringComparison.Ordinal);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return "";
            int s = ci + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            if (s >= json.Length) return "";

            if (json[s] == '{') { int e = FindBrace(json, s); return json.Substring(s, e - s + 1); }
            if (json[s] == '[') { int e = FindBracket(json, s); return json.Substring(s, e - s + 1); }
            if (json[s] == '"')
            {
                int qe = json.IndexOf('"', s + 1);
                return qe > s ? json.Substring(s + 1, qe - s - 1) : "";
            }
            int end = s;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
            return json.Substring(s, end - s).Trim();
        }

        /// <summary>중괄호 매칭. 문자열 내 괄호 무시 + 이스케이프 처리.</summary>
        public static int FindBrace(string s, int open)
        {
            int depth = 1;
            bool inStr = false;
            for (int i = open + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return s.Length - 1;
        }

        /// <summary>대괄호 매칭. 문자열 내 괄호 무시 + 이스케이프 처리.</summary>
        public static int FindBracket(string s, int open)
        {
            int depth = 1;
            bool inStr = false;
            for (int i = open + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '[') depth++;
                else if (c == ']') { depth--; if (depth == 0) return i; }
            }
            return s.Length - 1;
        }
    }
}
#endif
