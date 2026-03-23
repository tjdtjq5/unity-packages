using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.GameServer.Editor
{
    public class SupabaseSetup
    {
        readonly GameServerDashboard _dashboard;

        // 연결 테스트
        enum TestState { None, Testing, Success, Failed }
        TestState _testState;
        string _testError;
        UnityWebRequest _activeRequest;

        // 프로젝트 목록
        SupabaseManagementApi.ProjectInfo[] _projects;
        string[] _projectLabels;
        int _selectedProjectIndex = -1;
        bool _loadingProjects;
        string _projectsError;

        // Anon Key 자동 조회
        enum AnonKeyState { None, Loading, Done, Failed }
        AnonKeyState _anonKeyState;
        string _anonKeyError;

        public bool IsCompleted => _testState == TestState.Success;

        public SupabaseSetup(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = GameServerSettings.Instance;

            // ① 가입
            DrawStep1_SignUp();

            GUILayout.Space(6);

            // ② Access Token
            DrawStep2_AccessToken(settings);

            GUILayout.Space(6);

            // ③ 프로젝트 선택
            DrawStep3_ProjectSelect(settings);

            GUILayout.Space(6);

            // ④ DB Password
            DrawStep4_DbPassword(settings);

            GUILayout.Space(6);

            // ⑤ 연결 테스트
            EditorUI.DrawSubLabel("⑤ 연결 테스트");
            EditorUI.BeginBody();
            DrawConnectionTest(settings);
            EditorUI.EndBody();
        }

        // ── ① 가입 ──

        void DrawStep1_SignUp()
        {
            EditorUI.DrawSubLabel("① Supabase 가입 + 프로젝트 만들기");
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "계정이 없다면 가입 후 새 프로젝트를 만드세요.\n" +
                "· 리전: Northeast Asia 추천\n" +
                "· DB 비밀번호: 기억해두세요 (④에서 입력)");
            GUILayout.Space(4);
            if (EditorUI.DrawLinkButton("Supabase 가입/로그인", GameServerDashboard.COL_SUPABASE))
                Application.OpenURL("https://supabase.com/dashboard");
            EditorUI.EndBody();
        }

        // ── ② Access Token ──

        void DrawStep2_AccessToken(GameServerSettings settings)
        {
            EditorUI.DrawSubLabel("② Access Token 입력");
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "Supabase > Account > Access Tokens에서 토큰을 생성하세요.\n" +
                "이 토큰으로 Anon Key, Auth 설정이 자동 처리됩니다.");
            if (EditorUI.DrawLinkButton("Access Token 발급", GameServerDashboard.COL_SUPABASE))
                Application.OpenURL("https://supabase.com/dashboard/account/tokens");

            GUILayout.Space(4);
            var token = EditorUI.DrawPasswordField("Access Token", GameServerSettings.SupabaseAccessToken);
            if (token != GameServerSettings.SupabaseAccessToken)
            {
                GameServerSettings.SupabaseAccessToken = token;
                // 토큰 변경 시 프로젝트 목록 초기화
                _projects = null;
                _projectLabels = null;
                _selectedProjectIndex = -1;
                _anonKeyState = AnonKeyState.None;
            }

            // 토큰 입력 시 프로젝트 목록 조회 버튼
            if (!string.IsNullOrEmpty(GameServerSettings.SupabaseAccessToken) && _projects == null && !_loadingProjects)
            {
                GUILayout.Space(4);
                if (EditorUI.DrawColorButton("프로젝트 목록 조회", GameServerDashboard.COL_SUPABASE, 28))
                    FetchProjects();
            }

            if (_loadingProjects)
                EditorUI.DrawLoading(true, "프로젝트 목록 조회 중...");

            if (!string.IsNullOrEmpty(_projectsError))
                EditorUI.DrawDescription($"✗ {_projectsError}", EditorUI.COL_ERROR);

            EditorUI.EndBody();
        }

        // ── ③ 프로젝트 선택 ──

        void DrawStep3_ProjectSelect(GameServerSettings settings)
        {
            var hasToken = !string.IsNullOrEmpty(GameServerSettings.SupabaseAccessToken);

            EditorUI.DrawSubLabel("③ 프로젝트 선택");
            EditorUI.BeginBody();

            if (!hasToken)
            {
                EditorUI.DrawDescription("②에서 Access Token을 먼저 입력하세요.", EditorUI.COL_WARN);
                EditorUI.EndBody();
                return;
            }

            if (_projects != null && _projects.Length > 0)
            {
                // 드롭다운
                var prev = _selectedProjectIndex;
                _selectedProjectIndex = EditorGUILayout.Popup("프로젝트", _selectedProjectIndex, _projectLabels);

                if (_selectedProjectIndex != prev && _selectedProjectIndex >= 0)
                    OnProjectSelected(settings, _projects[_selectedProjectIndex]);

                // 선택된 프로젝트 정보
                if (_selectedProjectIndex >= 0)
                {
                    var p = _projects[_selectedProjectIndex];
                    EditorUI.DrawCellLabel($"  URL: https://{p.id}.supabase.co", 0, EditorUI.COL_MUTED);
                    EditorUI.DrawCellLabel($"  상태: {p.status}  |  리전: {p.region}", 0, EditorUI.COL_MUTED);

                    // Anon Key 상태
                    GUILayout.Space(4);
                    switch (_anonKeyState)
                    {
                        case AnonKeyState.Loading:
                            EditorUI.DrawLoading(true, "Anon Key 조회 중...");
                            break;
                        case AnonKeyState.Done:
                            EditorUI.DrawDescription("✓ Anon Key 자동 조회 완료", EditorUI.COL_SUCCESS);
                            break;
                        case AnonKeyState.Failed:
                            EditorUI.DrawDescription($"✗ Anon Key 조회 실패: {_anonKeyError}", EditorUI.COL_ERROR);
                            EditorUI.DrawDescription("③을 넘어갈 수 없습니다. 토큰과 프로젝트를 확인하세요.", EditorUI.COL_WARN);
                            break;
                    }
                }
            }
            else if (_projects != null && _projects.Length == 0)
            {
                EditorUI.DrawDescription("프로젝트가 없습니다. Supabase에서 먼저 프로젝트를 만드세요.", EditorUI.COL_WARN);
            }
            else
            {
                EditorUI.DrawDescription("②에서 [프로젝트 목록 조회]를 눌러주세요.", EditorUI.COL_MUTED);
            }

            EditorUI.EndBody();
        }

        // ── ④ DB Password ──

        void DrawStep4_DbPassword(GameServerSettings settings)
        {
            EditorUI.DrawSubLabel("④ DB Password");
            EditorUI.BeginBody();

            // Anon Key 미완료 시 차단
            if (_anonKeyState != AnonKeyState.Done &&
                string.IsNullOrEmpty(GameServerSettings.SupabaseAnonKey))
            {
                EditorUI.DrawDescription("③에서 프로젝트를 선택하면 자동으로 Anon Key가 조회됩니다.", EditorUI.COL_WARN);
                EditorUI.EndBody();
                return;
            }

            EditorUI.DrawDescription(
                "프로젝트 생성 시 설정한 비밀번호입니다.\n" +
                "잊었다면 Settings > Database에서 리셋할 수 있습니다.");

            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorUI.DrawLinkButton("Database 설정 페이지", GameServerDashboard.COL_SUPABASE))
                    Application.OpenURL(settings.SupabaseDatabaseSettingsUrl);
            }

            GUILayout.Space(4);
            var dbPw = EditorUI.DrawPasswordField("DB Password", GameServerSettings.SupabaseDbPassword);
            if (dbPw != GameServerSettings.SupabaseDbPassword)
                GameServerSettings.SupabaseDbPassword = dbPw;

            EditorUI.EndBody();
        }

        // ── 프로젝트 목록 조회 ──

        async void FetchProjects()
        {
            _loadingProjects = true;
            _projectsError = null;
            _dashboard.Repaint();

            var (ok, projects, error) = await SupabaseManagementApi.ListProjects(
                GameServerSettings.SupabaseAccessToken);

            _loadingProjects = false;

            if (ok)
            {
                _projects = projects;
                _projectLabels = new string[projects.Length];
                for (var i = 0; i < projects.Length; i++)
                    _projectLabels[i] = $"{projects[i].name} ({projects[i].region})";

                // 이미 설정된 URL이 있으면 자동 선택
                var currentRef = GameServerSettings.Instance.SupabaseProjectId;
                if (!string.IsNullOrEmpty(currentRef))
                {
                    for (var i = 0; i < projects.Length; i++)
                    {
                        if (projects[i].id == currentRef)
                        {
                            _selectedProjectIndex = i;
                            // Anon Key도 조회
                            FetchAnonKey(projects[i].id);
                            break;
                        }
                    }
                }
            }
            else
            {
                _projectsError = error;
            }

            _dashboard.Repaint();
        }

        // ── 프로젝트 선택 시 ──

        void OnProjectSelected(GameServerSettings settings, SupabaseManagementApi.ProjectInfo project)
        {
            // URL 자동 설정
            settings.supabaseUrl = $"https://{project.id}.supabase.co";
            settings.Save();

            // Anon Key 자동 조회
            FetchAnonKey(project.id);

            // Auth URL 동기화 캐시 초기화
            AuthUrlSyncManager.InvalidateCache();
        }

        async void FetchAnonKey(string projectRef)
        {
            _anonKeyState = AnonKeyState.Loading;
            _dashboard.Repaint();

            var (ok, anonKey, error) = await SupabaseManagementApi.GetAnonKey(
                projectRef, GameServerSettings.SupabaseAccessToken);

            if (ok)
            {
                GameServerSettings.SupabaseAnonKey = anonKey;
                _anonKeyState = AnonKeyState.Done;

                // 익명 로그인 + Auth URL 자동 설정
                RunAutoAuthSetup(projectRef);
            }
            else
            {
                _anonKeyState = AnonKeyState.Failed;
                _anonKeyError = error;
            }

            _dashboard.Repaint();
        }

        async void RunAutoAuthSetup(string projectRef)
        {
            var token = GameServerSettings.SupabaseAccessToken;
            var bundleId = PlayerSettings.applicationIdentifier;
            var settings = GameServerSettings.Instance;
            var siteUrl = $"{bundleId}://auth";
            var redirectUrls = $"{bundleId}://auth,http://localhost:*/**";
            if (!string.IsNullOrEmpty(settings.cloudRunUrl))
                redirectUrls = $"{bundleId}://auth,{settings.cloudRunUrl.TrimEnd('/')}/auth/callback,http://localhost:*/**";

            var body = "{" +
                $"\"external_anonymous_users_enabled\":true," +
                $"\"site_url\":\"{siteUrl}\"," +
                $"\"uri_allow_list\":\"{redirectUrls}\"" +
                "}";

            var (ok, error) = await SupabaseManagementApi.PatchAuthConfig(projectRef, token, body);
            if (ok)
                Debug.Log("[GameServer] 자동 Auth 설정 완료: 익명 로그인 + Auth URL");
            else
                Debug.LogWarning($"[GameServer] Auth 자동 설정 실패: {error}");
        }

        // ── 연결 테스트 ──

        void DrawConnectionTest(GameServerSettings settings)
        {
            switch (_testState)
            {
                case TestState.None:
                    bool canTest = !string.IsNullOrEmpty(settings.supabaseUrl) &&
                                   !string.IsNullOrEmpty(GameServerSettings.SupabaseAnonKey);
                    using (new EditorGUI.DisabledGroupScope(!canTest))
                    {
                        if (EditorUI.DrawColorButton("연결 테스트", GameServerDashboard.COL_SUPABASE, 28))
                            RunConnectionTest(settings);
                    }
                    if (!canTest)
                        EditorUI.DrawDescription("프로젝트를 선택하고 Anon Key가 조회되면 테스트할 수 있습니다.", EditorUI.COL_WARN);
                    break;

                case TestState.Testing:
                    EditorUI.DrawLoading(true, "연결 테스트 중...");
                    break;

                case TestState.Success:
                    EditorUI.DrawDescription("✓ Supabase 연결 성공!", EditorUI.COL_SUCCESS);
                    GUILayout.Space(4);
                    if (EditorUI.DrawColorButton("다시 테스트", GameServerDashboard.COL_SUPABASE))
                        RunConnectionTest(settings);
                    break;

                case TestState.Failed:
                    EditorUI.DrawDescription($"✗ {_testError}", EditorUI.COL_ERROR);
                    GUILayout.Space(4);
                    if (EditorUI.DrawColorButton("다시 테스트", EditorUI.COL_ERROR))
                        RunConnectionTest(settings);
                    break;
            }
        }

        void RunConnectionTest(GameServerSettings settings)
        {
            var url = settings.supabaseUrl?.TrimEnd('/');
            var key = GameServerSettings.SupabaseAnonKey;

            if (string.IsNullOrEmpty(url) || !url.Contains("supabase"))
            {
                _testState = TestState.Failed;
                _testError = "URL이 https://xxx.supabase.co 형식이어야 합니다.";
                _dashboard.Repaint();
                return;
            }

            _testState = TestState.Testing;
            _dashboard.Repaint();

            _activeRequest = new UnityWebRequest($"{url}/auth/v1/settings", "GET");
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("apikey", key);
            _activeRequest.SetRequestHeader("Authorization", $"Bearer {key}");
            _activeRequest.timeout = 10;
            _activeRequest.SendWebRequest();

            EditorApplication.update += PollConnectionTest;
        }

        void PollConnectionTest()
        {
            if (_activeRequest == null || !_activeRequest.isDone) return;

            EditorApplication.update -= PollConnectionTest;

            var code = _activeRequest.responseCode;
            var body = _activeRequest.downloadHandler?.text ?? "";
            var error = _activeRequest.error ?? "";
            var result = _activeRequest.result;

            _activeRequest.Dispose();
            _activeRequest = null;

            if (result == UnityWebRequest.Result.Success)
            {
                _testState = TestState.Success;
                GameServerSettings.Instance.Save();
            }
            else if (code == 401 || code == 403)
            {
                _testState = TestState.Failed;
                _testError = $"인증 실패 (HTTP {code}) — Anon Key를 확인하세요.";
            }
            else if (code == 0)
            {
                _testState = TestState.Failed;
                _testError = $"서버에 연결할 수 없습니다 — URL을 확인하세요.";
            }
            else
            {
                _testState = TestState.Failed;
                _testError = $"HTTP {code}: {error}";
            }

            _dashboard.Repaint();
        }

        public void Cleanup()
        {
            EditorApplication.update -= PollConnectionTest;
            if (_activeRequest != null)
            {
                _activeRequest.Abort();
                _activeRequest.Dispose();
                _activeRequest = null;
            }
        }
    }
}
