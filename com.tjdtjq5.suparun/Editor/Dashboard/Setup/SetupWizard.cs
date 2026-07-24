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

            EditorGUILayout.LabelField("시작하기", EditorStyles.boldLabel);
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

            // 상태 기호: 2=완료(✓), 3=건너뜀(△), 1=현재(●), 0=미진행(○)
            var parts = new string[STEP_COUNT];
            for (int i = 0; i < STEP_COUNT; i++)
            {
                var dot = states[i] switch { 2 => "✓", 3 => "△", 1 => "●", _ => "○" };
                parts[i] = $"{dot} {StepLabels[i]}";
            }
            EditorGUILayout.LabelField(string.Join("  ─  ", parts), EditorStyles.boldLabel);
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
            EditorGUILayout.LabelField($"Step 1/{STEP_COUNT}: .NET SDK", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "서버 코드 빌드 검증에 사용됩니다.\n설치하면 배포 전에 에러를 미리 잡을 수 있습니다.",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (PrerequisiteChecker.IsDotnetInstalled())
            {
                var ver = PrerequisiteChecker.GetDotnetMajorVersion();
                EditorGUILayout.LabelField($"  ✓ .NET SDK {ver}.0 설치됨");
            }
            else
            {
                EditorGUILayout.LabelField("  ⚠ .NET SDK 미설치");
                GUILayout.Space(4);
                if (EditorGUILayout.LinkButton(".NET SDK 설치하기"))
                    Application.OpenURL("https://dotnet.microsoft.com/download");

                GUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "설치 후 [새로고침]을 눌러주세요.", EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("새로고침"))
                    PrerequisiteChecker.InvalidateCache();
            }

            EditorGUILayout.EndVertical();
        }

        void DrawSupabaseStep()
        {
            EditorGUILayout.LabelField($"Step 2/{STEP_COUNT}: Supabase 연결 (필수)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "게임 데이터를 저장할 데이터베이스입니다.\n무료로 시작할 수 있습니다.",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            _supabaseSetup.OnDraw();
        }

        void DrawGhStep()
        {
            EditorGUILayout.LabelField($"Step 3/{STEP_COUNT}: GitHub CLI", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("서버 코드를 GitHub에 push할 때 필요합니다.", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var gh = PrerequisiteChecker.CheckGh();

            if (gh.LoggedIn)
            {
                EditorGUILayout.LabelField($"  ✓ gh {gh.Version} 설치됨");
                EditorGUILayout.LabelField($"  ✓ {gh.Account} 로그인됨");
            }
            else if (gh.Installed)
            {
                EditorGUILayout.LabelField($"  ✓ gh {gh.Version} 설치됨");
                EditorGUILayout.LabelField("  ⚠ 로그인 필요");
                GUILayout.Space(4);
                if (GUILayout.Button("GitHub 로그인", GUILayout.Height(28)))
                    PrerequisiteChecker.RunGhLogin();
            }
            else
            {
                EditorGUILayout.LabelField("  ⚠ gh CLI 미설치");
                GUILayout.Space(4);
                if (EditorGUILayout.LinkButton("GitHub CLI 설치하기"))
                    Application.OpenURL("https://cli.github.com");
            }

            GUILayout.Space(4);
            if (GUILayout.Button("새로고침"))
                PrerequisiteChecker.InvalidateCache();

            EditorGUILayout.EndVertical();
        }

        void DrawGcloudStep()
        {
            EditorGUILayout.LabelField($"Step 4/{STEP_COUNT}: Google Cloud CLI", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Cloud Run 서버 배포에 필요합니다.", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var gcloud = PrerequisiteChecker.CheckGcloud();

            if (gcloud.LoggedIn)
            {
                EditorGUILayout.LabelField($"  ✓ gcloud {gcloud.Version} 설치됨");
                EditorGUILayout.LabelField($"  ✓ {gcloud.Account} 로그인됨");
            }
            else if (gcloud.Installed)
            {
                EditorGUILayout.LabelField($"  ✓ gcloud {gcloud.Version} 설치됨");
                EditorGUILayout.LabelField("  ⚠ 로그인 필요");
                GUILayout.Space(4);
                if (GUILayout.Button("Google 로그인", GUILayout.Height(28)))
                    PrerequisiteChecker.RunGcloudLogin();
            }
            else
            {
                EditorGUILayout.LabelField("  ⚠ gcloud CLI 미설치");
                GUILayout.Space(4);
                if (EditorGUILayout.LinkButton("gcloud CLI 설치하기"))
                    Application.OpenURL("https://cloud.google.com/sdk/docs/install");
            }

            GUILayout.Space(4);
            if (GUILayout.Button("새로고침"))
                PrerequisiteChecker.InvalidateCache();

            EditorGUILayout.EndVertical();
        }

        void DrawDeployStep()
        {
            EditorGUILayout.LabelField($"Step 5/{STEP_COUNT}: 배포 설정 (선택)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "서버를 Cloud Run에 배포할 때 필요합니다.\n개발은 LocalGameDB로 가능합니다.",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            _deploySetup.OnDraw();
        }

        // ── 네비게이션 ──

        void DrawNavigation()
        {
            EditorGUILayout.BeginHorizontal();

            // ← 이전
            if (_currentStep > 0)
            {
                if (GUILayout.Button("이전", GUILayout.Height(28)))
                {
                    _currentStep--;
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.FlexibleSpace();

            if (_currentStep == 1)
            {
                // Supabase: 연결 테스트 통과 필요
                using (new EditorGUI.DisabledGroupScope(!_supabaseSetup.IsCompleted))
                {
                    if (GUILayout.Button("다음", GUILayout.Height(28)))
                    {
                        _currentStep++;
                        GUIUtility.ExitGUI();
                    }
                }
                if (!_supabaseSetup.IsCompleted)
                    EditorGUILayout.LabelField("연결 테스트를 통과해야 합니다.", EditorStyles.wordWrappedMiniLabel);
            }
            else if (_currentStep == STEP_COUNT - 1)
            {
                // 마지막 스텝: 건너뛰기 + 완료
                if (GUILayout.Button("건너뛰기", GUILayout.Height(28)))
                {
                    _deploySetup.OnSkip();
                    _showCompletion = true;
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(8);
                if (GUILayout.Button("완료", GUILayout.Height(28)))
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
                    if (GUILayout.Button("건너뛰기", GUILayout.Height(28)))
                    {
                        _currentStep++;
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.Space(8);
                }
                if (GUILayout.Button("다음", GUILayout.Height(28)))
                {
                    _currentStep++;
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 완료 화면 ──

        void DrawCompletion()
        {
            var settings = SupaRunSettings.Instance;
            var gh = PrerequisiteChecker.CheckGh();
            var gcloud = PrerequisiteChecker.CheckGcloud();

            GUILayout.Space(20);
            EditorGUILayout.LabelField("설정 완료!", EditorStyles.boldLabel);
            GUILayout.Space(12);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
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
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);
            EditorGUILayout.LabelField("건너뛴 항목은 설정에서 언제든 설정할 수 있습니다.", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "지금 바로 Unity Play를 눌러보세요!\n" +
                "[UserData]와 [Service]를 작성하면\n" +
                "LocalGameDB로 즉시 테스트됩니다.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.Space(16);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("  대시보드 열기  ", GUILayout.Height(32)))
                _dashboard.OnSetupCompleted();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        static void DrawCompletionRow(string name, string status, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                ok ? $"  ✓ {name}" : $"  - {name}",
                GUILayout.Width(150));
            EditorGUILayout.LabelField(status);
            EditorGUILayout.EndHorizontal();
        }
    }
}
