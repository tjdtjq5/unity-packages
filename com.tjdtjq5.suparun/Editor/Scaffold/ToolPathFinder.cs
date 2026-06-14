using System;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// CLI 실행 파일 경로 탐색. PATH(where/command -v) 우선, 없으면 알려진 설치 경로(knownPaths) fallback.
    /// 이전엔 PrerequisiteChecker의 FindDotnet/FindGcloud/FindGh가 동일 알고리즘을 3중 중복(~160 LOC)했다 — 단일화.
    /// runProcess/fileExists를 주입받아 단위 테스트 가능(실제 Process/파일시스템 없이).
    /// </summary>
    public static class ToolPathFinder
    {
        /// <summary>toolName을 PATH → knownPaths 순으로 찾아 절대경로 반환. 못 찾으면 null.</summary>
        public static string Find(
            string toolName,
            string[] knownPaths,
            Func<string, string, (int code, string output)> runProcess,
            Func<string, bool> fileExists,
            bool isWindows)
        {
            // 1. PATH에서 찾기 (where / command -v)
            var (code, output) = isWindows
                ? runProcess("cmd.exe", $"/c where {toolName}")
                : runProcess("/bin/sh", $"-lc \"command -v {toolName}\"");
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var first = output.Split('\n', '\r')[0].Trim();
                if (!string.IsNullOrEmpty(first) && fileExists(first))
                    return first;
            }

            // 2. 알려진 설치 경로 fallback
            if (knownPaths != null)
                foreach (var p in knownPaths)
                    if (fileExists(p)) return p;

            return null;
        }
    }
}
