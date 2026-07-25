using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunDashboard : EditorWindow
    {
        // ── 색상 ──
        public static readonly Color COL_DOCKER   = new(0.13f, 0.59f, 0.95f);

        // ── 모드 ──
        enum Mode { Setup, Dashboard, Settings }
        Mode _mode;

        // ── 대시보드 탭 ──
        static readonly string[] DashboardTabs = { "Status", "Deploy", "Monitor" };
        int _activeTab;
        Vector2 _scrollPos;

        // ── 인스턴스 ──
        SetupWizard _setupWizard;
        SettingsView _settingsView;
        StatusTab _statusTab;
        DeployTab _deployTab;
        MonitorTab _monitorTab;

        // ── 알림 ──
        string _notification;
        SupaRunUI.NotificationType _notificationType;

        [MenuItem("Tjdtjq/SupaRun/Dashboard %#q")]
        public static void Open()
        {
            var wnd = GetWindow<SupaRunDashboard>("SupaRun");
            wnd.minSize = new Vector2(520, 480);
        }

        [MenuItem("Tjdtjq/SupaRun/Admin %#d")]
        public static void OpenAdmin() => OpenAdminAsync().Forget();

        static async UniTaskVoid OpenAdminAsync()
        {
            var settings = SupaRunSettings.Instance;
            if (string.IsNullOrEmpty(settings.cloudRunUrl))
            {
                if (settings.IsSupabaseConfigured)
                    EditorUtility.DisplayDialog("Admin", "서버가 아직 배포되지 않았습니다.\nDeploy 후 다시 시도하세요.", "확인");
                else
                    EditorUtility.DisplayDialog("Admin", "Supabase 설정이 필요합니다.\nDashboard > Settings에서 연결하세요.", "확인");
                return;
            }

            // 어드민은 아이콘/컴포넌트 맵을 DB 에서 읽는다 (ADR-0004). 여기가 그것들을 굽기에
            // 정확한 시점이다 — 실제로 필요해지는 순간이고, 컴파일마다 굽는 낭비가 없다.
            // 페이지를 열기 전에 끝내야 첫 화면부터 아이콘이 보인다.
            await SchemaAutoSync.SyncAdminAssets();

            Application.OpenURL(settings.cloudRunUrl.TrimEnd('/') + "/admin");
        }

        void OnEnable()
        {
            _setupWizard = new SetupWizard(this);
            _settingsView = new SettingsView(this);
            _statusTab = new StatusTab(this);
            _deployTab = new DeployTab(this);
            _monitorTab = new MonitorTab(this);
            _mode = SupaRunSettings.Instance.setupCompleted ? Mode.Dashboard : Mode.Setup;

            // CLI 캐시 워밍업 (백그라운드 — 설정 진입 시 지연 방지)
            // InvalidateCache로 옛 false 박힘 제거 → WarmCacheAsync가 새로 검사. UI 멈춤 X (Task.Run).
            PrerequisiteChecker.InvalidateCache();
            PrerequisiteChecker.WarmCacheAsync();

            // Auth URL 변경 감지 + 자동 동기화
            AuthUrlSyncManager.CheckAndSync(SupaRunSettings.Instance);
        }

        void OnDisable()
        {
            _setupWizard?.Cleanup();
        }

        void OnGUI()
        {
            switch (_mode)
            {
                case Mode.Setup:
                    DrawWindowHeader();
                    _setupWizard.OnDraw();
                    break;

                case Mode.Dashboard:
                    DrawDashboardMode();
                    break;

                case Mode.Settings:
                    DrawSettingsMode();
                    break;
            }
        }

        static void DrawWindowHeader()
        {
            EditorGUILayout.LabelField($"SupaRun v{SupaRunSettings.VERSION}", EditorStyles.largeLabel);
            EditorGUILayout.Space();
        }

        /// <summary>헤더 + 뱃지 + ⚙ 버튼. 반환: ⚙ 클릭 여부.</summary>
        static bool DrawWindowHeaderWithGear((string name, int state)[] badges)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"SupaRun v{SupaRunSettings.VERSION}", EditorStyles.largeLabel);

            if (badges != null)
            {
                foreach (var (name, state) in badges)
                {
                    var icon = state == 1 ? "✓" : state == 2 ? "⚠" : "○";
                    GUILayout.Label($"{icon} {name}", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                }
            }

            GUILayout.FlexibleSpace();
            bool gearClicked = GUILayout.Button(EditorGUIUtility.IconContent("_Popup"),
                EditorStyles.miniButton, GUILayout.Width(28));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            return gearClicked;
        }

        void DrawDashboardMode()
        {
            // 헤더 + 뱃지 + ⚙
            var badges = GetStatusBadges();
            if (DrawWindowHeaderWithGear(badges))
                _mode = Mode.Settings;

            // Access Token 만료 경고 (상단 고정)
            DrawTokenWarning();

            // 탭 바
            _activeTab = GUILayout.Toolbar(_activeTab, DashboardTabs);

            // 알림
            SupaRunUI.DrawNotificationBar(ref _notification, _notificationType);

            // 탭 내용
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_activeTab)
            {
                case 0: _statusTab.OnDraw(); break;
                case 1: _deployTab.OnDraw(); break;
                case 2: _monitorTab.OnDraw(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawSettingsMode()
        {
            DrawWindowHeader();
            if (GUILayout.Button("← 대시보드로 돌아가기", EditorStyles.miniButton))
                _mode = Mode.Dashboard;

            DrawTokenWarning();
            SupaRunUI.DrawNotificationBar(ref _notification, _notificationType);
            _settingsView.OnDraw();
        }

        void DrawTokenWarning()
        {
            if (!AuthUrlSyncManager.IsTokenExpired) return;

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.3f, 0.3f, 0.3f);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = prev;

            EditorGUILayout.LabelField("  ⚠ Access Token이 만료되었습니다. Settings > Supabase에서 재발급하세요.");

            if (GUILayout.Button("Settings", GUILayout.Width(70)))
                _mode = Mode.Settings;

            EditorGUILayout.EndHorizontal();
        }

        (string name, int state)[] GetStatusBadges()
        {
            var settings = SupaRunSettings.Instance;

            int supabaseState = 0; // 회색
            if (settings.IsSupabaseConfigured) supabaseState = 1; // 초록

            int cloudRunState = 0; // 회색: 미설정
            if (settings.IsGcpConfigured && settings.gcpCloudRunApiEnabled
                && !string.IsNullOrEmpty(settings.gcpServiceAccountEmail))
            {
                cloudRunState = !string.IsNullOrEmpty(settings.cloudRunUrl) ? 1 : 2;
                // 1=초록: 배포됨, 2=노랑: 설정 완료+미배포
            }
            else if (settings.IsGcpConfigured)
            {
                cloudRunState = 2; // 노랑: 설정 중
            }

            return new[] { ("Supabase", supabaseState), ("Cloud Run", cloudRunState) };
        }

        // ── Public API ──

        public void ShowNotification(string message, SupaRunUI.NotificationType type)
        {
            _notification = message;
            _notificationType = type;
            Repaint();
        }

        public void OnSetupCompleted()
        {
            var settings = SupaRunSettings.Instance;
            settings.setupCompleted = true;
            settings.Save();
            _mode = Mode.Dashboard;
            Repaint();
        }

        public void OpenSettings()
        {
            _mode = Mode.Settings;
            GUIUtility.ExitGUI();
        }

        public void BackToDashboard()
        {
            _mode = Mode.Dashboard;
            GUIUtility.ExitGUI();
        }

        public void OpenSetup()
        {
            _setupWizard = new SetupWizard(this);
            _mode = Mode.Setup;
            GUIUtility.ExitGUI();
        }

        public new void Repaint() => base.Repaint();
    }
}
