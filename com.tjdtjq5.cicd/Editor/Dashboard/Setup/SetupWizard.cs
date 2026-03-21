#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    public class SetupWizard
    {
        readonly BuildAutomationWindow _dashboard;
        readonly IWizardStep[] _steps;

        int _currentStep;
        bool _showCompletion;
        Vector2 _scrollPos;

        static readonly string[] StepLabels = { "소개", "라이선스", "플랫폼", "배포", "Secrets", "완료" };
        int StepCount => _steps.Length;

        public SetupWizard(BuildAutomationWindow dashboard)
        {
            _dashboard = dashboard;
            _steps = new IWizardStep[]
            {
                new GameCIIntroStep(),
                new LicenseStep(),
                new PlatformStep(),
                new DeployTargetStep(),
                new SecretsGuideStep(),
                new CompleteStep(dashboard)
            };
        }

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorUI.DrawSectionHeader("시작하기", BuildAutomationWindow.COL_PRIMARY);
            GUILayout.Space(8);

            if (_showCompletion)
            {
                _steps[StepCount - 1].OnDraw();
            }
            else
            {
                DrawStepIndicator();
                GUILayout.Space(8);
                _steps[_currentStep].OnDraw();
                GUILayout.Space(12);
                DrawNavigation();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawStepIndicator()
        {
            var states = new int[StepCount];
            for (int i = 0; i < StepCount; i++)
            {
                if (i < _currentStep)
                    states[i] = _steps[i].IsCompleted ? 2 : 3; // 2=완료, 3=건너뜀
                else if (i == _currentStep)
                    states[i] = 1; // 현재
                else
                    states[i] = 0; // 미진행
            }
            EditorUI.DrawStepIndicator(StepLabels, states);
        }

        void DrawNavigation()
        {
            EditorUI.BeginRow();

            // ← 이전
            if (_currentStep > 0)
            {
                if (EditorUI.DrawColorButton("이전", EditorUI.COL_MUTED, 28))
                    _currentStep--;
            }

            EditorUI.FlexSpace();

            var step = _steps[_currentStep];
            bool canProceed = !step.IsRequired || step.IsCompleted;

            if (_currentStep == StepCount - 2) // Secrets 스텝 (마지막 전)
            {
                EditorUI.BeginDisabled(!canProceed);
                if (EditorUI.DrawColorButton("완료 →", EditorUI.COL_SUCCESS, 28))
                    _showCompletion = true;
                EditorUI.EndDisabled();
            }
            else if (_currentStep < StepCount - 2)
            {
                // 필수 아닌 스텝은 건너뛰기 가능
                if (!step.IsRequired)
                {
                    if (EditorUI.DrawColorButton("건너뛰기", EditorUI.COL_WARN, 28))
                        _currentStep++;
                    GUILayout.Space(8);
                }

                // 다음
                EditorUI.BeginDisabled(!canProceed);
                if (EditorUI.DrawColorButton("다음 →", BuildAutomationWindow.COL_PRIMARY, 28))
                    _currentStep++;
                EditorUI.EndDisabled();
            }

            EditorUI.EndRow();

            if (!canProceed && step.IsRequired)
                EditorUI.DrawDescription("  위 항목을 완료해야 다음으로 진행할 수 있습니다.", EditorUI.COL_WARN);
        }
    }
}
#endif
