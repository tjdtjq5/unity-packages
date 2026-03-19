using System.Diagnostics;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class DeploySetup
    {
        readonly GameServerDashboard _dashboard;

        const string TOKEN_URL =
            "https://github.com/settings/tokens/new?scopes=repo,workflow&description=GameServer";

        enum RepoState { None, Creating, Created, Failed, ExistsOther }
        RepoState _repoState;
        string _repoError;
        string _repoUrl;
        Process _repoProcess;
        string _pendingAccount;

        public bool IsSkipped { get; private set; }

        public DeploySetup(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            // 설정하면?/안하면?
            EditorTabBase.DrawInfoBox(
                new[]
                {
                    "서버를 인터넷에 배포 가능",
                    "다른 사람이 게임에 접속 가능",
                    "테스트 단계 무료 (월 200만 요청)",
                },
                new[]
                {
                    "Unity Play에서 LocalGameDB로 개발 가능",
                    "나중에 ⚙ 버튼에서 언제든 설정 가능",
                });

            GUILayout.Space(8);

            // ── GitHub ──
            EditorTabBase.DrawSectionHeader("GitHub", GameServerDashboard.COL_GITHUB);
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription("서버 코드를 저장하고 자동 배포합니다.");

            GUILayout.Space(4);
            EditorTabBase.DrawSubLabel("① Token 생성");
            EditorTabBase.DrawDescription(
                "아래 링크를 클릭하면 필요한 권한이 미리 세팅된 토큰 생성 페이지가 열립니다.\nGenerate token만 클릭하세요.");
            if (EditorTabBase.DrawLinkBtn("토큰 생성 (권한 자동 세팅)"))
                Application.OpenURL(TOKEN_URL);
            GUILayout.Space(4);
            var token = EditorGUILayout.PasswordField("Token", GameServerSettings.GithubToken);
            if (token != GameServerSettings.GithubToken)
                GameServerSettings.GithubToken = token;
            EditorTabBase.DrawDescription("ⓘ 토큰은 로컬에만 저장됩니다.");

            GUILayout.Space(6);
            EditorTabBase.DrawSubLabel("② gh CLI");
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

            GUILayout.Space(6);
            EditorTabBase.DrawSubLabel("③ Repository");
            if (string.IsNullOrEmpty(settings.githubRepoName))
            {
                settings.githubRepoName = PlayerSettings.productName.Replace(" ", "") + "-server";
                settings.Save();
            }
            EditorGUILayout.PropertyField(so.FindProperty("githubRepoName"), new GUIContent("Repo Name"));
            EditorTabBase.DrawDescription("ⓘ private 레포로 생성됩니다.");
            GUILayout.Space(4);
            DrawRepoCreateButton(settings, gh);

            EditorTabBase.EndBody();

            GUILayout.Space(8);

            // ── Google Cloud ──
            EditorTabBase.DrawSectionHeader("Google Cloud", GameServerDashboard.COL_GCP);
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription("Cloud Run에 서버를 배포합니다.");

            GUILayout.Space(4);
            EditorTabBase.DrawSubLabel("① GCP 가입 + 결제 활성화");
            EditorTabBase.DrawDescription("결제 활성화 = 유료가 아닙니다.\n무료 할당량 내에서는 과금이 발생하지 않습니다.");
            EditorGUILayout.BeginHorizontal();
            if (EditorTabBase.DrawLinkBtn("GCP 콘솔", GameServerDashboard.COL_GCP))
                Application.OpenURL("https://console.cloud.google.com");
            if (EditorTabBase.DrawLinkBtn("무료 체험 ($300)", GameServerDashboard.COL_GCP))
                Application.OpenURL("https://cloud.google.com/free");
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorTabBase.DrawSubLabel("② 프로젝트");
            if (EditorTabBase.DrawLinkBtn("프로젝트 만들기", GameServerDashboard.COL_GCP))
                Application.OpenURL("https://console.cloud.google.com/projectcreate");
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(so.FindProperty("gcpProjectId"), new GUIContent("Project ID"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpRegion"), new GUIContent("Region"));
            EditorTabBase.DrawDescription("추천: asia-northeast3 (서울)");

            GUILayout.Space(6);
            EditorTabBase.DrawSubLabel("③ gcloud CLI");
            var gcloud = PrerequisiteChecker.CheckGcloud();
            EditorTabBase.DrawToolStatus("gcloud", gcloud.Installed, gcloud.Version, gcloud.LoggedIn, gcloud.Account);
            if (!gcloud.Installed)
            {
                if (EditorTabBase.DrawLinkBtn("gcloud CLI 설치하기", GameServerDashboard.COL_GCP))
                    Application.OpenURL("https://cloud.google.com/sdk/docs/install");
            }
            else if (!gcloud.LoggedIn)
            {
                if (EditorTabBase.DrawColorBtn("로그인 실행", GameServerDashboard.COL_GCP))
                    PrerequisiteChecker.RunGcloudLogin();
            }
            else if (!string.IsNullOrEmpty(settings.gcpProjectId) && gcloud.Project != settings.gcpProjectId)
            {
                if (EditorTabBase.DrawColorBtn("프로젝트 설정", GameServerDashboard.COL_GCP))
                    PrerequisiteChecker.SetGcloudProject(settings.gcpProjectId);
            }

            if (gcloud.Installed && gcloud.LoggedIn && !string.IsNullOrEmpty(gcloud.Project))
                EditorTabBase.DrawDescription($"✓ 프로젝트: {gcloud.Project}", EditorTabBase.COL_SUCCESS);

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
                    if (canCreate)
                        EditorTabBase.DrawDescription("지금 안 해도 나중에 배포 시 자동 생성됩니다.");
                    break;

                case RepoState.Creating:
                    EditorTabBase.DrawLoading(true, "레포 생성 중...");
                    break;

                case RepoState.Created:
                    EditorTabBase.DrawDescription("✓ 레포 생성 완료!", EditorTabBase.COL_SUCCESS);
                    if (!string.IsNullOrEmpty(_repoUrl))
                    {
                        if (EditorTabBase.DrawLinkBtn("GitHub에서 보기"))
                            Application.OpenURL(_repoUrl);
                    }
                    break;

                case RepoState.ExistsOther:
                    EditorTabBase.DrawDescription(
                        "⚠ 이 레포에 다른 코드가 있습니다.\n배포하면 기존 내용이 덮어씌워집니다.",
                        EditorTabBase.COL_WARN);
                    GUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    if (EditorTabBase.DrawColorBtn("연결 (덮어쓰기)", EditorTabBase.COL_WARN, 28))
                    {
                        _repoState = RepoState.Created;
                        _repoUrl = $"https://github.com/{_pendingAccount}/{settings.githubRepoName}";
                        settings.Save();
                        _dashboard.Repaint();
                    }
                    GUILayout.Space(8);
                    if (EditorTabBase.DrawColorBtn("취소", EditorTabBase.COL_MUTED, 28))
                    {
                        _repoState = RepoState.None;
                        _dashboard.Repaint();
                    }
                    EditorGUILayout.EndHorizontal();
                    break;

                case RepoState.Failed:
                    EditorTabBase.DrawDescription($"✗ {_repoError}", EditorTabBase.COL_ERROR);
                    GUILayout.Space(4);
                    if (EditorTabBase.DrawColorBtn("다시 시도", EditorTabBase.COL_ERROR, 28))
                        CreateRepo(settings, PrerequisiteChecker.CheckGh());
                    break;
            }
        }

        void CreateRepo(GameServerSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            _repoState = RepoState.Creating;
            _pendingAccount = gh.Account;
            _dashboard.Repaint();

            try
            {
                _repoProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "gh",
                        Arguments = $"repo create {settings.githubRepoName} --private --confirm",
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
            var settings = GameServerSettings.Instance;

            _repoProcess.Dispose();
            _repoProcess = null;

            if (exitCode == 0)
            {
                // 새 레포 생성 성공
                _repoState = RepoState.Created;
                _repoUrl = $"https://github.com/{_pendingAccount}/{settings.githubRepoName}";
                settings.Save();
            }
            else if (output.Contains("already exists"))
            {
                // 이미 존재 → 내용 확인
                CheckExistingRepo(settings);
            }
            else
            {
                _repoState = RepoState.Failed;
                _repoError = $"레포 생성 실패:\n{output}";
            }

            _dashboard.Repaint();
        }

        void CheckExistingRepo(GameServerSettings settings)
        {
            var (code, body) = PrerequisiteChecker.Run("gh",
                $"api repos/{_pendingAccount}/{settings.githubRepoName}/contents --jq \".[].name\"");

            if (code != 0 || string.IsNullOrEmpty(body.Trim()))
            {
                // 빈 레포 또는 API 실패 → 그냥 연결
                _repoState = RepoState.Created;
                _repoUrl = $"https://github.com/{_pendingAccount}/{settings.githubRepoName}";
                settings.Save();
                return;
            }

            // 파일이 있음 → Program.cs 또는 Dockerfile 있으면 우리 서버 레포
            if (body.Contains("Program.cs") || body.Contains("Dockerfile") || body.Contains("GameServer"))
            {
                _repoState = RepoState.Created;
                _repoUrl = $"https://github.com/{_pendingAccount}/{settings.githubRepoName}";
                settings.Save();
                return;
            }

            // 다른 코드가 있음 → 경고 (UI는 OnDraw에서 표시)
            _repoState = RepoState.ExistsOther;
        }

        public void OnSkip() => IsSkipped = true;
    }
}
