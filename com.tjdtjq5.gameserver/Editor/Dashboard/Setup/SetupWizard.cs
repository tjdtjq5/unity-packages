using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class SetupWizard
    {
        readonly GameServerDashboard _dashboard;
        readonly ISetupStep[] _steps;
        int _currentStep;
        bool _showCompletion;
        Vector2 _scrollPos;

        static readonly string[] StepLabels = { "Supabase", "GCP", "GitHub" };

        public SetupWizard(GameServerDashboard dashboard)
        {
            _dashboard = dashboard;
            _steps = new ISetupStep[]
            {
                new SupabaseSetupStep(dashboard),
                new GcpSetupStep(dashboard),
                new GitHubSetupStep(dashboard),
            };
        }

        public void Cleanup()
        {
            foreach (var step in _steps)
                if (step is SupabaseSetupStep supabase)
                    supabase.Cleanup();
        }

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

                // Step 제목 + 설명
                var step = _steps[_currentStep];
                EditorTabBase.DrawSubLabel(
                    $"Step {_currentStep + 1}/{_steps.Length}: {step.Title}" +
                    (step.IsRequired ? " (필수)" : " (선택)"));
                GUILayout.Space(2);
                EditorTabBase.DrawDescription(step.Description);
                GUILayout.Space(8);

                step.OnDraw();
                GUILayout.Space(12);
                DrawNavigation();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawStepIndicator()
        {
            var states = new int[_steps.Length];
            for (int i = 0; i < _steps.Length; i++)
            {
                if (_steps[i].IsCompleted) states[i] = 2;
                else if (_steps[i].IsSkipped) states[i] = 3;
                else if (i == _currentStep) states[i] = 1;
                else states[i] = 0;
            }
            EditorTabBase.DrawStepIndicator(StepLabels, states);
        }

        void DrawNavigation()
        {
            var step = _steps[_currentStep];

            EditorGUILayout.BeginHorizontal();

            // ← 이전
            if (_currentStep > 0)
            {
                if (EditorTabBase.DrawColorBtn("← 이전", EditorTabBase.COL_MUTED, 28))
                    _currentStep--;
            }

            GUILayout.FlexibleSpace();

            // 건너뛰기 (선택 Step만)
            if (!step.IsRequired)
            {
                if (EditorTabBase.DrawColorBtn("건너뛰기", EditorTabBase.COL_WARN, 28))
                {
                    step.OnSkip();
                    GoNext();
                }
                GUILayout.Space(8);
            }

            // 다음 → / 완료
            bool isLast = _currentStep >= _steps.Length - 1;
            string nextLabel = isLast ? "완료" : "다음 →";
            Color nextColor = isLast ? EditorTabBase.COL_SUCCESS : GameServerDashboard.COL_PRIMARY;

            // 필수 Step은 완료되어야 다음 가능
            bool canProceed = !step.IsRequired || step.IsCompleted;

            using (new EditorGUI.DisabledGroupScope(!canProceed))
            {
                if (EditorTabBase.DrawColorBtn(nextLabel, nextColor, 28))
                    GoNext();
            }

            EditorGUILayout.EndHorizontal();

            if (step.IsRequired && !step.IsCompleted)
                EditorTabBase.DrawDescription("ⓘ 연결 테스트를 통과해야 다음으로 진행할 수 있습니다.",
                    EditorTabBase.COL_WARN);
        }

        void GoNext()
        {
            if (_currentStep < _steps.Length - 1)
                _currentStep++;
            else
                _showCompletion = true;
        }

        void DrawCompletion()
        {
            GUILayout.Space(20);
            EditorTabBase.DrawSectionHeader("✅ 설정 완료!", EditorTabBase.COL_SUCCESS);
            GUILayout.Space(12);

            var settings = GameServerSettings.Instance;

            EditorTabBase.BeginBody();
            DrawCompletionRow("Supabase",
                settings.IsSupabaseConfigured ? "Connected" : "미설정",
                settings.IsSupabaseConfigured);
            DrawCompletionRow("Google Cloud",
                settings.IsGcpConfigured ? $"Project: {settings.gcpProjectId}" : "건너뜀",
                settings.IsGcpConfigured);
            DrawCompletionRow("GitHub",
                settings.IsGitHubConfigured ? $"Repo: {settings.githubRepoName}" : "건너뜀",
                settings.IsGitHubConfigured);
            EditorTabBase.EndBody();

            GUILayout.Space(4);
            EditorTabBase.DrawDescription("건너뛴 항목은 ⚙ 버튼에서 언제든 설정할 수 있습니다.");
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
