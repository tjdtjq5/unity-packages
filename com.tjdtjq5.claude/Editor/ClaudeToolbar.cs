using Tjdtjq5.EditorToolkit.Editor.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// 메인 툴바 우측에 Claude 버튼을 삽입한다.
    /// 클릭 시 ClaudeCodeLauncher.Open() 호출 → 터미널 실행.
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

        static ClaudeToolbar()
        {
            _injected = false;
            _pollCount = 0;

            EditorApplication.delayCall += TryInject;
            EditorApplication.update += PollInject;
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
                tooltip = "Claude Code 터미널 열기\n(첫 클릭: 메인, 이후: worktree 새 탭)",
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

            var label = new Label("Claude")
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
            label.pickingMode = PickingMode.Ignore;
            btn.Add(label);

            // 호버
            btn.RegisterCallback<PointerEnterEvent>(_ =>
                btn.style.backgroundColor = BtnHoverColor);
            btn.RegisterCallback<PointerLeaveEvent>(_ =>
                btn.style.backgroundColor = BtnColor);

            // 클릭 → 터미널 실행
            btn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                    ClaudeCodeLauncher.Open();
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
