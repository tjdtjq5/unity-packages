#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    public class BuildAutomationWindow : EditorWindow
    {
        // ── 버전 (package.json에서 읽기) ──
        static string _version;
        public static string Version
        {
            get
            {
                if (_version != null) return _version;
                var guids = UnityEditor.AssetDatabase.FindAssets("package t:TextAsset",
                    new[] { "Packages/com.tjdtjq5.cicd" });
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("package.json")) continue;
                    var json = System.IO.File.ReadAllText(path);
                    var m = System.Text.RegularExpressions.Regex.Match(json, "\"version\":\\s*\"([^\"]+)\"");
                    if (m.Success) { _version = $"v{m.Groups[1].Value}"; return _version; }
                }
                _version = "v0.1.0";
                return _version;
            }
        }

        // ── 색상 ──
        public static readonly Color COL_PRIMARY = new(0.20f, 0.70f, 0.50f);
        public static readonly Color COL_ANDROID = new(0.24f, 0.80f, 0.56f);
        public static readonly Color COL_IOS     = new(0.40f, 0.70f, 0.95f);
        public static readonly Color COL_WINDOWS = new(0.00f, 0.47f, 0.84f);
        public static readonly Color COL_WEBGL   = new(0.90f, 0.55f, 0.20f);

        // ── 모드 ──
        enum Mode { Setup, Dashboard, Settings }
        Mode _mode;

        // ── 대시보드 탭 ──
        static readonly string[] DashboardTabs = { "CI/CD", "Version" };
        static readonly Color[] DashboardTabColors =
        {
            EditorUI.COL_INFO,
            EditorUI.COL_SUCCESS
        };
        int _activeTab;
        Vector2 _scrollPos;

        // ── 인스턴스 ──
        SetupWizard _setupWizard;
        CICDTab _cicdTab;
        VersionTab _versionTab;
        SettingsTab _settingsTab;

        // ── 알림 ──
        string _notification;
        EditorUI.NotificationType _notificationType;

        [MenuItem("Tools/Build Automation/Dashboard")]
        public static void Open()
        {
            var wnd = GetWindow<BuildAutomationWindow>("Build Automation");
            wnd.minSize = new Vector2(520, 480);
        }

        void OnEnable()
        {
            // 캐시 워밍업
            GitHelper.InvalidateCache();
            GitVersionResolver.InvalidateCache();
            WorkflowGenerator.InvalidateCache();
            CacheHealthChecker.Invalidate();
            GhChecker.WarmCacheAsync();

            _setupWizard = new SetupWizard(this);
            _cicdTab = new CICDTab(this);
            _versionTab = new VersionTab();
            _settingsTab = new SettingsTab(this);
            _mode = BuildAutomationSettings.Instance.setupCompleted
                ? Mode.Dashboard : Mode.Setup;
        }

        void OnGUI()
        {
            EditorUI.DrawWindowBackground(position);

            switch (_mode)
            {
                case Mode.Setup:
                    EditorUI.DrawWindowHeader("Build Automation", Version, COL_PRIMARY);
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
            var badges = GetStatusBadges();
            if (EditorUI.DrawWindowHeaderWithGear("Build Automation", Version, COL_PRIMARY, badges))
                _mode = Mode.Settings;

            _activeTab = EditorUI.DrawTabBar(DashboardTabs, _activeTab, DashboardTabColors, COL_PRIMARY);

            EditorUI.DrawNotificationBar(ref _notification, _notificationType);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            switch (_activeTab)
            {
                case 0: _cicdTab.OnDraw(); break;
                case 1: _versionTab.OnDraw(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawSettingsMode()
        {
            EditorUI.DrawWindowHeader("Build Automation", Version, COL_PRIMARY);
            if (EditorUI.DrawBackButton("← 대시보드로 돌아가기"))
                _mode = Mode.Dashboard;

            EditorUI.DrawNotificationBar(ref _notification, _notificationType);
            _settingsTab.OnDraw();
        }

        (string name, int state)[] GetStatusBadges()
        {
            var s = BuildAutomationSettings.Instance;

            int platformState = s.HasAnyPlatform ? 1 : 0;

            // yml 존재 여부 (캐싱됨)
            bool ymlExists = WorkflowGenerator.WorkflowExists();
            int cicdState = ymlExists ? 1 : (s.setupCompleted ? 2 : 0);

            return new[] { ("Platform", platformState), ("CI/CD", cicdState) };
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
            var s = BuildAutomationSettings.Instance;
            s.setupCompleted = true;
            s.Save();
            _mode = Mode.Dashboard;
            Repaint();
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
#endif
