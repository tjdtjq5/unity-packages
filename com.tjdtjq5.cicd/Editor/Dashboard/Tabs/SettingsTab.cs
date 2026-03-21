#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Settings 탭: 플랫폼/배포/알림 설정 변경</summary>
    public class SettingsTab
    {
        readonly BuildAutomationWindow _window;
        readonly PlatformStep _platformStep = new();
        readonly DeployTargetStep _deployStep = new();

        Vector2 _scrollPos;

        public SettingsTab(BuildAutomationWindow window)
        {
            _window = window;
        }

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // ── 플랫폼 설정 ──
            _platformStep.OnDraw();

            GUILayout.Space(8);

            // ── 배포/알림 설정 ──
            _deployStep.OnDraw();

            GUILayout.Space(8);

            // ── Unity 계정 ──
            EditorUI.DrawSectionHeader("Unity 계정", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();
            var s = BuildAutomationSettings.Instance;

            BuildAutomationSettings.UnityEmail =
                EditorUI.DrawTextField("이메일", BuildAutomationSettings.UnityEmail);
            BuildAutomationSettings.UnityPassword =
                EditorUI.DrawPasswordField("비밀번호", BuildAutomationSettings.UnityPassword);

            EditorUI.DrawDescription(
                "CI 서버에서 Unity 인증에 사용됩니다.\n" +
                "변경 시 Secret을 다시 등록해야 합니다. (SetupWizard 재실행)",
                EditorUI.COL_MUTED);
            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── yml 재생성 ──
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "플랫폼, 배포 대상, 알림을 변경하면 워크플로우를 재생성하세요.\n" +
                "Unity 계정 변경은 SetupWizard를 다시 실행하세요.",
                EditorUI.COL_WARN);
            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("워크플로우 재생성", EditorUI.COL_SUCCESS, 28))
            {
                var yml = WorkflowGenerator.Generate(s);
                var path = WorkflowGenerator.SaveToProject(yml);
                AssetDatabase.Refresh();
                _window.ShowNotification($"재생성됨: {path}", EditorUI.NotificationType.Success);
            }
            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── 위저드 재실행 ──
            EditorUI.BeginBody();
            if (EditorUI.DrawColorButton("SetupWizard 다시 실행", EditorUI.COL_MUTED, 24))
                _window.OpenSetup();
            EditorUI.EndBody();

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
