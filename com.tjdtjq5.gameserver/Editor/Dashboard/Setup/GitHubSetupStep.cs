using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class GitHubSetupStep : ISetupStep
    {
        readonly GameServerDashboard _dashboard;

        const string TOKEN_URL =
            "https://github.com/settings/tokens/new?scopes=repo,workflow&description=GameServer";

        enum RepoState { None, Creating, Created, Failed }
        RepoState _repoState;
        string _repoError;
        string _repoUrl;

        public string Title => "GitHub";
        public string Description => "서버 코드를 저장하고 자동 배포할 때 필요합니다.\n개발은 LocalGameDB로 가능하므로, 배포할 때 설정해도 됩니다.";
        public Color AccentColor => GameServerDashboard.COL_GITHUB;
        public bool IsRequired => false;
        public bool IsCompleted => GameServerSettings.Instance.IsGitHubConfigured;
        public bool IsSkipped { get; private set; }

        public GitHubSetupStep(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            // 설정하면?/안하면?
            EditorTabBase.DrawInfoBox(
                new[]
                {
                    "코드 푸시 시 서버 자동 배포 (CI/CD)",
                    "서버 코드 버전 관리",
                    "팀원과 협업 가능",
                },
                new[]
                {
                    "배포할 때마다 수동으로 해야 함",
                    "나중에 ⚙ 버튼에서 언제든 설정 가능",
                });

            GUILayout.Space(8);

            // ① Token
            EditorTabBase.DrawSubLabel("① Personal Access Token 생성");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "아래 링크를 클릭하면 필요한 권한이 미리 세팅된 토큰 생성 페이지가 열립니다.\nGenerate token만 클릭하세요.");
            GUILayout.Space(2);
            if (EditorTabBase.DrawLinkBtn("토큰 생성 페이지 (권한 자동 세팅)"))
                Application.OpenURL(TOKEN_URL);
            GUILayout.Space(4);

            var token = EditorGUILayout.PasswordField("Token", GameServerSettings.GithubToken);
            if (token != GameServerSettings.GithubToken)
                GameServerSettings.GithubToken = token;

            EditorTabBase.DrawDescription("ⓘ 토큰은 로컬에만 저장되며 외부로 전송되지 않습니다.");
            EditorTabBase.EndBody();

            GUILayout.Space(4);

            // ② gh CLI
            EditorTabBase.DrawSubLabel("② gh CLI");
            EditorTabBase.BeginBody();
            var gh = PrerequisiteChecker.CheckGh();
            EditorTabBase.DrawToolStatus("gh", gh.Installed, gh.Version, gh.LoggedIn, gh.Account);

            if (!gh.Installed)
            {
                if (EditorTabBase.DrawLinkBtn("gh CLI 설치하기"))
                    Application.OpenURL("https://cli.github.com");
            }
            else if (!gh.LoggedIn)
            {
                if (EditorTabBase.DrawColorBtn("로그인 실행", EditorTabBase.COL_INFO))
                    PrerequisiteChecker.RunGhLogin();
            }

            EditorTabBase.EndBody();

            GUILayout.Space(4);

            // ③ Repo
            EditorTabBase.DrawSubLabel("③ Repository");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription("서버 코드가 저장될 GitHub 레포입니다.");
            GUILayout.Space(2);

            // 기본값 제안
            if (string.IsNullOrEmpty(settings.githubRepoName))
            {
                var defaultName = PlayerSettings.productName.Replace(" ", "") + "-server";
                settings.githubRepoName = defaultName;
                settings.Save();
            }

            EditorGUILayout.PropertyField(so.FindProperty("githubRepoName"), new GUIContent("Repo Name"));
            EditorTabBase.DrawDescription("ⓘ private 레포로 생성됩니다.");

            GUILayout.Space(6);
            DrawRepoCreateButton(settings, gh);

            EditorTabBase.EndBody();

            so.ApplyModifiedProperties();
        }

        void DrawRepoCreateButton(GameServerSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            switch (_repoState)
            {
                case RepoState.None:
                    bool canCreate = gh.Installed && gh.LoggedIn &&
                                     !string.IsNullOrEmpty(settings.githubRepoName);
                    using (new EditorGUI.DisabledGroupScope(!canCreate))
                    {
                        if (EditorTabBase.DrawColorBtn("레포 생성", GameServerDashboard.COL_PRIMARY, 28))
                            CreateRepo(settings, gh);
                    }
                    if (!canCreate)
                    {
                        if (!gh.Installed)
                            EditorTabBase.DrawDescription("gh CLI를 설치하세요.", EditorTabBase.COL_WARN);
                        else if (!gh.LoggedIn)
                            EditorTabBase.DrawDescription("gh CLI에 로그인하세요.", EditorTabBase.COL_WARN);
                        else
                            EditorTabBase.DrawDescription("Repo Name을 입력하세요.", EditorTabBase.COL_WARN);
                    }
                    else
                    {
                        EditorTabBase.DrawDescription("지금 생성하지 않아도 나중에 초기화 시 자동 생성됩니다.");
                    }
                    break;

                case RepoState.Creating:
                    EditorTabBase.DrawLoading(true, "레포 생성 중...");
                    break;

                case RepoState.Created:
                    EditorTabBase.DrawDescription($"✓ 레포 생성 완료!", EditorTabBase.COL_SUCCESS);
                    if (!string.IsNullOrEmpty(_repoUrl))
                    {
                        if (EditorTabBase.DrawLinkBtn("GitHub에서 보기"))
                            Application.OpenURL(_repoUrl);
                    }
                    break;

                case RepoState.Failed:
                    EditorTabBase.DrawDescription($"✗ {_repoError}", EditorTabBase.COL_ERROR);
                    GUILayout.Space(4);
                    if (EditorTabBase.DrawColorBtn("다시 시도", EditorTabBase.COL_ERROR, 28))
                        CreateRepo(settings, PrerequisiteChecker.CheckGh());
                    break;
            }
        }

        System.Diagnostics.Process _repoProcess;
        string _pendingRepoName;
        string _pendingAccount;

        void CreateRepo(GameServerSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            _repoState = RepoState.Creating;
            _pendingRepoName = settings.githubRepoName;
            _pendingAccount = gh.Account;
            _dashboard.Repaint();

            try
            {
                _repoProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "gh",
                        Arguments = $"repo create {_pendingRepoName} --private --confirm",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                    EnableRaisingEvents = true,
                };
                _repoProcess.Start();
                EditorApplication.update += PollRepoCreate;
            }
            catch (System.Exception ex)
            {
                _repoState = RepoState.Failed;
                _repoError = $"프로세스 실행 실패: {ex.Message}";
                _dashboard.Repaint();
            }
        }

        void PollRepoCreate()
        {
            if (_repoProcess == null || !_repoProcess.HasExited) return;

            EditorApplication.update -= PollRepoCreate;

            var exitCode = _repoProcess.ExitCode;
            var stdout = _repoProcess.StandardOutput.ReadToEnd();
            var stderr = _repoProcess.StandardError.ReadToEnd();
            var output = string.IsNullOrEmpty(stdout) ? stderr : stdout;
            _repoProcess.Dispose();
            _repoProcess = null;

            if (exitCode == 0)
            {
                _repoState = RepoState.Created;
                _repoUrl = $"https://github.com/{_pendingAccount}/{_pendingRepoName}";
                GameServerSettings.Instance.Save();
            }
            else
            {
                _repoState = RepoState.Failed;
                if (output.Contains("already exists"))
                    _repoError = $"'{_pendingRepoName}' 레포가 이미 존재합니다.\n다른 이름을 사용하거나 기존 레포를 사용하세요.";
                else if (output.Contains("authentication"))
                    _repoError = "인증 실패. gh CLI 로그인을 확인하세요.";
                else
                    _repoError = $"레포 생성 실패:\n{output}";
            }

            _dashboard.Repaint();
        }

        public void OnSkip() => IsSkipped = true;
    }
}
