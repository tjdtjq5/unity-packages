#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 1: GameCI 소개 + gh CLI 설치 + 사전 조건 확인</summary>
    public class GameCIIntroStep : IWizardStep
    {
        public string StepLabel => "시작하기";
        public bool IsCompleted => _hasGitHubRepo && GhChecker.Check().LoggedIn;
        public bool IsRequired => true;

        bool _hasGitHubRepo;
        bool _showActions;
        bool _showCost;

        public GameCIIntroStep()
        {
            _hasGitHubRepo = !string.IsNullOrEmpty(GitHelper.GetGitHubRepo());
        }

        public void OnDraw()
        {
            EditorUI.DrawSubLabel("Step 1/6: 시작하기");
            EditorUI.DrawDescription(
                "GameCI는 GitHub가 대신 Unity 빌드를 해주는 무료 서비스입니다.\n" +
                "이 패키지는 GameCI 설정 파일을 자동으로 만들어줍니다.");

            GUILayout.Space(8);

            // ── FAQ ──
            if (EditorUI.DrawToggleRow("GitHub Actions란?", _showActions))
                _showActions = !_showActions;
            if (_showActions)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription(
                    "GitHub 서버가 코드를 대신 빌드/테스트/배포해주는 서비스입니다.\n" +
                    "프로젝트에 설정 파일(.yml)을 넣으면 자동으로 실행됩니다.\n\n" +
                    "이 패키지가 그 설정 파일을 자동으로 생성합니다.\n" +
                    "yml 문법을 알 필요 없습니다.");
                EditorUI.EndBody();
            }

            if (EditorUI.DrawToggleRow("비용은?", _showCost))
                _showCost = !_showCost;
            if (_showCost)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription(
                    "Public 리포: 무제한 무료\n" +
                    "Private 리포: 월 2,000분 무료 (Linux 러너 기준)\n" +
                    "iOS 빌드: macOS 러너 필요 (Linux 대비 10배 비쌈)\n\n" +
                    "솔로 개발자 기준 무료 범위 내에서 충분합니다.");
                EditorUI.EndBody();
            }

            GUILayout.Space(8);

            // ── GitHub 리포 감지 ──
            EditorUI.DrawSectionHeader("사전 조건", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            var repo = GitHelper.GetGitHubRepo();
            if (!string.IsNullOrEmpty(repo))
            {
                _hasGitHubRepo = true;
                EditorUI.DrawCellLabel($"  ✓ GitHub 리포 감지됨: {repo}", 0, EditorUI.COL_SUCCESS);
            }
            else
            {
                _hasGitHubRepo = false;
                EditorUI.DrawCellLabel("  ✗ GitHub 리포를 찾을 수 없습니다", 0, EditorUI.COL_ERROR);
                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "이 프로젝트를 GitHub에 올려야 CI/CD를 사용할 수 있습니다.",
                    EditorUI.COL_WARN);
            }

            EditorUI.EndBody();

            GUILayout.Space(4);

            // ── gh CLI 감지 ──
            EditorUI.DrawSectionHeader("gh CLI (필수)", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            var gh = GhChecker.Check();

            if (gh.LoggedIn)
            {
                EditorUI.DrawCellLabel($"  ✓ gh {gh.Version} 설치됨", 0, EditorUI.COL_SUCCESS);
                EditorUI.DrawCellLabel($"  ✓ {gh.Account} 로그인됨", 0, EditorUI.COL_SUCCESS);
            }
            else if (gh.Installed)
            {
                EditorUI.DrawCellLabel($"  ✓ gh {gh.Version} 설치됨", 0, EditorUI.COL_SUCCESS);
                EditorUI.DrawCellLabel("  ✗ 로그인 필요", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawColorButton("GitHub 로그인", BuildAutomationWindow.COL_PRIMARY, 28))
                    GhChecker.RunGhLogin();
            }
            else
            {
                EditorUI.DrawCellLabel("  ✗ gh CLI 미설치", 0, EditorUI.COL_ERROR);
                GUILayout.Space(4);
                EditorUI.DrawDescription(
                    "gh CLI가 있어야:\n" +
                    "• 에디터에서 빌드 시작 (Release)\n" +
                    "• 빌드 상태 모니터링\n" +
                    "• 다운로드 링크 확인\n" +
                    "이 가능합니다.", EditorUI.COL_MUTED);
                GUILayout.Space(4);
                if (EditorUI.DrawLinkButton("gh CLI 설치하기"))
                    Application.OpenURL("https://cli.github.com");
            }

            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("새로고침", EditorUI.COL_MUTED))
            {
                GitHelper.InvalidateCache();
                GhChecker.InvalidateCache();
                _hasGitHubRepo = !string.IsNullOrEmpty(GitHelper.GetGitHubRepo());
            }

            EditorUI.EndBody();
        }
    }
}
#endif
