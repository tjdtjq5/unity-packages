using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.GameServer.Editor
{
    public class SupabaseSetup
    {
        readonly GameServerDashboard _dashboard;
        enum TestState { None, Testing, Success, Failed }
        TestState _testState;
        string _testError;
        UnityWebRequest _activeRequest;

        public bool IsCompleted => _testState == TestState.Success;

        public SupabaseSetup(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            // ①
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

            // ②
            EditorTabBase.DrawSubLabel("② Project URL 복사");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "Supabase 대시보드 홈에 Project URL이 표시되어 있습니다.\nhttps://xxxxxxxx.supabase.co 형태입니다.");
            if (EditorTabBase.DrawLinkBtn("Supabase 대시보드", GameServerDashboard.COL_SUPABASE))
                Application.OpenURL("https://supabase.com/dashboard");
            GUILayout.Space(4);
            EditorGUILayout.PropertyField(so.FindProperty("supabaseUrl"), new GUIContent("Project URL"));
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ③
            EditorTabBase.DrawSubLabel("③ Anon Key 복사");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "Settings > API > \"anon public\" 키를 복사하세요.\n" +
                "\"Project API keys\" 섹션에 2개의 키가 있습니다:\n" +
                "  · anon (public) ← 이것을 복사\n" +
                "  · service_role (secret) ← 이것은 아닙니다");
            EditorTabBase.DrawDescription(
                "eyJhbG... 으로 시작하는 긴 문자열입니다.", EditorTabBase.COL_MUTED);

            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorTabBase.DrawLinkBtn("API 설정 페이지", GameServerDashboard.COL_SUPABASE))
                    Application.OpenURL(settings.SupabaseApiSettingsUrl);
            }

            GUILayout.Space(4);
            var anonKey = EditorGUILayout.TextField("Anon Key", GameServerSettings.SupabaseAnonKey);
            if (anonKey != GameServerSettings.SupabaseAnonKey)
                GameServerSettings.SupabaseAnonKey = anonKey;
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ④
            EditorTabBase.DrawSubLabel("④ DB Password");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription("프로젝트 생성 시 설정한 비밀번호입니다.\n잊었다면 Settings > Database에서 리셋할 수 있습니다.");

            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorTabBase.DrawLinkBtn("Database 설정 페이지", GameServerDashboard.COL_SUPABASE))
                    Application.OpenURL(settings.SupabaseDatabaseSettingsUrl);
            }

            GUILayout.Space(4);
            var dbPw = EditorGUILayout.PasswordField("DB Password", GameServerSettings.SupabaseDbPassword);
            if (dbPw != GameServerSettings.SupabaseDbPassword)
                GameServerSettings.SupabaseDbPassword = dbPw;
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ⑤
            EditorTabBase.DrawSubLabel("⑤ 보안 설정");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription(
                "Settings > API에서 확인하세요:\n" +
                "  ✓ Enable Data API — 켜기 (기본값)\n" +
                "  ✓ Enable Auto RLS — 켜기 ⚠ 중요");
            EditorTabBase.DrawDescription(
                "Auto RLS를 켜면 REST API로는 아무도 DB에 접근할 수 없고, " +
                "우리 서버만 접근할 수 있습니다.", EditorTabBase.COL_WARN);

            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorTabBase.DrawLinkBtn("API 보안 설정 페이지", GameServerDashboard.COL_SUPABASE))
                    Application.OpenURL(settings.SupabaseApiSettingsUrl);
            }
            EditorTabBase.EndBody();

            GUILayout.Space(6);

            // ⑥
            EditorTabBase.DrawSubLabel("⑥ 연결 테스트");
            EditorTabBase.BeginBody();
            DrawConnectionTest(settings);
            EditorTabBase.EndBody();

            so.ApplyModifiedProperties();
        }

        void DrawConnectionTest(GameServerSettings settings)
        {
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
                _testError = $"인증 실패 (HTTP {code})\nAnon Key가 올바른지 확인해주세요.";
            }
            else if (code == 0)
            {
                _testState = TestState.Failed;
                _testError = $"서버에 연결할 수 없습니다.\nURL: {GameServerSettings.Instance.supabaseUrl}\n에러: {error}";
            }
            else
            {
                _testState = TestState.Failed;
                _testError = $"HTTP {code}\n응답: {body}\n에러: {error}";
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
