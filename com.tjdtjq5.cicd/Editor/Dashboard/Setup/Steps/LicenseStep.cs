#if UNITY_EDITOR
using System;
using System.IO;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 2: Unity 라이선스 (Unity Hub로 발급 → .ulf 자동 탐색 → Secret 등록)</summary>
    public class LicenseStep : IWizardStep
    {
        public string StepLabel => "라이선스";
        public bool IsCompleted => !string.IsNullOrEmpty(_ulfContent)
            && IsValidEmail(BuildAutomationSettings.UnityEmail)
            && !string.IsNullOrEmpty(BuildAutomationSettings.UnityPassword);
        public bool IsRequired => true;

        string _ulfPath;
        string _ulfContent;
        string _error;
        bool _showAccountHelp;
        bool _showHubFallback;
        bool _initialized;

        // 체크리스트 상태 (미발견 시 사용자 진행 추적)
        bool _step1Done;
        bool _step2Done;
        bool _step3Done;
        bool _step4Done;

        public void OnDraw()
        {
            if (!_initialized)
            {
                _initialized = true;
                TryAutoDetect();
            }

            EditorUI.DrawSubLabel("Step 2/6: Unity 라이선스");
            EditorUI.DrawDescription(
                "CI 서버에서 Unity를 실행하려면 라이선스 인증이 필요합니다.\n" +
                "Unity Hub에서 발급된 .ulf 파일을 사용합니다.");

            GUILayout.Space(8);

            DrawUnityAccount();

            GUILayout.Space(8);

            EditorUI.DrawSectionHeader("라이선스 파일 (.ulf)", BuildAutomationWindow.COL_PRIMARY);

            if (!string.IsNullOrEmpty(_ulfContent))
                DrawFoundState();
            else
                DrawNotFoundState();

            if (!string.IsNullOrEmpty(_error))
            {
                GUILayout.Space(4);
                EditorUI.DrawDescription($"  ✗ {_error}", EditorUI.COL_ERROR);
            }
        }

        // ── Unity 계정 입력 ──

        void DrawUnityAccount()
        {
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

            if (EditorUI.DrawToggleRow("⚠ Google/Apple로 Unity에 로그인한 경우", _showAccountHelp))
                _showAccountHelp = !_showAccountHelp;
            if (_showAccountHelp)
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
        }

        // ── 라이선스 파일 발견 상태 ──

        void DrawFoundState()
        {
            EditorUI.BeginBody();
            EditorUI.DrawCellLabel($"  ✓ {Path.GetFileName(_ulfPath)} 로드됨", 0, EditorUI.COL_SUCCESS);
            if (!string.IsNullOrEmpty(_ulfPath))
                EditorUI.DrawDescription($"  {_ulfPath}", EditorUI.COL_MUTED);
            GUILayout.Space(4);

            EditorUI.BeginRow();
            if (EditorUI.DrawColorButton("다시 탐색", EditorUI.COL_MUTED))
                TryAutoDetect();
            if (EditorUI.DrawColorButton("다른 파일 선택", EditorUI.COL_MUTED))
                SelectUlfManually();
            EditorUI.EndRow();
            EditorUI.EndBody();
        }

        // ── 라이선스 파일 없음 상태 (체크리스트) ──

        void DrawNotFoundState()
        {
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "Unity Hub에서 Personal 라이선스를 추가한 뒤 [다시 탐색]을 누르세요.",
                EditorUI.COL_INFO);
            GUILayout.Space(6);

            _step1Done = DrawChecklistRow(_step1Done,
                "1. Unity Hub 실행",
                () => Application.OpenURL("unityhub://"),
                "Unity Hub 열기");

            _step2Done = DrawChecklistRow(_step2Done,
                "2. Preferences → Licenses 탭 이동 (Hub 내부)",
                null, null);

            _step3Done = DrawChecklistRow(_step3Done,
                "3. Add → Get a free personal license 클릭",
                null, null);

            _step4Done = DrawChecklistRow(_step4Done,
                "4. 아래 [다시 탐색] 버튼 클릭",
                null, null);

            GUILayout.Space(8);

            EditorUI.BeginRow();
            if (EditorUI.DrawColorButton("다시 탐색", BuildAutomationWindow.COL_PRIMARY, 28))
            {
                _step4Done = true;
                TryAutoDetect();
                if (string.IsNullOrEmpty(_ulfContent) && string.IsNullOrEmpty(_error))
                    _error = $"라이선스 파일을 찾지 못했습니다. 예상 경로: {GetDefaultUlfPath() ?? "(알 수 없음)"}";
            }
            if (EditorUI.DrawColorButton("직접 파일 선택", EditorUI.COL_MUTED))
                SelectUlfManually();
            EditorUI.EndRow();

            EditorUI.EndBody();

            // ── 접힘 가이드: Hub 미설치 대응 ──
            GUILayout.Space(4);
            if (EditorUI.DrawToggleRow("▶ Unity Hub가 안 열리나요?", _showHubFallback))
                _showHubFallback = !_showHubFallback;
            if (_showHubFallback)
            {
                EditorUI.BeginSubBox();
                EditorUI.DrawDescription(
                    "Unity Hub가 설치되어 있지 않거나 unityhub:// 딥링크가 차단된 경우:\n" +
                    "1. 아래 링크에서 Unity Hub 설치\n" +
                    "2. Hub 실행 후 Unity 계정으로 로그인\n" +
                    "3. Preferences → Licenses → Add → Get a free personal license",
                    EditorUI.COL_WARN);
                if (EditorUI.DrawLinkButton("Unity Hub 다운로드"))
                    Application.OpenURL("https://unity.com/download");
                EditorUI.EndSubBox();
            }
        }

        // 체크리스트 한 줄 (토글 + 라벨 + 선택 버튼)
        bool DrawChecklistRow(bool done, string label, Action onClick, string buttonText)
        {
            EditorUI.BeginRow();

            done = EditorGUILayout.Toggle(done, GUILayout.Width(20));

            EditorUI.DrawCellLabel($"  {label}", 0,
                done ? EditorUI.COL_SUCCESS : EditorUI.COL_INFO);

            if (onClick != null && !string.IsNullOrEmpty(buttonText))
            {
                if (EditorUI.DrawColorButton(buttonText, EditorUI.COL_MUTED))
                {
                    onClick();
                    done = true;
                }
            }

            EditorUI.EndRow();
            return done;
        }

        // ── 자동 탐색 / 파일 로드 ──

        void TryAutoDetect()
        {
            var path = GetDefaultUlfPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                LoadUlfFile(path);
                _error = null;
            }
            catch (Exception ex)
            {
                _error = $".ulf 자동 탐색 실패: {ex.Message}";
            }
        }

        static string GetDefaultUlfPath()
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

        void SelectUlfManually()
        {
            var initDir = GetDefaultUlfPath();
            if (!string.IsNullOrEmpty(initDir))
                initDir = Path.GetDirectoryName(initDir);

            var path = EditorUtility.OpenFilePanel(
                "Unity 라이선스 파일 (.ulf) 선택", initDir ?? "", "ulf");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                LoadUlfFile(path);
                _error = null;
            }
            catch (Exception ex)
            {
                _error = $".ulf 파일 읽기 실패: {ex.Message}";
            }
        }

        void LoadUlfFile(string path)
        {
            var content = File.ReadAllText(path);
            if (!content.Contains("<License") && !content.Contains("<?xml"))
                throw new Exception("올바른 .ulf 파일이 아닙니다.");

            _ulfPath = path;
            _ulfContent = content;
            BuildAutomationSettings.UlfContent = content;
            Debug.Log($"[CICD] .ulf 파일 로드됨: {path} ({content.Length} bytes)");
        }

        static bool IsValidEmail(string email) =>
            !string.IsNullOrEmpty(email) && email.Contains("@") && email.Contains(".");
    }
}
#endif
