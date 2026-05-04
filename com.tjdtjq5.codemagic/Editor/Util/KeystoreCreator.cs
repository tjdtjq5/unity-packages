#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Codemagic.Editor.Util
{
    /// <summary>keytool로 Android Keystore 생성. PlayerSettings 자동 적용 + .gitignore 자동 추가.</summary>
    /// <remarks>
    /// 비번/alias 검증과 다이얼로그 UI는 caller가 처리. 이 클래스는 keytool 실행에 집중.
    /// </remarks>
    public static class KeystoreCreator
    {
        /// <summary>keystore 생성. 사용자가 SaveFilePanel에서 취소하면 (false, null, null) 반환.</summary>
        /// <param name="alias">Key alias.</param>
        /// <param name="keystorePass">Keystore 비밀번호.</param>
        /// <param name="keyPass">Key 비밀번호.</param>
        /// <param name="saveDirHint">SaveFilePanel 기본 디렉토리. null이면 프로젝트 루트.</param>
        /// <returns>(success, savePath, error). 취소 시 (false, null, null).</returns>
        public static (bool success, string savePath, string error) Create(
            string alias, string keystorePass, string keyPass, string saveDirHint = null)
        {
            // 저장 경로 선택
            var defaultDir = string.IsNullOrEmpty(saveDirHint) ? PlatformPaths.ProjectRoot : saveDirHint;
            var savePath = EditorUtility.SaveFilePanel(
                "Keystore 저장 위치", defaultDir, "user", "keystore");
            if (string.IsNullOrEmpty(savePath))
                return (false, null, null);

            // keytool 경로 찾기
            var keytoolPath = FindKeytool();
            if (keytoolPath == null)
                return (false, null, "keytool을 찾을 수 없습니다. JDK가 설치되어 있는지 확인하세요.");

            // keytool 실행
            var args = $"-genkeypair -v" +
                $" -keystore \"{savePath}\"" +
                $" -alias \"{alias}\"" +
                $" -keyalg RSA -keysize 2048 -validity 10000" +
                $" -storepass \"{keystorePass}\"" +
                $" -keypass \"{keyPass}\"" +
                $" -dname \"CN=Developer, C=KR\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = keytoolPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p!.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);

                if (p.ExitCode == 0 && File.Exists(savePath))
                {
                    // PlayerSettings 자동 적용
                    PlayerSettings.Android.keystoreName = savePath;
                    PlayerSettings.Android.keystorePass = keystorePass;
                    PlayerSettings.Android.keyaliasName = alias;
                    PlayerSettings.Android.keyaliasPass = keyPass;

                    // .gitignore에 *.keystore 자동 추가
                    EnsureGitignoreKeystore(PlatformPaths.ProjectRoot);

                    Debug.Log($"[Codemagic] Keystore 생성 완료: {savePath}");
                    return (true, savePath, null);
                }

                Debug.LogError($"[Codemagic] Keystore 생성 실패: {stderr}");
                return (false, null, string.IsNullOrEmpty(stderr) ? "keytool 실행 실패 (exit code != 0)" : stderr);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Codemagic] keytool 실행 실패: {ex.Message}");
                return (false, null, ex.Message);
            }
        }

        /// <summary>keytool 실행 파일 탐색. Unity 내장 → AndroidExternalToolsSettings → JAVA_HOME 순.</summary>
        public static string FindKeytool()
        {
            // 1. Unity 에디터 경로 기반 (가장 확실)
            // Unity.exe → Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool
            var editorPath = EditorApplication.applicationPath;
            if (!string.IsNullOrEmpty(editorPath))
            {
                var editorDir = Path.GetDirectoryName(editorPath);
                var kt = Path.Combine(editorDir!, "Data", "PlaybackEngines",
                    "AndroidPlayer", "OpenJDK", "bin", "keytool.exe");
                if (File.Exists(kt)) return kt;
                // macOS/Linux
                kt = Path.Combine(editorDir!, "Data", "PlaybackEngines",
                    "AndroidPlayer", "OpenJDK", "bin", "keytool");
                if (File.Exists(kt)) return kt;
            }

            // 2. AndroidExternalToolsSettings (설정에서 JDK 경로를 바꾼 경우)
            try
            {
                var type = typeof(EditorWindow).Assembly.GetType(
                    "UnityEditor.Android.AndroidExternalToolsSettings");
                if (type != null)
                {
                    var prop = type.GetProperty("jdkRootPath",
                        BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        var jdk = prop.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(jdk))
                        {
                            var kt = Path.Combine(jdk, "bin", "keytool.exe");
                            if (File.Exists(kt)) return kt;
                            kt = Path.Combine(jdk, "bin", "keytool");
                            if (File.Exists(kt)) return kt;
                        }
                    }
                }
            }
            catch { /* 무시 */ }

            // 3. JAVA_HOME
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var kt2 = Path.Combine(javaHome, "bin", "keytool.exe");
                if (File.Exists(kt2)) return kt2;
            }

            return null;
        }

        static void EnsureGitignoreKeystore(string projectRoot)
        {
            var gitignorePath = Path.Combine(projectRoot, ".gitignore");
            try
            {
                string content = File.Exists(gitignorePath)
                    ? File.ReadAllText(gitignorePath) : "";

                if (!content.Contains("*.keystore"))
                {
                    content = content.TrimEnd() + "\n\n# Android Keystore\n*.keystore\n*.jks\n";
                    File.WriteAllText(gitignorePath, content);
                    Debug.Log("[Codemagic] .gitignore에 *.keystore 추가됨");
                }
            }
            catch { /* 무시 */ }
        }
    }
}
#endif
