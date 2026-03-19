using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class GameServerDashboard : EditorWindow
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
        static readonly string[] DashboardTabs = { "Status", "Deploy", "Cost" };
        static readonly Color[] DashboardTabColors =
        {
            EditorTabBase.COL_SUCCESS,
            EditorTabBase.COL_WARN,
            EditorTabBase.COL_MUTED
        };
        int _activeTab;
        Vector2 _scrollPos;

        // ── 인스턴스 ──
        SetupWizard _setupWizard;
        SettingsView _settingsView;

        // ── 알림 ──
        string _notification;
        EditorTabBase.NotificationType _notificationType;

        [MenuItem("Tools/GameServer/Dashboard %#g")]
        public static void Open()
        {
            var wnd = GetWindow<GameServerDashboard>("GameServer");
            wnd.minSize = new Vector2(520, 480);
        }

        void OnEnable()
        {
            _setupWizard = new SetupWizard(this);
            _settingsView = new SettingsView(this);
            _mode = GameServerSettings.Instance.setupCompleted ? Mode.Dashboard : Mode.Setup;
        }

        void OnDisable()
        {
            _setupWizard?.Cleanup();
        }

        void OnGUI()
        {
            EditorTabBase.DrawWindowBackground(position);

            switch (_mode)
            {
                case Mode.Setup:
                    EditorTabBase.DrawWindowHeader("GameServer", "v0.1.0", COL_PRIMARY);
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
            if (EditorTabBase.DrawWindowHeaderWithGear("GameServer", "v0.1.0", COL_PRIMARY, badges))
                _mode = Mode.Settings;

            // 탭 바
            _activeTab = EditorTabBase.DrawTabBar(DashboardTabs, _activeTab, DashboardTabColors, COL_PRIMARY);

            // 알림
            EditorTabBase.DrawNotificationBar(ref _notification, _notificationType);

            // 탭 내용
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_activeTab)
            {
                case 0: EditorTabBase.DrawPlaceholder("Status — 구현 예정"); break;
                case 1: EditorTabBase.DrawPlaceholder("Deploy — 구현 예정"); break;
                case 2: EditorTabBase.DrawPlaceholder("Cost — 구현 예정"); break;
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawSettingsMode()
        {
            EditorTabBase.DrawWindowHeader("GameServer", "v0.1.0", COL_PRIMARY);
            if (EditorTabBase.DrawBackButton("← 대시보드로 돌아가기"))
                _mode = Mode.Dashboard;

            EditorTabBase.DrawNotificationBar(ref _notification, _notificationType);
            _settingsView.OnDraw();
        }

        (string name, int state)[] GetStatusBadges()
        {
            var settings = GameServerSettings.Instance;

            int supabaseState = 0; // 회색
            if (settings.IsSupabaseConfigured) supabaseState = 1; // 초록

            int cloudRunState = 0;
            if (settings.IsGcpConfigured && !string.IsNullOrEmpty(settings.cloudRunUrl))
                cloudRunState = 1;

            return new[] { ("Supabase", supabaseState), ("Cloud Run", cloudRunState) };
        }

        // ── Public API ──

        public void ShowNotification(string message, EditorTabBase.NotificationType type)
        {
            _notification = message;
            _notificationType = type;
            Repaint();
        }

        public void OnSetupCompleted()
        {
            var settings = GameServerSettings.Instance;
            settings.setupCompleted = true;
            settings.Save();
            _mode = Mode.Dashboard;
            Repaint();
        }

        public void OpenSettings() => _mode = Mode.Settings;
        public void BackToDashboard() => _mode = Mode.Dashboard;

        public new void Repaint() => base.Repaint();
    }
}
