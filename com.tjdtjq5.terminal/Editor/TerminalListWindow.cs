using System.Collections.Generic;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Terminal
{
    /// <summary>
    /// 터미널 프로필 목록 편집 창. 변경은 즉시 저장.
    /// 자동 감지는 설치된 알려진 터미널을 추가만 한다 (사용자 데이터 우선).
    /// </summary>
    public class TerminalListWindow : EditorWindow
    {
        List<TerminalProfile> _profiles;

        // 설치 여부 캐시 — 프로세스 실행 비용이 있어 행 단위 lazy 계산.
        // 키는 객체 참조: 편집 중 무효화하지 않고 [자동 감지]/[설치 확인]에서만 초기화.
        readonly Dictionary<TerminalProfile, bool?> _installedCache = new();

        Vector2 _scroll;

        [MenuItem("Tjdtjq/Terminal/목록 편집")]
        public static void Open()
        {
            var wnd = GetWindow<TerminalListWindow>("Terminal 목록");
            wnd.minSize = new Vector2(560, 220);
        }

        void OnEnable()
        {
            _profiles = TerminalProfiles.Load();
            _installedCache.Clear();
        }

        void OnGUI()
        {
            EditorTabBase.DrawSectionHeader("터미널 목록", EditorTabBase.COL_INFO);
            GUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int removeIndex = -1;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (DrawRow(_profiles[i])) removeIndex = i;
            }

            if (removeIndex >= 0)
            {
                var removed = _profiles[removeIndex];
                _profiles.RemoveAt(removeIndex);
                _installedCache.Remove(removed);
                if (TerminalProfiles.SelectedName == removed.name)
                    TerminalProfiles.SelectedName = _profiles.Count > 0 ? _profiles[0].name : "";
                SaveAndRefresh();
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 추가", GUILayout.Height(24)))
            {
                _profiles.Add(new TerminalProfile { name = "새 터미널", command = "" });
                SaveAndRefresh();
            }
            if (GUILayout.Button("자동 감지", GUILayout.Height(24)))
            {
                int added = TerminalProfiles.AddMissingInstalled(_profiles);
                _installedCache.Clear();
                SaveAndRefresh();
                ShowNotification(new GUIContent(added > 0 ? $"{added}개 추가됨" : "추가할 터미널 없음"));
            }
            if (GUILayout.Button("설치 확인 ↻", GUILayout.Height(24), GUILayout.Width(90)))
            {
                _installedCache.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "{dir} = 프로젝트 경로, {dirUri} = URL 인코딩 경로 · scheme:// 으로 시작하면 URI로 실행",
                EditorStyles.miniLabel);
        }

        /// <summary>행 하나를 그린다. 삭제 버튼이 눌리면 true.</summary>
        bool DrawRow(TerminalProfile p)
        {
            bool remove = false;
            EditorGUILayout.BeginHorizontal();

            // 선택 라디오
            bool isSelected = TerminalProfiles.SelectedName == p.name;
            bool nowSelected = GUILayout.Toggle(isSelected, "", EditorStyles.radioButton, GUILayout.Width(18));
            if (nowSelected && !isSelected)
            {
                TerminalProfiles.SelectedName = p.name;
                TerminalToolbar.RefreshLabel();
            }

            // 설치 여부 (✓/✗/-)
            var installed = GetInstalledCached(p);
            var mark = installed == true ? "✓" : installed == false ? "✗" : "-";
            var markColor = installed == true ? EditorTabBase.COL_SUCCESS
                : installed == false ? EditorTabBase.COL_ERROR : EditorTabBase.COL_MUTED;
            var prevColor = GUI.color;
            GUI.color = markColor;
            GUILayout.Label(mark, GUILayout.Width(16));
            GUI.color = prevColor;

            // 이름 / 명령
            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.TextField(p.name, GUILayout.Width(130));
            var newCommand = EditorGUILayout.TextField(p.command);
            if (EditorGUI.EndChangeCheck())
            {
                if (TerminalProfiles.SelectedName == p.name)
                    TerminalProfiles.SelectedName = newName;
                p.name = newName;
                p.command = newCommand;
                SaveAndRefresh();
            }

            // 삭제
            if (GUILayout.Button("−", GUILayout.Width(22)))
                remove = true;

            EditorGUILayout.EndHorizontal();
            return remove;
        }

        bool? GetInstalledCached(TerminalProfile p)
        {
            if (_installedCache.TryGetValue(p, out var cached)) return cached;
            var result = TerminalProfiles.CheckInstalled(p);
            _installedCache[p] = result;
            return result;
        }

        void SaveAndRefresh()
        {
            TerminalProfiles.Save(_profiles);
            TerminalToolbar.RefreshLabel();
        }
    }
}
