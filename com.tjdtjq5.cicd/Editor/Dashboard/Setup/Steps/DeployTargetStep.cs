#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 4: 배포 대상 선택 + 웹훅 알림 설정</summary>
    public class DeployTargetStep : IWizardStep
    {
        public string StepLabel => "배포";
        public bool IsCompleted => BuildAutomationSettings.Instance.HasAnyDeploy;
        public bool IsRequired => false; // GitHub Releases가 기본이라 건너뛰기 가능

        public void OnDraw()
        {
            var s = BuildAutomationSettings.Instance;

            EditorUI.DrawSubLabel("Step 4/6: 배포 대상 선택");
            EditorUI.DrawDescription(
                "빌드 완료 후 어디에 배포할지 선택하세요.\n" +
                "GitHub Releases는 기본 활성입니다.");

            GUILayout.Space(8);

            // ── 배포 대상 ──
            EditorUI.DrawSectionHeader("배포 대상", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            // GitHub Releases (항상 활성)
            EditorUI.BeginDisabled(true);
            EditorGUILayout.Toggle("✓ GitHub Releases (기본)", true);
            EditorUI.EndDisabled();
            EditorUI.DrawDescription(
                "빌드 아티팩트를 GitHub Releases에 자동 업로드합니다.\n" +
                "누구나 다운로드 링크로 접근할 수 있습니다.", EditorUI.COL_MUTED);

            GUILayout.Space(8);

            // Google Play
            EditorUI.BeginDisabled(!s.enableAndroid);
            s.deployGooglePlay = EditorGUILayout.Toggle("Google Play", s.deployGooglePlay && s.enableAndroid);
            EditorUI.EndDisabled();
            if (!s.enableAndroid && s.deployGooglePlay)
                s.deployGooglePlay = false;
            if (!s.enableAndroid)
                EditorUI.DrawDescription("Android 플랫폼을 먼저 선택하세요.", EditorUI.COL_MUTED);

            // App Store
            EditorUI.BeginDisabled(!s.enableIOS);
            s.deployAppStore = EditorGUILayout.Toggle("App Store (TestFlight)", s.deployAppStore && s.enableIOS);
            EditorUI.EndDisabled();
            if (!s.enableIOS && s.deployAppStore)
                s.deployAppStore = false;
            if (!s.enableIOS)
                EditorUI.DrawDescription("iOS 플랫폼을 먼저 선택하세요.", EditorUI.COL_MUTED);

            // Steam
            EditorUI.BeginDisabled(!s.enableWindows);
            s.deploySteam = EditorGUILayout.Toggle("Steam", s.deploySteam && s.enableWindows);
            EditorUI.EndDisabled();
            if (!s.enableWindows && s.deploySteam)
                s.deploySteam = false;
            if (!s.enableWindows)
                EditorUI.DrawDescription("Windows 플랫폼을 먼저 선택하세요.", EditorUI.COL_MUTED);

            if (s.deploySteam)
            {
                EditorGUI.indentLevel++;
                s.steamAppId = EditorUI.DrawTextField("Steam App ID", s.steamAppId,
                    "Steamworks 대시보드에서 확인 (비워두면 Secret으로 관리)");
                s.steamDepotId = EditorUI.DrawTextField("Steam Depot ID", s.steamDepotId);
                if (EditorUI.DrawLinkButton("Steamworks 대시보드에서 확인"))
                    Application.OpenURL("https://partner.steamgames.com/apps");
                EditorGUI.indentLevel--;
            }

            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── 알림 ──
            EditorUI.DrawSectionHeader("빌드 완료 알림", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            EditorUI.DrawDescription(
                "빌드 완료 시 다운로드 링크와 함께 알림을 보냅니다.", EditorUI.COL_MUTED);
            GUILayout.Space(4);

            s.notifyChannel = (NotifyChannel)EditorGUILayout.EnumPopup("알림 채널", s.notifyChannel);

            if (s.notifyChannel != NotifyChannel.None)
            {
                s.webhookUrl = EditorUI.DrawTextField("웹훅 URL", s.webhookUrl,
                    $"{s.notifyChannel} 웹훅 URL을 입력하세요");

                if (string.IsNullOrEmpty(s.webhookUrl))
                {
                    EditorUI.DrawDescription(
                        "⚠ 웹훅 URL을 입력하지 않으면 알림이 발송되지 않습니다.",
                        EditorUI.COL_WARN);
                }

                if (s.notifyChannel == NotifyChannel.Discord)
                {
                    if (EditorUI.DrawLinkButton("Discord 웹훅 만드는 방법"))
                        Application.OpenURL("https://support.discord.com/hc/en-us/articles/228383668");
                }
                else if (s.notifyChannel == NotifyChannel.Slack)
                {
                    if (EditorUI.DrawLinkButton("Slack Incoming Webhooks 설정"))
                        Application.OpenURL("https://api.slack.com/messaging/webhooks");
                }
            }

            EditorUI.EndBody();

            if (GUI.changed) s.Save();
        }
    }
}
#endif
