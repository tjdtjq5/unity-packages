using System;
using System.Collections.Generic;
using System.Linq;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Claude Code 런처 설정 창
    /// </summary>
    public class ClaudeCodeSettingsWindow : EditorWindow
    {
        static readonly Color COL_PRIMARY = new(0.42f, 0.36f, 0.91f);

        // ── 드롭다운 인자 목록 ──
        static readonly string[] ArgOptions =
        {
            "인자 추가...",
            // 모델
            "--model sonnet",
            "--model opus",
            "--model haiku",
            // 모드
            "--permission-mode default",
            "--permission-mode plan",
            "--permission-mode acceptEdits",
            // 노력도
            "--effort low",
            "--effort medium",
            "--effort high",
            // 기타
            "--verbose",
            "--continue",
            "--debug",
            "--worktree",
            "--ide",
        };

        // ── 상태 ──
        List<string> _selectedArgs = new();
        Color _mainColor;
        Color _wtColor;
        string _windowName;
        bool _autoLaunch;
        int _dropdownIdx;

        // ── GUIStyle 캐시 ──
        GUIStyle _titleStyle;
        GUIStyle _hintStyle;
        GUIStyle _tagStyle;
        GUIStyle _tagRemoveStyle;

        public static void Open()
        {
            var wnd = GetWindow<ClaudeCodeSettingsWindow>("Claude Code Settings");
            wnd.minSize = new Vector2(360, 320);
        }

        void OnEnable()
        {
            ParseArgs(ClaudeCodeSettings.AdditionalArgs);
            _mainColor = ClaudeCodeSettings.MainTabColor;
            _wtColor = ClaudeCodeSettings.WorktreeTabColor;
            _windowName = ClaudeCodeSettings.WindowName;
            _autoLaunch = ClaudeCodeSettings.AutoLaunch;
        }

        void EnsureStyles()
        {
            _titleStyle ??= new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 16, fixedHeight = 26f };
            _hintStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                { normal = { textColor = EditorTabBase.COL_MUTED } };
            _tagStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal =
                {
                    background = EditorTabBase.GetBgStyle(EditorTabBase.BG_CARD).normal.background,
                    textColor = Color.white
                },
                padding = new RectOffset(6, 4, 2, 2),
                margin = new RectOffset(2, 2, 2, 2),
            };
            _tagRemoveStyle ??= new GUIStyle(EditorStyles.miniButton)
            {
                normal = { textColor = EditorTabBase.COL_ERROR },
                fixedWidth = 16,
                fixedHeight = 16,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 4, 2, 2),
                fontSize = 9,
            };
        }

        void OnGUI()
        {
            EnsureStyles();

            // ── 배너 ──
            var rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorTabBase.BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), COL_PRIMARY);
            EditorGUI.LabelField(
                new Rect(rect.x + 8, rect.y, rect.width - 16, rect.height),
                "Claude Code Settings", _titleStyle);

            GUILayout.Space(6);

            // ── Claude 명령어 ──
            EditorTabBase.DrawSectionHeader("Claude 명령어", COL_PRIMARY);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            DrawArgSelector();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ── Windows Terminal ──
            EditorTabBase.DrawSectionHeader("Windows Terminal", EditorTabBase.COL_INFO);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            EditorGUILayout.LabelField("윈도우 이름", _hintStyle);
            EditorGUI.BeginChangeCheck();
            _windowName = EditorGUILayout.TextField(_windowName);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.WindowName = _windowName;

            GUILayout.Space(6);
            EditorGUILayout.LabelField("탭 색상", _hintStyle);

            EditorGUI.BeginChangeCheck();
            _mainColor = EditorGUILayout.ColorField("메인 탭", _mainColor);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.MainTabColor = _mainColor;

            EditorGUI.BeginChangeCheck();
            _wtColor = EditorGUILayout.ColorField("워크트리 탭", _wtColor);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.WorktreeTabColor = _wtColor;

            GUILayout.Space(2);
            EditorGUILayout.LabelField(
                $"메인: {ClaudeCodeSettings.ColorToHex(_mainColor)}  |  워크트리: {ClaudeCodeSettings.ColorToHex(_wtColor)}",
                _hintStyle);

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ── 워크트리 동작 ──
            EditorTabBase.DrawSectionHeader("워크트리 동작", EditorTabBase.COL_WARN);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _autoLaunch = EditorGUILayout.Toggle("생성 후 자동 실행", _autoLaunch);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.AutoLaunch = _autoLaunch;

            EditorGUILayout.LabelField(
                "활성화하면 워크트리 생성 직후 자동으로 Claude를 실행합니다.",
                _hintStyle);

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // ── 사용법 안내 ──
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_CARD));
            GUILayout.Space(4);
            EditorGUILayout.LabelField("사용법", new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = EditorTabBase.COL_MUTED }, fontSize = 11 });
            EditorGUILayout.LabelField(
                "툴바 \u2726 Claude 버튼 좌클릭 \u2192 Manager 윈도우\n" +
                "- 메인 실행: 프로젝트 루트에서 Claude 실행\n" +
                "- 워크트리: 독립 환경에서 병렬 작업\n" +
                "우클릭 \u2192 이 설정 창",
                new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                    { normal = { textColor = new Color(0.70f, 0.70f, 0.75f) } });
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        // ── 인자 선택 UI ──

        void DrawArgSelector()
        {
            // 드롭다운
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("추가 인자", _hintStyle, GUILayout.Width(60));

            // 이미 선택된 옵션은 드롭다운에서 비활성 표시
            EditorGUI.BeginChangeCheck();
            _dropdownIdx = EditorGUILayout.Popup(_dropdownIdx, BuildDropdownLabels());
            if (EditorGUI.EndChangeCheck() && _dropdownIdx > 0)
            {
                var selected = ArgOptions[_dropdownIdx];
                if (!_selectedArgs.Contains(selected))
                {
                    _selectedArgs.Add(selected);
                    SaveArgs();
                }
                _dropdownIdx = 0;
            }
            EditorGUILayout.EndHorizontal();

            // 선택된 인자 태그 목록
            if (_selectedArgs.Count > 0)
            {
                GUILayout.Space(4);
                DrawArgTags();
            }

            // 미리보기
            GUILayout.Space(2);
            var preview = $"> claude {BuildArgsString()}".Trim();
            EditorGUILayout.LabelField(preview, _hintStyle);
        }

        void DrawArgTags()
        {
            int removeIdx = -1;

            // flow layout: 한 줄에 태그들을 나열, 넘치면 다음 줄
            EditorGUILayout.BeginHorizontal();
            float lineWidth = 0f;
            float maxWidth = EditorGUIUtility.currentViewWidth - 30f;

            for (int i = 0; i < _selectedArgs.Count; i++)
            {
                var content = new GUIContent(_selectedArgs[i]);
                float tagW = _tagStyle.CalcSize(content).x + 22f; // tag + x button

                if (lineWidth + tagW > maxWidth && lineWidth > 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    lineWidth = 0f;
                }

                // 태그 배경
                EditorGUILayout.BeginHorizontal(EditorTabBase.GetBgStyle(EditorTabBase.BG_CARD));
                EditorGUILayout.LabelField(content, _tagStyle, GUILayout.Width(tagW - 22f));
                if (GUILayout.Button("\u2715", _tagRemoveStyle))
                    removeIdx = i;
                EditorGUILayout.EndHorizontal();

                lineWidth += tagW + 4f;
            }

            EditorGUILayout.EndHorizontal();

            if (removeIdx >= 0)
            {
                _selectedArgs.RemoveAt(removeIdx);
                SaveArgs();
            }
        }

        // ── 헬퍼 ──

        string[] BuildDropdownLabels()
        {
            var labels = new string[ArgOptions.Length];
            labels[0] = ArgOptions[0];
            for (int i = 1; i < ArgOptions.Length; i++)
            {
                labels[i] = _selectedArgs.Contains(ArgOptions[i])
                    ? $"\u2713 {ArgOptions[i]}"
                    : ArgOptions[i];
            }
            return labels;
        }

        void ParseArgs(string raw)
        {
            _selectedArgs.Clear();
            if (string.IsNullOrWhiteSpace(raw)) return;

            // 알려진 옵션과 매칭
            var remaining = raw.Trim();
            foreach (var opt in ArgOptions)
            {
                if (opt == ArgOptions[0]) continue;
                if (remaining.Contains(opt))
                {
                    _selectedArgs.Add(opt);
                    remaining = remaining.Replace(opt, "").Trim();
                }
            }

            // 매칭 안 된 잔여 인자도 개별 추가
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                foreach (var part in remaining.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    _selectedArgs.Add(part);
            }
        }

        string BuildArgsString()
        {
            return string.Join(" ", _selectedArgs);
        }

        void SaveArgs()
        {
            ClaudeCodeSettings.AdditionalArgs = BuildArgsString();
        }
    }
}
