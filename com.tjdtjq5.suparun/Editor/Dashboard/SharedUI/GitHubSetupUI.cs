using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>GitHub 설정 UI. Setup Step 2와 Settings에서 공용.</summary>
    public static class GitHubSetupUI
    {
        const string TOKEN_URL =
            "https://github.com/settings/tokens/new?scopes=repo,workflow&description=SupaRun";

        static bool _repoCreated;
        static string _checkedRepoKey;

        /// <summary>레포가 생성 완료 상태인지. GcpSetupUI에서 참조.</summary>
        public static bool IsRepoReady => _repoCreated;

        enum Phase { NoCli, NotLoggedIn, NoConfig, Complete }

        static Phase GetPhase(PrerequisiteChecker.ToolStatus gh, SupaRunSettings s)
        {
            if (!gh.Installed) return Phase.NoCli;
            if (!gh.LoggedIn) return Phase.NotLoggedIn;
            if (!s.IsGitHubConfigured) return Phase.NoConfig;
            return Phase.Complete;
        }

        public static void Draw(SupaRunDashboard dashboard, SupaRunSettings settings)
        {
            var gh = PrerequisiteChecker.CheckGh();
            var phase = GetPhase(gh, settings);

            // 완료된 단계 요약
            if (phase > Phase.NoCli)
                EditorGUILayout.LabelField($"  ✓ gh ({gh.Version})");
            if (phase > Phase.NotLoggedIn)
                EditorGUILayout.LabelField($"  ✓ {gh.Account}");
            if (phase == Phase.Complete)
            {
                var repo = $"{gh.Account}/{settings.githubRepoName}";
                if (_checkedRepoKey != repo)
                {
                    _checkedRepoKey = repo;
                    _repoCreated = PrerequisiteChecker.RepoExists(repo);
                }
                if (_repoCreated)
                {
                    EditorGUILayout.LabelField($"  ✓ {settings.githubRepoName}");
                    GUILayout.Space(2);
                    if (EditorGUILayout.LinkButton($"GitHub ({repo})"))
                        Application.OpenURL($"https://github.com/{repo}");
                }
                else
                {
                    EditorGUILayout.LabelField($"  ⚠ {settings.githubRepoName} (레포 미생성)");
                    GUILayout.Space(4);
                    if (GUILayout.Button($"'{settings.githubRepoName}' 레포 생성", GUILayout.Height(28)))
                    {
                        var (ok, existed, err) = PrerequisiteChecker.EnsureRepoExists(repo);
                        if (ok)
                        {
                            _repoCreated = true;
                            var msg = existed ? "GitHub 레포 확인 완료 (이미 존재)" : "GitHub 레포 생성 완료!";
                            dashboard.ShowNotification(msg, SupaRunUI.NotificationType.Success);
                        }
                        else
                        {
                            dashboard.ShowNotification(err, SupaRunUI.NotificationType.Error);
                        }
                    }
                }
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
            EditorGUILayout.LabelField("gh CLI를 설치하세요.\nGitHub 연동에 필요한 도구입니다.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);
            if (EditorGUILayout.LinkButton("gh CLI 설치하기"))
                Application.OpenURL("https://cli.github.com");
        }

        static void DrawLogin()
        {
            EditorGUILayout.LabelField("GitHub 계정으로 로그인하세요.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);
            if (GUILayout.Button("로그인", GUILayout.Height(28)))
                PrerequisiteChecker.RunGhLogin();
        }

        static void DrawConfig(SupaRunDashboard dashboard,
            SupaRunSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            // Token
            EditorGUILayout.LabelField("Token을 생성하세요. 아래 링크는 권한이 미리 세팅됩니다.", EditorStyles.wordWrappedMiniLabel);
            if (EditorGUILayout.LinkButton("토큰 생성 (권한 자동 세팅)"))
                Application.OpenURL(TOKEN_URL);
            GUILayout.Space(2);
            var token = EditorGUILayout.PasswordField(
                new GUIContent("Token", "서버 레포 접근용"),
                SupaRunSettings.Instance.GithubToken);
            if (token != SupaRunSettings.Instance.GithubToken)
                SupaRunSettings.Instance.GithubToken = token;
            EditorGUILayout.LabelField("  * 로컬에만 저장됩니다");

            GUILayout.Space(6);

            // Repo — 드롭다운 or 새로 만들기
            EditorGUILayout.LabelField("서버 코드를 저장할 Repository를 선택하세요.", EditorStyles.wordWrappedMiniLabel);
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

                var newIdx = EditorGUILayout.Popup("Repository", currentIdx, repoLabels);

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

        static void DrawNewRepoInput(SupaRunSettings settings)
        {
            if (string.IsNullOrEmpty(settings.githubRepoName))
                settings.githubRepoName = PlayerSettings.productName.Replace(" ", "") + "-server";

            var newName = EditorGUILayout.TextField(
                new GUIContent("Repo Name", "서버 코드 저장소 (자동 생성됨)"),
                settings.githubRepoName);
            if (newName != settings.githubRepoName)
            {
                settings.githubRepoName = newName;
                settings.Save();
            }
            EditorGUILayout.LabelField("  * GitHub 설정 완료 후 레포 생성 버튼이 표시됩니다");
        }
    }
}
