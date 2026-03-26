using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>Feature 설치/제거.</summary>
    public static class FeatureInstaller
    {
        /// <summary>Feature 설치. 의존성도 함께 설치. 마지막에 Refresh 1회.</summary>
        /// <returns>설치된 Feature id 목록.</returns>
        public static List<string> Install(FeatureInfo feature)
        {
            var installed = new List<string>();
            InstallInternal(feature, installed);

            if (installed.Count > 0)
                AssetDatabase.Refresh();

            return installed;
        }

        /// <summary>커스텀 Feature 생성. 빈 폴더 + feature.json.</summary>
        public static string CreateCustom(string id, string displayName)
        {
            var folderName = ToPascalCase(id);
            var destDir = Path.Combine(FeatureRegistry.InstallRoot, folderName);

            if (Directory.Exists(destDir))
            {
                Debug.LogWarning($"[SupaRun:Features] '{id}' 폴더가 이미 존재합니다.");
                return null;
            }

            Directory.CreateDirectory(destDir);

            var info = new FeatureInfo
            {
                name = displayName,
                id = id,
                description = "",
                tier = 99,
                dependencies = System.Array.Empty<string>()
            };

            var json = JsonConvert.SerializeObject(info, Formatting.Indented);
            File.WriteAllText(Path.Combine(destDir, "feature.json"), json);

            Debug.Log($"[SupaRun:Features] 커스텀 Feature '{displayName}' 생성 → {destDir}");
            AssetDatabase.Refresh();
            return destDir;
        }

        /// <summary>Feature 제거. 폴더 삭제.</summary>
        public static bool Uninstall(FeatureInfo feature)
        {
            if (!feature.isInstalled || string.IsNullOrEmpty(feature.installPath))
                return false;

            // 다른 Feature가 이것에 의존하는지 확인
            var dependents = FeatureRegistry.GetInstalled()
                .Where(f => f.dependencies != null && f.dependencies.Contains(feature.id))
                .ToList();

            if (dependents.Count > 0)
            {
                var names = string.Join(", ", dependents.Select(f => f.name));
                Debug.LogWarning($"[SupaRun:Features] '{feature.name}'을 사용하는 Feature가 있습니다: {names}");
                return false;
            }

            // 폴더 삭제
            if (Directory.Exists(feature.installPath))
            {
                AssetDatabase.DeleteAsset(feature.installPath);
                Debug.Log($"[SupaRun:Features] '{feature.name}' 제거 완료");
            }

            AssetDatabase.Refresh();
            return true;
        }

        // ── 내부 ──

        static void InstallInternal(FeatureInfo feature, List<string> installed)
        {
            // 이미 설치됨
            if (feature.isInstalled) return;

            // 의존성 먼저 설치
            if (feature.dependencies != null)
            {
                var allFeatures = FeatureRegistry.GetAll();
                foreach (var depId in feature.dependencies)
                {
                    var dep = allFeatures.FirstOrDefault(f => f.id == depId);
                    if (dep == null)
                    {
                        Debug.LogWarning($"[SupaRun:Features] 의존성 '{depId}'를 찾을 수 없습니다.");
                        continue;
                    }
                    if (dep.isInstalled) continue;

                    InstallInternal(dep, installed);
                }
            }

            // 템플릿 복사
            if (string.IsNullOrEmpty(feature.sourcePath))
            {
                Debug.LogWarning($"[SupaRun:Features] '{feature.name}' 템플릿 경로가 없습니다.");
                return;
            }

            var folderName = ToPascalCase(feature.id);
            var destDir = Path.Combine(FeatureRegistry.InstallRoot, folderName);
            CopyDirectory(feature.sourcePath, destDir);

            installed.Add(feature.id);
            Debug.Log($"[SupaRun:Features] '{feature.name}' 설치 완료 → {destDir}");
        }

        /// <summary>"currency" → "Currency", "daily-mission" → "DailyMission"</summary>
        static string ToPascalCase(string id)
        {
            var parts = id.Split('-', '_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join("", parts);
        }

        static void CopyDirectory(string source, string dest)
        {
            if (!Directory.Exists(dest))
                Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".meta")) continue;

                var destPath = Path.Combine(dest, fileName);

                // Service 파일은 에디터 전용 래핑 → 빌드에서 컴파일 제외
                if (IsServerOnlyFile(fileName))
                {
                    var content = File.ReadAllText(file);
                    File.WriteAllText(destPath,
                        "#if UNITY_EDITOR\n" + content + "\n#endif // UNITY_EDITOR\n");
                }
                else
                {
                    File.Copy(file, destPath, true);
                }
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var dirName = Path.GetFileName(dir);
                CopyDirectory(dir, Path.Combine(dest, dirName));
            }
        }

        /// <summary>서버 전용 파일 판별. Service 클래스는 빌드에 포함하지 않음.</summary>
        static bool IsServerOnlyFile(string fileName)
        {
            return fileName.EndsWith("Service.cs");
        }
    }
}
