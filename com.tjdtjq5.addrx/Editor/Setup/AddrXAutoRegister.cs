#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>AssetPostprocessor — Assets/Addressables/ 하위 에셋 Import/Move/Delete 시 자동 등록/해제.</summary>
    public class AddrXAutoRegister : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var rules = AddrXSetupRules.Instance;
            if (rules == null) return;
            var root = rules.RootPath + "/";

            // ── 폴더 → Rules 동기화 (1뎁스 = 그룹만) ──
            SyncFoldersToRules(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths, rules, root);

            // ── 에셋 등록/해제 ──
            var toRegister = FilterAssets(importedAssets, movedAssets, root);
            var toRemove = FilterAssets(deletedAssets, movedFromAssetPaths, root);

            if (toRegister.Count == 0 && toRemove.Count == 0) return;

            foreach (var path in toRemove)
                RemoveEntry(settings, rules, path);

            var duplicates = DetectDuplicates(toRegister, rules, settings);
            foreach (var path in toRegister)
                RegisterAsset(settings, rules, path, duplicates);

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
        }

        /// <summary>1뎁스 폴더 생성/삭제를 감지하여 Addressables 그룹을 동기화.</summary>
        static void SyncFoldersToRules(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom,
            AddrXSetupRules rules, string root)
        {
            // 새 폴더 → Addressables 그룹 생성
            foreach (var path in ConcatArrays(imported, moved))
            {
                if (!path.StartsWith(root) || !AssetDatabase.IsValidFolder(path)) continue;

                var relative = path.Substring(root.Length);
                if (relative.Contains("/")) continue; // 1뎁스만

                var groupName = relative;
                if (string.IsNullOrEmpty(groupName)) continue;

                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null && settings.FindGroup(groupName) == null)
                {
                    var group = settings.CreateGroup(groupName, false, false, true,
                        null, typeof(BundledAssetGroupSchema));
                    ApplyGroupSchema(group, rules.IsGroupRemote(groupName));
                    AddrXLog.Info("Setup", $"폴더 감지 → Addressables 그룹 생성: {groupName}");
                }
            }

            // 삭제된 폴더 → Addressables 그룹 삭제
            foreach (var path in ConcatArrays(deleted, movedFrom))
            {
                if (!path.StartsWith(root)) continue;

                var relative = path.Substring(root.Length);
                if (relative.Contains("/")) continue;

                var groupName = relative;
                if (string.IsNullOrEmpty(groupName)) continue;

                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                {
                    var addrGroup = settings.FindGroup(groupName);
                    if (addrGroup != null)
                    {
                        settings.RemoveGroup(addrGroup);
                        AddrXLog.Info("Setup", $"폴더 삭제 → Addressables 그룹 제거: {groupName}");
                    }
                }
            }
        }

        /// <summary>삭제된 에셋의 엔트리 제거.</summary>
        static void RemoveEntry(
            AddressableAssetSettings settings, AddrXSetupRules rules, string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
            {
                var entry = settings.FindAssetEntry(guid);
                if (entry != null)
                {
                    settings.RemoveAssetEntry(entry.guid);
                    return;
                }
            }

            var groupName = rules.GetGroupName(path);
            var address = rules.GetAddress(path);
            if (groupName == null || address == null) return;

            var group = settings.FindGroup(groupName);
            if (group == null) return;

            var match = group.entries.FirstOrDefault(e => e.address == address);
            if (match != null)
                settings.RemoveAssetEntry(match.guid);
        }

        /// <summary>단일 에셋을 Addressables에 등록. Label Category 디폴트 라벨 자동 부여.</summary>
        internal static bool RegisterAsset(
            AddressableAssetSettings settings, AddrXSetupRules rules,
            string path, HashSet<string> duplicates)
        {
            var groupName = rules.GetGroupName(path);
            var address = rules.GetAddress(path);
            if (groupName == null || address == null) return false;

            if (duplicates.Contains(address))
            {
                AddrXLog.Error("Setup",
                    $"파일명 중복으로 등록 차단: {address}\n  경로: {path}\n  → 파일명을 변경해주세요.");
                return false;
            }

            var group = settings.FindGroup(groupName)
                ?? CreateGroup(settings, groupName, rules.IsGroupRemote(groupName));

            var guid = AssetDatabase.AssetPathToGUID(path);
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;

            // Label Category 디폴트 라벨 부여
            var labels = rules.GetLabelsForAsset(guid);
            foreach (var label in labels)
            {
                settings.AddLabel(label);
                entry.SetLabel(label, true);
            }

            return true;
        }

        /// <summary>Addressables 그룹 생성 + 로컬/원격 스키마 적용.</summary>
        static AddressableAssetGroup CreateGroup(
            AddressableAssetSettings settings, string groupName, bool isRemote)
        {
            var group = settings.CreateGroup(groupName, false, false, true,
                null, typeof(BundledAssetGroupSchema));
            ApplyGroupSchema(group, isRemote);
            return group;
        }

        /// <summary>그룹의 BundledAssetGroupSchema에 로컬/원격 경로를 적용.</summary>
        internal static void ApplyGroupSchema(AddressableAssetGroup group, bool isRemote)
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var buildPath = isRemote
                ? settings.profileSettings.GetValueByName(settings.activeProfileId, "RemoteBuildPath")
                : settings.profileSettings.GetValueByName(settings.activeProfileId, "LocalBuildPath");
            var loadPath = isRemote
                ? settings.profileSettings.GetValueByName(settings.activeProfileId, "RemoteLoadPath")
                : settings.profileSettings.GetValueByName(settings.activeProfileId, "LocalLoadPath");

            if (!string.IsNullOrEmpty(buildPath))
                schema.BuildPath.SetVariableByName(settings, isRemote ? "RemoteBuildPath" : "LocalBuildPath");
            if (!string.IsNullOrEmpty(loadPath))
                schema.LoadPath.SetVariableByName(settings, isRemote ? "RemoteLoadPath" : "LocalLoadPath");
        }

        static List<string> FilterAssets(string[] a, string[] b, string root)
        {
            var seen = new HashSet<string>();
            var result = new List<string>(a.Length + b.Length);
            foreach (var p in a)
                if (p.StartsWith(root) && !AssetDatabase.IsValidFolder(p) && seen.Add(p))
                    result.Add(p);
            foreach (var p in b)
                if (p.StartsWith(root) && !AssetDatabase.IsValidFolder(p) && seen.Add(p))
                    result.Add(p);
            return result;
        }

        static IEnumerable<string> ConcatArrays(string[] a, string[] b)
        {
            foreach (var s in a) yield return s;
            foreach (var s in b) yield return s;
        }

        /// <summary>같은 그룹 내 주소 중복 감지. 이미 등록된 에셋의 재임포트는 중복으로 세지 않는다.</summary>
        internal static HashSet<string> DetectDuplicates(
            List<string> newPaths, AddrXSetupRules rules, AddressableAssetSettings settings)
        {
            var addressMap = new Dictionary<string, int>();

            // 주소 → GUID 매핑 (재임포트 판별용)
            var addressToGuids = new Dictionary<string, HashSet<string>>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    addressMap.TryGetValue(entry.address, out int c);
                    addressMap[entry.address] = c + 1;

                    if (!addressToGuids.TryGetValue(entry.address, out var guids))
                    {
                        guids = new HashSet<string>();
                        addressToGuids[entry.address] = guids;
                    }
                    guids.Add(entry.guid);
                }
            }

            foreach (var path in newPaths)
            {
                var address = rules.GetAddress(path);
                if (address == null) continue;

                // 같은 GUID가 이미 같은 주소로 등록되어 있으면 재임포트 → 스킵
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (addressToGuids.TryGetValue(address, out var existing) && existing.Contains(guid))
                    continue;

                addressMap.TryGetValue(address, out int c);
                addressMap[address] = c + 1;

                // newPaths 내 같은 GUID 중복 카운트 방지
                if (!addressToGuids.TryGetValue(address, out var guids))
                {
                    guids = new HashSet<string>();
                    addressToGuids[address] = guids;
                }
                guids.Add(guid);
            }

            return new HashSet<string>(
                addressMap.Where(kv => kv.Value > 1).Select(kv => kv.Key));
        }
    }
}
#endif
