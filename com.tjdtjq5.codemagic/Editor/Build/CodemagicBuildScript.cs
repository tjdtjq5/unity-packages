#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Build
{
    /// <summary>Codemagic CI/CD에서 호출하는 Unity 빌드 진입점.</summary>
    /// <remarks>
    /// codemagic.yaml의 BUILD_METHOD에서 호출:
    ///   Unity -batchmode -executeMethod Tjdtjq5.Codemagic.Editor.Build.CodemagicBuildScript.PerformAndroidBuild
    ///
    /// 패키지 asmdef로 컴파일되므로 GameCI의 cp 단계 불필요 — 항상 사용 가능.
    /// (기존 SurvivorsDuo의 _Project/_Editor/Codemagic/CodemagicBuildScript.cs 흡수)
    ///
    /// 환경변수 (yaml에서 -e로 전달):
    ///   - VERSION (예: "0.1.3") → PlayerSettings.bundleVersion
    ///   - ANDROID_VERSION_CODE → PlayerSettings.Android.bundleVersionCode
    ///   - ANDROID_KEYSTORE_BASE64 → 프로젝트 루트에 .keystore 파일로 디코드
    ///   - ANDROID_KEYSTORE_PASS / ANDROID_KEYALIAS_NAME / ANDROID_KEYALIAS_PASS → 서명 자격증명
    ///   - ANDROID_KEYSTORE_NAME (기본 "user.keystore") → 디코드된 keystore 파일명
    ///   - KEYSTORE_PATH → 직접 경로 fallback (로컬 Editor 사용 시)
    ///   - BUILD_PATH (기본 "build/Android") + BUILD_FILE (기본 "{name}.apk")
    /// </remarks>
    public static class CodemagicBuildScript
    {
        public static void PerformAndroidBuild()
        {
            try
            {
                ConfigureVersion();
                ConfigureAndroidSigning();
                var report = ExecuteBuild(BuildTarget.Android, ResolveBuildPath("apk"));
                Exit(report);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Codemagic] Build failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        public static void PerformIOSBuild()
        {
            try
            {
                ConfigureVersion();
                var report = ExecuteBuild(BuildTarget.iOS, ResolveBuildPath(""));
                Exit(report);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Codemagic] Build failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        // ── 설정 ──

        static void ConfigureVersion()
        {
            var version = Environment.GetEnvironmentVariable("VERSION");
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
                Debug.Log($"[Codemagic] bundleVersion = {version}");
            }

            var versionCode = Environment.GetEnvironmentVariable("ANDROID_VERSION_CODE");
            if (int.TryParse(versionCode, out int code))
            {
                PlayerSettings.Android.bundleVersionCode = code;
                Debug.Log($"[Codemagic] bundleVersionCode = {code}");
            }
        }

        static void ConfigureAndroidSigning()
        {
            // GameCI naming (yaml에서 전달)
            var keystoreBase64 = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_BASE64");
            var keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
            var keyAlias = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
            var keyPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
            var keystoreName = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_NAME") ?? "user.keystore";

            // 직접 경로 fallback (로컬 Editor 사용 시)
            var keystorePath = Environment.GetEnvironmentVariable("KEYSTORE_PATH");

            if (string.IsNullOrEmpty(keystoreBase64) && string.IsNullOrEmpty(keystorePath))
            {
                Debug.LogWarning("[Codemagic] ANDROID_KEYSTORE_BASE64/KEYSTORE_PATH 모두 미설정, 서명 스킵 (debug build).");
                return;
            }

            // base64 → 프로젝트 루트에 keystore 파일 작성
            if (!string.IsNullOrEmpty(keystoreBase64))
            {
                var bytes = Convert.FromBase64String(keystoreBase64);
                keystorePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), keystoreName);
                System.IO.File.WriteAllBytes(keystorePath, bytes);
                Debug.Log($"[Codemagic] keystore written: {keystorePath} ({bytes.Length} bytes)");
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyPass;
            Debug.Log($"[Codemagic] Signing configured: keystore={keystorePath}, alias={keyAlias}");
        }

        static string ResolveBuildPath(string extension)
        {
            // GameCI naming: BUILD_PATH is directory, BUILD_FILE is filename
            var buildPath = Environment.GetEnvironmentVariable("BUILD_PATH");
            var buildFile = Environment.GetEnvironmentVariable("BUILD_FILE");
            var buildName = Environment.GetEnvironmentVariable("BUILD_NAME") ?? "App";

            if (!string.IsNullOrEmpty(buildPath) && !string.IsNullOrEmpty(buildFile))
                return System.IO.Path.Combine(buildPath, buildFile);

            if (!string.IsNullOrEmpty(buildPath)) return buildPath;

            return string.IsNullOrEmpty(extension)
                ? "build/iOS"
                : $"build/Android/{buildName}.{extension}";
        }

        // ── 빌드 실행 ──

        static BuildReport ExecuteBuild(BuildTarget target, string outputPath)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("Build Settings에 활성 씬이 없습니다.");

            Debug.Log($"[Codemagic] Building {scenes.Length} scenes for {target} → {outputPath}");

            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            });
        }

        static void Exit(BuildReport report)
        {
            var summary = report.summary;
            Debug.Log($"[Codemagic] Build {summary.result}: {summary.totalSize} bytes, {summary.totalTime}");

            if (summary.result == BuildResult.Succeeded)
            {
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[Codemagic] Build failed with result: {summary.result}");
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
