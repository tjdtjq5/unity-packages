#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using Tjdtjq5.EditorToolkit.Editor;
using Tjdtjq5.AddrX.Editor.Update;
namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>AddrX 통합 매니저 윈도우. Setup + Tracker + Analysis 탭. 톱니바퀴 → Settings 패널.</summary>
    public class AddrXManagerWindow : EditorWindow
    {
        static readonly Color Accent = new(0.3f, 0.75f, 0.95f);

        EditorTabBase[] _tabs;
        int _activeTab;
        bool _showSettings;
        SettingsPanel _settingsPanel;

        [MenuItem("Window/AddrX/Manager %#a")]
        static void Open()
        {
            var w = GetWindow<AddrXManagerWindow>("AddrX");
            w.minSize = new Vector2(520, 400);
        }

        void OnEnable()
        {
            _tabs = new EditorTabBase[]
            {
                new SetupTab(Repaint),
                new TrackerTab(Repaint),
                new AnalysisTab(Repaint),
                new UpdateTab(Repaint),
            };
            foreach (var t in _tabs) t.OnEnable();
        }

        void OnDisable()
        {
            if (_tabs == null) return;
            foreach (var t in _tabs) t.OnDisable();
        }

        void OnGUI()
        {
            EditorUI.DrawWindowBackground(position);

            var badges = new (string, int)[]
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ("Tracking", AddrXSettings.Instance.EnableTracking ? 1 : 0),
                ("Leak", AddrXSettings.Instance.EnableLeakDetection ? 1 : 0),
#endif
            };

            if (EditorUI.DrawWindowHeaderWithGear("AddrX", "0.1", Accent, badges))
                _showSettings = !_showSettings;

            if (_showSettings)
            {
                _settingsPanel ??= new SettingsPanel(() => _showSettings = false);
                EditorUI.BeginBody();
                _settingsPanel.OnDraw();
                EditorUI.EndBody();
            }
            else
            {
                _activeTab = EditorUI.DrawTabBar(
                    _tabs.Select(t => t.TabName).ToArray(),
                    _activeTab,
                    _tabs.Select(t => t.TabColor).ToArray(),
                    EditorUI.COL_MUTED);

                EditorUI.BeginBody();
                _tabs[_activeTab].OnDraw();
                EditorUI.EndBody();
            }
        }

        void Update()
        {
            if (_tabs != null && !_showSettings && _activeTab < _tabs.Length)
                _tabs[_activeTab].OnUpdate();
        }
    }
}
#endif
