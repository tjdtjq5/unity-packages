using System;
using Cysharp.Threading.Tasks;
using Tjdtjq5.EditorToolkit.Editor;
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

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var settings = SupaRunSettings.Instance;
            var gh = PrerequisiteChecker.CheckGh();
            var gcloud = PrerequisiteChecker.CheckGcloud();

            // 상태 요약 바
            EditorUI.DrawStatusBar(new[]
            {
                ("Supabase", settings.IsSupabaseConfigured ? 1 : 0),
                ("GitHub", gh.LoggedIn && settings.IsGitHubConfigured ? 1 : gh.Installed ? 2 : 0),
                ("GCP", gcloud.LoggedIn && settings.gcpCloudRunApiEnabled ? 1
                    : gcloud.Installed ? 2 : 0),
            });

            // 시크릿 저장 위치 안내 — private repo 전용 가정
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "⚠ 시크릿(DB Password, Access Token, GitHub Token, Cron Secret)은 " +
                "ProjectSettings/SupaRunProjectSettings.json에 평문 저장되어 git에 커밋됩니다. " +
                "private repo 전용 사용을 가정합니다.",
                MessageType.Warning);
            GUILayout.Space(4);

            DrawSupabaseCard(settings);
            DrawGitHubCard(settings, gh);
            DrawGcpCard(settings, gcloud);
            DrawAuthCard(settings);
            DrawToolsCard(gh, gcloud);

            GUILayout.Space(4);
            DrawLogSection(settings);

            GUILayout.Space(8);
            EditorUI.DrawActionBar(new (string, Color, Action)[]
            {
                ("저장", EditorUI.COL_SUCCESS, () =>
                {
                    settings.Save();
                    _dashboard.ShowNotification("설정 저장 완료", EditorUI.NotificationType.Success);
                }),
                ("초기 설정 다시 실행", EditorUI.COL_WARN, () =>
                {
                    if (EditorUtility.DisplayDialog("초기 설정",
                        "Setup Wizard를 처음부터 다시 시작합니다.\n기존 설정은 유지됩니다.", "확인", "취소"))
                    {
                        settings.setupCompleted = false;
                        settings.Save();
                        _dashboard.OpenSetup();
                    }
                }),
            });

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

            EditorUI.BeginServiceCard("Tools", EditorUI.COL_INFO,
                status, state, summary, ref _toolsExpanded);

            if (_toolsExpanded)
            {
                GUILayout.Space(4);

                // .NET SDK
                if (dotnet)
                    EditorUI.DrawCellLabel(
                        $"  .NET SDK {PrerequisiteChecker.GetDotnetMajorVersion()}.0", 0, EditorUI.COL_SUCCESS);
                else
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel("  .NET SDK 미설치", 0, EditorUI.COL_WARN);
                    if (EditorUI.DrawLinkButton("설치하기"))
                        Application.OpenURL("https://dotnet.microsoft.com/download");
                    EditorUI.EndRow();
                }

                // gh CLI
                if (gh.LoggedIn)
                    EditorUI.DrawCellLabel(
                        $"  gh CLI {gh.Version} ({gh.Account})", 0, EditorUI.COL_SUCCESS);
                else if (gh.Installed)
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel($"  gh CLI {gh.Version} (로그인 필요)", 0, EditorUI.COL_WARN);
                    if (EditorUI.DrawLinkButton("로그인"))
                        PrerequisiteChecker.RunGhLogin();
                    EditorUI.EndRow();
                }
                else
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel("  gh CLI 미설치", 0, EditorUI.COL_WARN);
                    if (EditorUI.DrawLinkButton("설치하기"))
                        Application.OpenURL("https://cli.github.com");
                    EditorUI.EndRow();
                }

                // gcloud CLI
                if (gcloud.LoggedIn)
                    EditorUI.DrawCellLabel(
                        $"  gcloud CLI {gcloud.Version} ({gcloud.Account})", 0, EditorUI.COL_SUCCESS);
                else if (gcloud.Installed)
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel($"  gcloud CLI {gcloud.Version} (로그인 필요)", 0, EditorUI.COL_WARN);
                    if (EditorUI.DrawLinkButton("로그인"))
                        PrerequisiteChecker.RunGcloudLogin();
                    EditorUI.EndRow();
                }
                else
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel("  gcloud CLI 미설치", 0, EditorUI.COL_WARN);
                    if (EditorUI.DrawLinkButton("설치하기"))
                        Application.OpenURL("https://cloud.google.com/sdk/docs/install");
                    EditorUI.EndRow();
                }

                GUILayout.Space(4);
                if (EditorUI.DrawColorButton("새로고침", EditorUI.COL_MUTED))
                    PrerequisiteChecker.InvalidateCache();
            }

            EditorUI.EndServiceCard(ref _toolsExpanded);
        }

        // ── Supabase 카드 ──

        void DrawSupabaseCard(SupaRunSettings settings)
        {
            var status = settings.IsSupabaseConfigured ? "Connected" : "미설정";
            var state = settings.IsSupabaseConfigured ? 1 : 0;
            var summary = settings.IsSupabaseConfigured
                ? settings.supabaseUrl : "Supabase 설정이 필요합니다";
            var hasToken = !string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken);

            var expanded = EditorUI.BeginServiceCard("Supabase", SupaRunDashboard.COL_SUPABASE,
                status, state, summary, ref _supabaseExpanded);

            if (expanded)
            {
                // ── Access Token (최상단) ──
                GUILayout.Space(4);
                var token = EditorUI.DrawPasswordField("Access Token", SupaRunSettings.Instance.SupabaseAccessToken,
                    "자동 설정용");
                if (token != SupaRunSettings.Instance.SupabaseAccessToken)
                {
                    SupaRunSettings.Instance.SupabaseAccessToken = token;
                    _settingsProjects = null;
                    _settingsProjectIndex = -1;
                    AuthUrlSyncManager.InvalidateCache();
                }
                EditorUI.BeginRow();
                if (EditorUI.DrawLinkButton("Access Token 발급"))
                    Application.OpenURL("https://supabase.com/dashboard/account/tokens");
                EditorUI.FlexSpace();
                if (hasToken && _settingsProjects == null && !_settingsLoadingProjects)
                {
                    if (EditorUI.DrawColorButton("프로젝트 조회", SupaRunDashboard.COL_SUPABASE))
                        FetchSettingsProjects();
                }
                EditorUI.EndRow();

                if (_settingsLoadingProjects)
                    EditorUI.DrawLoading(true, "프로젝트 목록 조회 중...");

                GUILayout.Space(6);
                EditorUI.DrawCellLabel("── 프로젝트 설정 ──", 0, EditorUI.COL_MUTED);
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
                        FetchAnonKey(settings);
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
                    EditorUI.DrawCellLabel($"  Anon Key: {anonDisplay}", 0,
                        string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAnonKey) ? EditorUI.COL_MUTED : EditorUI.COL_SUCCESS);
                }
                else
                {
                    var anonKey = EditorUI.DrawTextField("Anon Key", SupaRunSettings.Instance.SupabaseAnonKey, "수동 입력");
                    if (anonKey != SupaRunSettings.Instance.SupabaseAnonKey)
                        SupaRunSettings.Instance.SupabaseAnonKey = anonKey;
                    if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
                    {
                        if (EditorUI.DrawLinkButton("API Keys 페이지에서 복사"))
                            Application.OpenURL(settings.SupabaseApiSettingsUrl);
                    }
                }

                // ── DB Password ──
                GUILayout.Space(2);
                var dbPw = EditorUI.DrawPasswordField("DB Password", SupaRunSettings.Instance.SupabaseDbPassword, "프로젝트 생성 시 비밀번호");
                if (dbPw != SupaRunSettings.Instance.SupabaseDbPassword)
                    SupaRunSettings.Instance.SupabaseDbPassword = dbPw;
            }

            GUILayout.Space(4);
            EditorUI.BeginRow();
            if (EditorUI.DrawColorButton("연결 테스트", SupaRunDashboard.COL_SUPABASE))
                RunConnectionTest(settings);
            EditorUI.FlexSpace();
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorUI.DrawLinkButton("대시보드"))
                    Application.OpenURL(settings.SupabaseDashboardUrl);
            }
            EditorUI.EndRow();

            EditorUI.EndServiceCard(ref _supabaseExpanded);
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

            EditorUI.BeginServiceCard("GitHub", SupaRunDashboard.COL_GITHUB,
                status, state, summary, ref _githubExpanded);

            if (_githubExpanded)
            {
                GUILayout.Space(4);
                GitHubSetupUI.Draw(_dashboard, settings);
            }

            EditorUI.EndServiceCard(ref _githubExpanded);
        }

        // ── Auth 카드 ──

        void DrawAuthCard(SupaRunSettings settings)
        {
            var count = settings.enabledAuthProviders.Count;
            var status = $"{count}개 활성";
            var summary = string.Join(", ", settings.enabledAuthProviders);

            EditorUI.BeginServiceCard("Auth", SupaRunDashboard.COL_PRIMARY,
                status, 1, summary, ref _authExpanded);

            if (_authExpanded)
            {
                GUILayout.Space(4);

                // Auth config 조회 (Access Token이 있으면 펼칠 때 한번만)
                var hasToken = !string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken);
                if (hasToken && !_authConfigLoaded && !_authConfigLoading)
                    FetchAuthConfig(settings);

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
                    EditorUI.BeginSubBox();

                    EditorUI.BeginRow();

                    // > / v + 이름 + Supabase 상태
                    var supabaseStatus = GetProviderSupabaseStatus(provider);
                    var label = supabaseStatus != null
                        ? $"{guide.displayName}  {supabaseStatus}"
                        : guide.displayName;
                    if (EditorUI.DrawToggleRow(label, isExpanded, EditorUI.COL_INFO))
                    {
                        _providerExpanded[provider] = !isExpanded;
                        GUI.FocusControl(null);
                    }

                    // [x] 제거
                    EditorUI.FlexSpace();
                    if (EditorUI.DrawRemoveButton())
                    {
                        var msg = provider == "Guest"
                            ? "Guest를 제거하면 자동 로그인이 비활성화됩니다.\n게임 시작 시 직접 로그인 UI를 구현해야 합니다."
                            : $"{guide.displayName} 로그인을 제거합니다.";
                        if (EditorUtility.DisplayDialog("로그인 방식 제거", msg, "제거", "취소"))
                            toRemove = provider;
                    }
                    EditorUI.EndRow();

                    // 펼침 → 가이드
                    if (isExpanded)
                        DrawProviderGuide(settings, provider, guide);

                    EditorUI.EndSubBox();
                }

                // 제거 처리
                if (toRemove != null)
                {
                    DisableProviderOnSupabase(settings, toRemove);
                    settings.enabledAuthProviders.Remove(toRemove);
                    _providerExpanded.Remove(toRemove);
                    settings.Save();
                }

                GUILayout.Space(4);

                // [+ 로그인 방식 추가]
                if (_showProviderDropdown)
                {
                    EditorUI.BeginBody();
                    bool hasAvailable = false;
                    foreach (var p in AuthProviderGuide.AvailableProviders)
                    {
                        if (settings.enabledAuthProviders.Contains(p)) continue;
                        hasAvailable = true;
                        var guide = AuthProviderGuide.Get(p);
                        var label = guide.requiresSDK ? $"{guide.displayName} (SDK)" : guide.displayName;
                        if (EditorUI.DrawMiniButton(label))
                        {
                            settings.enabledAuthProviders.Add(p);
                            settings.Save();
                            EnableProviderOnSupabase(settings, p);
                            _showProviderDropdown = false;
                        }
                    }
                    if (!hasAvailable)
                        EditorUI.DrawDescription("모든 로그인 방식이 추가되었습니다.", EditorUI.COL_MUTED);
                    GUILayout.Space(2);
                    if (EditorUI.DrawColorButton("닫기", EditorUI.COL_MUTED))
                        _showProviderDropdown = false;
                    EditorUI.EndBody();
                }
                else
                {
                    if (EditorUI.DrawColorButton("+ 로그인 방식 추가", EditorUI.COL_MUTED))
                        _showProviderDropdown = true;
                }
            }

            EditorUI.EndServiceCard(ref _authExpanded);
        }

        void DrawProviderGuide(SupaRunSettings settings, string providerKey, GuideInfo guide)
        {
            EditorUI.BeginBody();
            var projectId = settings.SupabaseProjectId;
            var hasToken = !string.IsNullOrEmpty(SupaRunSettings.Instance.SupabaseAccessToken);
            var needsCredentials = AuthProviderGuide.RequiresClientCredentials(providerKey);

            // 설정 완료 체크 — 완료됐으면 상태 요약만 표시
            if (IsProviderConfigured(providerKey))
            {
                DrawProviderCompleted(settings, providerKey, guide);
                EditorUI.EndBody();
                return;
            }

            // SDK 상태
            if (guide.requiresSDK)
            {
                var installed = AuthProviderGuide.IsSDKInstalled(providerKey);
                EditorUI.DrawCellLabel(
                    installed ? $"  {guide.sdkName} 설치됨" : $"  {guide.sdkName} 미설치",
                    0, installed ? EditorUI.COL_SUCCESS : EditorUI.COL_WARN);
                GUILayout.Space(4);
            }

            // Guest는 자동 처리됨 (추가 시 EnableProviderOnSupabase 호출)
            if (providerKey == "Guest")
            {
                if (hasToken)
                    EditorUI.DrawDescription("✓ Access Token으로 자동 활성화됨", EditorUI.COL_SUCCESS);
                else
                    DrawStepBasedGuide(guide, providerKey, projectId);
                EditorUI.EndBody();
                return;
            }

            // Access Token 있고 Credentials 필요한 Provider → 자동화 UI
            if (hasToken && needsCredentials)
            {
                DrawAutoProviderSetup(settings, providerKey, guide, projectId);
                EditorUI.EndBody();
                return;
            }

            // GPGS: 외부 가이드 + 마지막 단계만 자동화
            if (hasToken && providerKey == "GPGS")
            {
                DrawGpgsAutoGuide(settings, providerKey, guide, projectId);
                EditorUI.EndBody();
                return;
            }

            // fallback: 기존 step 가이드
            DrawStepBasedGuide(guide, providerKey, projectId);
            EditorUI.EndBody();
        }

        // ── 설정 완료 체크 ──

        bool IsProviderConfigured(string provider)
        {
            if (string.IsNullOrEmpty(_authConfigJson)) return false;

            var prefix = AuthProviderGuide.GetApiFieldPrefix(provider);
            if (prefix == null) return false;

            // enabled 확인
            var enabledKey = $"\"{prefix}_enabled\"";
            var idx = _authConfigJson.IndexOf(enabledKey, System.StringComparison.Ordinal);
            if (idx < 0) return false;
            var colonIdx = _authConfigJson.IndexOf(':', idx + enabledKey.Length);
            if (colonIdx < 0) return false;
            var afterColon = _authConfigJson.Substring(colonIdx + 1, System.Math.Min(10, _authConfigJson.Length - colonIdx - 1)).Trim();
            if (!afterColon.StartsWith("true")) return false;

            // Guest는 enabled만으로 완료
            if (provider == "Guest") return true;

            // OAuth는 client_id도 필요
            if (!AuthProviderGuide.RequiresClientCredentials(provider)) return true;
            var cidKey = $"\"{prefix}_client_id\"";
            var cidIdx = _authConfigJson.IndexOf(cidKey, System.StringComparison.Ordinal);
            if (cidIdx < 0) return false;
            var cidColon = _authConfigJson.IndexOf(':', cidIdx + cidKey.Length);
            if (cidColon < 0) return false;
            var cidAfter = _authConfigJson.Substring(cidColon + 1, System.Math.Min(10, _authConfigJson.Length - cidColon - 1)).Trim();
            return !cidAfter.StartsWith("\"\"") && !cidAfter.StartsWith("null");
        }

        // ── 설정 완료 화면 ──

        void DrawProviderCompleted(SupaRunSettings settings, string providerKey, GuideInfo guide)
        {
            EditorUI.DrawDescription("✓ Supabase 활성화됨", EditorUI.COL_SUCCESS);

            if (AuthProviderGuide.RequiresClientCredentials(providerKey))
                EditorUI.DrawDescription("✓ Client ID 설정됨", EditorUI.COL_SUCCESS);

            // nonce skip 확인
            var prefix = AuthProviderGuide.GetApiFieldPrefix(providerKey);
            if (prefix != null && _authConfigJson != null)
            {
                var nonceKey = $"\"{prefix}_skip_nonce_check\"";
                if (_authConfigJson.IndexOf(nonceKey, System.StringComparison.Ordinal) >= 0)
                {
                    var nIdx = _authConfigJson.IndexOf(nonceKey, System.StringComparison.Ordinal);
                    var nColon = _authConfigJson.IndexOf(':', nIdx + nonceKey.Length);
                    if (nColon >= 0)
                    {
                        var nAfter = _authConfigJson.Substring(nColon + 1, System.Math.Min(10, _authConfigJson.Length - nColon - 1)).Trim();
                        if (nAfter.StartsWith("true"))
                            EditorUI.DrawDescription("✓ nonce skip 활성화됨", EditorUI.COL_SUCCESS);
                    }
                }
            }

            GUILayout.Space(4);
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorUI.DrawLinkButton("Supabase에서 확인"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/auth/providers");
            }
        }

        // ── 자동화 Provider UI (Google, Apple, Discord 등) ──

        void DrawAutoProviderSetup(SupaRunSettings settings, string providerKey, GuideInfo guide, string projectId)
        {
            // Step 1: 외부 서비스에서 Client ID/Secret 발급
            EditorUI.DrawCellLabel("  ① 외부 서비스에서 OAuth 앱 등록", 0, EditorUI.COL_INFO);
            var providerLower = providerKey.ToLower();
            EditorUI.DrawDescription($"  {guide.displayName} 개발자 콘솔에서 OAuth 앱을 만들고\n  Client ID와 Secret을 발급받으세요.");
            if (EditorUI.DrawLinkButton("공식 설정 가이드"))
                Application.OpenURL($"https://supabase.com/docs/guides/auth/social-login/auth-{providerLower}");

            GUILayout.Space(8);

            // Step 2: Client ID/Secret 입력 + [Supabase에 적용]
            EditorUI.DrawCellLabel("  ② Client ID / Secret 입력 → 자동 적용", 0, EditorUI.COL_INFO);

            if (!_providerClientId.ContainsKey(providerKey)) _providerClientId[providerKey] = "";
            if (!_providerSecret.ContainsKey(providerKey)) _providerSecret[providerKey] = "";

            _providerClientId[providerKey] = EditorUI.DrawTextField("Client ID", _providerClientId[providerKey]);
            _providerSecret[providerKey] = EditorUI.DrawPasswordField("Client Secret", _providerSecret[providerKey]);

            GUILayout.Space(4);

            if (!_providerApplyState.ContainsKey(providerKey)) _providerApplyState[providerKey] = "";
            var state = _providerApplyState[providerKey];

            if (state == "applying")
            {
                EditorUI.DrawLoading(true, "Supabase에 적용 중...");
            }
            else if (state == "done")
            {
                EditorUI.DrawDescription("✓ Supabase에 자동 적용 완료! (활성화 + nonce skip + email optional)", EditorUI.COL_SUCCESS);
            }
            else
            {
                if (state.StartsWith("error:"))
                    EditorUI.DrawDescription($"✗ {state.Substring(6)}", EditorUI.COL_ERROR);

                var canApply = !string.IsNullOrEmpty(_providerClientId[providerKey]) &&
                               !string.IsNullOrEmpty(_providerSecret[providerKey]);
                using (new EditorGUI.DisabledGroupScope(!canApply))
                {
                    if (EditorUI.DrawColorButton("Supabase에 적용", SupaRunDashboard.COL_SUPABASE, 28))
                        ApplyProviderToSupabase(settings, providerKey);
                }
                if (!canApply)
                    EditorUI.DrawDescription("  Client ID와 Secret을 입력하세요.", EditorUI.COL_MUTED);
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
                EditorUI.DrawCellLabel("  Step 4/4: Supabase 자동 설정", 0, EditorUI.COL_INFO);
                EditorUI.DrawDescription("  GPGS는 Google OAuth 기반입니다.\n  Google Cloud Console의 Client ID/Secret을 입력하세요.");

                if (!_providerClientId.ContainsKey(providerKey)) _providerClientId[providerKey] = "";
                if (!_providerSecret.ContainsKey(providerKey)) _providerSecret[providerKey] = "";

                _providerClientId[providerKey] = EditorUI.DrawTextField("Client ID", _providerClientId[providerKey]);
                _providerSecret[providerKey] = EditorUI.DrawPasswordField("Client Secret", _providerSecret[providerKey]);

                GUILayout.Space(4);

                if (!_providerApplyState.ContainsKey(providerKey)) _providerApplyState[providerKey] = "";
                var state = _providerApplyState[providerKey];

                if (state == "done")
                {
                    EditorUI.DrawDescription("✓ Google provider 자동 적용 완료!", EditorUI.COL_SUCCESS);
                }
                else
                {
                    if (state == "applying") EditorUI.DrawLoading(true, "적용 중...");
                    if (state.StartsWith("error:")) EditorUI.DrawDescription($"✗ {state.Substring(6)}", EditorUI.COL_ERROR);

                    var canApply = !string.IsNullOrEmpty(_providerClientId[providerKey]) &&
                                   !string.IsNullOrEmpty(_providerSecret[providerKey]);
                    using (new EditorGUI.DisabledGroupScope(!canApply || state == "applying"))
                    {
                        if (EditorUI.DrawColorButton("Supabase에 적용", SupaRunDashboard.COL_SUPABASE, 28))
                            ApplyProviderToSupabase(settings, providerKey);
                    }
                }

                // 이전 버튼
                GUILayout.Space(4);
                if (EditorUI.DrawColorButton("< 이전", EditorUI.COL_MUTED, 24))
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

            EditorUI.DrawCellLabel($"  Step {step + 1}/{total}", 0, EditorUI.COL_INFO);
            GUILayout.Space(4);

            var desc = current.description
                .Replace("{Supabase프로젝트ID}", projectId)
                .Replace("{PROJECT_ID}", projectId);
            var lines = desc.Split('\n').Length;
            EditorGUILayout.SelectableLabel(desc,
                new GUIStyle(EditorStyles.label) { fontSize = 11, normal = { textColor = EditorUI.COL_MUTED }, wordWrap = true },
                GUILayout.Height(18 * lines));

            GUILayout.Space(4);
            if (current.links != null)
            {
                EditorUI.BeginRow();
                foreach (var (label, url) in current.links)
                {
                    if (EditorUI.DrawLinkButton(label))
                        Application.OpenURL(url.Replace("{PROJECT_ID}", projectId));
                }
                EditorUI.EndRow();
            }

            GUILayout.Space(4);
            EditorUI.BeginRow();
            if (step > 0 && EditorUI.DrawColorButton("< 이전", EditorUI.COL_MUTED, 24))
            {
                _providerStep[providerKey] = step - 1;
                GUI.FocusControl(null);
                GUIUtility.ExitGUI();
            }
            EditorUI.FlexSpace();
            if (step < guide.guideSteps.Length - 1 && EditorUI.DrawColorButton("다음 >", EditorUI.COL_INFO, 24))
            {
                _providerStep[providerKey] = step + 1;
                GUI.FocusControl(null);
                GUIUtility.ExitGUI();
            }
            EditorUI.EndRow();
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

            var (ok, error) = await SupabaseManagementApi.PatchAuthConfig(projectRef, token, body);

            _providerApplyState[providerKey] = ok ? "done" : $"error:{error}";

            // 적용 성공 시 auth config 캐시 갱신
            if (ok) _authConfigLoaded = false;
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
            EditorUI.BeginSubBox();

            // 현재 값 표시
            GUILayout.Space(2);
            EditorUI.DrawCellLabel($"  Site URL: {mobileUrl}", 0, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel($"  Redirect: {mobileUrl}", 0, EditorUI.COL_MUTED);
            if (pcUrl != null)
                EditorUI.DrawCellLabel($"  Redirect: {pcUrl}", 0, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel("  Redirect: http://localhost:*/**", 0, EditorUI.COL_MUTED);

            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                GUILayout.Space(2);
                if (EditorUI.DrawLinkButton("Supabase에서 확인"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/auth/url-configuration");
            }

            EditorUI.EndSubBox();
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

            EditorUI.BeginServiceCard("GCP", SupaRunDashboard.COL_GCP,
                status, state, summary, ref _gcpExpanded);

            if (_gcpExpanded)
            {
                GUILayout.Space(4);
                GcpSetupUI.Draw(_dashboard, settings);
            }

            EditorUI.EndServiceCard(ref _gcpExpanded);
        }

        // ── Supabase API 연동 메서드 ──

        // ── Auth Config 조회 (provider 상태 확인용) ──

        async UniTaskVoid FetchAuthConfig(SupaRunSettings settings)
        {
            _authConfigLoading = true;
            var (ok, json, _) = await SupabaseManagementApi.GetAuthConfig(
                settings.SupabaseProjectId, SupaRunSettings.Instance.SupabaseAccessToken);
            _authConfigLoading = false;
            if (ok)
            {
                _authConfigJson = json;
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

            var enabledKey = $"{prefix}_enabled";

            // JSON에서 해당 키 검색
            var idx = _authConfigJson.IndexOf($"\"{enabledKey}\"", System.StringComparison.Ordinal);
            if (idx < 0) return null;

            // 값 추출
            var colonIdx = _authConfigJson.IndexOf(':', idx + enabledKey.Length + 2);
            if (colonIdx < 0) return null;
            var afterColon = _authConfigJson.Substring(colonIdx + 1, System.Math.Min(10, _authConfigJson.Length - colonIdx - 1)).Trim();
            var enabled = afterColon.StartsWith("true");

            if (!enabled) return "[Supabase 미활성화]";

            // Client ID 확인 (Guest 제외)
            if (provider == "Guest") return "[Supabase 활성화됨]";

            var clientIdKey = $"{prefix}_client_id";
            var cidIdx = _authConfigJson.IndexOf($"\"{clientIdKey}\"", System.StringComparison.Ordinal);
            if (cidIdx < 0) return "[활성화됨, Client ID 미확인]";

            var cidColon = _authConfigJson.IndexOf(':', cidIdx + clientIdKey.Length + 2);
            if (cidColon < 0) return "[활성화됨]";
            var cidAfter = _authConfigJson.Substring(cidColon + 1, System.Math.Min(20, _authConfigJson.Length - cidColon - 1)).Trim();

            if (cidAfter.StartsWith("\"\"") || cidAfter.StartsWith("null"))
                return "[활성화됨, Client ID 미설정]";

            return "[설정 완료]";
        }

        async UniTaskVoid FetchSettingsProjects()
        {
            _settingsLoadingProjects = true;
            var (ok, projects, _) = await SupabaseManagementApi.ListProjects(
                SupaRunSettings.Instance.SupabaseAccessToken);
            _settingsLoadingProjects = false;

            if (ok)
            {
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
            var (ok, anonKey, error) = await SupabaseManagementApi.GetAnonKey(
                settings.SupabaseProjectId, SupaRunSettings.Instance.SupabaseAccessToken);
            if (ok)
            {
                SupaRunSettings.Instance.SupabaseAnonKey = anonKey;
                _dashboard.ShowNotification("Anon Key 자동 조회 완료", EditorUI.NotificationType.Success);
            }
            else
            {
                _dashboard.ShowNotification($"조회 실패: {error}", EditorUI.NotificationType.Error);
            }
        }

        async UniTaskVoid RunConnectionTest(SupaRunSettings settings)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                _dashboard.ShowNotification("Access Token을 입력하면 상세 연결 테스트가 가능합니다", EditorUI.NotificationType.Info);
                return;
            }

            // Phase 1: Management API (프로젝트 상태)
            _dashboard.ShowNotification("1/2 프로젝트 상태 확인 중...", EditorUI.NotificationType.Info);

            var (ok, name, status, region, error) = await SupabaseManagementApi.GetProjectInfo(
                settings.SupabaseProjectId, token);
            if (!ok)
            {
                _dashboard.ShowNotification($"연결 실패: {error}", EditorUI.NotificationType.Error);
                return;
            }

            // Phase 2: DB Connection (Password 검증)
            var dbPw = SupaRunSettings.Instance.SupabaseDbPassword;
            if (!string.IsNullOrEmpty(dbPw))
            {
                _dashboard.ShowNotification("2/2 DB 비밀번호 검증 중...", EditorUI.NotificationType.Info);

                var projectId = settings.SupabaseProjectId;
                var (dbOk, dbError) = await PostgresConnectionTester.VerifyPassword(
                    projectId, token, dbPw);

                if (!dbOk)
                {
                    _dashboard.ShowNotification($"DB 연결 실패: {dbError}", EditorUI.NotificationType.Error);
                    return;
                }

                _dashboard.ShowNotification($"{name} ({region}) — {status} + DB 연결 OK", EditorUI.NotificationType.Success);
            }
            else
            {
                _dashboard.ShowNotification($"{name} ({region}) — {status} (DB 비밀번호 미입력)", EditorUI.NotificationType.Success);
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
            var (ok, error) = await SupabaseManagementApi.PatchAuthConfig(
                settings.SupabaseProjectId, token, body);

            if (ok)
                Debug.Log($"[SupaRun] {provider} Supabase에 자동 활성화됨");
            else
                Debug.LogWarning($"[SupaRun] {provider} 활성화 실패: {error}");
        }

        /// <summary>Provider를 Supabase에서 비활성화.</summary>
        async UniTaskVoid DisableProviderOnSupabase(SupaRunSettings settings, string provider)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;

            var prefix = AuthProviderGuide.GetApiFieldPrefix(provider);
            if (prefix == null) return;

            var body = $"{{\"{prefix}_enabled\":false}}";
            var (ok, _) = await SupabaseManagementApi.PatchAuthConfig(
                settings.SupabaseProjectId, token, body);

            if (ok)
                Debug.Log($"[SupaRun] {provider} Supabase에서 비활성화됨");
        }

        // ── 서버 로그 ──

        void DrawLogSection(SupaRunSettings settings)
        {
            if (!EditorUI.DrawSectionFoldout(ref _foldLog, "서버 로그", EditorUI.COL_INFO))
                return;

            EditorUI.BeginBody();
            var newVal = EditorGUILayout.Toggle(
                new GUIContent("Cloud Run 로그 -> Console", "배포된 서버 로그를 Unity Console에 표시"),
                settings.serverLogToConsole);
            if (newVal != settings.serverLogToConsole)
            {
                settings.serverLogToConsole = newVal;
                settings.Save();
            }
            EditorUI.EndBody();
        }
    }
}
