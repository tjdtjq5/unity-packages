#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Manifest
{
    /// <summary>manifest.json의 패키지 참조를 file: ↔ git URL로 스왑.</summary>
    /// <remarks>
    /// /ft:pkg-dev 컨벤션과 호환 — dependencies 안에 "file:..." 경로,
    /// dependencies 바깥에 "_{shortname}_remote": "git URL" 백업 필드를 둠.
    /// 백업 필드가 있어야 자동 swap 가능.
    /// </remarks>
    public static class ManifestModeSwapper
    {
        public struct LocalPackage
        {
            public string PackageName;   // "com.tjdtjq5.cicd"
            public string ShortName;     // "cicd"
            public string LocalPath;     // "file:../../unity-packages/com.tjdtjq5.cicd"
            public string RemoteUrl;     // 백업 필드의 git URL (HasBackup일 때만 유효)
            public bool HasBackup;       // _shortname_remote 필드 존재 여부
        }

        static string ManifestPath => Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "Packages", "manifest.json");

        /// <summary>manifest.json에서 file: 경로 패키지를 모두 탐지.</summary>
        public static List<LocalPackage> DetectLocalPackages()
        {
            var result = new List<LocalPackage>();
            if (!File.Exists(ManifestPath)) return result;

            var content = File.ReadAllText(ManifestPath);
            var depRegex = new Regex("\"([\\w\\.\\-]+)\"\\s*:\\s*\"(file:[^\"]+)\"");
            foreach (Match m in depRegex.Matches(content))
            {
                var packageName = m.Groups[1].Value;
                var localPath = m.Groups[2].Value;
                var shortName = ExtractShortName(packageName);
                var (hasBackup, remoteUrl) = ReadBackup(content, shortName);

                result.Add(new LocalPackage
                {
                    PackageName = packageName,
                    ShortName = shortName,
                    LocalPath = localPath,
                    RemoteUrl = remoteUrl,
                    HasBackup = hasBackup,
                });
            }
            return result;
        }

        /// <summary>file: → git URL로 swap. 백업 필드는 그대로 보존.</summary>
        public static void SwapToRemote(IList<LocalPackage> packages)
        {
            if (packages == null || packages.Count == 0) return;
            var content = File.ReadAllText(ManifestPath);

            foreach (var p in packages)
            {
                if (!p.HasBackup) continue;
                content = content.Replace(
                    $"\"{p.PackageName}\": \"{p.LocalPath}\"",
                    $"\"{p.PackageName}\": \"{p.RemoteUrl}\"");
            }
            File.WriteAllText(ManifestPath, content);
        }

        /// <summary>git URL → file: 워킹트리만 복원. 커밋하지 않음.</summary>
        public static void SwapToLocal(IList<LocalPackage> packages)
        {
            if (packages == null || packages.Count == 0) return;
            var content = File.ReadAllText(ManifestPath);

            foreach (var p in packages)
            {
                if (!p.HasBackup) continue;
                content = content.Replace(
                    $"\"{p.PackageName}\": \"{p.RemoteUrl}\"",
                    $"\"{p.PackageName}\": \"{p.LocalPath}\"");
            }
            File.WriteAllText(ManifestPath, content);
        }

        static string ExtractShortName(string packageName)
        {
            var idx = packageName.LastIndexOf('.');
            return idx >= 0 ? packageName.Substring(idx + 1) : packageName;
        }

        static (bool, string) ReadBackup(string content, string shortName)
        {
            var key = $"_{shortName}_remote";
            var regex = new Regex($"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]+)\"");
            var match = regex.Match(content);
            return match.Success ? (true, match.Groups[1].Value) : (false, null);
        }
    }
}
#endif
