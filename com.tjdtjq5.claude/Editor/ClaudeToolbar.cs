using Tjdtjq5.EditorToolkit.Editor.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// 메인 툴바 우측에 Claude 버튼을 삽입한다.
    /// 좌클릭: Manager 윈도우, 우클릭: 설정.
    /// 활성 워크트리 수를 벳지로 표시.
    /// </summary>
    [InitializeOnLoad]
    static class ClaudeToolbar
    {
        const string ContainerId = "ClaudeToolbarContainer";
        const int MaxPollFrames = 100;

        // Claude 보라색
        static readonly Color BtnColor = new(0.28f, 0.24f, 0.56f, 1f);
        static readonly Color BtnHoverColor = new(0.42f, 0.36f, 0.91f, 1f);
        static readonly Color TextColor = new(0.92f, 0.90f, 1f, 1f);

        static int _pollCount;
        static bool _injected;

        // 벳지
        static Label _badgeLabel;
        static Label _statusDot;
        static double _lastBadgeCheck;
        static int _cachedWtCount = -1;

        static ClaudeToolbar()
        {
            _injected = false;
            _pollCount = 0;

            EditorApplication.delayCall += TryInject;
            EditorApplication.update += PollInject;
            EditorApplication.update += BadgePoll;
            EditorApplication.delayCall += RefreshBadge;
        }

        static void PollInject()
        {
            if (_injected || _pollCount >= MaxPollFrames)
            {
                EditorApplication.update -= PollInject;
                return;
            }
            _pollCount++;
            TryInject();
        }

        static void BadgePoll()
        {
            if (EditorApplication.timeSinceStartup - _lastBadgeCheck < 10.0) return;
            _lastBadgeCheck = EditorApplication.timeSinceStartup;
            RefreshBadge();
        }

        internal static void RefreshBadge()
        {
            ClaudeCodeLauncher.GetActiveWorktreesAsync(list =>
            {
                int count = list?.Count ?? 0;
                if (count == _cachedWtCount) return;
                _cachedWtCount = count;

                if (_badgeLabel != null)
                    _badgeLabel.text = count > 0 ? $"Claude [{count}]" : "Claude";
            });

            UpdateStatusDot();
        }

        static void UpdateStatusDot()
        {
            if (_statusDot == null) return;

            if (!ClaudeCodeSettings.MonitorEnabled)
            {
                _statusDot.style.color = new StyleColor(Color.clear);
                return;
            }

            var dotColor = ChannelBridge.CurrentState switch
            {
                ChannelBridge.State.Connected => new Color(0.3f, 0.85f, 0.4f),   // 초록
                ChannelBridge.State.Connecting => new Color(0.9f, 0.8f, 0.2f),   // 노랑
                ChannelBridge.State.Error => new Color(0.9f, 0.3f, 0.3f),        // 빨강
                _ => new Color(0.5f, 0.5f, 0.5f),                                 // 회색
            };

            _statusDot.style.color = new StyleColor(dotColor);
        }

        static void TryInject()
        {
            if (_injected) return;

            var toolbarRoot = ToolbarHelper.GetToolbarRoot();
            if (toolbarRoot == null) return;

            var existing = toolbarRoot.Q(ContainerId);
            existing?.RemoveFromHierarchy();

            // after-spacer (우측 영역) 맨 앞에 삽입
            var afterSpacer = ToolbarHelper.FindAfterSpacerContainer(toolbarRoot);
            if (afterSpacer == null)
            {
                // 폴백: PlayMode 오른쪽
                var playZone = ToolbarHelper.FindPlayZone(toolbarRoot);
                if (playZone == null) return;
                var parent = playZone.parent;
                var idx = parent.IndexOf(playZone);
                parent.Insert(idx + 1, BuildButton());
            }
            else
            {
                afterSpacer.Insert(0, BuildButton());
            }

            _injected = true;
            EditorApplication.update -= PollInject;
        }

        static VisualElement BuildButton()
        {
            var container = new VisualElement
            {
                name = ContainerId,
                tooltip = "Claude Code Manager\n(우클릭: 설정)",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginLeft = 4,
                    marginRight = 4,
                }
            };

            var btn = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    backgroundColor = BtnColor,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 3,
                    paddingBottom = 3,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                }
            };

            // ✦ 아이콘 (Claude 스파클)
            var icon = new Label("\u2726")
            {
                style =
                {
                    color = TextColor,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginRight = 4,
                }
            };
            icon.pickingMode = PickingMode.Ignore;
            btn.Add(icon);

            _badgeLabel = new Label("Claude")
            {
                style =
                {
                    color = TextColor,
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 0,
                    marginBottom = 0,
                    paddingTop = 0,
                    paddingBottom = 0,
                }
            };
            _badgeLabel.pickingMode = PickingMode.Ignore;
            btn.Add(_badgeLabel);

            // ● 상태 인디케이터
            _statusDot = new Label("\u25CF")
            {
                style =
                {
                    color = Color.clear, // 초기: 숨김
                    fontSize = 8,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginLeft = 4,
                }
            };
            _statusDot.pickingMode = PickingMode.Ignore;
            btn.Add(_statusDot);

            // 호버
            btn.RegisterCallback<PointerEnterEvent>(_ =>
                btn.style.backgroundColor = BtnHoverColor);
            btn.RegisterCallback<PointerLeaveEvent>(_ =>
                btn.style.backgroundColor = BtnColor);

            // 좌클릭 → Manager 윈도우
            btn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                    ClaudeCodeManagerWindow.Open();
            });

            // 우클릭 → 설정
            btn.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                ClaudeCodeSettingsWindow.Open();
            });

            container.Add(btn);
            return container;
        }
    }
}
