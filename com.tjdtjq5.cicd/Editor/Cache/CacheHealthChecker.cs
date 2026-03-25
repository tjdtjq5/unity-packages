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
        const string PREF_PLATFORMS = PREF + "Platforms";
        const string PREF_KEYSTORE = PREF + "Keystore";
        const string PREF_SCRIPTING_BACKEND = PREF + "ScriptingBackend";
        const string PREF_PACKAGE_NAME = PREF + "PackageName";
        const string PREF_ANDROID_FORMAT = PREF + "AndroidFormat";

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

        /// <summary>감지 결과 반환. 첫 호출 시 동기 실행 (EditorPrefs 메인 스레드 필수).</summary>
        public static Alert[] GetAlerts()
        {
            if (_cached != null) return _cached;

            var snapshot = new MainThreadSnapshot
            {
                unityVersion = Application.unityVersion,
                buildHistory = BuildTracker.GetHistory(1),
                packageName = PlayerSettings.applicationIdentifier,
                platforms = GetCurrentPlatformString(),
                keystore = GetCurrentKeystoreString(),
                scriptingBackend = GetCurrentScriptingBackend(),
                androidFormat = GetCurrentAndroidFormat(),
            };

            _cached = RunAllChecks(snapshot);
            return _cached;
        }

        struct MainThreadSnapshot
        {
            public string unityVersion;
            public BuildTracker.HistoryEntry[] buildHistory;
            public string packageName;
            public string platforms;
            public string keystore;
            public string scriptingBackend;
            public string androidFormat;
        }

        public static void Invalidate() => _cached = null;

        public static void SaveBuildSnapshot(string tag)
        {
            EditorPrefs.SetString(PREF_UNITY_VERSION, Application.unityVersion);
            EditorPrefs.SetString(PREF_LAST_BUILD_DATE, DateTime.UtcNow.ToString("o"));
            EditorPrefs.SetString(PREF_LAST_BUILD_TAG, tag.TrimStart('v'));
            EditorPrefs.SetString(PREF_PLATFORMS, GetCurrentPlatformString());
            EditorPrefs.SetString(PREF_KEYSTORE, GetCurrentKeystoreString());
            EditorPrefs.SetString(PREF_SCRIPTING_BACKEND, GetCurrentScriptingBackend());
            EditorPrefs.SetString(PREF_PACKAGE_NAME, PlayerSettings.applicationIdentifier);
            EditorPrefs.SetString(PREF_ANDROID_FORMAT, GetCurrentAndroidFormat());
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

        static Alert[] RunAllChecks(MainThreadSnapshot s)
        {
            EnsureBaselineSnapshot(s);

            var alerts = new List<Alert>();

            CheckManifestLocalPaths(alerts);
            CheckFirstBuild(alerts);
            CheckPreviousBuildFailure(alerts, s.buildHistory);
            CheckUnityVersionChange(alerts, s.unityVersion);
            CheckPlatformChange(alerts, s.platforms);
            CheckCacheExpiry(alerts);
            CheckKeystoreChange(alerts, s.keystore);
            CheckScriptingBackendChange(alerts, s.scriptingBackend);
            CheckPackageNameChange(alerts, s.packageName);
            CheckGradleConfigChange(alerts);
            CheckAndroidFormatChange(alerts, s.androidFormat);
            CheckUnusedCaches(alerts, s);
            CheckGitChanges(alerts);

            return alerts.ToArray();
        }

        static void EnsureBaselineSnapshot(MainThreadSnapshot s)
        {
            bool isNew = !EditorPrefs.HasKey(PREF_UNITY_VERSION);

            // 완전 새 스냅샷
            if (isNew)
            {
                EditorPrefs.SetString(PREF_UNITY_VERSION, s.unityVersion);
                EditorPrefs.SetString(PREF_PLATFORMS, s.platforms);
                EditorPrefs.SetString(PREF_KEYSTORE, s.keystore);
                EditorPrefs.SetString(PREF_SCRIPTING_BACKEND, s.scriptingBackend);
                EditorPrefs.SetString(PREF_PACKAGE_NAME, s.packageName);
                EditorPrefs.SetString(PREF_ANDROID_FORMAT, s.androidFormat);
                return;
            }

            // 기존 스냅샷에 누락된 키만 보충 (버전 업그레이드 시)
            if (!EditorPrefs.HasKey(PREF_PACKAGE_NAME))
                EditorPrefs.SetString(PREF_PACKAGE_NAME, s.packageName);
            if (!EditorPrefs.HasKey(PREF_SCRIPTING_BACKEND))
                EditorPrefs.SetString(PREF_SCRIPTING_BACKEND, s.scriptingBackend);
            if (!EditorPrefs.HasKey(PREF_ANDROID_FORMAT))
                EditorPrefs.SetString(PREF_ANDROID_FORMAT, s.androidFormat);
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
                    CacheTypes.Library, CacheTypes.IL2CPP, CacheTypes.DockerImage));
        }

        static void CheckPlatformChange(List<Alert> alerts, string currentPlatforms)
        {
            var saved = EditorPrefs.GetString(PREF_PLATFORMS, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != currentPlatforms)
                alerts.Add(new Alert(Severity.Warning,
                    $"빌드 플랫폼 변경 ({saved} → {currentPlatforms})",
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
                    CacheTypes.Library, CacheTypes.Gradle, CacheTypes.IL2CPP, CacheTypes.DockerImage));
        }

        static void CheckKeystoreChange(List<Alert> alerts, string currentKeystore)
        {
            var saved = EditorPrefs.GetString(PREF_KEYSTORE, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != currentKeystore)
                alerts.Add(new Alert(Severity.Info,
                    "Android 서명 설정 변경 — Gradle 캐시 영향 가능",
                    CacheTypes.Gradle));
        }

        // ── 신규 검사 ──

        static void CheckScriptingBackendChange(List<Alert> alerts, string currentBackend)
        {
            var saved = EditorPrefs.GetString(PREF_SCRIPTING_BACKEND, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != currentBackend)
                alerts.Add(new Alert(Severity.Warning,
                    $"스크립팅 백엔드 변경 ({saved} → {currentBackend}) — 전체 재컴파일 필요",
                    CacheTypes.Library, CacheTypes.IL2CPP));
        }

        /// <summary>패키지명(Bundle ID) 변경 — Gradle 캐시 무효화 필요.</summary>
        static void CheckPackageNameChange(List<Alert> alerts, string currentPackageName)
        {
            var saved = EditorPrefs.GetString(PREF_PACKAGE_NAME, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != currentPackageName)
                alerts.Add(new Alert(Severity.Warning,
                    $"패키지명 변경 ({saved} → {currentPackageName})",
                    CacheTypes.Gradle, CacheTypes.Library));
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

        /// <summary>APK ↔ AAB 전환 시 Gradle 캐시 무효화 필요.</summary>
        static void CheckAndroidFormatChange(List<Alert> alerts, string currentFormat)
        {
            var saved = EditorPrefs.GetString(PREF_ANDROID_FORMAT, "");
            if (string.IsNullOrEmpty(saved)) return;
            if (saved != currentFormat)
                alerts.Add(new Alert(Severity.Warning,
                    $"Android 빌드 포맷 변경 ({saved} → {currentFormat})",
                    CacheTypes.Gradle));
        }

        /// <summary>활성화된 캐시가 현재 설정과 맞지 않는 경우 안내.</summary>
        static void CheckUnusedCaches(List<Alert> alerts, MainThreadSnapshot snap)
        {
            var s = BuildAutomationSettings.Instance;

            // Android 비활성인데 Gradle 캐시 있음
            if (!s.enableAndroid && s.HasCache(CacheTypes.Gradle))
                alerts.Add(new Alert(Severity.Info,
                    "Android 미사용 — Gradle 캐시 불필요",
                    CacheTypes.Gradle));

            // Mono 백엔드인데 IL2CPP 캐시 있음
            if (snap.scriptingBackend == "Mono" && s.HasCache(CacheTypes.IL2CPP))
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

        static string GetCurrentAndroidFormat()
        {
            var s = BuildAutomationSettings.Instance;
            return s.androidBuildFormat.ToString();
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
