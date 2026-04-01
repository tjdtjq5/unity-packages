#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor.Update
{
    /// <summary>version_route.json을 관리한다. 앱 버전별 카탈로그 매핑.</summary>
    public static class VersionRouteManager
    {
        const string DefaultFileName = "version_route.json";

        /// <summary>버전 라우트 파일을 로드한다.</summary>
        public static VersionRoute Load(string filePath)
        {
            if (!File.Exists(filePath))
                return new VersionRoute();

            var json = File.ReadAllText(filePath);
            return JsonUtility.FromJson<VersionRoute>(json) ?? new VersionRoute();
        }

        /// <summary>버전 라우트 파일을 저장한다.</summary>
        public static void Save(VersionRoute route, string filePath)
        {
            var json = JsonUtility.ToJson(route, true);
            File.WriteAllText(filePath, json);
            AddrXLog.Info("VersionRoute", $"저장 완료: {filePath}");
        }

        /// <summary>앱 버전에 해당하는 카탈로그 파일명을 반환한다.</summary>
        public static string GetCatalogForVersion(VersionRoute route, string appVersion)
        {
            foreach (var entry in route.routes)
            {
                if (entry.appVersion == appVersion)
                    return entry.catalogFile;
            }
            return null;
        }

        /// <summary>최소 버전 미만인 라우트 엔트리 목록을 반환한다.</summary>
        public static List<RouteEntry> GetCleanableEntries(VersionRoute route)
        {
            if (string.IsNullOrEmpty(route.minimum))
                return new List<RouteEntry>();

            var result = new List<RouteEntry>();
            foreach (var entry in route.routes)
            {
                if (CompareVersions(entry.appVersion, route.minimum) < 0)
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>사용 중인 고유 카탈로그 파일 수를 반환한다.</summary>
        public static int GetUniqueCatalogCount(VersionRoute route)
        {
            var set = new HashSet<string>();
            foreach (var entry in route.routes)
                set.Add(entry.catalogFile);
            return set.Count;
        }

        /// <summary>시맨틱 버전 비교. a < b → 음수, a == b → 0, a > b → 양수.</summary>
        static int CompareVersions(string a, string b)
        {
            var aParts = a.Split('.');
            var bParts = b.Split('.');
            int len = Math.Max(aParts.Length, bParts.Length);

            for (int i = 0; i < len; i++)
            {
                int av = ParseVersionPart(aParts, i, a);
                int bv = ParseVersionPart(bParts, i, b);
                if (av != bv) return av - bv;
            }
            return 0;
        }

        static int ParseVersionPart(string[] parts, int index, string fullVersion)
        {
            if (index >= parts.Length) return 0;
            if (int.TryParse(parts[index], out var value)) return value;
            AddrXLog.Warning("VersionRoute",
                $"버전 '{fullVersion}'의 파트 '{parts[index]}'가 숫자가 아닙니다. 0으로 처리됨.");
            return 0;
        }
    }

    [Serializable]
    public class VersionRoute
    {
        public string minimum = "";
        public List<RouteEntry> routes = new();
    }

    [Serializable]
    public class RouteEntry
    {
        public string appVersion;
        public string catalogFile;
    }
}
#endif
