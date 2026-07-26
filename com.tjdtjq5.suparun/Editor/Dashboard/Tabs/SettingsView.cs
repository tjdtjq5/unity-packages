using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SettingsView
    {
        readonly SupaRunDashboard _dashboard;
        Vector2 _scrollPos;
        bool _toolsExpanded;
        bool _supabaseExpanded, _githubExpanded, _gcpExpanded, _authExpanded;
        bool _foldLog;

        // Auth providers
        readonly System.Collections.Generic.Dictionary<string, bool> _providerExpanded = new();
        readonly System.Collections.Generic.Dictionary<string, int> _providerStep = new();
        readonly System.Collections.Generic.Dictionary<string, string> _providerClientId = new();
        readonly System.Collections.Generic.Dictionary<string, string> _providerSecret = new();
        readonly System.Collections.Generic.Dictionary<string, string> _providerApplyState = new(); // "", "applying", "done", "error:{msg}"
        bool _showProviderDropdown;

        // Supabase auth config 캐시 (provider 상태 조회용)
        string _authConfigJson;
        bool _authConfigLoading;
        bool _authConfigLoaded;

        // Supabase 프로젝트 드롭다운
        SupabaseManagementApi.ProjectInfo[] _settingsProjects;
        string[] _settingsProjectLabels;
        int _settingsProjectIndex = -1;
        bool _settingsLoadingProjects;

        public SettingsView(SupaRunDashboard dashboard) => _dashboard = dashboard;

        // ── 공용 그리기 헬퍼 ──

        /// <summary>상태 아이콘: 1=설정됨(✓), 2=일부/진행 중(⚠), 0=미설정(○)</summary>
        static string StateIcon(int state) => state == 1 ? "✓" : state == 2 ? "⚠" : "○";

        /// <summary>서비스 카드 시작. 폴드아웃 헤더(이름 + 상태), 접힘 시 요약 표시. 반환: expanded.</summary>
        static bool BeginServiceCard(string name, string status, int statusState,
            string summaryLine, ref bool expanded)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            expanded = EditorGUILayout.Foldout(expanded, $"{name} — {StateIcon(statusState)} {status}", true);
            if (!expanded)
                EditorGUILayout.LabelField(summaryLine ?? "", EditorStyles.miniLabel);
            return expanded;
        }

        /// <summary>서비스 카드 끝.</summary>
        static void EndServiceCard()
        {
            EditorGUILayout.EndVertical();
        }

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var settings = SupaRunSettings.Instance;
            var gh = PrerequisiteChecker.CheckGh();
            var gcloud = PrerequisiteChecker.CheckGcloud();

            // 상태 요약 바
            var statusItems = new (string name, int state)[]
            {
                ("Supabase", settings.IsSupabaseConfigured ? 1 : 0),
                ("GitHub", gh.LoggedIn && settings.IsGitHubConfigured ? 1 : gh.Installed ? 2 : 0),
                ("GCP", gcloud.LoggedIn && settings.gcpCloudRunApiEnabled ? 1
                    : gcloud.Installed ? 2 : 0),
            };
            var statusParts = new string[statusItems.Length];
            for (int i = 0; i < statusItems.Length; i++)
                statusParts[i] = $"{StateIcon(statusItems[i].state)} {statusItems[i].name}";
            EditorGUILayout.LabelField(string.Join("   |   ", statusParts), EditorStyles.miniLabel);

            // 시크릿 저장 위치 안내 — private repo 전용 가정
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "⚠ 시크릿(DB Password, Access Token, GitHub Token, Cron Secret)은 " +
                "ProjectSettings/SupaRunProjectSettings.json에 평문 저장되어 git에 커밋됩니다. " +
                "private repo 전용 사용을 가정합니다.",
                MessageType.Warning);
            GUILayout.Space(4);

            DrawEditorAuthCard();
            DrawEnvironmentCard(settings);
            ProjectManager.Draw(settings);
            DrawSupabaseCard(settings);
            DrawGitHubCard(settings, gh);
            DrawGcpCard(settings, gcloud);
            DrawAuthCard(settings);
            DrawToolsCard(gh, gcloud);

            GUILayout.Space(4);
            DrawLogSection(settings);

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("저장", GUILayout.Height(22)))
            {
                settings.Save();
                _dashboard.ShowNotification("설정 저장 완료", SupaRunUI.NotificationType.Success);
            }
            if (GUILayout.Button("초기 설정 다시 실행", GUILayout.Height(22)))
            {
                if (EditorUtility.DisplayDialog("초기 설정",
                    "Setup Wizard를 처음부터 다시 시작합니다.\n기존 설정은 유지됩니다.", "확인", "취소"))
                {
                    settings.setupCompleted = false;
                    settings.Save();
                    _dashboard.OpenSetup();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        // ── Tools 카드 ──

        void DrawToolsCard(PrerequisiteChecker.ToolStatus gh, PrerequisiteChecker.ToolStatus gcloud)
        {
            var dotnet = PrerequisiteChecker.IsDotnetInstalled();
            int installed = (dotnet ? 1 : 0) + (gh.Installed ? 1 : 0) + (gcloud.Installed ? 1 : 0);
            bool allInstalled = installed == 3;

            // 하나라도 미설치면 기본 펼침
            if (!allInstalled && !_toolsExpanded)
                _toolsExpanded = true;

            var status = $"{installed}/3 설치됨";
            var state = allInstalled ? 1 : 2;
            var summary = allInstalled ? ".NET SDK, gh CLI, gcloud CLI" : "설치가 필요한 도구가 있습니다";

            BeginServiceCard("Tools", status, state, summary, ref _toolsExpanded);

            if (_toolsExpanded)
            {
                GUILayout.Space(4);

                // .NET SDK
                if (dotnet)
                    EditorGUILayout.LabelField(
                        $"  ✓ .NET SDK {PrerequisiteChecker.GetDotnetMajorVersion()}.0");
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("  ⚠ .NET SDK 미설치");
                    if (EditorGUILayout.LinkButton("설치하기"))
                        Application.OpenURL("https://dotnet.microsoft.com/download");
                    EditorGUILayout.EndHorizontal();
                }

                // gh CLI
                if (gh.LoggedIn)
                    EditorGUILayout.LabelField(
                        $"  ✓ gh CLI {gh.Version} ({gh.Account})");
                else if (gh.Installed)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  ⚠ gh CLI {gh.Version} (로그인 필요)");
                    if (EditorGUILayout.LinkButton("로그인"))
                        PrerequisiteChecker.RunGhLogin();
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("  ⚠ gh CLI 미설치");
                    if (EditorGUILayout.LinkButton("설치하기"))
                        Application.OpenURL("https://cli.github.com");
                    EditorGUILayout.EndHorizontal();
                }

                // gcloud CLI
                if (gcloud.LoggedIn)
                    EditorGUILayout.LabelField(
                        $"  ✓ gcloud CLI {gcloud.Version} ({gcloud.Account})");
                else if (gcloud.Installed)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  ⚠ gcloud CLI {gcloud.Version} (로그인 필요)");
                    if (EditorGUILayout.LinkButton("로그인"))
                        PrerequisiteChecker.RunGcloudLogin();
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("  ⚠ gcloud CLI 미설치");
                    if (EditorGUILayout.LinkButton("설치하기"))
                        Application.OpenURL("https://cloud.google.com/sdk/docs/install");
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.Space(4);
                if (GUILayout.Button("새로고침"))
                    PrerequisiteChecker.InvalidateCache();
            }

            EndServiceCard();
        }

        // ── Supabase 카드 ──

        // ── 에디터 로그인 ──

        // 이름 앞에 editor 를 붙인다 — 아래 Auth 카드(_authExpanded)는 게임 로그인 프로바이더 설정이라
        // 서로 다른 것이다. 이쪽은 **에디터가 Supabase 에 로그인하는 것**이다.
        bool _editorAuthExpanded = true;
        bool _editorAuthBusy;
        bool? _editorAuthIsAdmin;

        /// <summary>
        /// 에디터를 Supabase 에 로그인시킨다.
        ///
        /// 왜 필요한가: 지금까지는 PAT(계정 마스터키)만 들고 다녀서, 설정을 공유하려면 파일을
        /// git 에 올리는 수밖에 없었고 그 파일에 비밀이 들어갔다. 로그인할 수 있으면
        /// `is_admin()` 으로 보호된 자리에서 설정을 받아올 수 있다 —
        /// **git 에는 공개값만 남기고 나머지를 DB 로 옮기는 토대**다.
        ///
        /// 콜백은 이미 돌고 있는 로컬 브리지가 받는다(gcloud auth login 과 같은 방식).
        /// </summary>
        void DrawEditorAuthCard()
        {
            var signedIn = SupaRunEditorAuth.IsSignedIn;
            var status = signedIn ? "로그인됨" : "로그인 안 됨";
            var summary = signedIn
                ? SupaRunEditorAuth.Email
                : "Google 계정으로 로그인하면 환경마다 계정을 따로 만들 필요가 없습니다";

            var expanded = BeginServiceCard("에디터 로그인", status, signedIn ? 1 : 0, summary, ref _editorAuthExpanded);

            if (expanded)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "관리자 계정을 Google 하나로 통일합니다. 각 환경의 admin_user 에 같은 이메일을 등록하면\n" +
                    "환경마다 이메일·비밀번호를 따로 만들지 않아도 되고, 사람이 나가면 계정 하나만 막으면 됩니다.",
                    EditorStyles.wordWrappedMiniLabel);

                if (!SupaRunBridge.Running)
                {
                    EditorGUILayout.HelpBox(
                        "로컬 브리지가 실행 중이 아닙니다. 로그인 콜백을 받을 수 없습니다.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField($"콜백 주소  {SupaRunBridge.CallbackUrl}", EditorStyles.miniLabel);
                }

                GUILayout.Space(4);
                if (signedIn)
                {
                    EditorGUILayout.LabelField($"계정  {SupaRunEditorAuth.Email}");
                    if (_editorAuthIsAdmin.HasValue)
                        EditorGUILayout.LabelField(_editorAuthIsAdmin.Value
                            ? "이 환경의 관리자입니다."
                            : "⚠ 이 환경의 admin_user 에 등록되어 있지 않습니다.", EditorStyles.miniLabel);

                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(_editorAuthBusy))
                    {
                        if (GUILayout.Button("권한 확인", GUILayout.Height(22))) CheckAdmin().Forget();
                        if (GUILayout.Button("로그아웃", GUILayout.Height(22)))
                        {
                            SupaRunEditorAuth.SignOut();
                            _editorAuthIsAdmin = null;
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // 첫 관리자는 여기서만 만들 수 있다 — 웹은 관리자만 admin_user 에 쓸 수 있어서
                    // 표가 비어 있으면 누구도 자신을 등록할 수 없다. 이 버튼이 그 매듭을 끊는다.
                    if (_editorAuthIsAdmin == false)
                    {
                        GUILayout.Space(2);
                        using (new EditorGUI.DisabledScope(_editorAuthBusy))
                        {
                            if (GUILayout.Button("이 계정을 편집 환경의 관리자로 등록", GUILayout.Height(22)))
                                RegisterAdmin().Forget();
                        }
                        EditorGUILayout.LabelField(
                            "환경마다 DB 가 다릅니다 — prod 는 편집 환경을 prod 로 바꾼 뒤 다시 눌러야 합니다.",
                            EditorStyles.wordWrappedMiniLabel);
                    }

                    DrawSecretShare();
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_editorAuthBusy || !SupaRunBridge.Running))
                    {
                        if (GUILayout.Button(_editorAuthBusy ? "브라우저에서 로그인 중…" : "Google 로 로그인", GUILayout.Height(24)))
                            SignIn().Forget();
                    }
                    EditorGUILayout.HelpBox(
                        "Supabase 에 Google 프로바이더가 켜져 있어야 합니다. 아래 Auth 카드에서 설정하세요.",
                        MessageType.Info);

                    // 방금 클론한 팀원이 정확히 이 상태다. 아무 설명이 없으면 "설정이 왜 비었지" 로 헤맨다.
                    if (string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken))
                        EditorGUILayout.HelpBox(
                            "이 컴퓨터에 비밀(Access Token·DB 비밀번호 등)이 없습니다.\n" +
                            "비밀은 git 에 담기지 않습니다 — 로그인한 뒤 [내려받기] 로 받아오세요.",
                            MessageType.Warning);
                }
            }

            EndServiceCard();
        }

        /// <summary>
        /// 공유 비밀 주고받기.
        ///
        /// 비밀은 이제 프로젝트 파일이 아니라 이 컴퓨터(EditorPrefs)에 있다 — git 에 안 올라간다.
        /// 그래서 팀원에게 전달할 길이 필요하고, 그게 편집 환경 DB 의 `suparun_secret` 이다.
        /// </summary>
        void DrawSecretShare()
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField("공유 비밀", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "git 에 올릴 수 없는 값들을 편집 환경 DB 에 두고 팀이 주고받습니다.\n" +
                string.Join(" · ", SupaRunSecretStore.Labels),
                EditorStyles.wordWrappedMiniLabel);

            // 관리자가 아니면 RLS 가 막는다. 눌러서 실패하는 것보다 이유를 먼저 보여준다.
            using (new EditorGUI.DisabledScope(_editorAuthBusy || _editorAuthIsAdmin != true))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("올리기", GUILayout.Height(22))) SecretPush().Forget();
                if (GUILayout.Button("내려받기", GUILayout.Height(22))) SecretPull().Forget();
                EditorGUILayout.EndHorizontal();
            }

            if (_editorAuthIsAdmin != true)
                EditorGUILayout.LabelField(
                    "관리자 권한이 확인되어야 주고받을 수 있습니다. 위에서 [권한 확인] 을 누르세요.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        async UniTaskVoid SecretPush()
        {
            _editorAuthBusy = true;
            _dashboard.Repaint();
            try
            {
                var r = await SupaRunSecretStore.PushAsync();
                if (!r.ShowErrorDialog("비밀 올리기")) return;
                _dashboard.ShowNotification($"{r.Value}개 항목을 올렸습니다", SupaRunUI.NotificationType.Success);
            }
            finally { _editorAuthBusy = false; _dashboard.Repaint(); }
        }

        async UniTaskVoid SecretPull()
        {
            _editorAuthBusy = true;
            _dashboard.Repaint();
            try
            {
                var r = await SupaRunSecretStore.PullAsync();
                if (!r.ShowErrorDialog("비밀 내려받기")) return;
                _dashboard.ShowNotification(
                    r.Value > 0 ? $"{r.Value}개 항목을 받았습니다" : "DB 에 저장된 비밀이 없습니다",
                    r.Value > 0 ? SupaRunUI.NotificationType.Success : SupaRunUI.NotificationType.Info);
            }
            finally { _editorAuthBusy = false; _dashboard.Repaint(); }
        }

        async UniTaskVoid SignIn()
        {
            _editorAuthBusy = true;
            _dashboard.Repaint();
            try
            {
                var ok = await SupaRunEditorAuth.SignInWithGoogleAsync();
                if (ok) await CheckAdminCore();
                else _dashboard.ShowNotification("로그인이 완료되지 않았습니다", SupaRunUI.NotificationType.Error);
            }
            finally { _editorAuthBusy = false; _dashboard.Repaint(); }
        }

        async UniTaskVoid CheckAdmin()
        {
            _editorAuthBusy = true;
            try { await CheckAdminCore(); }
            finally { _editorAuthBusy = false; _dashboard.Repaint(); }
        }

        async UniTask CheckAdminCore()
        {
            _editorAuthIsAdmin = await SupaRunEditorAuth.IsAdminAsync();
            _dashboard.Repaint();
        }

        async UniTaskVoid RegisterAdmin()
        {
            _editorAuthBusy = true;
            _dashboard.Repaint();
            try
            {
                var r = await SupaRunEditorAuth.RegisterSelfAsAdminAsync();
                if (!r.ShowErrorDialog("관리자 등록")) return;
                await CheckAdminCore();
                _dashboard.ShowNotification(
                    $"{SupaRunEditorAuth.Email} 을 관리자로 등록했습니다", SupaRunUI.NotificationType.Success);
            }
            finally { _editorAuthBusy = false; _dashboard.Repaint(); }
        }

        // ── 프로젝트 관리 ──

        ProjectManagerUI _projectManager;

        /// <summary>
        /// 환경 카드 바로 아래에 둔다 — "어느 프로젝트를 쓰는가"(환경)와
        /// "그 프로젝트가 실제로 있는가"(여기)는 같이 봐야 판단이 된다.
        /// </summary>
        ProjectManagerUI ProjectManager => _projectManager ??= new ProjectManagerUI(
            () => SupaRunSettings.Instance.SupabaseAccessToken,
            () => _dashboard.Repaint());

        // ── 환경 카드 ──

        bool _envExpanded = true;
        string _newEnvName = "";

        /// <summary>
        /// 환경 선택. 아래 Supabase/GCP 카드가 전부 **여기서 고른 환경**의 값을 보여준다.
        ///
        /// 편집 환경과 빌드 환경을 따로 두는 이유: dev 를 보면서 prod 빌드를 뽑는 것이 정상이다.
        /// 하나로 묶으면 빌드할 때마다 편집 환경을 바꿔야 하고, 되돌리는 것을 잊으면
        /// 그 다음 컴파일이 prod 스키마를 건드린다.
        /// </summary>
        void DrawEnvironmentCard(SupaRunSettings settings)
        {
            var envs = settings.Environments;
            var names = new string[envs.Count];
            for (int i = 0; i < envs.Count; i++) names[i] = envs[i].name;

            var editorIdx = System.Array.IndexOf(names, settings.EditorEnvironment);
            var buildIdx = System.Array.IndexOf(names, settings.BuildEnvironment);

            var summary = names.Length == 0
                ? "환경이 없습니다"
                : $"편집 {settings.EditorEnvironment}  →  빌드 {settings.BuildEnvironment}";

            var expanded = BeginServiceCard("환경", $"{names.Length}개", names.Length > 0 ? 1 : 0,
                summary, ref _envExpanded);

            if (expanded)
            {
                GUILayout.Space(4);

                var newEditorIdx = EditorGUILayout.Popup(
                    new GUIContent("편집 환경", "컴파일 시 스키마 자동 반영·어드민·에디터 플레이가 향하는 곳"),
                    editorIdx, names);
                if (newEditorIdx != editorIdx && newEditorIdx >= 0)
                    settings.EditorEnvironment = names[newEditorIdx];

                var newBuildIdx = EditorGUILayout.Popup(
                    new GUIContent("빌드 환경", "빌드 산출물에 구워지는 곳. 편집 환경과 달라도 된다"),
                    buildIdx, names);
                if (newBuildIdx != buildIdx && newBuildIdx >= 0)
                    settings.BuildEnvironment = names[newBuildIdx];

                // 라이브를 직접 편집 중이라는 사실은 눈에 띄어야 한다.
                if (LooksLikeProduction(settings.EditorEnvironment))
                {
                    EditorGUILayout.HelpBox(
                        $"편집 환경이 '{settings.EditorEnvironment}' 입니다. " +
                        "이 상태로 컴파일하면 스키마가 라이브에 반영되고, 어드민도 라이브를 가리킵니다.",
                        MessageType.Warning);
                }

                GUILayout.Space(6);
                EditorGUILayout.LabelField("환경 추가/삭제", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                _newEnvName = EditorGUILayout.TextField(_newEnvName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newEnvName)))
                {
                    if (GUILayout.Button("추가", GUILayout.Width(60)))
                    {
                        settings.AddEnvironment(_newEnvName.Trim());
                        _newEnvName = "";
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "새 환경은 값이 비어 있습니다. 편집 환경을 그쪽으로 바꾼 뒤 " +
                    "아래 Supabase/GCP 카드에서 채우세요.", MessageType.Info);

                using (new EditorGUI.DisabledScope(envs.Count <= 1))
                {
                    if (GUILayout.Button($"현재 편집 환경 '{settings.EditorEnvironment}' 삭제"))
                    {
                        if (EditorUtility.DisplayDialog("환경 삭제",
                            $"'{settings.EditorEnvironment}' 환경 설정을 지웁니다.\n" +
                            "Supabase 프로젝트나 배포된 서버는 그대로 남습니다.", "삭제", "취소"))
                        {
                            settings.RemoveEnvironment(settings.EditorEnvironment);
                        }
                    }
                }
            }

            // 접힘 여부와 무관하게 닫는다 — BeginServiceCard 가 항상 BeginVertical 을 열기 때문이다.
            EndServiceCard();
        }

        /// <summary>이름만 보고 라이브로 의심되는지. 경고 표시에만 쓴다.</summary>
        static bool LooksLikeProduction(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            return n.Contains("prod") || n.Contains("live") || n.Contains("release");
        }

        void DrawSupabaseCard(SupaRunSettings settings)
        {
            var status = settings.IsSupabaseConfigured ? "Connected" : "미설정";
            var state = settings.IsSupabaseConfigured ? 1 : 0;
            var summary = settings.IsSupabaseConfigured
                ? settings.supabaseUrl : "Supabase 설정이 필요합니다";
            var hasToken = !string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken);

            var expanded = BeginServiceCard("Supabase", status, state, summary, ref _supabaseExpanded);

            if (expanded)
            {
                // ── Access Token (최상단) ──
                GUILayout.Space(4);
                var token = EditorGUILayout.PasswordField(
                    new GUIContent("Access Token", "자동 설정용"),
                    SupaRunSettings.Instance.SupabaseAccessToken);
                if (token != SupaRunSettings.Instance.SupabaseAccessToken)
                {
                    SupaRunSettings.Instance.SupabaseAccessToken = token;
                    _settingsProjects = null;
                    _settingsProjectIndex = -1;
                    AuthUrlSyncManager.InvalidateCache();
                }
                EditorGUILayout.BeginHorizontal();
                if (EditorGUILayout.LinkButton("Access Token 발급"))
                    Application.OpenURL("https://supabase.com/dashboard/account/tokens");
                GUILayout.FlexibleSpace();
                if (hasToken && _settingsProjects == null && !_settingsLoadingProjects)
                {
                    if (GUILayout.Button("프로젝트 조회"))
                        _ = FetchSettingsProjects();
                }
                EditorGUILayout.EndHorizontal();

                if (_settingsLoadingProjects)
                    EditorGUILayout.HelpBox("프로젝트 목록 조회 중...", MessageType.Info);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("── 프로젝트 설정 ──");
                GUILayout.Space(2);

                // ── Project URL (드롭다운 또는 수동) ──
                if (hasToken && _settingsProjects != null && _settingsProjects.Length > 0)
                {
                    var prev = _settingsProjectIndex;
                    _settingsProjectIndex = EditorGUILayout.Popup("Project", _settingsProjectIndex, _settingsProjectLabels);
                    if (_settingsProjectIndex != prev && _settingsProjectIndex >= 0)
                    {
                        var p = _settingsProjects[_settingsProjectIndex];
                        settings.supabaseUrl = $"https://{p.id}.supabase.co";
                        settings.Save();
                        _ = FetchAnonKey(settings);
                        AuthUrlSyncManager.InvalidateCache();
                    }
                }
                else
                {
                    var newUrl = EditorGUILayout.TextField(
                        new GUIContent("Project URL", "https://xxx.supabase.co"),
                        settings.supabaseUrl);
                    if (newUrl != settings.supabaseUrl)
                    {
                        settings.supabaseUrl = newUrl;
                        settings.Save();
                    }
                }

                // ── Anon Key (읽기전용 표시) ──
                GUILayout.Space(2);
                if (hasToken)
                {
                    // 읽기전용 표시
                    var anonDisplay = string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAnonKey)
                        ? "(프로젝트 선택 시 자동 조회)"
                        : SupaRunSettings.Instance.SupabaseAnonKey.Length > 20
                            ? SupaRunSettings.Instance.SupabaseAnonKey.Substring(0, 20) + "..."
                            : SupaRunSettings.Instance.SupabaseAnonKey;
                    EditorGUILayout.LabelField($"  Anon Key: {anonDisplay}");
                }
                else
                {
                    var anonKey = EditorGUILayout.TextField(
                        new GUIContent("Anon Key", "수동 입력"),
                        SupaRunSettings.Instance.SupabaseAnonKey);
                    if (anonKey != SupaRunSettings.Instance.SupabaseAnonKey)
                        SupaRunSettings.Instance.SupabaseAnonKey = anonKey;
                    if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
                    {
                        if (EditorGUILayout.LinkButton("API Keys 페이지에서 복사"))
                            Application.OpenURL(settings.SupabaseApiSettingsUrl);
                    }
                }

                // ── DB Password ──
                GUILayout.Space(2);
                var dbPw = EditorGUILayout.PasswordField(
                    new GUIContent("DB Password", "프로젝트 생성 시 비밀번호"),
                    SupaRunSettings.Instance.SupabaseDbPassword);
                if (dbPw != SupaRunSettings.Instance.SupabaseDbPassword)
                    SupaRunSettings.Instance.SupabaseDbPassword = dbPw;

                DrawEdgeFunctionSection(settings);
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("연결 테스트"))
                _ = RunConnectionTest(settings);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorGUILayout.LinkButton("대시보드"))
                    Application.OpenURL(settings.SupabaseDashboardUrl);
            }
            EditorGUILayout.EndHorizontal();

            EndServiceCard();
        }

        // ── 어드민 대행 함수 ──

        bool _edgeFnBusy;
        string _edgeFnPing;

        /// <summary>
        /// 어드민이 PAT 를 거쳐야 하는 호출을 대신할 Edge Function.
        ///
        /// Cloud Run 이 아니라 여기 두는 이유: Cloud Run 은 **첫 배포 전에는 없다.** 배포에 필요한
        /// 값(DB 비밀번호·GitHub 토큰)을 어드민에서 받으려는데 어드민을 띄울 서버가 없는 순환이
        /// 생긴다. Edge Function 은 Supabase 프로젝트가 생기는 순간 존재해서 그 순환을 끊는다.
        /// </summary>
        void DrawEdgeFunctionSection(SupaRunSettings settings)
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField("어드민 대행 함수", EditorStyles.boldLabel);

            var upToDate = EdgeFunctionDeployer.IsUpToDate(settings.Current);
            EditorGUILayout.LabelField(
                upToDate
                    ? $"{EdgeFunctionDeployer.Slug} — 최신 소스가 올라가 있습니다."
                    : $"{EdgeFunctionDeployer.Slug} — 올릴 변경이 있습니다.",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUI.DisabledScope(_edgeFnBusy))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(upToDate ? "다시 배포" : "배포", GUILayout.Height(22)))
                    DeployEdgeFn(force: upToDate).Forget();
                if (GUILayout.Button("응답 확인", GUILayout.Height(22)))
                    PingEdgeFn().Forget();
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(_edgeFnPing))
                EditorGUILayout.LabelField(_edgeFnPing, EditorStyles.wordWrappedMiniLabel);
        }

        async UniTaskVoid DeployEdgeFn(bool force)
        {
            _edgeFnBusy = true;
            _dashboard.Repaint();
            try
            {
                var r = await EdgeFunctionDeployer.DeployAsync(force: force);
                if (!r.ShowErrorDialog("어드민 대행 함수 배포")) return;
                _dashboard.ShowNotification(
                    r.Value ? "배포했습니다" : "이미 최신이라 올리지 않았습니다",
                    SupaRunUI.NotificationType.Success);
            }
            finally { _edgeFnBusy = false; _dashboard.Repaint(); }
        }

        async UniTaskVoid PingEdgeFn()
        {
            _edgeFnBusy = true;
            _dashboard.Repaint();
            try
            {
                var r = await EdgeFunctionDeployer.PingAsync();
                _edgeFnPing = r.Ok ? $"응답: {r.Value}" : $"실패: {r.ToShortString()}";
            }
            finally { _edgeFnBusy = false; _dashboard.Repaint(); }
        }

        // ── GitHub 카드 (공용 UI) ──

        void DrawGitHubCard(SupaRunSettings settings, PrerequisiteChecker.ToolStatus gh)
        {
            var status = gh.LoggedIn && settings.IsGitHubConfigured ? "설정됨"
                : gh.Installed ? "설정 필요" : "미설정";
            var state = gh.LoggedIn && settings.IsGitHubConfigured ? 1 : gh.Installed ? 2 : 0;
            var summary = state == 1
                ? $"{gh.Account}/{settings.githubRepoName}"
                : "서버 코드 저장 + 자동 배포에 필요";

            BeginServiceCard("GitHub", status, state, summary, ref _githubExpanded);

            if (_githubExpanded)
            {
                GUILayout.Space(4);
                GitHubSetupUI.Draw(_dashboard, settings);
            }

            EndServiceCard();
        }

        // ── Auth 카드 ──

        void DrawAuthCard(SupaRunSettings settings)
        {
            var count = settings.enabledAuthProviders.Count;
            var status = $"{count}개 활성";
            var summary = string.Join(", ", settings.enabledAuthProviders);

            BeginServiceCard("Auth", status, 1, summary, ref _authExpanded);

            if (_authExpanded)
            {
                GUILayout.Space(4);

                // Auth config 조회 (Access Token이 있으면 펼칠 때 한번만)
                var hasToken = !string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken);
                if (hasToken && !_authConfigLoaded && !_authConfigLoading)
                    _ = FetchAuthConfig(settings);

                // OAuth URL 설정
                if (settings.enabledAuthProviders.Count > 0)
                {
                    GUILayout.Space(2);
                    DrawAuthUrlSection(settings);
                }

                // 활성화된 providers
                string toRemove = null;
                foreach (var provider in settings.enabledAuthProviders)
                {
                    var guide = AuthProviderGuide.Get(provider);
                    if (!_providerExpanded.ContainsKey(provider))
                        _providerExpanded[provider] = false;

                    var isExpanded = _providerExpanded[provider];

                    GUILayout.Space(2);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();

                    // > / v + 이름 + Supabase 상태
                    var supabaseStatus = GetProviderSupabaseStatus(provider);
                    var label = supabaseStatus != null
                        ? $"{guide.displayName}  {supabaseStatus}"
                        : guide.displayName;
                    if (GUILayout.Button($"  {(isExpanded ? "v " : "> ")}{label}", EditorStyles.label))
                    {
                        _providerExpanded[provider] = !isExpanded;
                        GUI.FocusControl(null);
                    }

                    // [x] 제거
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        var msg = provider == "Guest"
                            ? "Guest를 제거하면 자동 로그인이 비활성화됩니다.\n게임 시작 시 직접 로그인 UI를 구현해야 합니다."
                            : $"{guide.displayName} 로그인을 제거합니다.";
                        if (EditorUtility.DisplayDialog("로그인 방식 제거", msg, "제거", "취소"))
                            toRemove = provider;
                    }
                    EditorGUILayout.EndHorizontal();

                    // 펼침 → 가이드
                    if (isExpanded)
                        DrawProviderGuide(settings, provider, guide);

                    EditorGUILayout.EndVertical();
                }

                // 제거 처리
                if (toRemove != null)
                {
                    _ = DisableProviderOnSupabase(settings, toRemove);
                    settings.enabledAuthProviders.Remove(toRemove);
                    _providerExpanded.Remove(toRemove);
                    settings.Save();
                }

                GUILayout.Space(4);

                // [+ 로그인 방식 추가]
                if (_showProviderDropdown)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    bool hasAvailable = false;
                    foreach (var p in AuthProviderGuide.AvailableProviders)
                    {
                        if (settings.enabledAuthProviders.Contains(p)) continue;
                        hasAvailable = true;
                        var guide = AuthProviderGuide.Get(p);
                        var label = guide.requiresSDK ? $"{guide.displayName} (SDK)" : guide.displayName;
                        if (GUILayout.Button(label, EditorStyles.miniButton))
                        {
                            settings.enabledAuthProviders.Add(p);
                            settings.Save();
                            _ = EnableProviderOnSupabase(settings, p);
                            _showProviderDropdown = false;
                        }
                    }
                    if (!hasAvailable)
                        EditorGUILayout.LabelField("모든 로그인 방식이 추가되었습니다.", EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(2);
                    if (GUILayout.Button("닫기"))
                        _showProviderDropdown = false;
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    if (GUILayout.Button("+ 로그인 방식 추가"))
                        _showProviderDropdown = true;
                }
            }

            EndServiceCard();
        }

        void DrawProviderGuide(SupaRunSettings settings, string providerKey, GuideInfo guide)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var projectId = settings.SupabaseProjectId;
            var hasToken = !string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken);
            var needsCredentials = AuthProviderGuide.RequiresClientCredentials(providerKey);

            // 설정 완료 체크 — 완료됐으면 상태 요약만 표시
            if (IsProviderConfigured(providerKey))
            {
                DrawProviderCompleted(settings, providerKey, guide);
                EditorGUILayout.EndVertical();
                return;
            }

            // SDK 상태
            if (guide.requiresSDK)
            {
                var installed = AuthProviderGuide.IsSDKInstalled(providerKey);
                EditorGUILayout.LabelField(
                    installed ? $"  ✓ {guide.sdkName} 설치됨" : $"  ⚠ {guide.sdkName} 미설치");
                GUILayout.Space(4);
            }

            // Guest는 자동 처리됨 (추가 시 EnableProviderOnSupabase 호출)
            if (providerKey == "Guest")
            {
                if (hasToken)
                    EditorGUILayout.LabelField("✓ Access Token으로 자동 활성화됨", EditorStyles.wordWrappedMiniLabel);
                else
                    DrawStepBasedGuide(guide, providerKey, projectId);
                EditorGUILayout.EndVertical();
                return;
            }

            // Access Token 있고 Credentials 필요한 Provider → 자동화 UI
            if (hasToken && needsCredentials)
            {
                DrawAutoProviderSetup(settings, providerKey, guide, projectId);
                EditorGUILayout.EndVertical();
                return;
            }

            // GPGS: 외부 가이드 + 마지막 단계만 자동화
            if (hasToken && providerKey == "GPGS")
            {
                DrawGpgsAutoGuide(settings, providerKey, guide, projectId);
                EditorGUILayout.EndVertical();
                return;
            }

            // fallback: 기존 step 가이드
            DrawStepBasedGuide(guide, providerKey, projectId);
            EditorGUILayout.EndVertical();
        }

        // ── 설정 완료 체크 ──

        bool IsProviderConfigured(string provider)
        {
            var prefix = AuthProviderGuide.GetApiFieldPrefix(provider);
            if (prefix == null) return false;

            if (!AuthConfigParser.IsFieldTrue(_authConfigJson, $"{prefix}_enabled")) return false;

            // Guest는 enabled만으로 완료
            if (provider == "Guest") return true;

            // OAuth는 client_id도 필요
            if (!AuthProviderGuide.RequiresClientCredentials(provider)) return true;
            return AuthConfigParser.GetStringFieldState(_authConfigJson, $"{prefix}_client_id")
                == AuthConfigParser.FieldState.Set;
        }

        // ── 설정 완료 화면 ──

        void DrawProviderCompleted(SupaRunSettings settings, string providerKey, GuideInfo guide)
        {
            EditorGUILayout.LabelField("✓ Supabase 활성화됨", EditorStyles.wordWrappedMiniLabel);

            if (AuthProviderGuide.RequiresClientCredentials(providerKey))
                EditorGUILayout.LabelField("✓ Client ID 설정됨", EditorStyles.wordWrappedMiniLabel);

            // nonce skip 확인
            var prefix = AuthProviderGuide.GetApiFieldPrefix(providerKey);
            if (prefix != null && AuthConfigParser.IsFieldTrue(_authConfigJson, $"{prefix}_skip_nonce_check"))
                EditorGUILayout.LabelField("✓ nonce skip 활성화됨", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(4);
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorGUILayout.LinkButton("Supabase에서 확인"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/auth/providers");
            }
        }

        // ── 자동화 Provider UI (Google, Apple, Discord 등) ──

        void DrawAutoProviderSetup(SupaRunSettings settings, string providerKey, GuideInfo guide, string projectId)
        {
            // Step 1: 외부 서비스에서 Client ID/Secret 발급
            EditorGUILayout.LabelField("  ① 외부 서비스에서 OAuth 앱 등록", EditorStyles.boldLabel);
            var providerLower = providerKey.ToLower();
            EditorGUILayout.LabelField(
                $"  {guide.displayName} 개발자 콘솔에서 OAuth 앱을 만들고\n  Client ID와 Secret을 발급받으세요.",
                EditorStyles.wordWrappedMiniLabel);
            if (EditorGUILayout.LinkButton("공식 설정 가이드"))
                Application.OpenURL($"https://supabase.com/docs/guides/auth/social-login/auth-{providerLower}");

            GUILayout.Space(8);

            // Step 2: Client ID/Secret 입력 + [Supabase에 적용]
            EditorGUILayout.LabelField("  ② Client ID / Secret 입력 → 자동 적용", EditorStyles.boldLabel);

            if (!_providerClientId.ContainsKey(providerKey)) _providerClientId[providerKey] = "";
            if (!_providerSecret.ContainsKey(providerKey)) _providerSecret[providerKey] = "";

            _providerClientId[providerKey] = EditorGUILayout.TextField("Client ID", _providerClientId[providerKey]);
            _providerSecret[providerKey] = EditorGUILayout.PasswordField("Client Secret", _providerSecret[providerKey]);

            GUILayout.Space(4);

            if (!_providerApplyState.ContainsKey(providerKey)) _providerApplyState[providerKey] = "";
            var state = _providerApplyState[providerKey];

            if (state == "applying")
            {
                EditorGUILayout.HelpBox("Supabase에 적용 중...", MessageType.Info);
            }
            else if (state == "done")
            {
                EditorGUILayout.LabelField(
                    "✓ Supabase에 자동 적용 완료! (활성화 + nonce skip + email optional)",
                    EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                if (state.StartsWith("error:"))
                    EditorGUILayout.LabelField($"✗ {state.Substring(6)}", EditorStyles.wordWrappedMiniLabel);

                var canApply = !string.IsNullOrEmpty(_providerClientId[providerKey]) &&
                               !string.IsNullOrEmpty(_providerSecret[providerKey]);
                using (new EditorGUI.DisabledGroupScope(!canApply))
                {
                    if (GUILayout.Button("Supabase에 적용", GUILayout.Height(28)))
                        _ = ApplyProviderToSupabase(settings, providerKey);
                }
                if (!canApply)
                    EditorGUILayout.LabelField("  Client ID와 Secret을 입력하세요.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        // ── GPGS: 외부 가이드 3단계 + 마지막 자동 ──

        void DrawGpgsAutoGuide(SupaRunSettings settings, string providerKey, GuideInfo guide, string projectId)
        {
            // Step 1~3: 기존 가이드
            if (!_providerStep.ContainsKey(providerKey)) _providerStep[providerKey] = 0;
            var step = _providerStep[providerKey];

            if (step < 3)
            {
                // 외부 설정 가이드 (Step 1~3)
                DrawSingleStep(guide, providerKey, projectId, step, 4);
            }
            else
            {
                // Step 4: Client ID/Secret → 자동 적용 (Google provider 경유)
                EditorGUILayout.LabelField("  Step 4/4: Supabase 자동 설정", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "  GPGS는 Google OAuth 기반입니다.\n  Google Cloud Console의 Client ID/Secret을 입력하세요.",
                    EditorStyles.wordWrappedMiniLabel);

                if (!_providerClientId.ContainsKey(providerKey)) _providerClientId[providerKey] = "";
                if (!_providerSecret.ContainsKey(providerKey)) _providerSecret[providerKey] = "";

                _providerClientId[providerKey] = EditorGUILayout.TextField("Client ID", _providerClientId[providerKey]);
                _providerSecret[providerKey] = EditorGUILayout.PasswordField("Client Secret", _providerSecret[providerKey]);

                GUILayout.Space(4);

                if (!_providerApplyState.ContainsKey(providerKey)) _providerApplyState[providerKey] = "";
                var state = _providerApplyState[providerKey];

                if (state == "done")
                {
                    EditorGUILayout.LabelField("✓ Google provider 자동 적용 완료!", EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    if (state == "applying") EditorGUILayout.HelpBox("적용 중...", MessageType.Info);
                    if (state.StartsWith("error:"))
                        EditorGUILayout.LabelField($"✗ {state.Substring(6)}", EditorStyles.wordWrappedMiniLabel);

                    var canApply = !string.IsNullOrEmpty(_providerClientId[providerKey]) &&
                                   !string.IsNullOrEmpty(_providerSecret[providerKey]);
                    using (new EditorGUI.DisabledGroupScope(!canApply || state == "applying"))
                    {
                        if (GUILayout.Button("Supabase에 적용", GUILayout.Height(28)))
                            _ = ApplyProviderToSupabase(settings, providerKey);
                    }
                }

                // 이전 버튼
                GUILayout.Space(4);
                if (GUILayout.Button("< 이전", GUILayout.Height(24)))
                {
                    _providerStep[providerKey] = 2;
                    GUI.FocusControl(null);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // ── 기존 Step 기반 가이드 (fallback) ──

        void DrawStepBasedGuide(GuideInfo guide, string providerKey, string projectId)
        {
            if (guide.guideSteps == null || guide.guideSteps.Length == 0) return;

            if (!_providerStep.ContainsKey(providerKey)) _providerStep[providerKey] = 0;
            var step = _providerStep[providerKey];
            DrawSingleStep(guide, providerKey, projectId, step, guide.guideSteps.Length);
        }

        void DrawSingleStep(GuideInfo guide, string providerKey, string projectId, int step, int total)
        {
            if (step >= guide.guideSteps.Length) step = guide.guideSteps.Length - 1;
            var current = guide.guideSteps[step];

            EditorGUILayout.LabelField($"  Step {step + 1}/{total}", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var desc = current.description
                .Replace("{Supabase프로젝트ID}", projectId)
                .Replace("{PROJECT_ID}", projectId);
            var lines = desc.Split('\n').Length;
            EditorGUILayout.SelectableLabel(desc, EditorStyles.wordWrappedLabel,
                GUILayout.Height(18 * lines));

            GUILayout.Space(4);
            if (current.links != null)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var (label, url) in current.links)
                {
                    if (EditorGUILayout.LinkButton(label))
                        Application.OpenURL(url.Replace("{PROJECT_ID}", projectId));
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (step > 0 && GUILayout.Button("< 이전", GUILayout.Height(24)))
            {
                _providerStep[providerKey] = step - 1;
                GUI.FocusControl(null);
                GUIUtility.ExitGUI();
            }
            GUILayout.FlexibleSpace();
            if (step < guide.guideSteps.Length - 1 && GUILayout.Button("다음 >", GUILayout.Height(24)))
            {
                _providerStep[providerKey] = step + 1;
                GUI.FocusControl(null);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Provider Supabase 적용 ──

        async UniTaskVoid ApplyProviderToSupabase(SupaRunSettings settings, string providerKey)
        {
            _providerApplyState[providerKey] = "applying";

            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            var projectRef = settings.SupabaseProjectId;

            // GPGS는 Google provider로 적용
            var apiPrefix = providerKey == "GPGS"
                ? AuthProviderGuide.GetApiFieldPrefix("Google")
                : AuthProviderGuide.GetApiFieldPrefix(providerKey);

            if (apiPrefix == null)
            {
                _providerApplyState[providerKey] = "error:이 Provider는 API 자동 설정을 지원하지 않습니다";
                return;
            }

            var clientId = _providerClientId.ContainsKey(providerKey) ? _providerClientId[providerKey] : "";
            var secret = _providerSecret.ContainsKey(providerKey) ? _providerSecret[providerKey] : "";

            // JSON body 구성: 활성화 + credentials + nonce skip + email optional
            var body = "{" +
                $"\"{apiPrefix}_enabled\":true," +
                $"\"{apiPrefix}_client_id\":\"{EscapeJson(clientId)}\"," +
                $"\"{apiPrefix}_secret\":\"{EscapeJson(secret)}\"," +
                $"\"{apiPrefix}_skip_nonce_check\":true";

            // email optional은 별도 필드가 아닌 provider별 다름
            // Supabase는 대부분 provider에 _email_optional이 없고 글로벌 설정
            body += "}";

            var r = await SupabaseManagementApi.PatchAuthConfig(projectRef, token, body);

            _providerApplyState[providerKey] = r.Ok ? "done" : $"error:{r.Message}";

            // 적용 성공 시 auth config 캐시 갱신
            if (r.Ok) _authConfigLoaded = false;
        }

        static string EscapeJson(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        // ── Auth URL 동기화 섹션 ──

        void DrawAuthUrlSection(SupaRunSettings settings)
        {
            var bundleId = PlayerSettings.applicationIdentifier;
            var mobileUrl = $"{bundleId}://auth";
            var pcUrl = !string.IsNullOrEmpty(settings.cloudRunUrl)
                ? $"{settings.cloudRunUrl.TrimEnd('/')}/auth/callback"
                : null;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 현재 값 표시
            GUILayout.Space(2);
            EditorGUILayout.LabelField($"  Site URL: {mobileUrl}");
            EditorGUILayout.LabelField($"  Redirect: {mobileUrl}");
            if (pcUrl != null)
                EditorGUILayout.LabelField($"  Redirect: {pcUrl}");
            EditorGUILayout.LabelField("  Redirect: http://localhost:*/**");

            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                GUILayout.Space(2);
                if (EditorGUILayout.LinkButton("Supabase에서 확인"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/auth/url-configuration");
            }

            EditorGUILayout.EndVertical();
        }

        // ── GCP 카드 (공용 UI) ──

        void DrawGcpCard(SupaRunSettings settings, PrerequisiteChecker.ToolStatus gcloud)
        {
            var apiOk = settings.gcpCloudRunApiEnabled;
            var saOk = !string.IsNullOrEmpty(settings.gcpServiceAccountEmail);

            string status;
            int state;
            if (gcloud.LoggedIn && apiOk && saOk) { status = "설정됨"; state = 1; }
            else if (gcloud.Installed) { status = "설정 필요"; state = 2; }
            else { status = "미설정"; state = 0; }

            var summary = state == 1
                ? $"{settings.gcpProjectId} ({settings.gcpRegion})"
                : "서버 배포에 필요합니다";

            BeginServiceCard("GCP", status, state, summary, ref _gcpExpanded);

            if (_gcpExpanded)
            {
                GUILayout.Space(4);
                GcpSetupUI.Draw(_dashboard, settings);
            }

            EndServiceCard();
        }

        // ── Supabase API 연동 메서드 ──

        // ── Auth Config 조회 (provider 상태 확인용) ──

        async UniTaskVoid FetchAuthConfig(SupaRunSettings settings)
        {
            _authConfigLoading = true;
            var r = await SupabaseManagementApi.GetAuthConfig(
                settings.SupabaseProjectId, SupaRunSettings.Instance.SupabaseAccessToken);
            _authConfigLoading = false;
            if (r.Ok)
            {
                _authConfigJson = r.Value;
                _authConfigLoaded = true;
            }
            _dashboard.Repaint();
        }

        /// <summary>캐시된 auth config에서 provider 상태를 읽어 표시 문자열 반환.</summary>
        string GetProviderSupabaseStatus(string provider)
        {
            if (string.IsNullOrEmpty(_authConfigJson)) return null;

            var prefix = AuthProviderGuide.GetApiFieldPrefix(provider);
            if (prefix == null) return null; // GPGS, GameCenter

            // enabled 키가 config에 없으면(or malformed) 표시 안 함 — 원본 동작 보존
            if (AuthConfigParser.GetStringFieldState(_authConfigJson, $"{prefix}_enabled") == AuthConfigParser.FieldState.Missing)
                return null;
            if (!AuthConfigParser.IsFieldTrue(_authConfigJson, $"{prefix}_enabled"))
                return "[Supabase 미활성화]";

            // Client ID 확인 (Guest 제외)
            if (provider == "Guest") return "[Supabase 활성화됨]";

            switch (AuthConfigParser.GetStringFieldState(_authConfigJson, $"{prefix}_client_id"))
            {
                case AuthConfigParser.FieldState.Missing: return "[활성화됨, Client ID 미확인]";
                case AuthConfigParser.FieldState.Empty: return "[활성화됨, Client ID 미설정]";
                default: return "[설정 완료]";
            }
        }

        async UniTaskVoid FetchSettingsProjects()
        {
            _settingsLoadingProjects = true;
            var r = await SupabaseManagementApi.ListProjects(
                SupaRunSettings.Instance.SupabaseAccessToken);
            _settingsLoadingProjects = false;

            if (r.Ok)
            {
                var projects = r.Value;
                _settingsProjects = projects;
                _settingsProjectLabels = new string[projects.Length];
                for (var i = 0; i < projects.Length; i++)
                    _settingsProjectLabels[i] = $"{projects[i].name} ({projects[i].region})";

                // 현재 URL과 매칭
                var currentRef = SupaRunSettings.Instance.SupabaseProjectId;
                if (!string.IsNullOrEmpty(currentRef))
                {
                    for (var i = 0; i < projects.Length; i++)
                    {
                        if (projects[i].id == currentRef)
                        { _settingsProjectIndex = i; break; }
                    }
                }
            }
        }

        async UniTaskVoid FetchAnonKey(SupaRunSettings settings)
        {
            var r = await SupabaseManagementApi.GetAnonKey(
                settings.SupabaseProjectId, SupaRunSettings.Instance.SupabaseAccessToken);
            if (r.Ok)
            {
                SupaRunSettings.Instance.SupabaseAnonKey = r.Value;
                _dashboard.ShowNotification("Anon Key 자동 조회 완료", SupaRunUI.NotificationType.Success);
            }
            else
            {
                _dashboard.ShowNotification($"조회 실패: {r.ToShortString()}", SupaRunUI.NotificationType.Error);
            }
        }

        async UniTaskVoid RunConnectionTest(SupaRunSettings settings)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                _dashboard.ShowNotification("Access Token을 입력하면 상세 연결 테스트가 가능합니다", SupaRunUI.NotificationType.Info);
                return;
            }

            // Phase 1: Management API (프로젝트 상태)
            _dashboard.ShowNotification("1/2 프로젝트 상태 확인 중...", SupaRunUI.NotificationType.Info);

            var info = await SupabaseManagementApi.GetProject(settings.SupabaseProjectId, token);
            if (!info.Ok)
            {
                _dashboard.ShowNotification($"연결 실패: {info.ToShortString()}", SupaRunUI.NotificationType.Error);
                return;
            }
            var (name, status, region) = (info.Value.name, info.Value.status, info.Value.region);

            // Phase 2: DB Connection (Password 검증)
            var dbPw = SupaRunSettings.Instance.SupabaseDbPassword;
            if (!string.IsNullOrEmpty(dbPw))
            {
                _dashboard.ShowNotification("2/2 DB 비밀번호 검증 중...", SupaRunUI.NotificationType.Info);

                var projectId = settings.SupabaseProjectId;
                var (dbOk, dbError) = await PostgresConnectionTester.VerifyPassword(
                    projectId, token, dbPw);

                if (!dbOk)
                {
                    _dashboard.ShowNotification($"DB 연결 실패: {dbError}", SupaRunUI.NotificationType.Error);
                    return;
                }

                _dashboard.ShowNotification($"{name} ({region}) — {status} + DB 연결 OK", SupaRunUI.NotificationType.Success);
            }
            else
            {
                _dashboard.ShowNotification($"{name} ({region}) — {status} (DB 비밀번호 미입력)", SupaRunUI.NotificationType.Success);
            }
        }

        /// <summary>Provider를 Supabase에 활성화. Access Token 필요.</summary>
        async UniTaskVoid EnableProviderOnSupabase(SupaRunSettings settings, string provider)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;

            var prefix = AuthProviderGuide.GetApiFieldPrefix(provider);
            if (prefix == null) return;

            var body = $"{{\"{prefix}_enabled\":true}}";
            var r = await SupabaseManagementApi.PatchAuthConfig(
                settings.SupabaseProjectId, token, body);

            if (r.Ok)
                Debug.Log($"[SupaRun] {provider} Supabase에 자동 활성화됨");
            else
                r.LogIfFailed($"{provider} 활성화");
        }

        /// <summary>Provider를 Supabase에서 비활성화.</summary>
        async UniTaskVoid DisableProviderOnSupabase(SupaRunSettings settings, string provider)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;

            var prefix = AuthProviderGuide.GetApiFieldPrefix(provider);
            if (prefix == null) return;

            var body = $"{{\"{prefix}_enabled\":false}}";
            var r = await SupabaseManagementApi.PatchAuthConfig(
                settings.SupabaseProjectId, token, body);

            if (r.Ok)
                Debug.Log($"[SupaRun] {provider} Supabase에서 비활성화됨");
        }

        // ── 서버 로그 ──

        void DrawLogSection(SupaRunSettings settings)
        {
            _foldLog = EditorGUILayout.Foldout(_foldLog, "서버 로그", true);
            if (!_foldLog)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var newVal = EditorGUILayout.Toggle(
                new GUIContent("Cloud Run 로그 -> Console", "배포된 서버 로그를 Unity Console에 표시"),
                settings.serverLogToConsole);
            if (newVal != settings.serverLogToConsole)
            {
                settings.serverLogToConsole = newVal;
                settings.Save();
            }
            EditorGUILayout.EndVertical();
        }
    }
}
