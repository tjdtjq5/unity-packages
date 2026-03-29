using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class ServerCacheHealthChecker
    {
        static string PREF_DOTNET_VERSION => SupaRunSettings.PREF + "Cache_DotnetVersion";
        static string PREF_CODE_HASH => SupaRunSettings.PREF + "Cache_CodeHash";
        static string PREF_LAST_DEPLOY_DATE => SupaRunSettings.PREF + "Cache_LastDeployDate";
        static string PREF_LAST_DEPLOY_SUCCESS => SupaRunSettings.PREF + "Cache_LastDeploySuccess";

        public enum Severity { Error, Warning, Info }

        public struct Alert
        {
            public Severity Level;
            public string Message;
            public string[] AffectedCaches;

            public Alert(Severity level, string message, params string[] affected)
            {
                Level = level;
                Message = message;
                AffectedCaches = affected ?? Array.Empty<string>();
            }
        }

        static Alert[] _cached;

        public static Alert[] GetAlerts()
        {
            if (_cached != null) return _cached;
            _cached = RunAllChecks();
            return _cached;
        }

        public static void Invalidate() => _cached = null;

        /// <summary>배포 성공 시 현재 상태를 스냅샷으로 저장.</summary>
        public static void SaveDeploySnapshot(List<GeneratedFile> files)
        {
            var dotnetMajor = PrerequisiteChecker.GetDotnetMajorVersion();
            if (dotnetMajor < 8) dotnetMajor = 8;
            EditorPrefs.SetString(PREF_DOTNET_VERSION, dotnetMajor.ToString());
            EditorPrefs.SetString(PREF_CODE_HASH, ComputeFilesHash(files));

            EditorPrefs.SetString(PREF_LAST_DEPLOY_DATE, DateTime.UtcNow.ToString("o"));
            EditorPrefs.SetBool(PREF_LAST_DEPLOY_SUCCESS, true);

            Invalidate();
        }

        /// <summary>배포 실패 시 기록.</summary>
        public static void MarkDeployFailed()
        {
            EditorPrefs.SetBool(PREF_LAST_DEPLOY_SUCCESS, false);
            Invalidate();
        }

        /// <summary>변경 감지: 현재 코드 해시와 마지막 배포 해시 비교.</summary>
        public static bool IsCodeChanged(List<GeneratedFile> files)
        {
            var savedHash = EditorPrefs.GetString(PREF_CODE_HASH, "");
            if (string.IsNullOrEmpty(savedHash)) return true;
            return ComputeFilesHash(files) != savedHash;
        }

        public static DateTime? LastDeployDate
        {
            get
            {
                var raw = EditorPrefs.GetString(PREF_LAST_DEPLOY_DATE, "");
                return DateTime.TryParse(raw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                    ? dt : null;
            }
        }

        // ── 검사 실행 ──

        static Alert[] RunAllChecks()
        {
            var alerts = new List<Alert>();

            CheckFirstDeploy(alerts);
            CheckPreviousDeployFailure(alerts);
            CheckDotnetVersionChange(alerts);
            CheckCacheExpiry(alerts);

            return alerts.ToArray();
        }

        static void CheckFirstDeploy(List<Alert> alerts)
        {
            if (!EditorPrefs.HasKey(PREF_LAST_DEPLOY_DATE))
                alerts.Add(new Alert(Severity.Info, "첫 배포 — 캐시 없이 전체 빌드 수행"));
        }

        static void CheckPreviousDeployFailure(List<Alert> alerts)
        {
            if (!EditorPrefs.HasKey(PREF_LAST_DEPLOY_SUCCESS)) return;
            if (!EditorPrefs.GetBool(PREF_LAST_DEPLOY_SUCCESS, true))
                alerts.Add(new Alert(Severity.Warning,
                    "이전 배포 실패 — Docker 캐시 오염 가능",
                    ServerCacheTypes.Docker));
        }

        static void CheckDotnetVersionChange(List<Alert> alerts)
        {
            var saved = EditorPrefs.GetString(PREF_DOTNET_VERSION, "");
            if (string.IsNullOrEmpty(saved)) return;

            var current = PrerequisiteChecker.GetDotnetMajorVersion();
            if (current < 8) current = 8;

            if (saved != current.ToString())
                alerts.Add(new Alert(Severity.Warning,
                    $".NET 버전 변경 ({saved} → {current})",
                    ServerCacheTypes.NuGet, ServerCacheTypes.Docker));
        }

        static void CheckCacheExpiry(List<Alert> alerts)
        {
            var lastDate = LastDeployDate;
            if (lastDate == null) return;
            var days = (DateTime.UtcNow - lastDate.Value).TotalDays;
            if (days > 7)
                alerts.Add(new Alert(Severity.Warning,
                    $"마지막 배포 {(int)days}일 전 — Cloud Build 캐시 만료 가능",
                    ServerCacheTypes.NuGet, ServerCacheTypes.Docker));
        }

        // ── 해시 유틸 ──

        static string ComputeFilesHash(List<GeneratedFile> files)
        {
            using var sha = SHA256.Create();
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                if (f.Path.StartsWith(".github")) continue; // workflow 제외
                sb.Append(f.Path);
                sb.Append(f.Content);
            }
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToBase64String(bytes);
        }

    }
}
