using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
// ReSharper disable once RedundantUsingDirective
using System;

namespace Tjdtjq5.GameServer.Editor
{
    public class SupabaseSetupStep : ISetupStep
    {
        readonly GameServerDashboard _dashboard;
        enum TestState { None, Testing, Success, Failed }
        TestState _testState;
        string _testError;

        public string Title => "Supabase 연결";
        public string Description => "게임 데이터를 저장할 데이터베이스입니다. 무료로 시작할 수 있습니다.";
        public Color AccentColor => GameServerDashboard.COL_SUPABASE;
        public bool IsRequired => true;
        public bool IsCompleted => _testState == TestState.Success;
        public bool IsSkipped => false;

        public SupabaseSetupStep(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            // ① 프로젝트 만들기
            EditorTabBase.DrawSubLabel("① Supabase 프로젝트 만들기");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "아직 계정이 없다면 가입 후 새 프로젝트를 만드세요.\n" +
                "· 리전: Northeast Asia (ap-northeast-2) 추천\n" +
                "· DB 비밀번호: 기억해두세요 (아래에서 입력합니다)");
            GUILayout.Space(4);
            if (EditorTabBase.DrawLinkBtn("Supabase 가입/로그인", GameServerDashboard.COL_SUPABASE))
                Application.OpenURL("https://supabase.com/dashboard");
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ② Project URL
            EditorTabBase.DrawSubLabel("② Project URL 복사");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "Supabase 대시보드 홈 (프로젝트 선택 후 첫 화면)에\n" +
                "Project URL이 표시되어 있습니다.\n" +
                "https://xxxxxxxx.supabase.co 형태입니다.");

            if (EditorTabBase.DrawLinkBtn("Supabase 대시보드", GameServerDashboard.COL_SUPABASE))
                Application.OpenURL("https://supabase.com/dashboard");

