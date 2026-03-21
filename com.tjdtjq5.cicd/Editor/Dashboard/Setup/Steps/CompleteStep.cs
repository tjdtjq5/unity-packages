#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 6: yml 생성 + git push 자동 실행 + 완료</summary>
    public class CompleteStep : IWizardStep
    {
        public string StepLabel => "완료";
        public bool IsCompleted => _finished;
        public bool IsRequired => true;

        readonly BuildAutomationWindow _dashboard;

        bool _started;
        bool _finished;
        bool _ymlOk;
        bool _commitOk;
        bool _pushOk;
        string _error;
        string _ymlPath;

        public CompleteStep(BuildAutomationWindow dashboard)
        {
            _dashboard = dashboard;
        }

        public void OnDraw()
        {
            // 최초 진입 시 자동 실행
            if (!_started)
            {
                _started = true;
                RunSetup();
            }

            GUILayout.Space(20);

            if (!_finished)
            {
                // 진행 중
                EditorUI.DrawSectionHeader("설정 적용 중...", BuildAutomationWindow.COL_PRIMARY);
                EditorUI.BeginBody();
                DrawStepStatus("워크플로우 생성", _ymlOk);
                DrawStepStatus("git commit", _commitOk);
                DrawStepStatus("git push", _pushOk);
                EditorUI.DrawLoading(true, "잠시만 기다려주세요");
                EditorUI.EndBody();
            }
            else if (string.IsNullOrEmpty(_error))
            {
                // 성공
                EditorUI.DrawSectionHeader("설정 완료!", EditorUI.COL_SUCCESS);
                GUILayout.Space(12);

                EditorUI.BeginBody();
                DrawStepStatus("워크플로우 생성", true);
                DrawStepStatus("git commit", true);
                DrawStepStatus("git push", true);
                EditorUI.EndBody();

                GUILayout.Space(8);

                EditorUI.BeginBody();
                EditorUI.DrawDescription(
                    "모든 설정이 완료되었습니다!\n" +
                    "대시보드의 Release 버튼으로 빌드를 시작할 수 있습니다.",
                    EditorUI.COL_SUCCESS);
                EditorUI.EndBody();

                GUILayout.Space(16);
                EditorUI.BeginCenterRow();
                if (EditorUI.DrawColorButton("  대시보드 열기  ", BuildAutomationWindow.COL_PRIMARY, 32))
                    _dashboard.OnSetupCompleted();
                EditorUI.EndCenterRow();
            }
            else
            {
                // 실패
                EditorUI.DrawSectionHeader("설정 중 문제 발생", EditorUI.COL_WARN);
                GUILayout.Space(8);

                EditorUI.BeginBody();
                DrawStepStatus("워크플로우 생성", _ymlOk);
                DrawStepStatus("git commit", _commitOk);
                DrawStepStatus("git push", _pushOk);

                GUILayout.Space(4);
                EditorUI.DrawDescription($"  오류: {_error}", EditorUI.COL_ERROR);
                EditorUI.EndBody();

                GUILayout.Space(8);
                EditorUI.BeginRow();
                if (EditorUI.DrawColorButton("다시 시도", EditorUI.COL_WARN, 28))
                {
                    _started = false;
                    _finished = false;
                    _error = null;
                    _ymlOk = _commitOk = _pushOk = false;
                }
                GUILayout.Space(8);
                if (EditorUI.DrawColorButton("대시보드 열기 (건너뛰기)", EditorUI.COL_MUTED, 28))
                    _dashboard.OnSetupCompleted();
                EditorUI.EndRow();
            }
        }

        void RunSetup()
        {
            var settings = BuildAutomationSettings.Instance;

            // 1. yml 생성
            try
            {
                var yml = WorkflowGenerator.Generate(settings);
                _ymlPath = WorkflowGenerator.SaveToProject(yml);
                _ymlOk = true;
            }
            catch (System.Exception ex)
            {
                _error = $"yml 생성 실패: {ex.Message}";
                _finished = true;
                return;
            }

            // 2. git add + commit
            var addResult = GitHelper.RunGitWithCode("add .github/workflows/build-and-deploy.yml");
            if (addResult.exitCode != 0)
            {
                _error = $"git add 실패: {addResult.output}";
                _finished = true;
                return;
            }

            // 이미 커밋된 상태일 수 있음 (nothing to commit)
            var (commitCode, commitOutput) = GitHelper.RunGitWithCode(
                "commit -m \"Add CI/CD workflow (auto-generated)\"");
            _commitOk = commitCode == 0 || commitOutput.Contains("nothing to commit");

            if (!_commitOk)
            {
                _error = $"git commit 실패: {commitOutput}";
                _finished = true;
                return;
            }

            // 3. git push (비동기)
            System.Threading.Tasks.Task.Run(() =>
            {
                var (pushCode, pushOutput) = GitHelper.RunGitWithCode("push");
                EditorApplication.delayCall += () =>
                {
                    _pushOk = pushCode == 0;
                    if (!_pushOk)
                        _error = $"git push 실패: {pushOutput}";
                    _finished = true;
                    AssetDatabase.Refresh();
                };
            });
        }

        static void DrawStepStatus(string label, bool ok)
        {
            var icon = ok ? "✓" : "○";
            var color = ok ? EditorUI.COL_SUCCESS : EditorUI.COL_MUTED;
            EditorUI.DrawCellLabel($"  {icon} {label}", 0, color);
        }
    }
}
#endif
