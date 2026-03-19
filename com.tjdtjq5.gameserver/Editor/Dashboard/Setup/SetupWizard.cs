using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class SetupWizard
    {
        readonly GameServerDashboard _dashboard;
        readonly SupabaseSetup _supabaseSetup;
        readonly DeploySetup _deploySetup;
        int _currentStep; // 0=Supabase, 1=Deploy
        bool _showCompletion;
        Vector2 _scrollPos;

        static readonly string[] StepLabels = { "Supabase", "배포 설정" };

        public SetupWizard(GameServerDashboard dashboard)
        {
            _dashboard = dashboard;
            _supabaseSetup = new SupabaseSetup(dashboard);
            _deploySetup = new DeploySetup(dashboard);
        }

        public void Cleanup() => _supabaseSetup.Cleanup();

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorTabBase.DrawSectionHeader("🚀 시작하기", GameServerDashboard.COL_PRIMARY);
            GUILayout.Space(8);

            if (_showCompletion)
            {
                DrawCompletion();
            }
            else
            {
                DrawStepIndicator();
                GUILayout.Space(8);

                // Step 제목
                string stepTitle = _currentStep == 0
                    ? "Step 1/2: Supabase 연결 (필수)"
                    : "Step 2/2: 배포 설정 (선택)";
                EditorTabBase.DrawSubLabel(stepTitle);

                string desc = _currentStep == 0
                    ? "게임 데이터를 저장할 데이터베이스입니다. 무료로 시작할 수 있습니다."
                    : "서버를 Cloud Run에 배포할 때 필요합니다. 개발은 LocalGameDB로 가능합니다.";
                EditorTabBase.DrawDescription(desc);

                GUILayout.Space(8);

                if (_currentStep == 0)
                    _supabaseSetup.OnDraw();
                else
                    _deploySetup.OnDraw();

                GUILayout.Space(12);
                DrawNavigation();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawStepIndicator()
        {
            var states = new int[2];
            // Supabase
            states[0] = _supabaseSetup.IsCompleted ? 2 : (_currentStep == 0 ? 1 : 0);
            // Deploy
            states[1] = _deploySetup.IsSkipped ? 3 : (_currentStep == 1 ? 1 : 0);

            EditorTabBase.DrawStepIndicator(StepLabels, states);
        }

        void DrawNavigation()
        {
            EditorGUILayout.BeginHorizontal();

            // ← 이전
            if (_currentStep > 0)
            {
                if (EditorTabBase.DrawColorBtn("← 이전", EditorTabBase.COL_MUTED, 28))
                    _currentStep = 0;
            }

            GUILayout.FlexibleSpace();

            if (_currentStep == 0)
            {
                // Step 1: 다음 (연결 테스트 통과 필요)
                using (new UnityEditor.EditorGUI.DisabledGroupScope(!_supabaseSetup.IsCompleted))
                {
                    if (EditorTabBase.DrawColorBtn("다음 →", GameServerDashboard.COL_PRIMARY, 28))
                        _currentStep = 1;
                }
                if (!_supabaseSetup.IsCompleted)
                    EditorTabBase.DrawDescription("연결 테스트를 통과해야 다음으로 진행할 수 있습니다.", EditorTabBase.COL_WARN);
            }
            else
            {
                // Step 2: 건너뛰기 + 완료
                if (EditorTabBase.DrawColorBtn("건너뛰기", EditorTabBase.COL_WARN, 28))
                {
                    _deploySetup.OnSkip();
                    _showCompletion = true;
                }
                GUILayout.Space(8);
                if (EditorTabBase.DrawColorBtn("완료", EditorTabBase.COL_SUCCESS, 28))
                    _showCompletion = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawCompletion()
        {
            var settings = GameServerSettings.Instance;

            GUILayout.Space(20);
            EditorTabBase.DrawSectionHeader("✅ 설정 완료!", EditorTabBase.COL_SUCCESS);
            GUILayout.Space(12);

            EditorTabBase.BeginBody();
            DrawCompletionRow("Supabase", "Connected", true);
            DrawCompletionRow("GitHub",
                settings.IsGitHubConfigured ? $"Repo: {settings.githubRepoName}" : "건너뜀",
                settings.IsGitHubConfigured);
            DrawCompletionRow("Google Cloud",
                settings.IsGcpConfigured ? $"Project: {settings.gcpProjectId}" : "건너뜀",
                settings.IsGcpConfigured);
            EditorTabBase.EndBody();

            GUILayout.Space(4);
            EditorTabBase.DrawDescription("건너뛴 항목은 ⚙ 버튼에서 언제든 설정할 수 있습니다.");

            GUILayout.Space(8);
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "지금 바로 Unity Play를 눌러보세요!\n" +
                "[TableData]와 [ServerLogic]을 작성하면\n" +
                "LocalGameDB로 즉시 테스트됩니다.", EditorTabBase.COL_INFO);
            EditorTabBase.EndBody();

            GUILayout.Space(16);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (EditorTabBase.DrawColorBtn("  대시보드 열기  ", GameServerDashboard.COL_PRIMARY, 32))
                _dashboard.OnSetupCompleted();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        static void DrawCompletionRow(string name, string status, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            EditorTabBase.DrawCellLabel(
                ok ? $"  ✓ {name}" : $"  △ {name}",
                150, ok ? EditorTabBase.COL_SUCCESS : EditorTabBase.COL_WARN);
            EditorTabBase.DrawCellLabel(status, 0, EditorTabBase.COL_MUTED);
            EditorGUILayout.EndHorizontal();
        }
    }
}
