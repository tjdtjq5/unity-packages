#if UNITY_EDITOR
using System.IO;

namespace Tjdtjq5.Codemagic.Editor.License
{
    /// <summary>.ulf 파일 읽기 + 형식 검증. 파일 내용은 절대 로깅하지 않음.</summary>
    public static class UlfReader
    {
        /// <summary>.ulf 파일 읽기 + 검증. 성공 시 (true, content, null), 실패 시 (false, null, error).</summary>
        /// <remarks>
        /// 검증: "&lt;License" 또는 "&lt;?xml" 포함 여부. 둘 다 없으면 잘못된 형식.
        /// 호출자는 content를 Debug.Log에 직접 찍지 않아야 함 — 길이만 표기 권장.
        /// </remarks>
        public static (bool valid, string content, string error) TryRead(string path)
        {
            if (string.IsNullOrEmpty(path))
                return (false, null, "경로가 비어있습니다.");

            if (!File.Exists(path))
                return (false, null, $"파일이 존재하지 않습니다: {path}");

            try
            {
                var content = File.ReadAllText(path);
                if (string.IsNullOrEmpty(content))
                    return (false, null, "파일이 비어있습니다.");

                if (!content.Contains("<License") && !content.Contains("<?xml"))
                    return (false, null, "올바른 .ulf 형식이 아닙니다 (License/XML 헤더 없음).");

                return (true, content, null);
            }
            catch (System.Exception ex)
            {
                return (false, null, ex.Message);
            }
        }
    }
}
#endif
