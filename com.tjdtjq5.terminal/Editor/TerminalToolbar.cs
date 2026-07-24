using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tjdtjq5.Terminal
{
    /// <summary>
    /// 메인 툴바 우측에 터미널 버튼을 삽입한다.
    /// 좌클릭: 선택된 터미널로 프로젝트 루트 열기, 우클릭: 터미널 전환/목록 편집 메뉴.
    /// 버튼 라벨은 현재 선택된 터미널 이름.
    /// </summary>
    [InitializeOnLoad]
    static class TerminalToolbar
    {
        const string ContainerId = "TerminalToolbarContainer";
        const int MaxPollFrames = 100;

        static readonly Color BtnColor = new(0.16f, 0.19f, 0.23f, 1f);
        static readonly Color BtnHoverColor = new(0.25f, 0.30f, 0.36f, 1f);
        static readonly Color TextColor = new(0.85f, 0.93f, 0.90f, 1f);

        static int _pollCount;
        static bool _injected;
        static Label _label;

        static TerminalToolbar()
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

        internal static void RefreshLabel()
        {
            if (_label == null) return;
            _label.text = TerminalProfiles.GetSelected()?.name ?? "Terminal";
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
                tooltip = "좌클릭: 터미널 열기\n우클릭: 터미널 선택/목록 편집",
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

            // >_ 아이콘
            var icon = new Label(">_")
            {
                style =
                {
                    color = TextColor,
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginRight = 5,
                }
            };
            icon.pickingMode = PickingMode.Ignore;
            btn.Add(icon);

            _label = new Label("Terminal")
            {
                style =
                {
                    color = TextColor,
                    fontSize = 11,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            _label.pickingMode = PickingMode.Ignore;
            btn.Add(_label);

            RefreshLabel();

            // 호버
            btn.RegisterCallback<PointerEnterEvent>(_ =>
                btn.style.backgroundColor = BtnHoverColor);
            btn.RegisterCallback<PointerLeaveEvent>(_ =>
                btn.style.backgroundColor = BtnColor);

            // 좌클릭 → 터미널 실행
            btn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                    TerminalLauncher.Open();
            });

            // 우클릭 → 터미널 선택 메뉴
            btn.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                ShowMenu();
            });

            container.Add(btn);
            return container;
        }

        static void ShowMenu()
        {
            var menu = new GenericMenu();
            var list = TerminalProfiles.Load();
            var selectedName = TerminalProfiles.GetSelected()?.name;

            foreach (var p in list)
            {
                var captured = p.name;
                menu.AddItem(new GUIContent(captured), captured == selectedName, () =>
                {
                    TerminalProfiles.SelectedName = captured;
                    RefreshLabel();
                });
            }

            if (list.Count > 0) menu.AddSeparator("");
            menu.AddItem(new GUIContent("목록 편집..."), false, TerminalListWindow.Open);
            menu.ShowAsContext();
        }
    }
}
