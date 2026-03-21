#if UNITY_EDITOR
using System.IO;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 2: Unity 라이선스 (.alf → .ulf → Secret 등록)</summary>
    public class LicenseStep : IWizardStep
    {
        public string StepLabel => "라이선스";
        public bool IsCompleted => !string.IsNullOrEmpty(_ulfContent)
            && IsValidEmail(BuildAutomationSettings.UnityEmail)
            && !string.IsNullOrEmpty(BuildAutomationSettings.UnityPassword);
        public bool IsRequired => true;

        string _alfPath;
        string _ulfPath;
        string _ulfContent;
        string _error;
        bool _alfGenerating;
        bool _showHelp;

        public void OnDraw()
        {
            EditorUI.DrawSubLabel("Step 2/6: Unity 라이선스");
            EditorUI.DrawDescription(
                "CI 서버에서 Unity를 실행하려면 라이선스 인증이 필요합니다.\n" +
                "아래 3단계를 순서대로 진행하세요.");

            GUILayout.Space(8);

            // ── Unity 계정 입력 ──
            EditorUI.DrawSectionHeader("Unity 계정", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            EditorUI.DrawDescription(
                "Unity Hub 로그인에 사용하는 계정입니다. (GitHub 계정 아님)",
                EditorUI.COL_MUTED);
            GUILayout.Space(4);

            BuildAutomationSettings.UnityEmail =
                EditorUI.DrawTextField("이메일", BuildAutomationSettings.UnityEmail);

            var email = BuildAutomationSettings.UnityEmail;
            if (!string.IsNullOrEmpty(email) && !IsValidEmail(email))
                EditorUI.DrawDescription("  ⚠ 이메일 형식이 올바르지 않습니다.", EditorUI.COL_ERROR);

            BuildAutomationSettings.UnityPassword =
                EditorUI.DrawPasswordField("비밀번호", BuildAutomationSettings.UnityPassword);

            var pass = BuildAutomationSettings.UnityPassword;
            if (!string.IsNullOrEmpty(pass) && pass.Length < 6)
                EditorUI.DrawDescription("  ⚠ 비밀번호는 6자 이상이어야 합니다.", EditorUI.COL_ERROR);

            // Google/Apple 로그인 안내
            if (EditorUI.DrawToggleRow("⚠ Google/Apple로 Unity에 로그인한 경우", _showHelp))
                _showHelp = !_showHelp;
            if (_showHelp)
            {
                EditorUI.BeginSubBox();
                EditorUI.DrawDescription(
                    "Unity 계정에 비밀번호가 설정되어 있어야 합니다.\n" +
                    "id.unity.com → 보안 설정 → 비밀번호 추가\n" +
                    "기존 Google/Apple 로그인은 계속 사용 가능합니다.",
                    EditorUI.COL_WARN);
                if (EditorUI.DrawLinkButton("Unity 계정 보안 설정"))
                    Application.OpenURL("https://id.unity.com/en/account/edit");
                EditorUI.EndSubBox();
            }

            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── 라이선스 파일 3단계 ──
            EditorUI.DrawSectionHeader("라이선스 파일 (.ulf)", BuildAutomationWindow.COL_PRIMARY);

            // Step 1: .alf 생성
            EditorUI.BeginBody();
            EditorUI.DrawCellLabel("  Step 1. 활성화 파일 생성 (.alf)", 0, EditorUI.COL_INFO);
            GUILayout.Space(4);

            if (!string.IsNullOrEmpty(_alfPath) && File.Exists(_alfPath))
            {
                EditorUI.DrawCellLabel($"  ✓ {Path.GetFileName(_alfPath)}", 0, EditorUI.COL_SUCCESS);
            }
            else if (_alfGenerating)
            {
                EditorUI.DrawLoading(true, ".alf 파일 생성 중...");
            }
            else
            {
                EditorUI.DrawDescription(
                    "Unity를 batchmode로 실행하여 .alf 파일을 생성합니다.",
                    EditorUI.COL_MUTED);
                if (EditorUI.DrawColorButton(".alf 파일 생성", BuildAutomationWindow.COL_PRIMARY, 28))
                    GenerateAlf();
            }
            EditorUI.EndBody();

            GUILayout.Space(4);

            // Step 2: Unity 웹에서 .ulf 다운로드
            EditorUI.BeginBody();
            EditorUI.DrawCellLabel("  Step 2. .ulf 파일 다운로드 (수동)", 0, EditorUI.COL_INFO);
            GUILayout.Space(4);

            bool hasAlf = !string.IsNullOrEmpty(_alfPath) && File.Exists(_alfPath);
            EditorUI.BeginDisabled(!hasAlf);

            EditorUI.DrawDescription(
                "1. 아래 링크에서 .alf 파일을 업로드합니다\n" +
                "2. 시리얼 입력 화면이 나오면:\n" +
                "   → F12 (개발자 도구) 열기\n" +
                "   → Elements 탭에서 Ctrl+F → \"option-personal\" 검색\n" +
                "   → \"display: none\" 를 삭제\n" +
                "   → Personal License 옵션이 나타남 → 선택\n" +
                "3. .ulf 파일이 다운로드됩니다",
                EditorUI.COL_INFO);

            GUILayout.Space(4);
            EditorUI.BeginRow();
            if (EditorUI.DrawLinkButton("Unity 라이선스 활성화 페이지"))
                Application.OpenURL("https://license.unity3d.com/manual");

            if (hasAlf && EditorUI.DrawColorButton(".alf 파일 위치 열기", EditorUI.COL_MUTED))
                EditorUtility.RevealInFinder(_alfPath);
            EditorUI.EndRow();

            EditorUI.EndDisabled();
            EditorUI.EndBody();

            GUILayout.Space(4);

            // Step 3: .ulf 파일 선택
            EditorUI.BeginBody();
            EditorUI.DrawCellLabel("  Step 3. .ulf 파일 선택", 0, EditorUI.COL_INFO);
            GUILayout.Space(4);

            if (!string.IsNullOrEmpty(_ulfContent))
            {
                EditorUI.DrawCellLabel($"  ✓ {Path.GetFileName(_ulfPath)} 로드됨", 0, EditorUI.COL_SUCCESS);
                GUILayout.Space(2);
                if (EditorUI.DrawColorButton("다른 .ulf 파일 선택", EditorUI.COL_MUTED))
                    SelectUlf();
            }
            else
            {
                EditorUI.DrawDescription(
                    "다운로드한 .ulf 파일을 선택하세요.\n" +
                    "Step 5에서 GitHub Secret에 자동 등록됩니다.",
                    EditorUI.COL_MUTED);
                if (EditorUI.DrawColorButton(".ulf 파일 선택...", BuildAutomationWindow.COL_PRIMARY, 28))
                    SelectUlf();
            }

            EditorUI.EndBody();

            // 에러 표시
            if (!string.IsNullOrEmpty(_error))
            {
                GUILayout.Space(4);
                EditorUI.DrawDescription($"  ✗ {_error}", EditorUI.COL_ERROR);
            }
        }

        // ── .alf 생성 ──

        void GenerateAlf()
        {
            var unityPath = EditorApplication.applicationPath;
            if (string.IsNullOrEmpty(unityPath))
            {
                _error = "Unity 에디터 경로를 찾을 수 없습니다.";
                return;
            }

            // 출력 경로: 프로젝트 루트
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

            _alfGenerating = true;
            _error = null;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = unityPath,
                        Arguments = $"-batchmode -nographics -createManualActivationFile -logFile -",
                        WorkingDirectory = projectRoot,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using var p = System.Diagnostics.Process.Start(psi);
                    p!.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000); // 1분 타임아웃

                    // .alf 파일 찾기 (Unity가 현재 디렉토리에 생성)
                    string alfFound = null;
                    foreach (var f in Directory.GetFiles(projectRoot, "*.alf"))
                    {
                        alfFound = f;
                        break;
                    }

                    EditorApplication.delayCall += () =>
                    {
                        _alfGenerating = false;
                        if (!string.IsNullOrEmpty(alfFound) && File.Exists(alfFound))
                        {
                            _alfPath = alfFound;
                            Debug.Log($"[CICD] .alf 파일 생성됨: {alfFound}");
                        }
                        else
                        {
                            _error = ".alf 파일 생성에 실패했습니다. Unity 로그를 확인하세요.";
                        }
                    };
                }
                catch (System.Exception ex)
                {
                    EditorApplication.delayCall += () =>
                    {
                        _alfGenerating = false;
                        _error = $".alf 생성 실패: {ex.Message}";
                    };
                }
            });
        }

        // ── .ulf 선택 ──

        void SelectUlf()
        {
            var path = EditorUtility.OpenFilePanel("Unity 라이선스 파일 (.ulf) 선택", "", "ulf");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var content = File.ReadAllText(path);
                if (content.Contains("<License") || content.Contains("<?xml"))
                {
                    _ulfPath = path;
                    _ulfContent = content;
                    // Settings에 저장 (Secret 자동 등록에서 사용)
                    BuildAutomationSettings.UlfContent = content;
                    _error = null;
                    Debug.Log($"[CICD] .ulf 파일 로드됨: {path} ({content.Length} bytes)");
                }
                else
                {
                    _error = "올바른 .ulf 파일이 아닙니다.";
                }
            }
            catch (System.Exception ex)
            {
                _error = $".ulf 파일 읽기 실패: {ex.Message}";
            }
        }

        static bool IsValidEmail(string email) =>
            !string.IsNullOrEmpty(email) && email.Contains("@") && email.Contains(".");
    }
}
#endif
