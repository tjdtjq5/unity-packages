using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SetupWizard
    {
        readonly SupaRunDashboard _dashboard;
        readonly SupabaseSetup _supabaseSetup;
        readonly DeploySetup _deploySetup;

        // 0=.NET SDK, 1=Supabase, 2=gh CLI, 3=gcloud CLI, 4=배포 설정
        int _currentStep;
        bool _showCompletion;
        Vector2 _scrollPos;

        static readonly string[] StepLabels = { ".NET", "Supabase", "gh", "gcloud", "Deploy" };
        const int STEP_COUNT = 5;

        public SetupWizard(SupaRunDashboard dashboard)
        {
            _dashboard = dashboard;
            _supabaseSetup = new SupabaseSetup(dashboard);
            _deploySetup = new DeploySetup(dashboard);
        }

        public void Cleanup() => _supabaseSetup.Cleanup();

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorUI.DrawSectionHeader("시작하기", SupaRunDashboard.COL_PRIMARY);
            GUILayout.Space(8);

            if (_showCompletion)
            {
                DrawCompletion();
            }
            else
            {
                DrawStepIndicator();
                GUILayout.Space(8);
                DrawCurrentStep();
                GUILayout.Space(12);
                DrawNavigation();
            }

            EditorGUILayout.EndScrollView();
        }

        // ── 스텝 인디케이터 ──

        void DrawStepIndicator()
        {
            var states = new int[STEP_COUNT];
            for (int i = 0; i < STEP_COUNT; i++)
            {
                if (i < _currentStep)
                    states[i] = IsStepCompleted(i) ? 2 : 3; // 2=완료, 3=건너뜀
                else if (i == _currentStep)
                    states[i] = 1; // 현재
                else
                    states[i] = 0; // 미진행
            }
            EditorUI.DrawStepIndicator(StepLabels, states);
        }

        bool IsStepCompleted(int step) => step switch
        {
            0 => PrerequisiteChecker.IsDotnetInstalled(),
            1 => _supabaseSetup.IsCompleted,
            2 => PrerequisiteChecker.CheckGh().LoggedIn,
            3 => PrerequisiteChecker.CheckGcloud().LoggedIn,
            4 => SupaRunSettings.Instance.IsGitHubConfigured,
            _ => false
        };

        // ── 현재 스텝 내용 ──

        void DrawCurrentStep()
        {
            switch (_currentStep)
            {
                case 0: DrawDotnetStep(); break;
                case 1: DrawSupabaseStep(); break;
                case 2: DrawGhStep(); break;
                case 3: DrawGcloudStep(); break;
                case 4: DrawDeployStep(); break;
            }
        }

        void DrawDotnetStep()
        {
            EditorUI.DrawSubLabel($"Step 1/{STEP_COUNT}: .NET SDK");
            EditorUI.DrawDescription("서버 코드 빌드 검증에 사용됩니다.\n설치하면 배포 전에 에러를 미리 잡을 수 있습니다.");

            GUILayout.Space(8);
            EditorUI.BeginBody();

            if (PrerequisiteChecker.IsDotnetInstalled())
            {
                var ver = PrerequisiteChecker.GetDotnetMajorVersion();
                EditorUI.DrawCellLabel($"  .NET SDK {ver}.0 설치됨", 0, EditorUI.COL_SUCCESS);
            }
            else
            {
                EditorUI.DrawCellLabel("  .NET SDK 미설치", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawLinkButton(".NET SDK 설치하기", SupaRunDashboard.COL_PRIMARY))
                    Application.OpenURL("https://dotnet.microsoft.com/download");

                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "설치 후 [새로고침]을 눌러주세요.", EditorUI.COL_MUTED);

                if (EditorUI.DrawColorButton("새로고침", EditorUI.COL_MUTED))
                    PrerequisiteChecker.InvalidateCache();
            }

            EditorUI.EndBody();
        }

        void DrawSupabaseStep()
        {
            EditorUI.DrawSubLabel($"Step 2/{STEP_COUNT}: Supabase 연결 (필수)");
            EditorUI.DrawDescription("게임 데이터를 저장할 데이터베이스입니다.\n무료로 시작할 수 있습니다.");

            GUILayout.Space(8);
            _supabaseSetup.OnDraw();
        }

        void DrawGhStep()
        {
            EditorUI.DrawSubLabel($"Step 3/{STEP_COUNT}: GitHub CLI");
            EditorUI.DrawDescription("서버 코드를 GitHub에 push할 때 필요합니다.");

            GUILayout.Space(8);
            EditorUI.BeginBody();

            var gh = PrerequisiteChecker.CheckGh();

            if (gh.LoggedIn)
            {
                EditorUI.DrawCellLabel($"  gh {gh.Version} 설치됨", 0, EditorUI.COL_SUCCESS);
                EditorUI.DrawCellLabel($"  {gh.Account} 로그인됨", 0, EditorUI.COL_SUCCESS);
            }
            else if (gh.Installed)
            {
                EditorUI.DrawCellLabel($"  gh {gh.Version} 설치됨", 0, EditorUI.COL_SUCCESS);
                EditorUI.DrawCellLabel("  로그인 필요", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawColorButton("GitHub 로그인", SupaRunDashboard.COL_GITHUB, 28))
                    PrerequisiteChecker.RunGhLogin();
            }
            else
            {
                EditorUI.DrawCellLabel("  gh CLI 미설치", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawLinkButton("GitHub CLI 설치하기", SupaRunDashboard.COL_GITHUB))
                    Application.OpenURL("https://cli.github.com");
            }

            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("새로고침", EditorUI.COL_MUTED))
                PrerequisiteChecker.InvalidateCache();

            EditorUI.EndBody();
        }

        void DrawGcloudStep()
        {
            EditorUI.DrawSubLabel($"Step 4/{STEP_COUNT}: Google Cloud CLI");
            EditorUI.DrawDescription("Cloud Run 서버 배포에 필요합니다.");

            GUILayout.Space(8);
            EditorUI.BeginBody();

            var gcloud = PrerequisiteChecker.CheckGcloud();

            if (gcloud.LoggedIn)
            {
                EditorUI.DrawCellLabel($"  gcloud {gcloud.Version} 설치됨", 0, EditorUI.COL_SUCCESS);
                EditorUI.DrawCellLabel($"  {gcloud.Account} 로그인됨", 0, EditorUI.COL_SUCCESS);
            }
            else if (gcloud.Installed)
            {
                EditorUI.DrawCellLabel($"  gcloud {gcloud.Version} 설치됨", 0, EditorUI.COL_SUCCESS);
                EditorUI.DrawCellLabel("  로그인 필요", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawColorButton("Google 로그인", SupaRunDashboard.COL_GCP, 28))
                    PrerequisiteChecker.RunGcloudLogin();
            }
            else
            {
                EditorUI.DrawCellLabel("  gcloud CLI 미설치", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawLinkButton("gcloud CLI 설치하기", SupaRunDashboard.COL_GCP))
                    Application.OpenURL("https://cloud.google.com/sdk/docs/install");
            }

            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("새로고침", EditorUI.COL_MUTED))
                PrerequisiteChecker.InvalidateCache();

            EditorUI.EndBody();
        }

        void DrawDeployStep()
        {
            EditorUI.DrawSubLabel($"Step 5/{STEP_COUNT}: 배포 설정 (선택)");
            EditorUI.DrawDescription("서버를 Cloud Run에 배포할 때 필요합니다.\n개발은 LocalGameDB로 가능합니다.");

            GUILayout.Space(8);
            _deploySetup.OnDraw();
        }

        // ── 네비게이션 ──

        void DrawNavigation()
        {
            EditorUI.BeginRow();

            // ← 이전
            if (_currentStep > 0)
            {
                if (EditorUI.DrawColorButton("이전", EditorUI.COL_MUTED, 28))
                {
                    _currentStep--;
                    GUIUtility.ExitGUI();
                }
            }

            EditorUI.FlexSpace();

            if (_currentStep == 1)
            {
                // Supabase: 연결 테스트 통과 필요
                using (new EditorGUI.DisabledGroupScope(!_supabaseSetup.IsCompleted))
                {
                    if (EditorUI.DrawColorButton("다음", SupaRunDashboard.COL_PRIMARY, 28))
                    {
                        _currentStep++;
                        GUIUtility.ExitGUI();
                    }
                }
                if (!_supabaseSetup.IsCompleted)
                    EditorUI.DrawDescription("연결 테스트를 통과해야 합니다.", EditorUI.COL_WARN);
            }
            else if (_currentStep == STEP_COUNT - 1)
            {
                // 마지막 스텝: 건너뛰기 + 완료
                if (EditorUI.DrawColorButton("건너뛰기", EditorUI.COL_WARN, 28))
                {
                    _deploySetup.OnSkip();
                    _showCompletion = true;
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(8);
                if (EditorUI.DrawColorButton("완료", EditorUI.COL_SUCCESS, 28))
                {
                    _showCompletion = true;
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                // 나머지: 건너뛰기 + 다음
                if (!IsStepCompleted(_currentStep))
                {
                    if (EditorUI.DrawColorButton("건너뛰기", EditorUI.COL_WARN, 28))
                    {
                        _currentStep++;
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.Space(8);
                }
                if (EditorUI.DrawColorButton("다음", SupaRunDashboard.COL_PRIMARY, 28))
                {
                    _currentStep++;
                    GUIUtility.ExitGUI();
                }
            }

            EditorUI.EndRow();
        }

        // ── 완료 화면 ──

        void DrawCompletion()
        {
            var settings = SupaRunSettings.Instance;
            var gh = PrerequisiteChecker.CheckGh();
            var gcloud = PrerequisiteChecker.CheckGcloud();

            GUILayout.Space(20);
            EditorUI.DrawSectionHeader("설정 완료!", EditorUI.COL_SUCCESS);
            GUILayout.Space(12);

            EditorUI.BeginBody();
            DrawCompletionRow(".NET SDK",
                PrerequisiteChecker.IsDotnetInstalled()
                    ? $"{PrerequisiteChecker.GetDotnetMajorVersion()}.0" : "건너뜀",
                PrerequisiteChecker.IsDotnetInstalled());
            DrawCompletionRow("Supabase", "Connected", true);
            DrawCompletionRow("GitHub CLI",
                gh.LoggedIn ? gh.Account : "건너뜀", gh.LoggedIn);
            DrawCompletionRow("gcloud CLI",
                gcloud.LoggedIn ? gcloud.Account : "건너뜀", gcloud.LoggedIn);
            DrawCompletionRow("배포 설정",
                settings.IsGitHubConfigured ? settings.githubRepoName : "건너뜀",
                settings.IsGitHubConfigured);
            EditorUI.EndBody();

            GUILayout.Space(4);
            EditorUI.DrawDescription("건너뛴 항목은 설정에서 언제든 설정할 수 있습니다.");

            GUILayout.Space(8);
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "지금 바로 Unity Play를 눌러보세요!\n" +
                "[Table]과 [Service]를 작성하면\n" +
                "LocalGameDB로 즉시 테스트됩니다.", EditorUI.COL_INFO);
            EditorUI.EndBody();

            GUILayout.Space(16);
            EditorUI.BeginRow();
            EditorUI.FlexSpace();
            if (EditorUI.DrawColorButton("  대시보드 열기  ", SupaRunDashboard.COL_PRIMARY, 32))
                _dashboard.OnSetupCompleted();
            EditorUI.FlexSpace();
            EditorUI.EndRow();
        }

        static void DrawCompletionRow(string name, string status, bool ok)
        {
            EditorUI.BeginRow();
            EditorUI.DrawCellLabel(
                ok ? $"  ✓ {name}" : $"  - {name}",
                150, ok ? EditorUI.COL_SUCCESS : EditorUI.COL_WARN);
            EditorUI.DrawCellLabel(status, 0, EditorUI.COL_MUTED);
            EditorUI.EndRow();
        }
    }
}
