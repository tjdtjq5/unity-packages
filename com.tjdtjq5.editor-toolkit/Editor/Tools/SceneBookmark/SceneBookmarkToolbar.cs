using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Tjdtjq5.EditorToolkit.Editor.Tools
{
    /// <summary>
    /// 메인 툴바 플레이 버튼 왼쪽에 씬 북마크 버튼을 삽입한다.
    /// </summary>
    [InitializeOnLoad]
    static class SceneBookmarkToolbar
    {
        // --- 상수 ---
        const string ContainerId = "SceneBookmarkContainer";
        const int MaxPollFrames = 100;
        const float DragThreshold = 5f;

        // 색상
        static readonly Color ActiveColor = new(0.2f, 0.4f, 0.7f, 0.6f);
        static readonly Color InactiveColor = new(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color HoverColor = new(0.30f, 0.30f, 0.30f, 1f);
        static readonly Color AddBtnColor = new(0.18f, 0.18f, 0.18f, 1f);
        static readonly Color AddBtnHoverColor = new(0.28f, 0.28f, 0.28f, 1f);
        static readonly Color TextColor = new(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color ActiveTextColor = new(0.95f, 0.95f, 0.95f, 1f);
        static readonly Color DragColor = new(0.35f, 0.55f, 0.85f, 0.5f);

        // --- 상태 ---
        static SceneBookmarkData _data;
        static int _pollCount;
        static bool _injected;
        static bool _polling;

        // 드래그 상태
        static int _dragIndex = -1;
        static bool _isDragging;
        static float _dragStartX;
        static VisualElement _draggedElement;
        static int _dragPointerId = -1;

        // --- 초기화 ---
        static SceneBookmarkToolbar()
        {
            _data = SceneBookmarkData.Load();
            _injected = false;
            _pollCount = 0;
            _polling = true;

            EditorApplication.delayCall += TryInject;
            EditorApplication.update += PollInject;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // --- 주입 ---

        static void PollInject()
        {
            if (_injected || _pollCount >= MaxPollFrames)
            {
                EditorApplication.update -= PollInject;
                _polling = false;
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

            // 기존 컨테이너 제거 (도메인 리로드 대비)
            var existing = toolbarRoot.Q(ContainerId);
            existing?.RemoveFromHierarchy();

            var playZone = ToolbarHelper.FindPlayZone(toolbarRoot);
            if (playZone == null) return;

            var container = BuildContainer();
            var parent = playZone.parent;
            var playIndex = parent.IndexOf(playZone);
            parent.Insert(playIndex, container);

            _injected = true;
            EditorApplication.update -= PollInject;
            _polling = false;
        }

        // --- UI 빌드 ---

        static VisualElement BuildContainer()
        {
            var container = new VisualElement
            {
                name = ContainerId,
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginRight = 4,
                    marginLeft = 4,
                }
            };

            RebuildButtons(container);
            return container;
        }

        static void RebuildButtons(VisualElement container)
        {
            container.Clear();

            // 북마크 버튼들
            for (int i = 0; i < _data.Entries.Count; i++)
            {
                var entry = _data.Entries[i];
                var index = i;
                var btn = CreateBookmarkButton(entry, index);
                container.Add(btn);
            }

            // [+] 버튼
            var addBtn = CreateAddButton();
            container.Add(addBtn);
        }

        static VisualElement CreateBookmarkButton(SceneBookmarkEntry entry, int index)
        {
            var currentScene = SceneManager.GetActiveScene().path;
            var isActive = entry.scenePath == currentScene;

            var btn = new VisualElement
            {
                style =
                {
                    backgroundColor = isActive ? ActiveColor : InactiveColor,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 3,
                    paddingBottom = 3,
                    marginLeft = 1,
                    marginRight = 1,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                }
            };

            var label = new Label(entry.displayName)
            {
                style =
                {
                    color = isActive ? ActiveTextColor : TextColor,
                    fontSize = 11,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 0, marginBottom = 0,
                    paddingTop = 0, paddingBottom = 0,
                }
            };
            label.pickingMode = PickingMode.Ignore;
            btn.Add(label);

            // 호버 효과 (비활성 + 드래그 중이 아닐 때만)
            if (!isActive)
            {
                btn.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (!_isDragging) btn.style.backgroundColor = HoverColor;
                });
                btn.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (!_isDragging) btn.style.backgroundColor = InactiveColor;
                });
            }

            // --- 드래그 + 클릭 (포인터 캡처 기반) ---
            btn.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                _dragIndex = index;
                _isDragging = false;
                _dragStartX = evt.position.x;
                _draggedElement = btn;
                _dragPointerId = evt.pointerId;

                btn.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            btn.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_dragIndex != index || !btn.HasPointerCapture(_dragPointerId)) return;

                if (!_isDragging && Mathf.Abs(evt.position.x - _dragStartX) > DragThreshold)
                {
                    _isDragging = true;
                    btn.style.opacity = 0.6f;
                    btn.style.backgroundColor = DragColor;
                }
            });

            btn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0 || _dragIndex < 0) return;
                if (!btn.HasPointerCapture(_dragPointerId)) return;

                btn.ReleasePointer(_dragPointerId);

                if (_isDragging)
                    FinishDrag(evt.position.x, btn.parent);
                else
                    OpenScene(entry.scenePath);

                ResetDragState();
            });

            btn.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_dragIndex == index && _isDragging)
                {
                    ResetDragState();
                    RefreshToolbar();
                }
                else
                {
                    ResetDragState();
                }
            });

            btn.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape && _isDragging && _dragIndex == index)
                {
                    if (btn.HasPointerCapture(_dragPointerId))
                        btn.ReleasePointer(_dragPointerId);
                    evt.StopPropagation();
                }
            });

            // 우클릭 — 컨텍스트 메뉴
            btn.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                ShowContextMenu(entry);
            });

            return btn;
        }

        static VisualElement CreateAddButton()
        {
            var btn = new VisualElement
            {
                tooltip = "현재 씬을 북마크에 추가",
                style =
                {
                    backgroundColor = AddBtnColor,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 3,
                    paddingBottom = 3,
                    marginLeft = 2,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                }
            };

            var label = new Label("+")
            {
                style =
                {
                    color = TextColor,
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 0, marginBottom = 0,
                    paddingTop = 0, paddingBottom = 0,
                }
            };
            label.pickingMode = PickingMode.Ignore;
            btn.Add(label);

            btn.RegisterCallback<PointerEnterEvent>(_ =>
                btn.style.backgroundColor = AddBtnHoverColor);
            btn.RegisterCallback<PointerLeaveEvent>(_ =>
                btn.style.backgroundColor = AddBtnColor);

            btn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                    AddCurrentScene();
            });

            return btn;
        }

        // --- 드래그 헬퍼 ---

        static void ResetDragState()
        {
            if (_draggedElement != null)
                _draggedElement.style.opacity = 1f;
            _dragIndex = -1;
            _isDragging = false;
            _draggedElement = null;
            _dragPointerId = -1;
        }

        static void FinishDrag(float dropX, VisualElement container)
        {
            if (container == null || _dragIndex < 0) return;

            int targetIndex = _data.Entries.Count - 1;
            for (int i = 0; i < container.childCount - 1; i++) // -1: [+] 버튼 제외
            {
                var child = container[i];
                var center = child.worldBound.center.x;
                if (dropX < center)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex != _dragIndex)
            {
                _data.Reorder(_dragIndex, targetIndex);
                RefreshToolbar();
            }
        }

        // --- 액션 ---

        static void OpenScene(string scenePath)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SceneBookmark] Play 모드에서는 씬을 전환할 수 없습니다.");
                return;
            }

            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogWarning($"[SceneBookmark] 씬을 찾을 수 없습니다: {scenePath}");
                _data.Remove(scenePath);
                RefreshToolbar();
                return;
            }

            var currentScene = SceneManager.GetActiveScene();
            if (currentScene.path == scenePath) return;

            if (currentScene.isDirty)
                EditorSceneManager.SaveScene(currentScene);

            EditorSceneManager.OpenScene(scenePath);
        }

        static void AddCurrentScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("[SceneBookmark] 저장되지 않은 씬은 북마크할 수 없습니다.");
                return;
            }

            if (_data.Add(scene.path))
                RefreshToolbar();
        }

        static void PlayFromScene(string scenePath)
        {
            if (EditorApplication.isPlaying) return;

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (sceneAsset == null)
            {
                Debug.LogWarning($"[SceneBookmark] 씬 에셋을 로드할 수 없습니다: {scenePath}");
                return;
            }

            EditorSceneManager.playModeStartScene = sceneAsset;
            EditorApplication.isPlaying = true;
        }

        // --- 컨텍스트 메뉴 ---

        static void ShowContextMenu(SceneBookmarkEntry entry)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Play From This Scene"), false, () =>
                PlayFromScene(entry.scenePath));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Remove Bookmark"), false, () =>
            {
                _data.Remove(entry.scenePath);
                RefreshToolbar();
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Reveal in Project"), false, () =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.scenePath);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            });

            menu.ShowAsContext();
        }

        // --- 이벤트 핸들러 ---

        static void OnSceneChanged(Scene prev, Scene next)
        {
            RefreshToolbar();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorSceneManager.playModeStartScene = null;

            RefreshToolbar();
        }

        // --- 리프레시 ---

        static void RefreshToolbar()
        {
            var root = ToolbarHelper.GetToolbarRoot();
            if (root == null) return;

            var container = root.Q(ContainerId);
            if (container == null)
            {
                _injected = false;
                _pollCount = 0;
                if (!_polling)
                {
                    _polling = true;
                    EditorApplication.update += PollInject;
                }
                return;
            }

            _data = SceneBookmarkData.Load();
            RebuildButtons(container);
        }
    }
}
