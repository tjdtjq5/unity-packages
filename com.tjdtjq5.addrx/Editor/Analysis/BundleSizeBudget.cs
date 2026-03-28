#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace Tjdtjq5.AddrX.Editor.Analysis
{
    /// <summary>그룹별 크기 예산을 설정하고 초과 여부를 검사한다.</summary>
    public static class BundleSizeBudget
    {
        static readonly Dictionary<string, long> _budgets = new();
        const long DefaultBudget = 10L * 1024 * 1024; // 10MB

        /// <summary>특정 그룹의 크기 예산을 설정한다 (바이트).</summary>
        public static void SetBudget(string groupName, long bytes)
        {
            _budgets[groupName] = bytes;
        }

        /// <summary>특정 그룹의 예산을 반환한다. 미설정이면 기본값.</summary>
        public static long GetBudget(string groupName)
        {
            return _budgets.TryGetValue(groupName, out var budget)
                ? budget
                : DefaultBudget;
        }

        /// <summary>모든 그룹의 예산 초과 여부를 검사한다.</summary>
        public static List<BudgetViolation> Check()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AddrXLog.Error("BundleSizeBudget",
                    "Addressables 설정을 찾을 수 없습니다.");
                return new List<BudgetViolation>();
            }

            var violations = new List<BudgetViolation>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                long totalSize = 0;
                int entryCount = 0;

                foreach (var entry in group.entries)
                {
                    entryCount++;
                    var path = entry.AssetPath;
                    if (File.Exists(path))
                        totalSize += new FileInfo(path).Length;
                }

                var budget = GetBudget(group.Name);
                if (totalSize > budget)
                {
                    violations.Add(new BudgetViolation(
                        group.Name, totalSize, budget, entryCount));
                }
            }

            if (violations.Count > 0)
                AddrXLog.Warning("BundleSizeBudget",
                    $"{violations.Count}개 그룹이 예산 초과");

            return violations;
        }
    }

    public readonly struct BudgetViolation
    {
        public readonly string GroupName;
        public readonly long ActualBytes;
        public readonly long BudgetBytes;
        public readonly int EntryCount;

        public BudgetViolation(string name, long actual, long budget, int entryCount)
        {
            GroupName = name;
            ActualBytes = actual;
            BudgetBytes = budget;
            EntryCount = entryCount;
        }

        public float OverPercent =>
            (float)(ActualBytes - BudgetBytes) / BudgetBytes * 100f;

        public string ActualText => ActualBytes < 1024 * 1024
            ? $"{ActualBytes / 1024f:F0}KB"
            : $"{ActualBytes / (1024f * 1024f):F1}MB";

        public string BudgetText => BudgetBytes < 1024 * 1024
            ? $"{BudgetBytes / 1024f:F0}KB"
            : $"{BudgetBytes / (1024f * 1024f):F1}MB";
    }
}
#endif
