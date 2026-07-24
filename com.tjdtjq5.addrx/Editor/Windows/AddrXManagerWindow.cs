#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using Tjdtjq5.AddrX.Editor.Update;
namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>AddrX 통합 매니저 윈도우. Setup + Tracker + Analysis 탭. 톱니바퀴 → Settings 패널.</summary>
    public class AddrXManagerWindow : EditorWindow
    {
        AddrXTabBase[] _tabs;
        int _activeTab;
        bool _showSettings;
        SettingsPanel _settingsPanel;

        [MenuItem("Tjdtjq/AddrX/Manager %#a")]
        static void Open()
        {
            var w = GetWindow<AddrXManagerWindow>("AddrX");
            w.minSize = new Vector2(520, 400);
        }

        void OnEnable()
        {
            _tabs = new AddrXTabBase[]
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
            var badges = new (string, int)[]
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ("Tracking", AddrXSettings.Instance.EnableTracking ? 1 : 0),
                ("Leak", AddrXSettings.Instance.EnableLeakDetection ? 1 : 0),
#endif
            };

            // ── 헤더: 타이틀 + 상태 뱃지 + 톱니 버튼 ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("AddrX 0.1", EditorStyles.largeLabel);
            GUILayout.Label(
                string.Join("   ", badges.Select(b => $"{b.Item1}: {(b.Item2 == 1 ? "On" : "Off")}")),
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorGUIUtility.IconContent("_Popup"), EditorStyles.miniButton,
                    GUILayout.Width(28)))
                _showSettings = !_showSettings;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            if (_showSettings)
            {
                _settingsPanel ??= new SettingsPanel(() => _showSettings = false);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _settingsPanel.OnDraw();
                EditorGUILayout.EndVertical();
            }
            else
            {
                _activeTab = GUILayout.Toolbar(_activeTab,
                    _tabs.Select(t => t.TabName).ToArray());

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _tabs[_activeTab].OnDraw();
                EditorGUILayout.EndVertical();
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
