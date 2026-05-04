#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Util
{
    /// <summary>OS별 경로 헬퍼. Unity 라이선스/프로젝트 루트/셋업 상태 파일 위치를 결정.</summary>
    public static class PlatformPaths
    {
        /// <summary>OS별 Unity 라이선스(.ulf) 기본 경로. 알 수 없는 플랫폼이거나 HOME 미설정 Linux는 null.</summary>
        public static string UnityLicensePath
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "Unity", "Unity_lic.ulf");

                    case RuntimePlatform.OSXEditor:
                        return "/Library/Application Support/Unity/Unity_lic.ulf";

                    case RuntimePlatform.LinuxEditor:
                        var home = Environment.GetEnvironmentVariable("HOME");
                        return string.IsNullOrEmpty(home)
                            ? null
                            : Path.Combine(home, ".local/share/unity3d/Unity/Unity_lic.ulf");

                    default:
                        return null;
                }
            }
        }

        /// <summary>프로젝트 루트 (Assets의 부모 디렉토리).</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        /// <summary>Codemagic 셋업 상태 파일 — Library/codemagic-setup.json (gitignored).</summary>
        public static string SetupStateFile => Path.Combine(ProjectRoot, "Library", "codemagic-setup.json");

        /// <summary>프로젝트 루트 기준 keystore 파일 절대 경로.</summary>
        public static string KeystoreInProjectRoot(string keystoreName) =>
            Path.Combine(ProjectRoot, keystoreName);
    }
}
#endif
