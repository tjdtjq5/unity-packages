#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>빌드 전 git tag에서 버전을 자동으로 PlayerSettings에 설정 (로컬 빌드 전용)</summary>
    public class VersionPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // CI 환경에서는 GameCI가 버전을 처리하므로 스킵
            // (CI에서 git describe가 "dubious ownership" 에러를 반환하는 문제 방지)
            if (IsCIEnvironment())
            {
                Debug.Log("[CICD] CI 환경 감지 — VersionPreprocessor 스킵 (GameCI가 버전 처리)");
                return;
            }

            string version = GitVersionResolver.GetVersion();
            int buildCode = GitVersionResolver.ComputeBuildCode(version);

            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = buildCode;
            PlayerSettings.iOS.buildNumber = buildCode.ToString();

            Debug.Log($"[CICD] version={version}, buildCode={buildCode}");
        }

        static bool IsCIEnvironment()
        {
            // GitHub Actions, GitLab CI, Jenkins 등 주요 CI 환경 감지
            return !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
                || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("UNITY_SERIAL"));
        }
    }
}
#endif
