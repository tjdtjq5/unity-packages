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
            var s = BuildAutomationSettings.Instance;

            // ── 플랫폼 설정 ──
            _platformStep.OnDraw();

            GUILayout.Space(8);

            // ── 배포/알림 설정 ──
            _deployStep.OnDraw();

            GUILayout.Space(8);

            // 캐시 설정은 CI/CD 탭에서 관리

            GUILayout.Space(8);

            // ── Unity 계정 (GitHub Secrets 등록 상태 표시) ──
            EditorUI.DrawSectionHeader("Unity 계정 (GitHub Secrets)", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();
            DrawUnityCredentialStatus();
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

        // ── Unity 계정 등록 상태 ──

        static readonly string[] _licenseSecrets = { "UNITY_LICENSE", "UNITY_EMAIL", "UNITY_PASSWORD" };

        void DrawUnityCredentialStatus()
        {
            if (!SecretRegistry.IsLoaded)
            {
                SecretRegistry.Load();
                EditorUI.DrawDescription("GitHub Secrets 상태 확인 중...", EditorUI.COL_MUTED);
                return;
            }

            var allRegistered = SecretRegistry.AllRegistered(_licenseSecrets);
            if (allRegistered)
            {
                foreach (var name in _licenseSecrets)
                    EditorUI.DrawCellLabel($"  ✓ {name} 등록됨", 0, EditorUI.COL_SUCCESS);

                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "팀의 GitHub Secrets에 등록된 값을 CI에서 사용합니다.\n" +
                    "변경하려면 SetupWizard에서 [재등록]을 실행하세요.",
                    EditorUI.COL_MUTED);
            }
            else
            {
                int registered = SecretRegistry.RegisteredCount(_licenseSecrets);
                EditorUI.DrawCellLabel(
                    $"  ⚠ 일부 Secret 미등록 ({registered}/{_licenseSecrets.Length})",
                    0, EditorUI.COL_WARN);

                GUILayout.Space(4);
                foreach (var name in _licenseSecrets)
                {
                    var ok = SecretRegistry.IsRegistered(name);
                    EditorUI.DrawCellLabel(
                        $"  {(ok ? "✓" : "✗")} {name}",
                        0, ok ? EditorUI.COL_SUCCESS : EditorUI.COL_ERROR);
                }

                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "UNITY_LICENSE / UNITY_EMAIL / UNITY_PASSWORD 세 가지가 모두 필요합니다.\n" +
                    "SetupWizard를 실행해 등록하세요.",
                    EditorUI.COL_WARN);
            }

            GUILayout.Space(4);
            EditorUI.BeginRow();
            if (EditorUI.DrawColorButton("새로고침", EditorUI.COL_MUTED))
            {
                SecretRegistry.Invalidate();
                SecretRegistry.Load();
            }
            EditorUI.EndRow();
        }
    }
}
#endif
