using System.Linq;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    /// <summary>GitHub 설정 UI. Setup Step 2와 Settings에서 공용.</summary>
    public static class GitHubSetupUI
    {
        const string TOKEN_URL =
            "https://github.com/settings/tokens/new?scopes=repo,workflow&description=GameServer";

        enum Phase { NoCli, NotLoggedIn, NoConfig, Complete }

        static Phase GetPhase(PrerequisiteChecker.ToolStatus gh, GameServerSettings s)
        {
            if (!gh.Installed) return Phase.NoCli;
            if (!gh.LoggedIn) return Phase.NotLoggedIn;
            if (!s.IsGitHubConfigured) return Phase.NoConfig;
            return Phase.Complete;
        }

        public static void Draw(GameServerDashboard dashboard, GameServerSettings settings)
        {
            var gh = PrerequisiteChecker.CheckGh();
            var phase = GetPhase(gh, settings);

            // 완료된 단계 요약
            if (phase > Phase.NoCli)
                EditorUI.DrawCellLabel($"  \u2713 gh ({gh.Version})", 0, EditorUI.COL_SUCCESS);
            if (phase > Phase.NotLoggedIn)
                EditorUI.DrawCellLabel($"  \u2713 {gh.Account}", 0, EditorUI.COL_SUCCESS);
            if (phase == Phase.Complete)
            {
                EditorUI.DrawCellLabel($"  \u2713 {settings.githubRepoName}", 0, EditorUI.COL_SUCCESS);
                GUILayout.Space(2);
                if (EditorUI.DrawLinkButton($"GitHub ({gh.Account}/{settings.githubRepoName})"))
                    Application.OpenURL($"https://github.com/{gh.Account}/{settings.githubRepoName}");
                return;
            }

            GUILayout.Space(4);

            switch (phase)
            {
                case Phase.NoCli:
                    DrawCliInstall();
                    break;
                case Phase.NotLoggedIn:
                    DrawLogin();
                    break;
                case Phase.NoConfig:
                    DrawConfig(dashboard, settings, gh);
                    break;
            }
        }

        static void DrawCliInstall()
        {
            EditorUI.DrawDescription("gh CLI를 설치하세요.\nGitHub 연동에 필요한 도구입니다.");
            GUILayout.Space(4);
            if (EditorUI.DrawLinkButton("gh CLI 설치하기"))
                Application.OpenURL("https://cli.github.com");
        }

        static void DrawLogin()
        {
            EditorUI.DrawDescription("GitHub 계정으로 로그인하세요.");
            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("로그인", EditorUI.COL_INFO, 28))
                PrerequisiteChecker.RunGhLogin();
        }

        static void DrawConfig(GameServerDashboard dashboard,
            GameServerSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            // Token
            EditorUI.DrawDescription("Token을 생성하세요. 아래 링크는 권한이 미리 세팅됩니다.");
            if (EditorUI.DrawLinkButton("토큰 생성 (권한 자동 세팅)"))
                Application.OpenURL(TOKEN_URL);
            GUILayout.Space(2);
            var token = EditorUI.DrawPasswordField("Token", GameServerSettings.GithubToken, "서버 레포 접근용");
            if (token != GameServerSettings.GithubToken)
                GameServerSettings.GithubToken = token;
            EditorUI.DrawCellLabel("  * 로컬에만 저장됩니다", 0, EditorUI.COL_MUTED);

            GUILayout.Space(6);

            // Repo — 드롭다운 or 새로 만들기
            EditorUI.DrawDescription("서버 코드를 저장할 Repository를 선택하세요.");
            var repos = PrerequisiteChecker.GetGhRepos();

            if (repos.Length > 0)
            {
                var repoLabels = repos.Append("+ 새 레포 만들기").ToArray();
                var currentIdx = -1;
                for (int i = 0; i < repos.Length; i++)
                {
                    if (repos[i] == settings.githubRepoName)
                    { currentIdx = i; break; }
                }

                // 기본값 제안
                if (currentIdx < 0)
                {
                    var defaultName = PlayerSettings.productName.Replace(" ", "") + "-server";
                    for (int i = 0; i < repos.Length; i++)
                    {
                        if (repos[i] == defaultName)
                        { currentIdx = i; break; }
                    }
                }
                if (currentIdx < 0) currentIdx = repos.Length; // "새로 만들기" 선택

                var newIdx = EditorUI.DrawPopup("Repository", currentIdx, repoLabels);

                if (newIdx < repos.Length)
                {
                    settings.githubRepoName = repos[newIdx];
                    settings.Save();
                }
                else
                {
                    // 새로 만들기 → 이름 입력
                    DrawNewRepoInput(settings);
                }
            }
            else
            {
                DrawNewRepoInput(settings);
            }
        }

        static void DrawNewRepoInput(GameServerSettings settings)
        {
            using (var so = new SerializedObject(settings))
            {
                so.Update();
                if (string.IsNullOrEmpty(settings.githubRepoName))
                {
                    so.FindProperty("githubRepoName").stringValue =
                        PlayerSettings.productName.Replace(" ", "") + "-server";
                }
                EditorGUILayout.PropertyField(so.FindProperty("githubRepoName"),
                    new GUIContent("Repo Name", "서버 코드 저장소 (자동 생성됨)"));
                so.ApplyModifiedProperties();
            }
            EditorUI.DrawCellLabel("  * [배포] 시 자동 생성됩니다", 0, EditorUI.COL_MUTED);
        }
    }
}
