#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor.Analysis
{
    /// <summary>Addressable 그룹별 건강도 점수를 계산한다.</summary>
    public static class GroupHealthScore
    {
        const int RecommendedMaxEntries = 100;
        const long RecommendedMaxBytes = 10L * 1024 * 1024; // 10MB
        const float HighDepRatio = 50f;

        /// <summary>모든 그룹의 건강도를 평가한다.</summary>
        public static List<GroupScore> Evaluate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AddrXLog.Error("GroupHealthScore", "Addressables 설정을 찾을 수 없습니다.");
                return new List<GroupScore>();
            }

            var results = new List<GroupScore>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                results.Add(EvaluateGroup(group));
            }

            results.Sort((a, b) => a.Score.CompareTo(b.Score));
            return results;
        }

        static GroupScore EvaluateGroup(AddressableAssetGroup group)
        {
            var issues = new List<string>();
            float score = 100f;

            int entryCount = 0;
            long totalSize = 0;
            var allDeps = new HashSet<string>();

            foreach (var entry in group.entries)
            {
                entryCount++;
                var path = entry.AssetPath;

                if (File.Exists(path))
                    totalSize += new FileInfo(path).Length;

                var deps = AssetDatabase.GetDependencies(path, true);
                foreach (var dep in deps)
                    allDeps.Add(dep);
            }

            // 빈 그룹
            if (entryCount == 0)
            {
                score -= 10f;
                issues.Add("빈 그룹");
                return new GroupScore(group.Name, Mathf.Max(0, score),
                    entryCount, totalSize, issues);
            }

            // 에셋 수 과다
            if (entryCount > RecommendedMaxEntries)
            {
                float penalty = Mathf.Min(30f,
                    (entryCount - RecommendedMaxEntries) * 0.5f);
                score -= penalty;
                issues.Add(
                    $"에셋 수 과다: {entryCount}개 (권장 {RecommendedMaxEntries} 이하)");
            }

            // 크기 초과
            if (totalSize > RecommendedMaxBytes)
            {
                float overMb = (totalSize - RecommendedMaxBytes) / (1024f * 1024f);
                float penalty = Mathf.Min(30f, overMb * 3f);
                score -= penalty;
                issues.Add(
                    $"크기 초과: {totalSize / (1024f * 1024f):F1}MB " +
                    $"(권장 {RecommendedMaxBytes / (1024 * 1024)}MB 이하)");
            }

            // 의존성 복잡도
            float depRatio = (float)allDeps.Count / entryCount;
            if (depRatio > HighDepRatio)
            {
                float penalty = Mathf.Min(20f, (depRatio - HighDepRatio) * 1f);
                score -= penalty;
                issues.Add(
                    $"의존성 복잡도 높음: 에셋당 평균 {depRatio:F0}개 의존성");
            }

            return new GroupScore(group.Name, Mathf.Max(0, score),
                entryCount, totalSize, issues);
        }
    }

    public readonly struct GroupScore
    {
        public readonly string GroupName;
        public readonly float Score;
        public readonly int EntryCount;
        public readonly long TotalSizeBytes;
        public readonly List<string> Issues;

        public GroupScore(string name, float score, int entryCount,
            long totalSize, List<string> issues)
        {
            GroupName = name;
            Score = score;
            EntryCount = entryCount;
            TotalSizeBytes = totalSize;
            Issues = issues;
        }

        public string SizeText => TotalSizeBytes < 1024 * 1024
            ? $"{TotalSizeBytes / 1024f:F0}KB"
            : $"{TotalSizeBytes / (1024f * 1024f):F1}MB";
    }
}
#endif
