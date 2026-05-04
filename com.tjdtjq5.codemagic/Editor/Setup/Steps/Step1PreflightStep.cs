#if UNITY_EDITOR
using System.Collections.Generic;
using Tjdtjq5.Codemagic.Editor.Git;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 1/6 — 사전 체크 (Unity 6+ / Git / GitHub remote).</summary>
    public sealed class Step1PreflightStep : ISetupStep
    {
        public string Title => "사전 체크";
        public bool IsCompleted =>
            _lastResult != null && PreflightChecker.AllPassed(_lastResult);
        public bool IsRequired => true;

        List<PreflightChecker.CheckItem> _lastResult;

        public void OnEnter(SetupContext ctx)
        {
            // 진입 시 1회 자동 검사 — 사용자가 [다시 검사] 누르면 재실행.
            GitHelpers.InvalidateCache();
            _lastResult = PreflightChecker.RunAll();
        }

        public void OnDraw(SetupContext ctx)
        {
            EditorUI.DrawSubLabel("Step 1/6: 사전 체크");
            EditorUI.DrawDescription(
                "Codemagic이 동작하기 위한 환경을 확인합니다.\n" +
                "Unity 6+ / Git 저장소 / GitHub remote 셋이 필요합니다.");

            GUILayout.Space(8);

            EditorUI.BeginBody();
            if (_lastResult == null || _lastResult.Count == 0)
            {
                EditorUI.DrawDescription("검사 결과가 없습니다.", EditorUI.COL_MUTED);
            }
            else
            {
                foreach (var item in _lastResult)
                {
                    var mark = item.Passed ? "✓" : "✗";
                    var color = item.Passed ? EditorUI.COL_SUCCESS : EditorUI.COL_ERROR;
                    EditorUI.DrawCellLabel($"  {mark} {item.Name}", 0, color);
                    if (!string.IsNullOrEmpty(item.Detail))
                        EditorUI.DrawDescription($"      {item.Detail}",
                            item.Passed ? EditorUI.COL_MUTED : EditorUI.COL_WARN);
                }
            }
            EditorUI.EndBody();

            GUILayout.Space(4);

            EditorUI.BeginRow();
            if (EditorUI.DrawColorButton("다시 검사", EditorUI.COL_INFO))
            {
                GitHelpers.InvalidateCache();
                _lastResult = PreflightChecker.RunAll();
                ctx.Notification = "사전 체크 재실행 완료.";
            }
            EditorUI.EndRow();

            // 통과 못 한 항목별 보충 가이드.
            if (_lastResult != null)
            {
                bool hasFail = false;
                foreach (var item in _lastResult) if (!item.Passed) { hasFail = true; break; }
                if (hasFail)
                {
                    GUILayout.Space(4);
                    EditorUI.BeginSubBox();
                    EditorUI.DrawCellLabel("해결 가이드", 0, EditorUI.COL_WARN);
                    GUILayout.Space(2);
                    EditorUI.DrawDescription(
                        "  ㆍ Unity 6+: 프로젝트를 Unity 6 이상으로 업그레이드하세요.\n" +
                        "  ㆍ Git 저장소: 프로젝트 루트에서 `git init` 후 첫 커밋을 만드세요.\n" +
                        "  ㆍ GitHub remote: GitHub 저장소를 만들고 `git remote add origin <url>` 후 push하세요.\n" +
                        "    Codemagic은 GitHub 저장소를 통해 빌드합니다.",
                        EditorUI.COL_MUTED);
                    EditorUI.EndSubBox();
                }
            }
        }

        public void OnLeave(SetupContext ctx) { }
    }
}
#endif
