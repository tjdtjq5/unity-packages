#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor.Update
{
    /// <summary>두 빌드의 카탈로그 JSON을 비교하여 변경된 번들/해시를 추적한다.</summary>
    public static class BuildHashComparer
    {
        /// <summary>두 카탈로그 JSON 파일을 비교한다.</summary>
        public static CompareReport Compare(string oldCatalogPath, string newCatalogPath)
        {
            if (!File.Exists(oldCatalogPath))
            {
                AddrXLog.Error("BuildHashComparer", $"이전 카탈로그를 찾을 수 없습니다: {oldCatalogPath}");
                return CompareReport.Empty;
            }
            if (!File.Exists(newCatalogPath))
            {
                AddrXLog.Error("BuildHashComparer", $"새 카탈로그를 찾을 수 없습니다: {newCatalogPath}");
                return CompareReport.Empty;
            }

            var oldBundles = ParseBundleHashes(File.ReadAllText(oldCatalogPath));
            var newBundles = ParseBundleHashes(File.ReadAllText(newCatalogPath));

            var added = new List<BundleChange>();
            var removed = new List<BundleChange>();
            var changed = new List<BundleChange>();
            var unchanged = new List<string>();

            foreach (var kv in newBundles)
            {
                if (!oldBundles.TryGetValue(kv.Key, out var oldHash))
                    added.Add(new BundleChange(kv.Key, null, kv.Value));
                else if (oldHash != kv.Value)
                    changed.Add(new BundleChange(kv.Key, oldHash, kv.Value));
                else
                    unchanged.Add(kv.Key);
            }

            foreach (var kv in oldBundles)
            {
                if (!newBundles.ContainsKey(kv.Key))
                    removed.Add(new BundleChange(kv.Key, kv.Value, null));
            }

            AddrXLog.Info("BuildHashComparer",
                $"비교 완료: 추가 {added.Count}, 변경 {changed.Count}, 제거 {removed.Count}, 동일 {unchanged.Count}");

            return new CompareReport(added, changed, removed, unchanged);
        }

        /// <summary>
        /// 카탈로그 JSON에서 번들명 → 해시 매핑을 추출한다.
        /// 주의: 문자열 기반 휴리스틱 파싱. Addressables 카탈로그 포맷 변경 시 깨질 수 있음.
        /// "bundlename_hashvalue.bundle" 패턴에 의존한다.
        /// </summary>
        static Dictionary<string, string> ParseBundleHashes(string catalogJson)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(catalogJson)) return result;

            var lines = catalogJson.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim().Trim('"', ',');

                if (!trimmed.Contains(".bundle")) continue;

                var fileName = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(fileName)) continue;
                if (!fileName.EndsWith(".bundle")) continue;

                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var lastUnderscore = nameWithoutExt.LastIndexOf('_');

                string bundleName, hash;
                if (lastUnderscore > 0)
                {
                    bundleName = nameWithoutExt.Substring(0, lastUnderscore);
                    hash = nameWithoutExt.Substring(lastUnderscore + 1);
                }
                else
                {
                    bundleName = nameWithoutExt;
                    hash = "unknown";
                }

                result[bundleName] = hash;
            }

            return result;
        }
    }

    public readonly struct CompareReport
    {
        public readonly List<BundleChange> Added;
        public readonly List<BundleChange> Changed;
        public readonly List<BundleChange> Removed;
        public readonly List<string> Unchanged;

        public static readonly CompareReport Empty =
            new(new List<BundleChange>(), new List<BundleChange>(),
                new List<BundleChange>(), new List<string>());

        public CompareReport(List<BundleChange> added, List<BundleChange> changed,
            List<BundleChange> removed, List<string> unchanged)
        {
            Added = added;
            Changed = changed;
            Removed = removed;
            Unchanged = unchanged;
        }

        public bool IsEmpty => Added.Count == 0 && Changed.Count == 0 && Removed.Count == 0;
        public int TotalChanges => Added.Count + Changed.Count + Removed.Count;
    }

    public readonly struct BundleChange
    {
        public readonly string BundleName;
        public readonly string OldHash;
        public readonly string NewHash;

        public BundleChange(string name, string oldHash, string newHash)
        {
            BundleName = name;
            OldHash = oldHash;
            NewHash = newHash;
        }
    }
}
#endif
