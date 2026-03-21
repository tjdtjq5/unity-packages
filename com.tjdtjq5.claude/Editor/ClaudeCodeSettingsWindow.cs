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

        // ── 드롭다운 라벨 ──
        static readonly string[] SeverityLabels = { "Error만", "Warning+Error", "All" };
        static readonly string[] DiscordModeLabels = { "없음", "알림", "적극적 사용" };

        // ── 상태 ──
        List<string> _selectedArgs = new();
        Color _mainColor;
        Color _wtColor;
        string _windowName;
        bool _autoLaunch;
        int _dropdownIdx;

        // Channel 설정 상태
        bool _monitorEnabled;
        int _monitorSeverity;
        int _cooldownSeconds;
        int _discordMode;
        string _discordBotToken = "";
        string _discordChannelId = "";
        string _discordAllowedUsers = "";
        bool _remoteControlEnabled;
        bool _showToken;
        Vector2 _scrollPos;

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

            _monitorEnabled = ClaudeCodeSettings.MonitorEnabled;
            _monitorSeverity = ClaudeCodeSettings.MonitorSeverity;
            _cooldownSeconds = ClaudeCodeSettings.CooldownSeconds;
            _discordMode = ClaudeCodeSettings.DiscordMode;
            _discordBotToken = ClaudeCodeSettings.DiscordBotToken;
            _discordChannelId = ClaudeCodeSettings.DiscordChannelId;
            _discordAllowedUsers = ClaudeCodeSettings.DiscordAllowedUsers;
            _remoteControlEnabled = ClaudeCodeSettings.RemoteControlEnabled;
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
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

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

            GUILayout.Space(4);

            // ── 모니터 설정 ──
            EditorTabBase.DrawSectionHeader("모니터 설정", EditorTabBase.COL_SUCCESS);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _monitorEnabled = EditorGUILayout.Toggle("모니터 활성화", _monitorEnabled);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.MonitorEnabled = _monitorEnabled;

            EditorGUI.BeginDisabledGroup(!_monitorEnabled);

            EditorGUI.BeginChangeCheck();
            _monitorSeverity = EditorGUILayout.Popup("전달 심각도", _monitorSeverity, SeverityLabels);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.MonitorSeverity = _monitorSeverity;

            EditorGUI.BeginChangeCheck();
            _cooldownSeconds = EditorGUILayout.IntSlider("쿨다운 (초)", _cooldownSeconds, 5, 120);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.CooldownSeconds = _cooldownSeconds;

            EditorGUILayout.LabelField("에러 수정 중 같은 파일의 재전달을 방지합니다.", _hintStyle);

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ── Discord 설정 ──
            EditorTabBase.DrawSectionHeader("Discord 설정", EditorTabBase.COL_INFO);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _discordMode = EditorGUILayout.Popup("모드", _discordMode, DiscordModeLabels);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.DiscordMode = _discordMode;

            bool discordActive = _discordMode > 0;
            EditorGUI.BeginDisabledGroup(!discordActive);

            GUILayout.Space(4);
            EditorGUILayout.LabelField("Bot Token", _hintStyle);
            EditorGUILayout.BeginHorizontal();
            if (_showToken)
                _discordBotToken = EditorGUILayout.TextField(_discordBotToken);
            else
                _discordBotToken = EditorGUILayout.PasswordField(_discordBotToken);
            if (GUILayout.Button(_showToken ? "숨김" : "표시", EditorStyles.miniButton, GUILayout.Width(40)))
                _showToken = !_showToken;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            EditorGUILayout.LabelField("Channel ID", _hintStyle);
            _discordChannelId = EditorGUILayout.TextField(_discordChannelId);

            GUILayout.Space(2);
            EditorGUILayout.LabelField("허용 사용자 (쉼표 구분 Discord ID)", _hintStyle);
            _discordAllowedUsers = EditorGUILayout.TextField(_discordAllowedUsers);

            GUILayout.Space(6);
            if (GUILayout.Button("설정 적용", GUILayout.Height(24)))
            {
                ClaudeCodeSettings.DiscordBotToken = _discordBotToken;
                ClaudeCodeSettings.DiscordChannelId = _discordChannelId;
                ClaudeCodeSettings.DiscordAllowedUsers = _discordAllowedUsers;
                ChannelBridge.SendConfig();
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ── Remote Control ──
            EditorTabBase.DrawSectionHeader("Remote Control", COL_PRIMARY);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _remoteControlEnabled = EditorGUILayout.Toggle("기본 활성화", _remoteControlEnabled);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.RemoteControlEnabled = _remoteControlEnabled;

            EditorGUILayout.LabelField(
                "claude.ai/code나 모바일 앱에서 세션에 접속할 수 있습니다.",
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

            EditorGUILayout.EndScrollView();
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
