#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup
{
    /// <summary>
    /// 6단계 SetupWizard — Welcome / Preflight / Token / AppMatch / License / Keystore / Complete (총 7개).
    /// cicd `SetupWizard` 패턴 계승: Step 인디케이터 + Next/Back 네비게이션 + IsRequired 기반 Skip.
    /// </summary>
    public sealed class SetupWizard
    {
        public static readonly Color COL_PRIMARY = new(0.20f, 0.65f, 1f);

        readonly SetupContext _ctx;
        readonly ISetupStep[] _steps;
        readonly string[] _stepLabels;

        int _currentStep;
        Vector2 _scrollPos;

        public SetupContext Context => _ctx;

        public SetupWizard(SetupContext ctx, ISetupStep[] steps)
        {
            _ctx = ctx;
            _steps = steps;
            _stepLabels = new string[steps.Length];
            for (int i = 0; i < steps.Length; i++)
                _stepLabels[i] = steps[i].Title;

            _currentStep = Mathf.Clamp(ctx.State?.CurrentStep ?? 0, 0, steps.Length - 1);

            // Step 내부 버튼(Welcome의 [시작하기] 등)이 다음 단계 진행을 트리거할 수 있게 콜백 등록.
            _ctx.BindWizard(() => GoTo(_currentStep + 1));

            _steps[_currentStep].OnEnter(_ctx);
        }

        public void OnDraw()
        {
            // Welcome(Step 0)은 splash 모드 — 헤더/인디케이터/하단 navigation 모두 숨김.
            // 콘텐츠 자체가 큰 타이틀 + 가운데 [시작하기] 버튼 구성.
            bool isSplash = _currentStep == 0;

            // 상단 토스트 — 모든 step에서 노출. 빈 문자열이면 자체 처리로 미표시.
            // [Copy]/[X] 버튼은 EditorUI가 자동 제공 (suparun/cicd 패턴 일관).
            EditorUI.DrawNotificationBar(ref _ctx.Notification, _ctx.NotificationType);

            if (!isSplash)
            {
                EditorUI.DrawSectionHeader("Codemagic 셋업", COL_PRIMARY);
                GUILayout.Space(8);
                DrawStepIndicator();
                GUILayout.Space(8);
            }

            // Step 콘텐츠만 스크롤 영역 — 길어지면 여기만 스크롤
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            _steps[_currentStep].OnDraw(_ctx);
            EditorGUILayout.EndScrollView();

            if (!isSplash)
            {
                // Navigation — 윈도우 하단 고정 (스크롤 밖)
                GUILayout.Space(12);
                DrawNavigation();
            }
        }

        void DrawStepIndicator()
        {
            var states = new int[_steps.Length];
            for (int i = 0; i < _steps.Length; i++)
            {
                if (i < _currentStep)
                    states[i] = _steps[i].IsCompleted ? 2 : 3;  // 2=완료 / 3=건너뜀
                else if (i == _currentStep)
                    states[i] = 1;  // 현재
                else
                    states[i] = 0;  // 미진행
            }
            EditorUI.DrawStepIndicator(_stepLabels, states);
        }

        void DrawNavigation()
        {
            EditorUI.BeginRow();

            // ← 이전
            if (_currentStep > 0)
            {
                if (EditorUI.DrawColorButton("이전", EditorUI.COL_MUTED, 28))
                    GoTo(_currentStep - 1);
            }

            GUILayout.FlexibleSpace();

            var step = _steps[_currentStep];
            bool canProceed = !step.IsRequired || step.IsCompleted;
            bool isLast = _currentStep == _steps.Length - 1;

            if (!isLast)
            {
                if (!step.IsRequired)
                {
                    if (EditorUI.DrawColorButton("건너뛰기", EditorUI.COL_WARN, 28))
                        GoTo(_currentStep + 1);
                    GUILayout.Space(8);
                }

                EditorUI.BeginDisabled(!canProceed);
                if (EditorUI.DrawColorButton("다음 →", COL_PRIMARY, 28))
                    GoTo(_currentStep + 1);
                EditorUI.EndDisabled();
            }

            EditorUI.EndRow();

            if (!canProceed && step.IsRequired)
                EditorUI.DrawDescription("  위 항목을 완료해야 다음으로 진행할 수 있습니다.", EditorUI.COL_WARN);
        }

        void GoTo(int newIndex)
        {
            if (newIndex < 0 || newIndex >= _steps.Length) return;

            _steps[_currentStep].OnLeave(_ctx);
            _currentStep = newIndex;
            _ctx.State.CurrentStep = newIndex;
            _ctx.SaveState();
            _steps[_currentStep].OnEnter(_ctx);
        }
    }
}
#endif
