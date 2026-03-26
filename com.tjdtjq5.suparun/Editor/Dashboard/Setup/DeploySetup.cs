using Tjdtjq5.EditorToolkit.Editor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class DeploySetup
    {
        readonly SupaRunDashboard _dashboard;
        public bool IsSkipped { get; private set; }

        public DeploySetup(SupaRunDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = SupaRunSettings.Instance;

            // 설정하면?/안하면?
            EditorUI.DrawInfoBox(
                new[]
                {
                    "서버를 인터넷에 배포 가능",
                    "다른 사람이 게임에 접속 가능",
                    "테스트 단계 무료 (월 200만 요청)",
                },
                new[]
                {
                    "Unity Play에서 LocalGameDB로 개발 가능",
                    "나중에 설정에서 언제든 설정 가능",
                });

            GUILayout.Space(8);

            // ── GitHub ──
            EditorUI.DrawSectionHeader("GitHub", SupaRunDashboard.COL_GITHUB);
            EditorUI.BeginBody();
            GitHubSetupUI.Draw(_dashboard, settings);
            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── Google Cloud ──
            EditorUI.DrawSectionHeader("Google Cloud", SupaRunDashboard.COL_GCP);
            EditorUI.BeginBody();
            GcpSetupUI.Draw(_dashboard, settings);
            EditorUI.EndBody();
        }

        public void OnSkip() => IsSkipped = true;
    }
}
