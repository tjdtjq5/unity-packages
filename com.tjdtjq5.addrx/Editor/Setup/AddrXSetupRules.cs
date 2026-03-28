#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor
{
    [Serializable]
    public class RemoteGroupEntry
    {
        public string groupName;
    }

    [Serializable]
    public class LabelCategory
    {
        public string categoryName;
        public string defaultValue;
        public List<string> options = new();
    }

    [Serializable]
    public class LabelOverride
    {
        public string assetGuid;
        public string category;
        public string value;
    }

    /// <summary>폴더 규칙 기반 Addressables 매핑 데이터. Editor 전용 SO.</summary>
    public class AddrXSetupRules : ScriptableObject
    {
        const string AssetPath = "Assets/AddrX/Resources/AddrXSetupRules.asset";
        const string ResourcePath = "AddrXSetupRules";

        static AddrXSetupRules _instance;

        [SerializeField] string _rootPath = "Assets/Addressables";
        [SerializeField] List<RemoteGroupEntry> _remoteGroups = new();
        [SerializeField] List<LabelCategory> _labelCategories = new();
        [SerializeField] List<LabelOverride> _labelOverrides = new();

        public string RootPath => _rootPath;
        public List<RemoteGroupEntry> RemoteGroups => _remoteGroups;
        public List<LabelCategory> LabelCategories => _labelCategories;
        public List<LabelOverride> LabelOverrides => _labelOverrides;

        public static AddrXSetupRules Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = Resources.Load<AddrXSetupRules>(ResourcePath);
                return _instance;
            }
        }

        public static AddrXSetupRules GetOrCreate()
        {
            if (_instance != null && UnityEditor.AssetDatabase.Contains(_instance))
                return _instance;

            _instance = Resources.Load<AddrXSetupRules>(ResourcePath);
            if (_instance != null) return _instance;

            var dir = "Assets/AddrX/Resources";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _instance = CreateInstance<AddrXSetupRules>();
            _instance._remoteGroups = DefaultRemoteGroups();
            _instance._labelCategories = DefaultLabelCategories();
            UnityEditor.AssetDatabase.CreateAsset(_instance, AssetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return _instance;
        }

        /// <summary>기본 원격 그룹 프리셋. 여기에 있는 그룹만 Remote로 취급.</summary>
        static List<RemoteGroupEntry> DefaultRemoteGroups() => new()
        {
            new() { groupName = "Chapter2" },
            new() { groupName = "Chapter3" },
            new() { groupName = "Audio_BGM" },
        };

        static List<LabelCategory> DefaultLabelCategories() => new()
        {
            new() { categoryName = "Priority", defaultValue = "Required",
                options = new() { "Required", "Optional" } },
            new() { categoryName = "Quality", defaultValue = "Common",
                options = new() { "HD", "SD", "Common" } },
            new() { categoryName = "Region", defaultValue = "Global",
                options = new() { "Global", "KR", "JP", "EN" } },
            new() { categoryName = "Platform", defaultValue = "All",
                options = new() { "All", "Android", "iOS" } },
        };

        /// <summary>에셋 경로 → 주소 (그룹/파일명, 확장자 제외). 규칙 외 경로면 null.</summary>
        public string GetAddress(string assetPath)
        {
            if (!assetPath.StartsWith(_rootPath + "/")) return null;
            var relative = assetPath.Substring(_rootPath.Length + 1);
            var parts = relative.Split('/');
            if (parts.Length < 2) return null;
            var group = parts[0];
            var fileName = Path.GetFileNameWithoutExtension(parts[^1]);
            return $"{group}/{fileName}";
        }

        /// <summary>에셋 경로 → 그룹명 (1뎁스).</summary>
        public string GetGroupName(string assetPath)
        {
            if (!assetPath.StartsWith(_rootPath + "/")) return null;
            var relative = assetPath.Substring(_rootPath.Length + 1);
            var idx = relative.IndexOf('/');
            return idx > 0 ? relative.Substring(0, idx) : null;
        }

        /// <summary>에셋의 라벨 목록을 반환한다. 카테고리별 디폴트 + 오버라이드 적용.</summary>
        public List<string> GetLabelsForAsset(string assetGuid)
        {
            var labels = new List<string>();
            foreach (var cat in _labelCategories)
            {
                var ov = _labelOverrides.Find(
                    o => o.assetGuid == assetGuid && o.category == cat.categoryName);
                labels.Add(ov != null ? ov.value : cat.defaultValue);
            }
            return labels;
        }

        /// <summary>에셋의 특정 카테고리 라벨을 반환한다.</summary>
        public string GetLabelForCategory(string assetGuid, string categoryName)
        {
            var ov = _labelOverrides.Find(
                o => o.assetGuid == assetGuid && o.category == categoryName);
            if (ov != null) return ov.value;

            var cat = _labelCategories.Find(c => c.categoryName == categoryName);
            return cat?.defaultValue;
        }

        /// <summary>에셋의 라벨 오버라이드를 설정한다. 디폴트와 같으면 오버라이드 제거.</summary>
        public void SetLabelOverride(string assetGuid, string categoryName, string value)
        {
            var cat = _labelCategories.Find(c => c.categoryName == categoryName);
            if (cat == null) return;

            _labelOverrides.RemoveAll(
                o => o.assetGuid == assetGuid && o.category == categoryName);

            if (value != cat.defaultValue)
            {
                _labelOverrides.Add(new LabelOverride
                {
                    assetGuid = assetGuid,
                    category = categoryName,
                    value = value
                });
            }

            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>그룹이 원격인지 확인한다. _remoteGroups에 있으면 원격.</summary>
        public bool IsGroupRemote(string groupName)
        {
            return _remoteGroups.Exists(g => g.groupName == groupName);
        }

        /// <summary>그룹의 원격 여부를 설정한다.</summary>
        public void SetGroupRemote(string groupName, bool isRemote)
        {
            _remoteGroups.RemoveAll(g => g.groupName == groupName);
            if (isRemote)
                _remoteGroups.Add(new RemoteGroupEntry { groupName = groupName });
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>Assets/Addressables/ 하위 1뎁스 폴더 목록을 반환한다.</summary>
        public string[] GetGroupFolders()
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(_rootPath))
                return System.Array.Empty<string>();
            var subFolders = UnityEditor.AssetDatabase.GetSubFolders(_rootPath);
            var names = new string[subFolders.Length];
            for (int i = 0; i < subFolders.Length; i++)
                names[i] = Path.GetFileName(subFolders[i]);
            return names;
        }
    }
}
#endif
