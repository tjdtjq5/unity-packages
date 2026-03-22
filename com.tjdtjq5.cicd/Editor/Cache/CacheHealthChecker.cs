#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>CI 빌드 캐시 상태 감지 + 클린 빌드 권장 판단</summary>
    public static class CacheHealthChecker
    {
        // ── EditorPrefs 키 ──
        const string PREF = "CacheHealth_";
        const string PREF_UNITY_VERSION = PREF + "UnityVersion";
        const string PREF_LAST_BUILD_DATE = PREF + "LastBuildDate";
        const string PREF_LAST_BUILD_TAG = PREF + "LastBuildTag";
        const string PREF_LAST_BUILD_SUCCESS = PREF + "LastBuildSuccess";
        const string PREF_PLATFORMS = PREF + "Platforms";
        const string PREF_KEYSTORE = PREF + "Keystore";

        // ── 데이터 구조 ──

        public enum Severity { Error, Warning, Info }

        public struct Alert
        {
            public Severity Level;
            public string Message;
            public bool RecommendCleanBuild;

            public Alert(Severity level, string message, bool recommendClean = false)
            {
                Level = level;
                Message = message;
                RecommendCleanBuild = recommendClean;
            }
        }

        // ── 캐시 ──

        static Alert[] _cached;
        static bool _loading;

        /// <summary>감지 결과 반환. 첫 호출 시 비동기 로드 시작.</summary>
        public static Alert[] GetAlerts()
        {
            if (_cached != null) return _cached;
            if (_loading) return Array.Empty<Alert>();

            _loading = true;

            // 메인 스레드에서만 접근 가능한 값들을 미리 캡처
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

        /// <summary>캐시 클리어 → 다음 GetAlerts 호출 시 재검사</summary>
        public static void Invalidate() => _cached = null;

        /// <summary>릴리즈 성공 후 현재 상태를 스냅샷으로 저장</summary>
        public static void SaveBuildSnapshot(string tag)
        {
            EditorPrefs.SetString(PREF_UNITY_VERSION, Application.unityVersion);
            EditorPrefs.SetString(PREF_LAST_BUILD_DATE, DateTime.UtcNow.ToString("o"));
            EditorPrefs.SetString(PREF_LAST_BUILD_TAG, tag.TrimStart('v'));
            EditorPrefs.SetBool(PREF_LAST_BUILD_SUCCESS, true);
            EditorPrefs.SetString(PREF_PLATFORMS, GetCurrentPlatformString());
            EditorPrefs.SetString(PREF_KEYSTORE, GetCurrentKeystoreString());
            Invalidate();
        }

        /// <summary>마지막 빌드 태그 반환 (없으면 null)</summary>
        public static string LastBuildTag =>
            EditorPrefs.HasKey(PREF_LAST_BUILD_TAG)
                ? EditorPrefs.GetString(PREF_LAST_BUILD_TAG) : null;

        /// <summary>마지막 빌드 날짜 반환 (없으면 null)</summary>
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
            CheckGitChanges(alerts);

            return alerts.ToArray();
        }

        // ── 개별 검사 ──

        /// <summary>#9 manifest에 file: 로컬 경로 (빌드 실패 확실)</summary>
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

        /// <summary>첫 빌드 여부</summary>
        static void CheckFirstBuild(List<Alert> alerts)
        {
            if (!EditorPrefs.HasKey(PREF_LAST_BUILD_TAG))
                alerts.Add(new Alert(Severity.Info,
                    "첫 빌드 — 캐시 없이 전체 빌드 수행"));
        }

        /// <summary>#1 이전 빌드 실패 → 캐시 오염 가능</summary>
        static void CheckPreviousBuildFailure(List<Alert> alerts, BuildTracker.HistoryEntry[] history)
        {
            if (history.Length == 0) return;

            if (history[0].Status != "success")
                alerts.Add(new Alert(Severity.Warning,
                    "이전 빌드 실패 — 캐시 오염 가능", true));
        }

        /// <summary>#2 Unity 버전 변경</summary>
        static void CheckUnityVersionChange(List<Alert> alerts, string currentUnityVersion)
        {
            var saved = EditorPrefs.GetString(PREF_UNITY_VERSION, "");
            if (string.IsNullOrEmpty(saved)) return;

            if (saved != currentUnityVersion)
                alerts.Add(new Alert(Severity.Warning,
                    $"Unity 버전 변경 ({saved} → {currentUnityVersion})", true));
        }

        /// <summary>#3 플랫폼 추가/변경</summary>
        static void CheckPlatformChange(List<Alert> alerts)
        {
            var saved = EditorPrefs.GetString(PREF_PLATFORMS, "");
            if (string.IsNullOrEmpty(saved)) return;

            var current = GetCurrentPlatformString();
            if (saved != current)
                alerts.Add(new Alert(Severity.Info,
                    $"빌드 플랫폼 변경 ({saved} → {current})"));
        }

        /// <summary>#4 캐시 만료 (7일 초과)</summary>
        static void CheckCacheExpiry(List<Alert> alerts)
        {
            var lastDate = LastBuildDate;
            if (lastDate == null) return;

            var days = (DateTime.UtcNow - lastDate.Value).TotalDays;
            if (days > 7)
                alerts.Add(new Alert(Severity.Warning,
                    $"마지막 빌드 {(int)days}일 전 — GitHub 캐시 만료됨", true));
        }

        /// <summary>#10 키스토어 설정 변경</summary>
        static void CheckKeystoreChange(List<Alert> alerts)
        {
            var saved = EditorPrefs.GetString(PREF_KEYSTORE, "");
            if (string.IsNullOrEmpty(saved)) return;

            var current = GetCurrentKeystoreString();
            if (saved != current)
                alerts.Add(new Alert(Severity.Info,
                    "Android 서명 설정 변경 — Secrets 업데이트 필요할 수 있음"));
        }

        /// <summary>#5,6,7,8 git diff 기반 변경 감지 (배치)</summary>
        static void CheckGitChanges(List<Alert> alerts)
        {
            var lastTag = EditorPrefs.GetString(PREF_LAST_BUILD_TAG, "");
            if (string.IsNullOrEmpty(lastTag)) return;

            // 태그 존재 확인
            var tagCheck = GitHelper.RunGit($"tag -l v{lastTag}");
            if (string.IsNullOrEmpty(tagCheck)) return;

            // 한 번의 git diff 로 변경 파일 목록
            var diff = GitHelper.RunGit($"diff v{lastTag} HEAD --name-only");
            if (string.IsNullOrEmpty(diff)) return;

            var files = diff.Split('\n');

            bool hasGraphics = false;
            bool hasManifest = false;
            bool hasProjectSettings = false;

            foreach (var file in files)
            {
                var f = file.Trim();
                if (string.IsNullOrEmpty(f)) continue;

                // #5 그래픽스/렌더 설정
                if (f.Contains("GraphicsSettings") || f.Contains("QualitySettings") ||
                    f.Contains("URPAsset") || f.Contains("HDRPAsset"))
                    hasGraphics = true;

                // #6 패키지 구성
                if (f == "Packages/manifest.json")
                    hasManifest = true;

                // #7, #8 프로젝트 설정 (상세 검사 필요)
                if (f == "ProjectSettings/ProjectSettings.asset")
                    hasProjectSettings = true;
            }

            if (hasGraphics)
                alerts.Add(new Alert(Severity.Warning,
                    "렌더링/품질 설정 변경 — 셰이더 캐시 무효화 가능", true));

            if (hasManifest)
                alerts.Add(new Alert(Severity.Warning,
                    "패키지 구성 변경 — Library 재빌드 필요 가능", true));

            // ProjectSettings.asset 상세 검사
            if (hasProjectSettings)
                CheckProjectSettingsDetail(alerts, lastTag);
        }

        /// <summary>ProjectSettings.asset 내용 변경 세분화</summary>
        static void CheckProjectSettingsDetail(List<Alert> alerts, string lastTag)
        {
            var contentDiff = GitHelper.RunGit(
                $"diff v{lastTag} HEAD -- ProjectSettings/ProjectSettings.asset");
            if (string.IsNullOrEmpty(contentDiff)) return;

            if (contentDiff.Contains("scriptingBackend"))
                alerts.Add(new Alert(Severity.Warning,
                    "스크립팅 백엔드 변경 (Mono↔IL2CPP) — 전체 재컴파일 필요", true));

            if (contentDiff.Contains("applicationIdentifier") ||
                contentDiff.Contains("bundleVersion") ||
                contentDiff.Contains("companyName") ||
                contentDiff.Contains("productName"))
                alerts.Add(new Alert(Severity.Info,
                    "앱 식별자/번들 버전 변경"));
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
    }
}
#endif
