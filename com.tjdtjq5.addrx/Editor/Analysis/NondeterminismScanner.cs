#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Tjdtjq5.AddrX.Editor.Analysis
{
    /// <summary>AssetPostprocessor/ScriptedImporter 내 비결정성 패턴을 감지한다.</summary>
    public static class NondeterminismScanner
    {
        static readonly (string pattern, string message)[] Rules =
        {
            (@"DateTime\.(Now|UtcNow|Today)",
                "DateTime 사용 — 빌드 시점에 따라 에셋 해시가 달라질 수 있음"),
            (@"Random\.(Range|value|Next|Shared)",
                "Random 사용 — 빌드할 때마다 다른 결과 생성"),
            (@"Guid\.NewGuid\(\)",
                "Guid.NewGuid 사용 — 매 빌드마다 새 GUID 생성"),
            (@"Environment\.TickCount",
                "TickCount 사용 — 빌드 머신 상태에 따라 값이 달라짐"),
            (@"GetHashCode\(\)",
                "GetHashCode 사용 — .NET 런타임마다 결과가 다를 수 있음"),
        };

        /// <summary>프로젝트 내 AssetPostprocessor/ScriptedImporter 스크립트를 스캔.</summary>
        public static List<NondeterminismWarning> Scan()
        {
            var warnings = new List<NondeterminismWarning>();

            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs")) continue;

                // Packages, AddrX 자체는 스킵
                if (path.StartsWith("Packages/")) continue;

                var content = File.ReadAllText(path);

                // AssetPostprocessor 또는 ScriptedImporter를 상속한 파일만 검사
                if (!content.Contains("AssetPostprocessor") &&
                    !content.Contains("ScriptedImporter") &&
                    !content.Contains("IPreprocessBuildWithReport") &&
                    !content.Contains("IPostprocessBuildWithReport") &&
                    !content.Contains("BuildPlayerProcessor"))
                    continue;

                var lines = content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // 주석 라인 스킵
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//")) continue;

                    foreach (var (pattern, message) in Rules)
                    {
                        if (Regex.IsMatch(line, pattern))
                        {
                            warnings.Add(new NondeterminismWarning(
                                path, i + 1, pattern, message));
                        }
                    }
                }
            }

            if (warnings.Count > 0)
                AddrXLog.Warning("NondeterminismScanner",
                    $"{warnings.Count}개 비결정성 패턴 발견");

            return warnings;
        }
    }

    public readonly struct NondeterminismWarning
    {
        public readonly string FilePath;
        public readonly int Line;
        public readonly string Pattern;
        public readonly string Message;

        public NondeterminismWarning(string filePath, int line, string pattern, string message)
        {
            FilePath = filePath;
            Line = line;
            Pattern = pattern;
            Message = message;
        }
    }
}
#endif
