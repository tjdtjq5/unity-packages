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

        // ── 기본 설정 (settings.json) ──
        static readonly string[] ModelOptions = { "기본값 (미지정)", "sonnet", "opus", "haiku" };
        static readonly string[] ModelValues  = { "",               "sonnet", "opus", "haiku" };
        static readonly string[] EffortOptions = { "low", "medium", "high", "max" };

        // ── 드롭다운 인자 목록 ──
        static readonly string[] ArgOptions =
        {
            "인자 추가...",
            // 모드
            "--permission-mode default",
            "--permission-mode plan",
            "--permission-mode acceptEdits",
            // 기타
            "--verbose",
            "--continue",
            "--debug",
            "--worktree",
            "--ide",
        };

        // ── Discord ──

        // ── 상태 ──
        int _modelIdx;
        int _effortIdx;
        bool _bypassPermissions;
        List<string> _selectedArgs = new();
        Color _mainColor;
        Color _wtColor;
        string _windowName;
        bool _autoLaunch;
        int _dropdownIdx;

        // Channel 설정 상태
        bool _discordEnabled;
        string _discordBotToken = "";
        string _discordChannelId = "";
        bool _remoteControlEnabled;
        bool _rcSetupMode;
        bool _rcCheckingPrereq;
        RemoteControlHelper.PrereqResult? _rcPrereqResult;
        bool _showToken;
        Vector2 _scrollPos;

        // 위자드 상태 (EditorPrefs로 도메인 리로드 시 유지)
        int _wizardStep
        {
            get => EditorPrefs.GetInt("ClaudeCode_WizardStep", 0);
            set => EditorPrefs.SetInt("ClaudeCode_WizardStep", value);
        }
        bool _isValidatingToken;
        bool _isLoadingChannels;
        bool _isTesting;
        string _testResult;
        string _wizardError;
        List<DiscordSetupHelper.ChannelInfo> _channels = new();
        int _selectedChannelIdx = -1;

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
            // 기본 설정 로드
            var model = ClaudeCodeSettings.DefaultModel;
            _modelIdx = Math.Max(0, Array.IndexOf(ModelValues, model));
            var effort = ClaudeCodeSettings.DefaultEffortLevel;
            _effortIdx = Math.Max(0, Array.IndexOf(EffortOptions, effort));
            _bypassPermissions = ClaudeCodeSettings.BypassPermissions;

            ParseArgs(ClaudeCodeSettings.AdditionalArgs);
            _mainColor = ClaudeCodeSettings.MainTabColor;
            _wtColor = ClaudeCodeSettings.WorktreeTabColor;
            _windowName = ClaudeCodeSettings.WindowName;
            _autoLaunch = ClaudeCodeSettings.AutoLaunch;

            _discordEnabled = ClaudeCodeSettings.DiscordEnabled;
            _discordBotToken = ClaudeCodeSettings.DiscordBotToken;
            _discordChannelId = ClaudeCodeSettings.DiscordChannelId;
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

            // ── 기본 설정 ──
            EditorTabBase.DrawSectionHeader("기본 설정", COL_PRIMARY);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _modelIdx = EditorGUILayout.Popup("모델", _modelIdx, ModelOptions);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.DefaultModel = ModelValues[_modelIdx];
            EditorGUILayout.LabelField("~/.claude/settings.json 에 저장", _hintStyle);

            GUILayout.Space(2);

            EditorGUI.BeginChangeCheck();
            _effortIdx = EditorGUILayout.Popup("Effort", _effortIdx, EffortOptions);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.DefaultEffortLevel = EffortOptions[_effortIdx];
            EditorGUILayout.LabelField("CLI 인자(--effort)로 전달", _hintStyle);

            GUILayout.Space(6);

            EditorGUI.BeginChangeCheck();
            _bypassPermissions = EditorGUILayout.Toggle("권한 프롬프트 우회", _bypassPermissions);
            if (EditorGUI.EndChangeCheck())
                ClaudeCodeSettings.BypassPermissions = _bypassPermissions;
            EditorGUILayout.LabelField(
                "<project>/.claude/settings.local.json 의 defaultMode = \"bypassPermissions\"\n" +
                "체크 시 모든 도구 권한 프롬프트를 우회합니다. 변경 후 Claude 재실행 필요.",
                _hintStyle);

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

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

            // ── Discord 설정 ──
            var stepLabel = _wizardStep > 0 ? $"Discord 설정 [{_wizardStep}/3]" : "Discord 설정";
            EditorTabBase.DrawSectionHeader(stepLabel, EditorTabBase.COL_INFO);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            if (_wizardStep == 0)
                DrawDiscordNormal();
            else if (_wizardStep == 1)
                DrawDiscordStep1();
            else if (_wizardStep == 2)
                DrawDiscordStep2();
            else if (_wizardStep == 3)
                DrawDiscordStep3();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ── Remote Control ──
            EditorTabBase.DrawSectionHeader("Remote Control", COL_PRIMARY);
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));
            GUILayout.Space(4);

            if (_rcSetupMode)
                DrawRemoteControlSetup();
            else
                DrawRemoteControlNormal();

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

        // ── Discord 위자드 ──

        void DrawDiscordNormal()
        {
            if (!ClaudeCodeSettings.DiscordSetupDone || string.IsNullOrEmpty(_discordBotToken))
            {
                // 설정 전 — 모드 드롭다운 숨기고 설정 시작 버튼만 표시
                EditorGUILayout.LabelField("Discord 봇을 설정하면 알림/작업 지시를 받을 수 있습니다.", _hintStyle);
                GUILayout.Space(4);
                if (GUILayout.Button("Discord 설정 시작", GUILayout.Height(24)))
                    _wizardStep = 1;
                return;
            }

            // 설정 완료 — 상태 + 체크박스 + 버튼
            var chName = ClaudeCodeSettings.DiscordChannelName;
            var svName = ClaudeCodeSettings.DiscordServerName;
            EditorGUILayout.LabelField($"\u2705 Channel: #{chName}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(svName))
                EditorGUILayout.LabelField($"    Server: {svName}", _hintStyle);

            GUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _discordEnabled = EditorGUILayout.Toggle("Discord에서 대화", _discordEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                ClaudeCodeSettings.DiscordEnabled = _discordEnabled;

                if (_discordEnabled)
                {
                    // ON: Pipe 연결 + config 전송
                    if (ChannelBridge.CurrentState == ChannelBridge.State.Stopped)
                        ChannelBridge.Connect();
                    ChannelBridge.OnStateChanged -= OnDiscordToggleSendConfig;
                    ChannelBridge.OnStateChanged += OnDiscordToggleSendConfig;
                }
                else
                {
                    // OFF: config 전송 시도 후 Pipe 해제
                    ChannelBridge.SendConfig();
                    // Pipe도 끊어서 리소스 정리
                    EditorApplication.delayCall += () => ChannelBridge.Disconnect();
                }
            }
            EditorGUILayout.LabelField(
                "체크하면 Discord 채널에서 Claude에게 작업을 지시할 수 있습니다.\n" +
                "변경 후 Claude를 재실행해야 완전히 적용됩니다.",
                _hintStyle);

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("설정 변경", EditorStyles.miniButton))
                _wizardStep = 1;
            if (GUILayout.Button("테스트", EditorStyles.miniButton))
            {
                _isTesting = true;
                _testResult = null;
                DiscordSetupHelper.SendTestMessage(_discordBotToken, _discordChannelId,
                    () => { _isTesting = false; _testResult = "OK"; Repaint(); },
                    err => { _isTesting = false; _testResult = err; Repaint(); });
            }
            if (GUILayout.Button("연결 해제", EditorStyles.miniButton))
            {
                ClaudeCodeSettings.DiscordBotToken = "";
                ClaudeCodeSettings.DiscordChannelId = "";
                ClaudeCodeSettings.DiscordSetupDone = false;
                ClaudeCodeSettings.DiscordEnabled = false;
                _discordBotToken = "";
                _discordChannelId = "";
                _discordEnabled = false;
                ChannelBridge.SendConfig();
            }
            EditorGUILayout.EndHorizontal();

            DrawTestResult();
        }

        void DrawDiscordStep1()
        {
            EditorGUILayout.LabelField("Step 1: Discord 봇 생성", EditorStyles.boldLabel);
            GUILayout.Space(4);

            if (EditorTabBase.DrawColorBtn("\U0001f517 개발자 포털 열기", EditorTabBase.COL_INFO, 24))
                DiscordSetupHelper.OpenDeveloperPortal();

            GUILayout.Space(4);
            EditorGUILayout.LabelField(
                "1. \"New Application\" \u2192 이름 입력\n" +
                "2. 좌측 \"Bot\" 탭 클릭\n" +
                "3. \"Reset Token\" \u2192 토큰 복사\n" +
                "4. 같은 페이지 아래 Privileged Gateway Intents:\n" +
                "   MESSAGE CONTENT INTENT \u2192 활성화 (필수!)",
                _hintStyle);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Bot Token", _hintStyle);
            EditorGUILayout.BeginHorizontal();
            if (_showToken)
                _discordBotToken = EditorGUILayout.TextField(_discordBotToken);
            else
                _discordBotToken = EditorGUILayout.PasswordField(_discordBotToken);
            if (GUILayout.Button(_showToken ? "숨김" : "표시", EditorStyles.miniButton, GUILayout.Width(40)))
                _showToken = !_showToken;
            EditorGUILayout.EndHorizontal();

            DrawWizardError();

            GUILayout.Space(6);
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_discordBotToken) || _isValidatingToken);
            if (GUILayout.Button(_isValidatingToken ? "토큰 확인 중..." : "다음 \u2192", GUILayout.Height(24)))
            {
                _isValidatingToken = true;
                _wizardError = null;
                ClaudeCodeSettings.DiscordBotToken = _discordBotToken;

                DiscordSetupHelper.ValidateToken(_discordBotToken,
                    botName =>
                    {
                        _isValidatingToken = false;
                        _wizardError = null;
                        Debug.Log($"[Discord] 토큰 검증 성공: {botName}");
                        _wizardStep = 2;
                        _channels.Clear();
                        _selectedChannelIdx = -1;
                        Repaint();
                    },
                    err =>
                    {
                        _isValidatingToken = false;
                        if (err.Contains("intents") || err.Contains("Intents"))
                            _wizardError = $"토큰은 유효하지만 Intent 활성화가 필요합니다.\n" +
                                           "개발자 포털 > Bot > Privileged Gateway Intents >\n" +
                                           "MESSAGE CONTENT INTENT \u2192 활성화 후 다시 시도";
                        else if (err.Contains("TOKEN") || err.Contains("401") || err.Contains("token"))
                            _wizardError = "유효하지 않은 토큰입니다. 토큰을 다시 확인해주세요.";
                        else
                            _wizardError = $"연결 실패: {err}";
                        Repaint();
                    });
            }
            EditorGUI.EndDisabledGroup();

            // Intent 에러 시 개발자 포털 링크
            if (_wizardError != null && _wizardError.Contains("Intent"))
            {
                if (GUILayout.Button("Discord 개발자 포털 열기 (Bot 설정)", EditorStyles.miniButton))
                {
                    var clientId = DiscordSetupHelper.ExtractClientId(_discordBotToken);
                    if (!string.IsNullOrEmpty(clientId))
                        Application.OpenURL($"https://discord.com/developers/applications/{clientId}/bot");
                    else
                        DiscordSetupHelper.OpenDeveloperPortal();
                }
            }
        }

        void DrawDiscordStep2()
        {
            EditorGUILayout.LabelField("Step 2: 봇 초대 + 채널 선택", EditorStyles.boldLabel);
            GUILayout.Space(4);

            if (EditorTabBase.DrawColorBtn("\U0001f517 서버에 봇 초대하기", EditorTabBase.COL_INFO, 24))
                DiscordSetupHelper.OpenInviteUrl(_discordBotToken);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("봇을 서버에 추가한 후 채널 목록을 불러오세요:", _hintStyle);

            EditorGUI.BeginDisabledGroup(_isLoadingChannels);
            if (GUILayout.Button(_isLoadingChannels ? "불러오는 중..." : "채널 목록 불러오기"))
            {
                _isLoadingChannels = true;
                _channels.Clear();
                _wizardError = null;
                DiscordSetupHelper.FetchChannels(_discordBotToken,
                    list =>
                    {
                        _channels = list;
                        _isLoadingChannels = false;
                        _wizardError = list.Count == 0 ? "채널을 찾을 수 없습니다. 봇을 서버에 초대했는지 확인하세요." : null;
                        Repaint();
                    },
                    err =>
                    {
                        _isLoadingChannels = false;
                        _wizardError = $"채널 조회 실패: {err}";
                        Repaint();
                    });
            }
            EditorGUI.EndDisabledGroup();

            DrawWizardError();

            // 채널 목록 표시
            if (_channels.Count > 0)
            {
                GUILayout.Space(4);
                for (int i = 0; i < _channels.Count; i++)
                {
                    var ch = _channels[i];
                    bool selected = i == _selectedChannelIdx;
                    if (GUILayout.Toggle(selected, $"# {ch.Name}  ({ch.ServerName})", EditorStyles.radioButton))
                    {
                        _selectedChannelIdx = i;
                        _discordChannelId = ch.Id;
                    }
                }
            }

            GUILayout.Space(4);
            EditorGUILayout.LabelField("또는 직접 입력:", _hintStyle);
            _discordChannelId = EditorGUILayout.TextField("Channel ID", _discordChannelId);

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("\u2190 이전"))
                _wizardStep = 1;
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_discordChannelId));
            if (GUILayout.Button("다음 \u2192"))
            {
                // 채널 ID를 즉시 저장
                ClaudeCodeSettings.DiscordChannelId = _discordChannelId;
                _wizardStep = 3;
                _testResult = null;

                // 선택된 채널 이름 저장
                if (_selectedChannelIdx >= 0 && _selectedChannelIdx < _channels.Count)
                {
                    ClaudeCodeSettings.DiscordChannelName = _channels[_selectedChannelIdx].Name;
                    ClaudeCodeSettings.DiscordServerName = _channels[_selectedChannelIdx].ServerName;
                }
                else
                {
                    // [W7] 직접 입력 시 ID를 이름으로 표시
                    ClaudeCodeSettings.DiscordChannelName = _discordChannelId;
                    ClaudeCodeSettings.DiscordServerName = "";
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        void DrawDiscordStep3()
        {
            EditorGUILayout.LabelField("Step 3: 확인 + 테스트", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUILayout.LabelField("\u2705 Bot Token: 설정됨", EditorStyles.miniLabel);
            var chName = ClaudeCodeSettings.DiscordChannelName;
            var svName = ClaudeCodeSettings.DiscordServerName;
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(chName)
                    ? $"\u2705 Channel: {_discordChannelId}"
                    : $"\u2705 Channel: #{chName} ({svName})",
                EditorStyles.miniLabel);

            GUILayout.Space(6);
            EditorGUI.BeginDisabledGroup(_isTesting);
            if (EditorTabBase.DrawColorBtn(
                _isTesting ? "전송 중..." : "\U0001f514 테스트 메시지 보내기",
                EditorTabBase.COL_INFO, 24))
            {
                _isTesting = true;
                _testResult = null;
                DiscordSetupHelper.SendTestMessage(_discordBotToken, _discordChannelId,
                    () => { _isTesting = false; _testResult = "OK"; Repaint(); },
                    err => { _isTesting = false; _testResult = err; Repaint(); });
            }
            EditorGUI.EndDisabledGroup();

            DrawTestResult();

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("\u2190 이전"))
                _wizardStep = 2;
            if (GUILayout.Button("\u2705 완료", GUILayout.Height(24)))
            {
                ClaudeCodeSettings.DiscordBotToken = _discordBotToken;
                ClaudeCodeSettings.DiscordChannelId = _discordChannelId;
                ClaudeCodeSettings.DiscordSetupDone = true;
                _discordEnabled = true;
                ClaudeCodeSettings.DiscordEnabled = true;
                ChannelBridge.SendConfig();
                _wizardStep = 0;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Remote Control ──

        void DrawRemoteControlNormal()
        {
            EditorGUI.BeginChangeCheck();
            _remoteControlEnabled = EditorGUILayout.Toggle("기본 활성화", _remoteControlEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                if (_remoteControlEnabled && !ClaudeCodeSettings.RemoteControlSetupDone)
                {
                    // 첫 활성화 → 설정 안내 진입
                    _rcSetupMode = true;
                    _rcCheckingPrereq = true;
                    _rcPrereqResult = null;
                    _remoteControlEnabled = false; // 확인 전까지 미적용
                    RemoteControlHelper.CheckPrerequisites(result =>
                    {
                        _rcPrereqResult = result;
                        _rcCheckingPrereq = false;
                        Repaint();
                    });
                    return;
                }

                ClaudeCodeSettings.RemoteControlEnabled = _remoteControlEnabled;

                if (!_remoteControlEnabled)
                {
                    EditorUtility.DisplayDialog("Remote Control",
                        "다음 Claude 실행부터 Remote Control이 비활성화됩니다.\n" +
                        "이미 실행 중인 세션은 /remote-control 로 해제하세요.",
                        "확인");
                }
            }

            EditorGUILayout.LabelField(
                "claude.ai/code나 모바일 앱에서 세션에 접속할 수 있습니다.",
                _hintStyle);

            if (_remoteControlEnabled)
            {
                GUILayout.Space(2);
                var sessionName = RemoteControlHelper.GetSessionName();
                EditorGUILayout.LabelField($"세션 이름: \"{sessionName}\" (자동)", _hintStyle);

                if (ClaudeCodeSettings.DiscordEnabled)
                {
                    GUILayout.Space(2);
                    EditorGUILayout.LabelField(
                        "\U0001f4ac Discord로 접속 정보가 자동 전송됩니다",
                        _hintStyle);
                }
            }
        }

        void DrawRemoteControlSetup()
        {
            EditorGUILayout.LabelField("Remote Control 설정", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUILayout.LabelField(
                "다른 기기(폰, 태블릿, PC 브라우저)에서\n" +
                "이 Unity 프로젝트의 Claude 세션에 접속할 수 있습니다.",
                _hintStyle);

            GUILayout.Space(6);

            // 사전 요건 확인
            if (_rcCheckingPrereq)
            {
                EditorGUILayout.LabelField("\u23f3 사전 요건 확인 중...", EditorStyles.miniLabel);
            }
            else if (_rcPrereqResult.HasValue)
            {
                var r = _rcPrereqResult.Value;

                // Claude CLI 설치 + 버전
                if (r.ClaudeInstalled)
                {
                    var vLabel = r.VersionOk
                        ? $"\u2705 Claude Code v{r.Version} (v2.1.51 이상)"
                        : $"\u274c Claude Code v{r.Version} \u2192 v2.1.51 이상 필요";
                    EditorGUILayout.LabelField(vLabel, EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "\u274c Claude Code CLI가 설치되지 않았습니다",
                        EditorStyles.miniLabel);
                }

                // 안내 항목
                EditorGUILayout.LabelField("\u2139 OAuth 로그인 필요 (API 키 불가)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("\u2139 Pro / Max / Team / Enterprise 구독 필요", EditorStyles.miniLabel);

                GUILayout.Space(6);

                // 사용 방법
                EditorGUILayout.LabelField("사용 방법:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "1. Claude 실행 시 자동으로 Remote Control이 켜집니다\n" +
                    "2. claude.ai/code 에서 세션 목록을 확인하세요\n" +
                    $"3. 세션 이름: \"{RemoteControlHelper.GetSessionName()}\" (자동)",
                    _hintStyle);

                GUILayout.Space(4);

                // Discord 상태
                if (ClaudeCodeSettings.DiscordSetupDone && ClaudeCodeSettings.DiscordEnabled)
                {
                    EditorGUILayout.LabelField(
                        "\U0001f4ac Discord 설정됨 \u2014 세션 시작 시 접속 정보가 Discord로 전송됩니다",
                        _hintStyle);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "\U0001f4a1 Discord를 설정하면 다른 기기에서 더 편하게 알림을 받을 수 있습니다",
                        _hintStyle);
                }

                GUILayout.Space(6);

                // 버튼
                bool canActivate = r.ClaudeInstalled && r.VersionOk;

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(!canActivate);
                if (GUILayout.Button("\u2705 활성화", GUILayout.Height(24)))
                {
                    ClaudeCodeSettings.RemoteControlEnabled = true;
                    ClaudeCodeSettings.RemoteControlSetupDone = true;
                    _remoteControlEnabled = true;
                    _rcSetupMode = false;
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("취소", GUILayout.Height(24)))
                {
                    _rcSetupMode = false;
                    _remoteControlEnabled = false;
                }
                EditorGUILayout.EndHorizontal();

                if (!canActivate)
                {
                    var errorStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                        { normal = { textColor = EditorTabBase.COL_ERROR } };
                    EditorGUILayout.LabelField(
                        "\u274c Claude Code CLI를 설치하거나 업데이트한 후 재검사하세요",
                        errorStyle);

                    GUILayout.Space(2);
                    if (GUILayout.Button("재검사", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        _rcCheckingPrereq = true;
                        _rcPrereqResult = null;
                        RemoteControlHelper.CheckPrerequisites(result =>
                        {
                            _rcPrereqResult = result;
                            _rcCheckingPrereq = false;
                            Repaint();
                        });
                    }
                }
            }
        }

        static void OnDiscordToggleSendConfig(ChannelBridge.State state)
        {
            if (state == ChannelBridge.State.Connected)
            {
                ChannelBridge.OnStateChanged -= OnDiscordToggleSendConfig;
                ChannelBridge.SendConfig();
            }
        }

        void DrawTestResult()
        {
            if (_testResult == null) return;
            GUILayout.Space(2);
            if (_testResult == "OK")
                EditorGUILayout.LabelField("\u2705 테스트 메시지 전송 성공!", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField($"\u274c 실패: {_testResult}", _hintStyle);
        }

        void DrawWizardError()
        {
            if (string.IsNullOrEmpty(_wizardError)) return;
            GUILayout.Space(2);
            var errorStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                { normal = { textColor = EditorTabBase.COL_ERROR } };
            EditorGUILayout.LabelField($"\u274c {_wizardError}", errorStyle);
        }
    }
}
