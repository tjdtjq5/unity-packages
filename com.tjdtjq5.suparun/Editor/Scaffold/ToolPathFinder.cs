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
                var picked = PickExecutableLine(output, fileExists, isWindows);
                if (!string.IsNullOrEmpty(picked))
                    return picked;
            }

            // 2. 알려진 설치 경로 fallback
            if (knownPaths != null)
                foreach (var p in knownPaths)
                    if (fileExists(p)) return p;

            return null;
        }

        /// <summary>
        /// `where` / `command -v` 출력에서 실제 실행 가능한 line을 선택.
        /// Windows 한정 — `where`는 확장자 없는 unix-style shell script line과 `.cmd` line을 모두 출력하는데,
        /// 확장자 없는 line을 Process.Start FileName으로 직접 실행하면 Win32Exception("올바른 Win32 응용 프로그램이 아닙니다") 발생.
        /// → `.cmd`/`.exe`/`.bat` 확장자 line 우선. 없으면 첫 유효 line(macOS/Linux는 보통 단일 line이라 동일 동작).
        /// </summary>
        static string PickExecutableLine(string whereOutput, Func<string, bool> fileExists, bool isWindows)
        {
            if (string.IsNullOrWhiteSpace(whereOutput)) return null;
            string fallback = null;
            foreach (var line in whereOutput.Split('\n', '\r'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (!fileExists(trimmed)) continue;
                if (fallback == null) fallback = trimmed;
                if (isWindows)
                {
                    if (trimmed.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                        || trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || trimmed.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                        return trimmed;
                }
                else
                {
                    return trimmed;
                }
            }
            return fallback;
        }
    }
}
