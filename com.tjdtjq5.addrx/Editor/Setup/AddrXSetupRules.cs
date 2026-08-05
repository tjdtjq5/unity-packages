#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>
    /// 원격으로 취급할 <b>1뎁스 폴더</b> 항목.
    /// "원격이냐"는 콘텐츠 영역의 속성이지 번들 경계(그룹)의 속성이 아니므로
    /// _groupDepth 와 무관하게 항상 1뎁스 폴더명으로 식별한다.
    /// </summary>
    [Serializable]
    public class RemoteFolderEntry
    {
        [FormerlySerializedAs("groupName")]
        public string folderName;
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

        [Tooltip("그룹을 나눌 폴더 깊이. 1 = 1뎁스 폴더 하나가 그룹 하나(기본).\n" +
                 "올리면 그룹이 잘게 나뉘어 팀 작업 시 그룹 에셋 경합이 줄어든다.\n" +
                 "주소(GetAddress)는 깊이와 무관하게 항상 1뎁스 기준이라 바뀌지 않는다.\n" +
                 "변경 후에는 Setup 탭의 전체 동기화를 실행해야 반영된다.")]
        [SerializeField] int _groupDepth = 1;

        [FormerlySerializedAs("_remoteGroups")]
        [SerializeField] List<RemoteFolderEntry> _remoteFolders = new();
        [SerializeField] List<LabelCategory> _labelCategories = new();
        [SerializeField] List<LabelOverride> _labelOverrides = new();

        public string RootPath => _rootPath;
        public int GroupDepth => Math.Max(1, _groupDepth);
        public List<RemoteFolderEntry> RemoteFolders => _remoteFolders;
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
            _instance._remoteFolders = DefaultRemoteFolders();
            _instance._labelCategories = DefaultLabelCategories();
            UnityEditor.AssetDatabase.CreateAsset(_instance, AssetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return _instance;
        }

        /// <summary>기본 원격 폴더 프리셋. 여기에 있는 1뎁스 폴더만 Remote로 취급.</summary>
        static List<RemoteFolderEntry> DefaultRemoteFolders() => new()
        {
            new() { folderName = "Chapter2" },
            new() { folderName = "Chapter3" },
            new() { folderName = "Audio_BGM" },
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

        /// <summary>
        /// 에셋 경로 → 1뎁스 폴더명. "콘텐츠 영역"의 식별자로,
        /// 주소(<see cref="GetAddress"/>)와 원격 판정(<see cref="IsRemoteFolder"/>)의 기준이다.
        /// <see cref="GetGroupName"/>(번들 경계)과 달리 _groupDepth 의 영향을 받지 않는다.
        /// </summary>
        public string GetRootFolder(string assetPath)
        {
            if (!assetPath.StartsWith(_rootPath + "/")) return null;
            var relative = assetPath.Substring(_rootPath.Length + 1);
            var idx = relative.IndexOf('/');
            return idx > 0 ? relative.Substring(0, idx) : null;
        }

        /// <summary>
        /// 에셋 경로 → 그룹명. 폴더 조각을 _groupDepth 개까지 '-'로 이어 만든다.
        /// 폴더 깊이가 설정값보다 얕으면 있는 만큼만 쓴다(_groupDepth = 1 이면 <see cref="GetRootFolder"/>와 동일).
        /// </summary>
        /// <remarks>
        /// 구분자가 '-'인 것은 취향이 아니라 제약이다. 그룹명에 '/'가 들어가면
        /// AddressableAssetSettings.FindUniqueGroupName 이 '-'로 치환해 그룹을 만드는데,
        /// 조회는 치환 전 이름으로 하므로 FindGroup 이 매번 실패해 그룹이 무한 증식한다.
        /// </remarks>
        public string GetGroupName(string assetPath)
        {
            if (!assetPath.StartsWith(_rootPath + "/")) return null;
            var relative = assetPath.Substring(_rootPath.Length + 1);
            var parts = relative.Split('/');
            if (parts.Length < 2) return null;

            // 마지막 조각은 파일명이므로 폴더 조각만 센다.
            var take = Math.Min(GroupDepth, parts.Length - 1);
            return string.Join("-", parts, 0, take);
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

        /// <summary>
        /// 1뎁스 폴더가 원격인지 확인한다.
        /// ⚠인자는 <b>그룹명이 아니라 1뎁스 폴더명</b>이다(<see cref="GetRootFolder"/>).
        /// _groupDepth > 1 이면 그룹명 ≠ 폴더명이므로 <see cref="GetGroupName"/> 결과를 넘기면 안 된다.
        /// </summary>
        public bool IsRemoteFolder(string folderName)
        {
            return _remoteFolders.Exists(f => f.folderName == folderName);
        }

        /// <summary>
        /// 그룹 입도를 설정한다. 저장만 하며, 기존 엔트리의 재배치는
        /// 명시적 전체 동기화(Setup 탭)에서 이뤄진다 — 라벨/원격 설정과 동일한 규약.
        /// </summary>
        public void SetGroupDepth(int depth)
        {
            _groupDepth = Math.Max(1, depth);
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>1뎁스 폴더의 원격 여부를 설정한다.</summary>
        public void SetRemoteFolder(string folderName, bool isRemote)
        {
            _remoteFolders.RemoveAll(f => f.folderName == folderName);
            if (isRemote)
                _remoteFolders.Add(new RemoteFolderEntry { folderName = folderName });
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
