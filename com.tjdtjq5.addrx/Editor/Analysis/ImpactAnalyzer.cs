#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace Tjdtjq5.AddrX.Editor.Analysis
{
    /// <summary>에셋 로드 시 연쇄적으로 로드되는 번들(그룹)을 분석한다.</summary>
    public static class ImpactAnalyzer
    {
        /// <summary>Addressable 주소로 임팩트 분석을 실행한다.</summary>
        public static ImpactReport Analyze(string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AddrXLog.Error("ImpactAnalyzer", "Addressables 설정을 찾을 수 없습니다.");
                return ImpactReport.Empty;
            }

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry.address == address)
                        return AnalyzeEntry(entry, settings);
                }
            }

            AddrXLog.Warning("ImpactAnalyzer",
                $"주소 '{address}'에 해당하는 에셋을 찾을 수 없습니다.");
            return ImpactReport.Empty;
        }

        /// <summary>에셋 경로로 임팩트 분석을 실행한다.</summary>
        public static ImpactReport AnalyzeByPath(string assetPath)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AddrXLog.Error("ImpactAnalyzer", "Addressables 설정을 찾을 수 없습니다.");
                return ImpactReport.Empty;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                AddrXLog.Warning("ImpactAnalyzer", $"경로 '{assetPath}'를 찾을 수 없습니다.");
                return ImpactReport.Empty;
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                AddrXLog.Warning("ImpactAnalyzer",
                    $"경로 '{assetPath}'는 Addressables에 등록되지 않았습니다.");
                return ImpactReport.Empty;
            }

            return AnalyzeEntry(entry, settings);
        }

        /// <summary>등록된 모든 Addressable 에셋의 임팩트를 분석한다. 번들 수/크기 내림차순.</summary>
        public static List<ImpactReport> ScanAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AddrXLog.Error("ImpactAnalyzer", "Addressables 설정을 찾을 수 없습니다.");
                return new List<ImpactReport>();
            }

            var pathToGroup = BuildPathToGroupMap(settings);
            var results = new List<ImpactReport>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                    results.Add(AnalyzeEntry(entry, settings, pathToGroup));
            }

            results.Sort((a, b) => b.BundleCount != a.BundleCount
                ? b.BundleCount.CompareTo(a.BundleCount)
                : b.TotalBytes.CompareTo(a.TotalBytes));

            AddrXLog.Verbose("ImpactAnalyzer",
                $"전체 스캔: {results.Count}개 에셋 분석 완료");

            return results;
        }

        static Dictionary<string, string> BuildPathToGroupMap(
            AddressableAssetSettings settings)
        {
            var map = new Dictionary<string, string>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var e in group.entries)
                    map[e.AssetPath] = group.Name;
            }
            return map;
        }

        static ImpactReport AnalyzeEntry(
            AddressableAssetEntry entry, AddressableAssetSettings settings)
        {
            return AnalyzeEntry(entry, settings, BuildPathToGroupMap(settings));
        }

        static ImpactReport AnalyzeEntry(
            AddressableAssetEntry entry, AddressableAssetSettings settings,
            Dictionary<string, string> pathToGroup)
        {
            var assetPath = entry.AssetPath;
            var sourceGroup = entry.parentGroup.Name;

            // 모든 의존성 수집 (재귀)
            var allDeps = AssetDatabase.GetDependencies(assetPath, true);

            // 그룹별 의존성 집계
            var groupImpacts = new Dictionary<string, GroupImpact>();

            foreach (var dep in allDeps)
            {
                if (dep.StartsWith("Packages/")) continue;

                // 의존성이 직접 등록된 그룹 확인, 없으면 소스 그룹에 포함
                var groupName = pathToGroup.TryGetValue(dep, out var g)
                    ? g : sourceGroup;

                if (!groupImpacts.TryGetValue(groupName, out var impact))
                {
                    impact = new GroupImpact(groupName);
                    groupImpacts[groupName] = impact;
                }

                long size = File.Exists(dep) ? new FileInfo(dep).Length : 0;
                impact.Assets.Add(dep);
                impact.TotalBytes += size;
            }

            var impacts = groupImpacts.Values
                .OrderByDescending(i => i.TotalBytes)
                .ToList();

            long totalSize = impacts.Sum(i => i.TotalBytes);

            AddrXLog.Verbose("ImpactAnalyzer",
                $"'{entry.address}' 분석: {impacts.Count}개 번들, 총 {FormatSize(totalSize)}");

            return new ImpactReport(
                entry.address, assetPath, sourceGroup, impacts, totalSize);
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F0}KB";
            return $"{bytes / (1024f * 1024f):F1}MB";
        }
    }

    public readonly struct ImpactReport
    {
        public readonly string Address;
        public readonly string AssetPath;
        public readonly string SourceGroup;
        public readonly List<GroupImpact> Impacts;
        public readonly long TotalBytes;

        public static readonly ImpactReport Empty =
            new(null, null, null, new List<GroupImpact>(), 0);

        public ImpactReport(string address, string assetPath, string sourceGroup,
            List<GroupImpact> impacts, long totalBytes)
        {
            Address = address;
            AssetPath = assetPath;
            SourceGroup = sourceGroup;
            Impacts = impacts;
            TotalBytes = totalBytes;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Address);
        public string TotalSizeText => ImpactAnalyzer.FormatSize(TotalBytes);
        public int BundleCount => Impacts?.Count ?? 0;
        public int TotalAssetCount => Impacts?.Sum(i => i.Assets.Count) ?? 0;
    }

    public class GroupImpact
    {
        public readonly string GroupName;
        public readonly List<string> Assets = new();
        public long TotalBytes;

        public GroupImpact(string groupName) => GroupName = groupName;
        public string SizeText => ImpactAnalyzer.FormatSize(TotalBytes);
    }
}
#endif
