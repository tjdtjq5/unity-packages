using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    /// <summary>사용 가능/설치된 Feature를 스캔.</summary>
    public static class FeatureRegistry
    {
        const string INSTALL_ROOT = "Assets/GameServer/Features";

        /// <summary>모든 Feature 목록 (템플릿 + 설치됨 + 커스텀).</summary>
        public static List<FeatureInfo> GetAll()
        {
            var map = new Dictionary<string, FeatureInfo>();

            // 1. 패키지 템플릿 스캔
            var templateRoot = GetTemplateRoot();
            if (Directory.Exists(templateRoot))
            {
                foreach (var dir in Directory.GetDirectories(templateRoot))
                {
                    var info = LoadFeatureJson(dir);
                    if (info == null) continue;
                    info.sourcePath = dir;
                    map[info.id] = info;
                }
            }

            // 2. 설치된 Feature 스캔
            if (Directory.Exists(INSTALL_ROOT))
            {
                foreach (var dir in Directory.GetDirectories(INSTALL_ROOT))
                {
                    var info = LoadFeatureJson(dir);
                    if (info == null) continue;

                    info.installPath = dir;
                    info.isInstalled = true;

                    if (map.TryGetValue(info.id, out var existing))
                    {
                        // 템플릿에도 있는 Feature → 설치 상태 업데이트
                        existing.installPath = dir;
                        existing.isInstalled = true;
                    }
                    else
                    {
                        // 템플릿에 없는 Feature → 커스텀
                        info.isCustom = true;
                        map[info.id] = info;
                    }
                }
            }

            return map.Values
                .OrderBy(f => f.tier)
                .ThenBy(f => f.name)
                .ToList();
        }

        /// <summary>설치된 Feature만.</summary>
        public static List<FeatureInfo> GetInstalled()
        {
            return GetAll().Where(f => f.isInstalled).ToList();
        }

        /// <summary>설치 가능한 Feature만 (미설치 템플릿).</summary>
        public static List<FeatureInfo> GetAvailable()
        {
            return GetAll().Where(f => !f.isInstalled && !f.isCustom).ToList();
        }

        /// <summary>의존성이 모두 설치되어 있는지 확인.</summary>
        public static (bool ok, string[] missing) CheckDependencies(FeatureInfo feature)
        {
            if (feature.dependencies == null || feature.dependencies.Length == 0)
                return (true, System.Array.Empty<string>());

            var installed = GetInstalled().Select(f => f.id).ToHashSet();
            var missing = feature.dependencies.Where(dep => !installed.Contains(dep)).ToArray();
            return (missing.Length == 0, missing);
        }

        /// <summary>설치 루트 경로.</summary>
        public static string InstallRoot => INSTALL_ROOT;

        // ── 내부 ──

        static FeatureInfo LoadFeatureJson(string dirPath)
        {
            var jsonPath = Path.Combine(dirPath, "feature.json");
            if (!File.Exists(jsonPath)) return null;

            try
            {
                var json = File.ReadAllText(jsonPath);
                return JsonConvert.DeserializeObject<FeatureInfo>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GameServer:Features] feature.json 파싱 실패: {jsonPath}\n{ex.Message}");
                return null;
            }
        }

        static string GetTemplateRoot()
        {
            // 패키지 경로에서 Features~ 폴더 찾기
            var packagePath = Path.GetFullPath("Packages/com.tjdtjq5.gameserver");
            return Path.Combine(packagePath, "Features~");
        }
    }
}
