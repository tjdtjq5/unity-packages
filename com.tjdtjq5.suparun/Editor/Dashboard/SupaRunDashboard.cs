using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunDashboard : EditorWindow
    {
        // ── 색상 ──
        public static readonly Color COL_PRIMARY  = new(0.25f, 0.65f, 0.85f);
        public static readonly Color COL_SUPABASE = new(0.24f, 0.80f, 0.56f);
        public static readonly Color COL_GCP      = new(0.26f, 0.52f, 0.96f);
        public static readonly Color COL_GITHUB   = new(0.95f, 0.95f, 0.95f);
        public static readonly Color COL_DOCKER   = new(0.13f, 0.59f, 0.95f);

        // ── 모드 ──
        enum Mode { Setup, Dashboard, Settings }
        Mode _mode;

        // ── 대시보드 탭 ──
        static readonly string[] DashboardTabs = { "Status", "Deploy", "Monitor" };
        static readonly Color[] DashboardTabColors =
        {
            EditorUI.COL_SUCCESS,
            EditorUI.COL_WARN,
            EditorUI.COL_INFO
        };
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
        EditorUI.NotificationType _notificationType;

        [MenuItem("Tools/SupaRun/Dashboard %#q")]
        public static void Open()
        {
            var wnd = GetWindow<SupaRunDashboard>("SupaRun");
            wnd.minSize = new Vector2(520, 480);
        }

        [MenuItem("Tools/SupaRun/Data %#d")]
        public static void OpenData()
        {
            var settings = SupaRunSettings.Instance;
            if (settings.IsSupabaseConfigured)
                Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/editor");
            else
                EditorUtility.DisplayDialog("Data", "Supabase 설정이 필요합니다.\nDashboard > Settings에서 연결하세요.", "확인");
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
            EditorUI.DrawWindowBackground(position);

            switch (_mode)
            {
                case Mode.Setup:
                    EditorUI.DrawWindowHeader("SupaRun", $"v{SupaRunSettings.VERSION}", COL_PRIMARY);
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

        void DrawDashboardMode()
        {
            // 헤더 + 뱃지 + ⚙
            var badges = GetStatusBadges();
            if (EditorUI.DrawWindowHeaderWithGear("SupaRun", $"v{SupaRunSettings.VERSION}", COL_PRIMARY, badges))
                _mode = Mode.Settings;

            // Access Token 만료 경고 (상단 고정)
            DrawTokenWarning();

            // 탭 바
            _activeTab = EditorUI.DrawTabBar(DashboardTabs, _activeTab, DashboardTabColors, COL_PRIMARY);

            // 알림
            EditorUI.DrawNotificationBar(ref _notification, _notificationType);

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
            EditorUI.DrawWindowHeader("SupaRun", $"v{SupaRunSettings.VERSION}", COL_PRIMARY);
            if (EditorUI.DrawBackButton("← 대시보드로 돌아가기"))
                _mode = Mode.Dashboard;

            DrawTokenWarning();
            EditorUI.DrawNotificationBar(ref _notification, _notificationType);
            _settingsView.OnDraw();
        }

        void DrawTokenWarning()
        {
            if (!AuthUrlSyncManager.IsTokenExpired) return;

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.3f, 0.3f, 0.3f);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = prev;

            EditorUI.DrawCellLabel("  ⚠ Access Token이 만료되었습니다. Settings > Supabase에서 재발급하세요.", 0, EditorUI.COL_ERROR);

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

        public void ShowNotification(string message, EditorUI.NotificationType type)
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
