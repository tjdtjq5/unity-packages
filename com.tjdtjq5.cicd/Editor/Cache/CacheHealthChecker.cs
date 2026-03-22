#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>CI 빌드 캐시 상태 감지 + 캐시별 해제 권장 판단.</summary>
    public static class CacheHealthChecker
    {
        const string PREF = "CacheHealth_";
        const string PREF_UNITY_VERSION = PREF + "UnityVersion";
        const string PREF_LAST_BUILD_DATE = PREF + "LastBuildDate";
        const string PREF_LAST_BUILD_TAG = PREF + "LastBuildTag";
        const string PREF_LAST_BUILD_SUCCESS = PREF + "LastBuildSuccess";
        const string PREF_PLATFORMS = PREF + "Platforms";
        const string PREF_KEYSTORE = PREF + "Keystore";
        const string PREF_SCRIPTING_BACKEND = PREF + "ScriptingBackend";

        public enum Severity { Error, Warning, Info }

        public struct Alert
        {
            public Severity Level;
            public string Message;
            /// <summary>해제를 권장하는 캐시 ID 목록. 빈 배열이면 특정 캐시와 무관.</summary>
            public string[] AffectedCaches;

            public Alert(Severity level, string message, params string[] affected)
            {
                Level = level;
                Message = message;
                AffectedCaches = affected ?? Array.Empty<string>();
            }
        }

        static Alert[] _cached;
        static bool _loading;

        /// <summary>감지 결과 반환. 첫 호출 시 비동기 로드.</summary>
        public static Alert[] GetAlerts()
        {
            if (_cached != null) return _cached;
            if (_loading) return Array.Empty<Alert>();

            _loading = true;
            var unityVersion = Application.unityVersion;
            var buildHistory = BuildTracker.GetHistory(1);

            System.Threading.Tasks.Task.Run(() =>
            {
                var alerts = RunAllChecks(unityVersion, buildHistory);
                EditorApplication.delayCall += () =>
                {
                    _cached = alerts;
                    _loading = false;
                };
            });

            return Array.Empty<Alert>();
        }

        public static void Invalidate() => _cached = null;

        public static void SaveBuildSnapshot(string tag)
        {
            EditorPrefs.SetString(PREF_UNITY_VERSION, Application.unityVersion);
            EditorPrefs.SetString(PREF_LAST_BUILD_DATE, DateTime.UtcNow.ToString("o"));
            EditorPrefs.SetString(PREF_LAST_BUILD_TAG, tag.TrimStart('v'));
            EditorPrefs.SetBool(PREF_LAST_BUILD_SUCCESS, true);
            EditorPrefs.SetString(PREF_PLATFORMS, GetCurrentPlatformString());
            EditorPrefs.SetString(PREF_KEYSTORE, GetCurrentKeystoreString());
            EditorPrefs.SetString(PREF_SCRIPTING_BACKEND, GetCurrentScriptingBackend());
            Invalidate();
        }

        public static string LastBuildTag =>
            EditorPrefs.HasKey(PREF_LAST_BUILD_TAG)
                ? EditorPrefs.GetString(PREF_LAST_BUILD_TAG) : null;

        public static DateTime? LastBuildDate
        {
            get
            {
                var raw = EditorPrefs.GetString(PREF_LAST_BUILD_DATE, "");
                return DateTime.TryParse(raw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                    ? dt : null;
            }
        }

        // ── 전체 검사 ──

        static Alert[] RunAllChecks(string unityVersion, BuildTracker.HistoryEntry[] buildHistory)
        {
            var alerts = new List<Alert>();

            CheckManifestLocalPaths(alerts);
            CheckFirstBuild(alerts);
            CheckPreviousBuildFailure(alerts, buildHistory);
            CheckUnityVersionChange(alerts, unityVersion);
            CheckPlatformChange(alerts);
            CheckCacheExpiry(alerts);
            CheckKeystoreChange(alerts);
            CheckScriptingBackendChange(alerts);
            CheckGradleConfigChange(alerts);
            CheckUnusedCaches(alerts);
            CheckGitChanges(alerts);

            return alerts.ToArray();
        }

        // ── 기존 검사 (영향 캐시 명시) ──

        static void CheckManifestLocalPaths(List<Alert> alerts)
        {
            var repoRoot = GitHelper.GetRepoRoot();
            if (string.IsNullOrEmpty(repoRoot)) return;

            var manifestPath = Path.Combine(repoRoot, "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;

            var content = File.ReadAllText(manifestPath);
            if (content.Contains("\"file:"))
                alerts.Add(new Alert(Severity.Error,
                    "manifest.json에 로컬 경로(file:) 포함 — CI 빌드 실패 확실"));
        }

        static void CheckFirstBuild(List<Alert> alerts)
        {
            if (!EditorPrefs.HasKey(PREF_LAST_BUILD_TAG))
                alerts.Add(new Alert(Severity.Info, "첫 빌드 — 캐시 없이 전체 빌드 수행"));
        }

        static void CheckPreviousBuildFailure(List<Alert> alerts, BuildTracker.HistoryEntry[] history)
        {
            if (history.Length == 0) return;
            if (history[0].Status != "success")
                alerts.Add(new Alert(Severity.Warning,
                    "이전 빌드 실패 — 빌드 캐시 오염 가능",
                    CacheTypes.Library, CacheTypes.Gradle, CacheTypes.IL2CPP));
        }

        static void CheckUnityVersionChange(List<Alert> alerts, string currentVersion)
        {
            var saved = EditorPrefs.GetString(PREF_UNITY_VERSION, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != currentVersion)
                alerts.Add(new Alert(Severity.Warning,
                    $"Unity 버전 변경 ({saved} → {currentVersion})",
                    CacheTypes.Library, CacheTypes.IL2CPP, CacheTypes.Docker));
        }

        static void CheckPlatformChange(List<Alert> alerts)
        {
            var saved = EditorPrefs.GetString(PREF_PLATFORMS, "");
            if (string.IsNullOrEmpty(saved)) return;
            var current = GetCurrentPlatformString();
            if (saved != current)
                alerts.Add(new Alert(Severity.Warning,
                    $"빌드 플랫폼 변경 ({saved} → {current})",
                    CacheTypes.Library, CacheTypes.Gradle, CacheTypes.IL2CPP));
        }

        static void CheckCacheExpiry(List<Alert> alerts)
        {
            var lastDate = LastBuildDate;
            if (lastDate == null) return;
            var days = (DateTime.UtcNow - lastDate.Value).TotalDays;
            if (days > 7)
                alerts.Add(new Alert(Severity.Warning,
                    $"마지막 빌드 {(int)days}일 전 — GitHub 캐시 만료됨",
                    CacheTypes.Library, CacheTypes.Gradle, CacheTypes.IL2CPP));
        }

        static void CheckKeystoreChange(List<Alert> alerts)
        {
            var saved = EditorPrefs.GetString(PREF_KEYSTORE, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != GetCurrentKeystoreString())
                alerts.Add(new Alert(Severity.Info,
                    "Android 서명 설정 변경 — Gradle 캐시 영향 가능",
                    CacheTypes.Gradle));
        }

        // ── 신규 검사 ──

        static void CheckScriptingBackendChange(List<Alert> alerts)
        {
            var saved = EditorPrefs.GetString(PREF_SCRIPTING_BACKEND, "");
            if (string.IsNullOrEmpty(saved)) return;
            var current = GetCurrentScriptingBackend();
            if (saved != current)
                alerts.Add(new Alert(Severity.Warning,
                    $"스크립팅 백엔드 변경 ({saved} → {current}) — 전체 재컴파일 필요",
                    CacheTypes.Library, CacheTypes.IL2CPP));
        }

        static void CheckGradleConfigChange(List<Alert> alerts)
        {
            var lastTag = EditorPrefs.GetString(PREF_LAST_BUILD_TAG, "");
            if (string.IsNullOrEmpty(lastTag)) return;

            var tagCheck = GitHelper.RunGit($"tag -l v{lastTag}");
            if (string.IsNullOrEmpty(tagCheck)) return;

            var diff = GitHelper.RunGit($"diff v{lastTag} HEAD --name-only");
            if (string.IsNullOrEmpty(diff)) return;

            var files = diff.Split('\n');
            foreach (var f in files)
            {
                var file = f.Trim();
                if (file.Contains("gradle-wrapper.properties") ||
                    file.Contains("build.gradle") ||
                    file.Contains("settingsTemplate.gradle") ||
                    file.Contains("mainTemplate.gradle") ||
                    file.Contains("gradleTemplate.properties"))
                {
                    alerts.Add(new Alert(Severity.Warning,
                        "Gradle 설정 파일 변경",
                        CacheTypes.Gradle));
                    break;
                }
            }
        }

        /// <summary>활성화된 캐시가 현재 설정과 맞지 않는 경우 안내.</summary>
        static void CheckUnusedCaches(List<Alert> alerts)
        {
            var s = BuildAutomationSettings.Instance;

            // Android 비활성인데 Gradle 캐시 있음
            if (!s.enableAndroid && s.HasCache(CacheTypes.Gradle))
                alerts.Add(new Alert(Severity.Info,
                    "Android 미사용 — Gradle 캐시 불필요",
                    CacheTypes.Gradle));

            // Mono 백엔드인데 IL2CPP 캐시 있음
            if (GetCurrentScriptingBackend() == "Mono" && s.HasCache(CacheTypes.IL2CPP))
                alerts.Add(new Alert(Severity.Info,
                    "Mono 백엔드 — IL2CPP 캐시 불필요",
                    CacheTypes.IL2CPP));
        }

        static void CheckGitChanges(List<Alert> alerts)
        {
            var lastTag = EditorPrefs.GetString(PREF_LAST_BUILD_TAG, "");
            if (string.IsNullOrEmpty(lastTag)) return;

            var tagCheck = GitHelper.RunGit($"tag -l v{lastTag}");
            if (string.IsNullOrEmpty(tagCheck)) return;

            var diff = GitHelper.RunGit($"diff v{lastTag} HEAD --name-only");
            if (string.IsNullOrEmpty(diff)) return;

            var files = diff.Split('\n');
            bool hasGraphics = false, hasManifest = false;

            foreach (var f in files)
            {
                var file = f.Trim();
                if (string.IsNullOrEmpty(file)) continue;

                if (file.Contains("GraphicsSettings") || file.Contains("QualitySettings") ||
                    file.Contains("URPAsset") || file.Contains("HDRPAsset"))
                    hasGraphics = true;

                if (file == "Packages/manifest.json")
                    hasManifest = true;
            }

            if (hasGraphics)
                alerts.Add(new Alert(Severity.Warning,
                    "렌더링/품질 설정 변경 — 셰이더 캐시 무효화 가능",
                    CacheTypes.Library));

            if (hasManifest)
                alerts.Add(new Alert(Severity.Warning,
                    "패키지 구성 변경 — Library 재빌드 필요 가능",
                    CacheTypes.Library));
        }

        // ── 헬퍼 ──

        static string GetCurrentPlatformString()
        {
            var s = BuildAutomationSettings.Instance;
            var parts = new List<string>();
            if (s.enableAndroid) parts.Add("Android");
            if (s.enableIOS) parts.Add("iOS");
            if (s.enableWindows) parts.Add("Windows");
            if (s.enableWebGL) parts.Add("WebGL");
            return string.Join(",", parts);
        }

        static string GetCurrentKeystoreString()
        {
            var s = BuildAutomationSettings.Instance;
            return $"{s.keystorePath}|{s.keyAlias}";
        }

        static string GetCurrentScriptingBackend()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var backend = PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group));
            return backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono";
        }
    }
}
#endif
