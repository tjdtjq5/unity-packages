#if UNITY_EDITOR
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Tjdtjq5.Codemagic.Editor.License
{
    /// <summary>.ulf 내용에서 Unity SERIAL 추출. DeveloperData base64 → 4바이트 스킵 → ASCII.</summary>
    /// <remarks>
    /// codemagic.yaml의 `base64 -d | tail -c +5` 처리와 동일한 알고리즘.
    /// 추출된 SERIAL은 시크릿 — 호출자가 마스킹해서 로깅해야 함.
    /// </remarks>
    public static class UlfSerialExtractor
    {
        static readonly Regex DeveloperDataRegex =
            new Regex("<DeveloperData\\s+Value=\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>.ulf 내용에서 SERIAL 추출. 실패 시 null 반환 (예외 없음).</summary>
        public static string Extract(string ulfContent)
        {
            if (string.IsNullOrEmpty(ulfContent)) return null;

            // 1. base64 문자열 추출
            var match = DeveloperDataRegex.Match(ulfContent);
            if (!match.Success) return null;
            var base64 = match.Groups[1].Value;
            if (string.IsNullOrEmpty(base64)) return null;

            // 2. base64 디코드
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(base64);
            }
            catch
            {
                return null;
            }

            // 3. 첫 4바이트 스킵
            if (decoded.Length < 4) return null;
            int offset = 4;
            int length = decoded.Length - offset;

            // 4. ASCII로 변환
            var serial = Encoding.ASCII.GetString(decoded, offset, length);

            // 5. 끝의 null byte trim
            serial = serial.TrimEnd('\0');

            return string.IsNullOrEmpty(serial) ? null : serial;
        }
    }
}
#endif
