#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace Tjdtjq5.AddrX.Editor.Analysis
{
    /// <summary>여러 Addressable 그룹에 중복 포함된 에셋을 탐지한다.</summary>
    public static class DuplicateScanner
    {
        /// <summary>전체 그룹을 스캔하여 중복 에셋 리포트를 반환한다.</summary>
        public static DuplicateReport Scan()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                AddrXLog.Error("DuplicateScanner", "Addressables 설정을 찾을 수 없습니다.");
                return new DuplicateReport(new List<DuplicateEntry>());
            }

            // 에셋 경로 → 참조하는 그룹 이름 집합
            var assetToGroups = new Dictionary<string, HashSet<string>>();
            var depsCache = new Dictionary<string, string[]>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                foreach (var entry in group.entries)
                {
                    var path = entry.AssetPath;
                    AddAssetGroup(assetToGroups, path, group.Name);

                    // 암시적 의존성도 체크 (캐싱)
                    if (!depsCache.TryGetValue(path, out var deps))
                    {
                        deps = AssetDatabase.GetDependencies(path, true);
                        depsCache[path] = deps;
                    }
                    foreach (var dep in deps)
                    {
                        if (dep == path) continue;
                        if (dep.StartsWith("Packages/")) continue;
                        AddAssetGroup(assetToGroups, dep, group.Name);
                    }
                }
            }

            var duplicates = assetToGroups
                .Where(kvp => kvp.Value.Count > 1)
                .Select(kvp => new DuplicateEntry(kvp.Key, kvp.Value.ToList()))
                .OrderByDescending(e => e.Groups.Count)
                .ToList();

            if (duplicates.Count > 0)
                AddrXLog.Warning("DuplicateScanner",
                    $"{duplicates.Count}개 중복 에셋 발견");

            return new DuplicateReport(duplicates);
        }

        static void AddAssetGroup(
            Dictionary<string, HashSet<string>> map, string path, string groupName)
        {
            if (!map.TryGetValue(path, out var groups))
            {
                groups = new HashSet<string>();
                map[path] = groups;
            }
            groups.Add(groupName);
        }
    }

    public readonly struct DuplicateReport
    {
        public readonly List<DuplicateEntry> Entries;
        public DuplicateReport(List<DuplicateEntry> entries) => Entries = entries;
        public int Count => Entries?.Count ?? 0;
    }

    public readonly struct DuplicateEntry
    {
        public readonly string AssetPath;
        public readonly List<string> Groups;

        public DuplicateEntry(string path, List<string> groups)
        {
            AssetPath = path;
            Groups = groups;
        }
    }
}
#endif