            GUILayout.Space(4);
            EditorGUILayout.PropertyField(so.FindProperty("supabaseUrl"), new GUIContent("Project URL"));
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ③ Anon Key
            EditorTabBase.DrawSubLabel("③ Anon Key 복사");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "같은 페이지 (Settings > API)에서 \"anon public\" 키를 복사하세요.\n" +
                "\"Project API keys\" 섹션에 2개의 키가 있습니다:\n" +
                "  · anon (public) ← 이것을 복사\n" +
                "  · service_role (secret) ← 이것은 아닙니다");
            EditorTabBase.DrawDescription(
                "anon key는 로그인 등 공개 작업에 쓰이는 키입니다.\neyJhbG... 으로 시작하는 긴 문자열입니다.",
                EditorTabBase.COL_MUTED);

            GUILayout.Space(4);
            var anonKey = EditorGUILayout.TextField("Anon Key", GameServerSettings.SupabaseAnonKey);
            if (anonKey != GameServerSettings.SupabaseAnonKey)
                GameServerSettings.SupabaseAnonKey = anonKey;

            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ④ DB Password
            EditorTabBase.DrawSubLabel("④ DB Password");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription("프로젝트 생성 시 설정한 비밀번호입니다.\n잊었다면 Settings > Database에서 리셋할 수 있습니다.");

            if (!string.IsNullOrEmpty(settings.supabaseUrl) && !string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorTabBase.DrawLinkBtn("Database 설정 페이지"))
                    Application.OpenURL(settings.SupabaseDatabaseSettingsUrl);
            }

            GUILayout.Space(4);
            var dbPw = EditorGUILayout.PasswordField("DB Password", GameServerSettings.SupabaseDbPassword);
            if (dbPw != GameServerSettings.SupabaseDbPassword)
                GameServerSettings.SupabaseDbPassword = dbPw;

            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ⑤ 보안 설정
            EditorTabBase.DrawSubLabel("⑤ 보안 설정");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "게임 데이터 보호를 위해 아래 설정을 확인하세요.\n" +
                "Settings > API 페이지에서:\n" +
                "  ✓ Enable Data API — 켜기 (기본값)\n" +
                "  ✓ Enable Auto RLS — 켜기 ⚠ 중요");
            GUILayout.Space(4);
            EditorTabBase.DrawDescription(
                "Auto RLS를 켜면 REST API로는 아무도 DB에 직접 접근할 수 없고, " +
                "우리 서버만 접근할 수 있습니다. 반드시 켜세요.",
                EditorTabBase.COL_WARN);
            GUILayout.Space(4);

            if (!string.IsNullOrEmpty(settings.supabaseUrl) && !string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorTabBase.DrawLinkBtn("API 보안 설정 페이지", GameServerDashboard.COL_SUPABASE))
                    Application.OpenURL(settings.SupabaseApiSettingsUrl);
            }
            else
            {
                if (EditorTabBase.DrawLinkBtn("API 보안 설정 페이지", GameServerDashboard.COL_SUPABASE))
                    Application.OpenURL("https://supabase.com/dashboard/project/_/settings/api");
            }
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ⑥ 연결 테스트
            EditorTabBase.DrawSubLabel("⑥ 연결 테스트");
            DrawConnectionTest(settings);

            so.ApplyModifiedProperties();
        }

        void DrawConnectionTest(GameServerSettings settings)
        {
            EditorTabBase.BeginBody();

            switch (_testState)
            {
                case TestState.None:
                    bool canTest = !string.IsNullOrEmpty(settings.supabaseUrl) &&
                                   !string.IsNullOrEmpty(GameServerSettings.SupabaseAnonKey);
                    using (new EditorGUI.DisabledGroupScope(!canTest))
                    {
                        if (EditorTabBase.DrawColorBtn("연결 테스트", GameServerDashboard.COL_SUPABASE, 28))
                            RunConnectionTest(settings);
                    }
                    if (!canTest)
                        EditorTabBase.DrawDescription("URL과 Anon Key를 입력하면 테스트할 수 있습니다.", EditorTabBase.COL_WARN);
                    break;

                case TestState.Testing:
                    EditorTabBase.DrawLoading(true, "연결 테스트 중...");
                    break;

                case TestState.Success:
                    EditorTabBase.DrawDescription("✓ Supabase 연결 성공!", EditorTabBase.COL_SUCCESS);
                    GUILayout.Space(4);
                    if (EditorTabBase.DrawColorBtn("다시 테스트", GameServerDashboard.COL_SUPABASE))
                        RunConnectionTest(settings);
                    break;

                case TestState.Failed:
                    EditorTabBase.DrawDescription($"✗ {_testError}", EditorTabBase.COL_ERROR);
                    GUILayout.Space(4);
                    if (EditorTabBase.DrawColorBtn("다시 테스트", EditorTabBase.COL_ERROR))
                        RunConnectionTest(settings);
                    break;
            }

            EditorTabBase.EndBody();
        }

        UnityWebRequest _activeRequest;

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

            if (string.IsNullOrEmpty(key))
            {
                _testState = TestState.Failed;
                _testError = "Anon Key를 입력해주세요.";
                _dashboard.Repaint();
                return;
            }

            _testState = TestState.Testing;
            _dashboard.Repaint();

            // Auth 엔드포인트로 테스트 (REST API 설정과 무관하게 항상 동작)
            _activeRequest = new UnityWebRequest($"{url}/auth/v1/settings", "GET");
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("apikey", key);
            _activeRequest.SetRequestHeader("Authorization", $"Bearer {key}");
            _activeRequest.timeout = 10;
            _activeRequest.SendWebRequest();

            EditorApplication.update += PollConnectionTest;
        }

        /// <summary>윈도우 닫힐 때 정리용. Dashboard.OnDisable에서 호출.</summary>
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
                _testError = $"인증 실패 (HTTP {code})\n" +
                             "Anon Key가 올바른지 확인해주세요.\n" +
                             "Settings > API > 'anon public' 키를 복사했는지 확인하세요.";
            }
            else if (code == 0)
            {
                _testState = TestState.Failed;
                _testError = $"서버에 연결할 수 없습니다.\n" +
                             $"URL: {GameServerSettings.Instance.supabaseUrl}\n" +
                             $"에러: {error}";
            }
            else
            {
                _testState = TestState.Failed;
                _testError = $"HTTP {code}\n" +
                             $"URL: {GameServerSettings.Instance.supabaseUrl}\n" +
                             $"응답: {body}\n에러: {error}";
            }

            _dashboard.Repaint();
        }

        public void OnSkip() { }
    }
}
