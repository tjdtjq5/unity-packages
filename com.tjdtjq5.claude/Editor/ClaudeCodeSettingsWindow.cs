using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Claude Code 런처 설정 창
    /// </summary>
    public class ClaudeCodeSettingsWindow : EditorWindow
    {
        string _args;
        Color _mainColor;
        Color _wtColor;
        string _windowName;

        public static void Open()
        {
            var wnd = GetWindow<ClaudeCodeSettingsWindow>("Claude Code Settings");
            wnd.minSize = new Vector2(360, 260);
        }

        void OnEnable()
        {
            _args = ClaudeCodeSettings.AdditionalArgs;
            _mainColor = ClaudeCodeSettings.MainTabColor;
            _wtColor = ClaudeCodeSettings.WorktreeTabColor;
            _windowName = ClaudeCodeSettings.WindowName;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Claude Code Launcher Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 추가 인자
            EditorGUILayout.LabelField("추가 인자", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _args = EditorGUILayout.TextField(_args);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.AdditionalArgs = _args;

            EditorGUILayout.Space(8);

            // 탭 색상
            EditorGUILayout.LabelField("탭 색상 (Windows Terminal)", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            _mainColor = EditorGUILayout.ColorField("메인 탭", _mainColor);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.MainTabColor = _mainColor;

            EditorGUI.BeginChangeCheck();
            _wtColor = EditorGUILayout.ColorField("워크트리 탭", _wtColor);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.WorktreeTabColor = _wtColor;

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"메인: {ClaudeCodeSettings.ColorToHex(_mainColor)}  |  " +
                $"워크트리: {ClaudeCodeSettings.ColorToHex(_wtColor)}",
                MessageType.None);

            EditorGUILayout.Space(8);

            // 윈도우 이름
            EditorGUILayout.LabelField("Windows Terminal 윈도우 이름", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _windowName = EditorGUILayout.TextField(_windowName);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.WindowName = _windowName;

            EditorGUILayout.Space(12);

            // 사용법
            EditorGUILayout.HelpBox(
                "Tools > Claude Code > Open\n" +
                "- 첫 실행: 메인 Claude (메인 탭 색상)\n" +
                "- 이후: git worktree + 새 탭 (워크트리 탭 색상)\n\n" +
                "Windows Terminal 필요 (탭/색상 기능)\n" +
                "미설치 시 일반 PowerShell 새 창으로 fallback",
                MessageType.Info);

            EditorGUILayout.Space(4);

            // 리셋
            if (GUILayout.Button("런처 상태 리셋 (메인 미실행으로)"))
            {
                EditorPrefs.SetBool(ClaudeCodeLauncher.MainLaunchedKey, false);
                Debug.Log("[Claude Code] 런처 상태 리셋 완료");
            }
        }
    }
}
