#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 3: 빌드 플랫폼 선택 + 플랫폼별 설정</summary>
    public class PlatformStep : IWizardStep
    {
        public string StepLabel => "플랫폼";
        public bool IsCompleted
        {
            get
            {
                var s = BuildAutomationSettings.Instance;
                if (!s.HasAnyPlatform) return false;
                // Android 선택 시 Keystore 필수
                if (s.enableAndroid && !s.IsKeystoreConfigured) return false;
                // iOS 선택 시 Team ID 필수
                if (s.enableIOS && string.IsNullOrEmpty(s.iosTeamId)) return false;
                return true;
            }
        }
        public bool IsRequired => true;

        bool _androidFoldout;
        bool _iosFoldout;

        public void OnDraw()
        {
            var s = BuildAutomationSettings.Instance;

            EditorUI.DrawSubLabel("Step 3/6: 빌드 플랫폼 선택");
            EditorUI.DrawDescription("CI에서 빌드할 플랫폼을 선택하세요. 복수 선택 가능합니다.");

            GUILayout.Space(8);

            // ── Android ──
            EditorUI.BeginBody();
            DrawPlatformToggle(ref s.enableAndroid, "Android", BuildAutomationWindow.COL_ANDROID);

            if (s.enableAndroid)
            {
                EditorGUI.indentLevel++;
                s.androidBuildFormat = (AndroidBuildFormat)EditorGUILayout.EnumPopup(
                    "빌드 형식", s.androidBuildFormat);
                EditorGUI.indentLevel--;

                if (EditorUI.DrawSectionFoldout(ref _androidFoldout, "Keystore 서명 설정",
                    BuildAutomationWindow.COL_ANDROID))
                {
                    EditorUI.BeginSubBox();

                    // Keystore 파일 선택 또는 생성
                    EditorUI.BeginRow();
                    s.keystorePath = EditorUI.DrawTextField("Keystore", s.keystorePath);
                    if (EditorUI.DrawMiniButton("..."))
                    {
                        var path = EditorUtility.OpenFilePanel("Keystore 선택", "", "keystore,jks");
                        if (!string.IsNullOrEmpty(path)) s.keystorePath = path;
                    }
                    EditorUI.EndRow();

                    BuildAutomationSettings.KeystorePass =
                        EditorUI.DrawPasswordField("Keystore 비밀번호", BuildAutomationSettings.KeystorePass);

                    s.keyAlias = EditorUI.DrawTextField("Key Alias", s.keyAlias);

                    BuildAutomationSettings.KeyPass =
                        EditorUI.DrawPasswordField("Key 비밀번호", BuildAutomationSettings.KeyPass);

                    GUILayout.Space(4);

                    // Keystore 생성 버튼
                    if (string.IsNullOrEmpty(s.keystorePath) ||
                        !System.IO.File.Exists(s.keystorePath))
                    {
                        EditorUI.DrawDescription(
                            "Keystore가 없으면 아래 버튼으로 생성할 수 있습니다.",
                            EditorUI.COL_MUTED);
                        if (EditorUI.DrawColorButton("Keystore 생성",
                            BuildAutomationWindow.COL_ANDROID, 28))
                        {
                            CreateKeystore(s);
                        }
                    }
                    else
                    {
                        EditorUI.DrawCellLabel(
                            $"  ✓ {System.IO.Path.GetFileName(s.keystorePath)}",
                            0, EditorUI.COL_SUCCESS);
                    }

                    EditorUI.EndSubBox();
                }
            }
            EditorUI.EndBody();

            GUILayout.Space(4);

            // ── iOS ──
            EditorUI.BeginBody();
            DrawPlatformToggle(ref s.enableIOS, "iOS", BuildAutomationWindow.COL_IOS);

            if (s.enableIOS)
            {
                EditorGUI.indentLevel++;
                EditorUI.DrawDescription(
                    "⚠ macOS 러너가 필요합니다.\n" +
                    "GitHub Actions macOS 러너는 Linux 대비 10배 비용이 발생합니다.\n" +
                    "Private 리포의 경우 분 수 소진에 주의하세요.",
                    EditorUI.COL_WARN);
                EditorGUI.indentLevel--;

                if (EditorUI.DrawSectionFoldout(ref _iosFoldout, "iOS 설정",
                    BuildAutomationWindow.COL_IOS))
                {
                    EditorUI.BeginSubBox();
                    s.iosTeamId = EditorUI.DrawTextField("Team ID", s.iosTeamId,
                        "Apple Developer Team ID");
                    if (EditorUI.DrawLinkButton("Apple Developer 에서 Team ID 확인"))
                        Application.OpenURL("https://developer.apple.com/account#MembershipDetailsCard");
                    EditorUI.EndSubBox();
                }
            }
            EditorUI.EndBody();

            GUILayout.Space(4);

            // ── Windows / WebGL ──
            EditorUI.BeginBody();
            DrawPlatformToggle(ref s.enableWindows, "Windows", BuildAutomationWindow.COL_WINDOWS);
            DrawPlatformToggle(ref s.enableWebGL, "WebGL", BuildAutomationWindow.COL_WEBGL);
            EditorUI.EndBody();

            // 변경 사항 저장
            if (GUI.changed) s.Save();

            // ── 진행 불가 사유 표시 ──
            if (!s.HasAnyPlatform)
            {
                GUILayout.Space(4);
                EditorUI.DrawDescription("⚠ 최소 1개 플랫폼을 선택해야 합니다.", EditorUI.COL_WARN);
            }
            else if (s.enableAndroid && !s.IsKeystoreConfigured)
            {
                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "⚠ Android를 선택했으면 Keystore 서명을 설정해야 합니다.\n" +
                    "  Keystore가 없으면 [Keystore 생성] 버튼을 사용하세요.",
                    EditorUI.COL_WARN);
            }
            else if (s.enableIOS && string.IsNullOrEmpty(s.iosTeamId))
            {
                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "⚠ iOS를 선택했으면 Team ID를 입력해야 합니다.",
                    EditorUI.COL_WARN);
            }
        }

        static void DrawPlatformToggle(ref bool value, string label, Color color)
        {
            EditorUI.BeginRow();
            var prev = GUI.contentColor;
            GUI.contentColor = value ? color : EditorUI.COL_MUTED;
            value = EditorGUILayout.Toggle(value ? $"✓ {label}" : $"  {label}", value);
            GUI.contentColor = prev;
            EditorUI.EndRow();
        }

        /// <summary>keytool로 Keystore 생성 + Settings 자동 적용</summary>
        static void CreateKeystore(BuildAutomationSettings s)
        {
            // 비밀번호 검증
            var pass = BuildAutomationSettings.KeystorePass;
            var keyPass = BuildAutomationSettings.KeyPass;
            var alias = s.keyAlias;

            if (string.IsNullOrEmpty(pass) || pass.Length < 6)
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "Keystore 비밀번호를 6자 이상 입력하세요.", "확인");
                return;
            }
            if (string.IsNullOrEmpty(keyPass) || keyPass.Length < 6)
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "Key 비밀번호를 6자 이상 입력하세요.", "확인");
                return;
            }
            if (string.IsNullOrEmpty(alias))
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "Key Alias를 입력하세요.", "확인");
                return;
            }

            // 저장 경로 선택
            // 프로젝트 루트를 기본 경로로
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath)!.FullName;
            var savePath = EditorUtility.SaveFilePanel(
                "Keystore 저장 위치", projectRoot, "user", "keystore");
            if (string.IsNullOrEmpty(savePath)) return;

            // keytool 경로 찾기
            var keytoolPath = FindKeytool();
            if (keytoolPath == null)
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "keytool을 찾을 수 없습니다.\nJDK가 설치되어 있는지 확인하세요.", "확인");
                return;
            }

            // keytool 실행
            var args = $"-genkeypair -v" +
                $" -keystore \"{savePath}\"" +
                $" -alias \"{alias}\"" +
                $" -keyalg RSA -keysize 2048 -validity 10000" +
                $" -storepass \"{pass}\"" +
                $" -keypass \"{keyPass}\"" +
                $" -dname \"CN=Developer, C=KR\"";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = keytoolPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p!.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);

                if (p.ExitCode == 0 && System.IO.File.Exists(savePath))
                {
                    // Settings에 자동 적용
                    s.keystorePath = savePath;
                    PlayerSettings.Android.keystoreName = savePath;
                    PlayerSettings.Android.keystorePass = pass;
                    PlayerSettings.Android.keyaliasName = alias;
                    PlayerSettings.Android.keyaliasPass = keyPass;
                    s.Save();

                    // .gitignore에 *.keystore 추가
                    EnsureGitignoreKeystore(projectRoot);

                    Debug.Log($"[CICD] Keystore 생성 완료: {savePath}");
                    EditorUtility.DisplayDialog("Keystore 생성",
                        $"Keystore가 생성되었습니다!\n{savePath}\n\n" +
                        ".gitignore에 *.keystore가 추가되었습니다.", "확인");
                }
                else
                {
                    Debug.LogError($"[CICD] Keystore 생성 실패: {stderr}");
                    EditorUtility.DisplayDialog("Keystore 생성 실패", stderr, "확인");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CICD] keytool 실행 실패: {ex.Message}");
                EditorUtility.DisplayDialog("Keystore 생성 실패", ex.Message, "확인");
            }
        }

        static void EnsureGitignoreKeystore(string projectRoot)
        {
            var gitignorePath = System.IO.Path.Combine(projectRoot, ".gitignore");
            try
            {
                string content = System.IO.File.Exists(gitignorePath)
                    ? System.IO.File.ReadAllText(gitignorePath) : "";

                if (!content.Contains("*.keystore"))
                {
                    content = content.TrimEnd() + "\n\n# Android Keystore\n*.keystore\n*.jks\n";
                    System.IO.File.WriteAllText(gitignorePath, content);
                    Debug.Log("[CICD] .gitignore에 *.keystore 추가됨");
                }
            }
            catch { /* 무시 */ }
        }

        static string FindKeytool()
        {
            // 1. Unity 에디터 경로 기반 (가장 확실)
            // Unity.exe → Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool
            var editorPath = EditorApplication.applicationPath;
            if (!string.IsNullOrEmpty(editorPath))
            {
                var editorDir = System.IO.Path.GetDirectoryName(editorPath);
                var kt = System.IO.Path.Combine(editorDir!, "Data", "PlaybackEngines",
                    "AndroidPlayer", "OpenJDK", "bin", "keytool.exe");
                if (System.IO.File.Exists(kt)) return kt;
                // macOS/Linux
                kt = System.IO.Path.Combine(editorDir!, "Data", "PlaybackEngines",
                    "AndroidPlayer", "OpenJDK", "bin", "keytool");
                if (System.IO.File.Exists(kt)) return kt;
            }

            // 2. AndroidExternalToolsSettings (설정에서 JDK 경로를 바꾼 경우)
            try
            {
                var type = typeof(EditorWindow).Assembly.GetType(
                    "UnityEditor.Android.AndroidExternalToolsSettings");
                if (type != null)
                {
                    var prop = type.GetProperty("jdkRootPath",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop != null)
                    {
                        var jdk = prop.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(jdk))
                        {
                            var kt = System.IO.Path.Combine(jdk, "bin", "keytool.exe");
                            if (System.IO.File.Exists(kt)) return kt;
                            kt = System.IO.Path.Combine(jdk, "bin", "keytool");
                            if (System.IO.File.Exists(kt)) return kt;
                        }
                    }
                }
            }
            catch { /* 무시 */ }

            // 3. JAVA_HOME
            var javaHome = System.Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var kt2 = System.IO.Path.Combine(javaHome, "bin", "keytool.exe");
                if (System.IO.File.Exists(kt2)) return kt2;
            }

            return null;
        }
    }
}
#endif
