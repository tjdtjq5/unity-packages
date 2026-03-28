#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace Tjdtjq5.AddrX.Editor.Analysis
{
    /// <summary>에디터와 빌드 간 알려진 동작 차이를 룰 기반으로 스캔한다.</summary>
    public static class BehaviorDiffChecker
    {
        static readonly List<IDiffRule> _rules = new()
        {
            new ResourcesFolderRule(),
            new SceneDuplicateRule(),
            new SpriteAtlasRule()
        };

        public static IReadOnlyList<IDiffRule> Rules => _rules;

        /// <summary>커스텀 룰을 추가한다.</summary>
        public static void AddRule(IDiffRule rule) => _rules.Add(rule);

        /// <summary>모든 룰을 실행하여 경고 목록을 반환한다.</summary>
        public static List<DiffWarning> Check()
        {
            var settings = UnityEditor.AddressableAssets
                .AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                AddrXLog.Error("BehaviorDiffChecker",
                    "Addressables 설정을 찾을 수 없습니다.");
                return new List<DiffWarning>();
            }

            var warnings = new List<DiffWarning>();
            foreach (var rule in _rules)
                warnings.AddRange(rule.Check(settings));

            if (warnings.Count > 0)
                AddrXLog.Warning("BehaviorDiffChecker",
                    $"{warnings.Count}개 에디터/빌드 차이 경고");

            return warnings;
        }
    }

    /// <summary>동작 차이 검사 규칙 인터페이스.</summary>
    public interface IDiffRule
    {
        string Name { get; }
        List<DiffWarning> Check(AddressableAssetSettings settings);
    }

    public readonly struct DiffWarning
    {
        public readonly string RuleName;
        public readonly string Message;
        public readonly string AssetPath;

        public DiffWarning(string ruleName, string message, string assetPath = null)
        {
            RuleName = ruleName;
            Message = message;
            AssetPath = assetPath;
        }
    }

    /// <summary>Resources 폴더 에셋이 Addressables에도 등록된 경우.</summary>
    sealed class ResourcesFolderRule : IDiffRule
    {
        public string Name => "Resources Folder Conflict";

        public List<DiffWarning> Check(AddressableAssetSettings settings)
        {
            var warnings = new List<DiffWarning>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry.AssetPath.Contains("/Resources/"))
                    {
                        warnings.Add(new DiffWarning(Name,
                            "Resources 폴더 에셋이 Addressables에도 등록됨. " +
                            "런타임에서 이중 로드 가능.",
                            entry.AssetPath));
                    }
                }
            }
            return warnings;
        }
    }

    /// <summary>Build Settings와 Addressables에 동일 씬이 등록된 경우.</summary>
    sealed class SceneDuplicateRule : IDiffRule
    {
        public string Name => "Scene Duplicate Registration";

        public List<DiffWarning> Check(AddressableAssetSettings settings)
        {
            var warnings = new List<DiffWarning>();
            var buildScenes = new HashSet<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    buildScenes.Add(scene.path);
            }

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry.AssetPath.EndsWith(".unity")
                        && buildScenes.Contains(entry.AssetPath))
                    {
                        warnings.Add(new DiffWarning(Name,
                            "씬이 Build Settings와 Addressables에 동시 등록됨. " +
                            "빌드 시 중복 포함 가능.",
                            entry.AssetPath));
                    }
                }
            }
            return warnings;
        }
    }

    /// <summary>SpriteAtlas의 에디터/빌드 동작 차이 경고.</summary>
    sealed class SpriteAtlasRule : IDiffRule
    {
        public string Name => "SpriteAtlas Editor Behavior";

        public List<DiffWarning> Check(AddressableAssetSettings settings)
        {
            var warnings = new List<DiffWarning>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry.AssetPath.EndsWith(".spriteatlas"))
                    {
                        warnings.Add(new DiffWarning(Name,
                            "SpriteAtlas가 Addressables에 등록됨. " +
                            "에디터에서는 개별 스프라이트로 동작하지만 " +
                            "빌드에서는 아틀라스 전체가 로드됨.",
                            entry.AssetPath));
                    }
                }
            }
            return warnings;
        }
    }
}
#endif
