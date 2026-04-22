#if UNITY_ANDROID
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace Tjdtjq5.EOS.Editor.Build
{
    // PlayEveryWare EOS 플러그인의 eos_dependencies.androidlib 모듈은 AGP 3.6 시절 설정을
    // 유지하고 있어 Unity 6(AGP 8.x) Gradle 빌드에서 "Namespace not specified" 에러가 난다.
    // 업스트림은 Unity 6 미지원 방침 (Issue #863, 2024-08) 이라 직접 패치가 유일한 방법이다.
    // 원본 파일은 건드리지 않고, Gradle 프로젝트가 생성된 직후 복사본만 수정한다.
    public sealed class EOSAndroidGradlePatcher : IPostGenerateGradleAndroidProject
    {
        const string ModuleDirName = "eos_dependencies.androidlib";
        const string Namespace = "com.pew.eos_dependencies";
        const string LogPrefix = "[EOSAndroidGradlePatcher] ";
        // EOS SDK AAR이 Java 8+ API를 쓰므로 app/library 모듈에 desugaring이 필요하다.
        const string DesugarJdkLibs = "com.android.tools:desugar_jdk_libs:2.0.4";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            try
            {
                string moduleDir = Path.Combine(unityLibraryPath, ModuleDirName);
                if (!Directory.Exists(moduleDir))
                {
                    Debug.LogWarning(LogPrefix + "모듈 폴더 없음: " + moduleDir + " — 패치 스킵");
                    return;
                }

                string buildToolsVersion = ReadUnityBuildToolsVersion(unityLibraryPath);

                PatchBuildGradle(Path.Combine(moduleDir, "build.gradle"), buildToolsVersion);
                PatchAndroidManifest(Path.Combine(moduleDir, "AndroidManifest.xml"));

                // eos-sdk.aar이 Java 8+ API를 요구해서 이 AAR를 참조하는 모듈에
                // core library desugaring을 활성화해야 한다.
                EnableCoreLibraryDesugaring(Path.Combine(unityLibraryPath, "build.gradle"));
                string gradleRoot = Path.GetDirectoryName(unityLibraryPath);
                if (!string.IsNullOrEmpty(gradleRoot))
                {
                    EnableCoreLibraryDesugaring(Path.Combine(gradleRoot, "launcher", "build.gradle"));

                    // Unity 6000.3에서 launcher manifest에 installLocation="preferExternal"이
                    // 강제로 박히는 회귀 버그 (UUM-25965 유사 증상). 이 값은 일부 제조사 런쳐에서
                    // 앱을 드로어에서 숨기는 원인이 된다. auto로 강제 교체.
                    FixLauncherManifest(Path.Combine(gradleRoot, "launcher", "src", "main", "AndroidManifest.xml"));
                }
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "패치 실패: " + e);
                throw;
            }
        }

        static void PatchBuildGradle(string path, string buildToolsVersion)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning(LogPrefix + "build.gradle 없음: " + path);
                return;
            }

            string original = File.ReadAllText(path);
            string patched = original;

            // 1) buildscript { ... } 블록 제거.
            // Unity 루트가 AGP 8.10을 주입하므로 모듈 수준 buildscript는 불필요하다.
            // 남겨두면 "classpath com.android.tools.build:gradle:3.6.0"이 고대 의존성
            // (kotlin-stdlib-jdk8:1.3.61, asm:7.0, protobuf:3.4.0 등)을 끌어오다 실패한다.
            patched = RemoveTopLevelBlock(patched, "buildscript");

            // 2) namespace 선언 삽입 (없을 때만). AGP 8.x 필수.
            if (!Regex.IsMatch(patched, @"\bnamespace\s+[""']"))
            {
                patched = Regex.Replace(
                    patched,
                    @"(android\s*\{)",
                    "$1\n    namespace \"" + Namespace + "\"",
                    RegexOptions.Multiline);
            }

            // 3) buildToolsVersion을 Unity unityLibrary/build.gradle의 값과 동기화.
            // 제거만 하면 AGP 기본값(예: 35.0.0)으로 fallback되는데 Unity SDK에 해당 버전이
            // 없으면 Gradle이 자동 설치를 시도하다 Program Files 권한 문제로 실패한다.
            // 기존 줄은 제거하고 Unity가 쓰는 버전으로 새로 삽입.
            patched = Regex.Replace(
                patched,
                @"^[ \t]*buildToolsVersion[ \t]*=?[ \t]*['""][^'""]+['""][ \t]*\r?\n?",
                string.Empty,
                RegexOptions.Multiline);

            if (!string.IsNullOrEmpty(buildToolsVersion))
            {
                patched = Regex.Replace(
                    patched,
                    @"(compileSdkVersion[^\r\n]*\r?\n)",
                    "$1    buildToolsVersion \"" + buildToolsVersion + "\"\n",
                    RegexOptions.Multiline);
            }

            // 4) jcenter() 제거 — 2022년 shutdown, AGP 8.x에서 경고/실패 원인.
            patched = Regex.Replace(
                patched,
                @"^[ \t]*jcenter\(\)[ \t]*\r?\n?",
                string.Empty,
                RegexOptions.Multiline);

            if (!string.Equals(patched, original, StringComparison.Ordinal))
            {
                File.WriteAllText(path, patched);
                Debug.Log(LogPrefix + "build.gradle 패치 적용: " + path);
            }
            else
            {
                Debug.Log(LogPrefix + "build.gradle 이미 패치됨: " + path);
            }
        }

        static void PatchAndroidManifest(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning(LogPrefix + "AndroidManifest.xml 없음: " + path);
                return;
            }

            string original = File.ReadAllText(path);

            // <manifest ... package="..."> 의 package 속성 제거.
            // namespace 값을 build.gradle에 동일하게 선언했으므로 R 클래스 경로는 유지됨.
            string patched = Regex.Replace(
                original,
                @"\s+package\s*=\s*""[^""]*""",
                string.Empty);

            // <application android:theme="@style/Theme.AppCompat..."> 속성 제거.
            // Unity 6의 UnityPlayerGameActivity는 Material3 계열 테마를 쓰는데,
            // EOS가 application 전역에 AppCompat 테마를 강제하면 일부 기기/런쳐에서
            // 크래시 또는 앱 숨김 원인이 된다. Application 레벨 theme은 불필요.
            patched = Regex.Replace(
                patched,
                @"\s+android:theme\s*=\s*""[^""]*""",
                string.Empty);

            if (!string.Equals(patched, original, StringComparison.Ordinal))
            {
                File.WriteAllText(path, patched);
                Debug.Log(LogPrefix + "AndroidManifest.xml 패치 적용: " + path);
            }
            else
            {
                Debug.Log(LogPrefix + "AndroidManifest.xml 이미 패치됨: " + path);
            }
        }

        // launcher/src/main/AndroidManifest.xml의 installLocation을 auto로 강제 교체.
        // Unity 6000.3에서 Player Settings와 무관하게 preferExternal로 생성되는 회귀가 있어
        // 제조사 런쳐(삼성/샤오미 등)가 앱을 드로어에서 숨기는 증상을 유발한다.
        static void FixLauncherManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning(LogPrefix + "launcher AndroidManifest.xml 없음: " + manifestPath);
                return;
            }

            string original = File.ReadAllText(manifestPath);
            string patched = Regex.Replace(
                original,
                @"android:installLocation\s*=\s*""preferExternal""",
                @"android:installLocation=""auto""");

            if (!string.Equals(patched, original, StringComparison.Ordinal))
            {
                File.WriteAllText(manifestPath, patched);
                Debug.Log(LogPrefix + "launcher installLocation auto로 교체: " + manifestPath);
            }
            else
            {
                Debug.Log(LogPrefix + "launcher installLocation 이미 정상: " + manifestPath);
            }
        }

        // android.compileOptions에 coreLibraryDesugaringEnabled true 추가 +
        // dependencies에 desugar_jdk_libs 의존성을 추가한다.
        // 멱등성: 이미 적용된 항목은 건너뛴다.
        static void EnableCoreLibraryDesugaring(string gradlePath)
        {
            if (!File.Exists(gradlePath))
            {
                Debug.LogWarning(LogPrefix + "build.gradle 없음: " + gradlePath);
                return;
            }

            string original = File.ReadAllText(gradlePath);
            string patched = original;

            if (!Regex.IsMatch(patched, @"\bcoreLibraryDesugaringEnabled\s+true"))
            {
                var m = Regex.Match(patched, @"compileOptions\s*\{");
                if (m.Success)
                {
                    int insertAt = m.Index + m.Length;
                    patched = patched.Substring(0, insertAt)
                        + "\n        coreLibraryDesugaringEnabled true"
                        + patched.Substring(insertAt);
                }
                else
                {
                    Debug.LogWarning(LogPrefix + "compileOptions 블록을 찾지 못함: " + gradlePath);
                }
            }

            if (!Regex.IsMatch(patched, @"\bcoreLibraryDesugaring\s+['""]"))
            {
                var m = Regex.Match(patched, @"dependencies\s*\{");
                if (m.Success)
                {
                    int insertAt = m.Index + m.Length;
                    patched = patched.Substring(0, insertAt)
                        + "\n    coreLibraryDesugaring '" + DesugarJdkLibs + "'"
                        + patched.Substring(insertAt);
                }
                else
                {
                    Debug.LogWarning(LogPrefix + "dependencies 블록을 찾지 못함: " + gradlePath);
                }
            }

            if (!string.Equals(patched, original, StringComparison.Ordinal))
            {
                File.WriteAllText(gradlePath, patched);
                Debug.Log(LogPrefix + "core library desugaring 활성화: " + gradlePath);
            }
            else
            {
                Debug.Log(LogPrefix + "desugaring 이미 적용됨: " + gradlePath);
            }
        }

        // Unity가 생성한 unityLibrary/build.gradle에서 buildToolsVersion 값을 읽는다.
        // eos_dependencies가 Unity 메인 모듈과 동일한 build-tools를 쓰도록 맞춘다.
        static string ReadUnityBuildToolsVersion(string unityLibraryPath)
        {
            try
            {
                string mainGradle = Path.Combine(unityLibraryPath, "build.gradle");
                if (!File.Exists(mainGradle)) return null;

                string content = File.ReadAllText(mainGradle);
                var m = Regex.Match(content, @"buildToolsVersion\s*=?\s*['""]([^'""]+)['""]");
                if (m.Success)
                {
                    Debug.Log(LogPrefix + "unityLibrary buildToolsVersion 감지: " + m.Groups[1].Value);
                    return m.Groups[1].Value;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "buildToolsVersion 읽기 실패: " + e.Message);
            }
            return null;
        }

        // Gradle 최상위 블록(예: "buildscript { ... }")을 중괄호 균형 매칭으로 제거.
        // Regex만으론 중첩 중괄호를 안전하게 처리하기 어려워 수동 스캔을 쓴다.
        static string RemoveTopLevelBlock(string content, string keyword)
        {
            var match = Regex.Match(content, @"(^|\s)" + Regex.Escape(keyword) + @"\s*\{");
            if (!match.Success) return content;

            int openBrace = content.IndexOf('{', match.Index);
            if (openBrace < 0) return content;

            int depth = 1;
            int closeBrace = -1;
            for (int i = openBrace + 1; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) { closeBrace = i; break; }
                }
            }
            if (closeBrace < 0) return content;

            // 키워드 시작 위치 계산 (선행 공백 제외)
            int keywordStart = content.IndexOf(keyword, match.Index, StringComparison.Ordinal);
            int removeEnd = closeBrace + 1;
            // 블록 직후의 공백/개행 한 덩어리 소비
            while (removeEnd < content.Length && (content[removeEnd] == '\r' || content[removeEnd] == '\n'))
                removeEnd++;

            return content.Substring(0, keywordStart) + content.Substring(removeEnd);
        }
    }
}
#endif
