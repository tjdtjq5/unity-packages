#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>빌드 전 git tag에서 버전을 자동으로 PlayerSettings에 설정</summary>
    public class VersionPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string version = GitVersionResolver.GetVersion();
            int buildCode = GitVersionResolver.ComputeBuildCode(version);

            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = buildCode;
            PlayerSettings.iOS.buildNumber = buildCode.ToString();

            Debug.Log($"[BuildAutomation] version={version}, buildCode={buildCode}");
        }
    }
}
#endif
