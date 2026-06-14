#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Editor
{
    [CustomEditor(typeof(UIStateBinder))]
    public class UIStateBinderEditor : UnityEditor.Editor
    {
        // ─── Colors ────────────────────────────────────────
        private static readonly Color ColorHeader   = new(0.40f, 0.75f, 0.95f);
        private static readonly Color ColorState    = new(0.50f, 0.85f, 0.55f);
        private static readonly Color ColorObjects  = new(1.00f, 0.85f, 0.30f);
        private static readonly Color ColorAnimator = new(0.82f, 0.62f, 1.00f);
        private static readonly Color ColorVisual   = new(1.00f, 0.55f, 0.65f);
        private static readonly Color ColorEvent    = new(0.40f, 0.92f, 0.92f);
        private static readonly Color ColorExit     = new(0.85f, 0.45f, 0.35f);
        private static readonly Color ColorText     = new(0.95f, 0.78f, 0.40f);
        private static readonly Color ColorSprite   = new(0.60f, 0.85f, 0.50f);
        private static readonly Color ColorAlpha    = new(0.70f, 0.70f, 0.95f);
        private static readonly Color ColorTween    = new(0.55f, 0.90f, 0.75f);
        private static readonly Color ColorMuted    = new(0.60f, 0.60f, 0.65f);
        private static readonly Color ColorAdd      = new(0.40f, 0.75f, 0.40f);
        private static readonly Color ColorRemove   = new(0.90f, 0.35f, 0.35f);

        private static readonly Color BgCard    = new(0.20f, 0.20f, 0.24f);
        private static readonly Color BgInner   = new(0.17f, 0.17f, 0.21f);
        private static readonly Color BgSection = new(0.22f, 0.22f, 0.27f);
        private static readonly Color BgHeader  = new(0.13f, 0.13f, 0.16f);

        // ─── State ─────────────────────────────────────────
        private SerializedProperty _bindingsProp;
        private readonly Dictionary<int, bool> _foldouts = new();

        // ─── Drag & Drop ──────────────────────────────────
        private bool _isDragging;
        private int _dragSourceIndex = -1;
        private int _dragTargetIndex = -1;
        private readonly List<Rect> _cardHeaderRects = new();

        // ─── Preview ───────────────────────────────────────
        private const string PREVIEW_DEFAULT = "__default__";
        private string _previewState;
        private bool _previewHasSelfDeactivation;
        private readonly List<(GameObject go, bool active)> _snapshotObjects = new();
        private readonly List<(UnityEngine.UI.Graphic g, Color c)> _snapshotVisuals = new();
        private readonly List<(TMPro.TMP_Text t, string v)> _snapshotTexts = new();
        private readonly List<(UnityEngine.UI.Image img, Sprite s)> _snapshotSprites = new();
        private readonly List<(CanvasGroup cg, float a)> _snapshotAlphas = new();

        // ─── Texture Cache ─────────────────────────────────
        private static readonly Dictionary<Color, Texture2D> TexCache = new();

        private void OnEnable()
        {
            _bindingsProp = serializedObject.FindProperty("_bindings");
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(_previewState))
                RestorePreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawMainHeader();
            GUILayout.Space(4);
            DrawStatePreviewBar();
            DrawBindingsList();
            GUILayout.Space(4);
            DrawAddButton();

            serializedObject.ApplyModifiedProperties();
        }

        // ─── Main Header ──────────────────────────────────
        private void DrawMainHeader()
        {
            var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BgHeader);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), ColorHeader * 0.6f);

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ColorHeader }
            };
            EditorGUI.LabelField(rect, "UI State Binder", style);
        }

        // ─── Preview State ──────────────────────────────────
        private void DrawStatePreviewBar()
        {
            var names = CollectStateNames();
            if (names.Count == 0) return;

            string currentState = Application.isPlaying
                ? (((UIStateBinder)target).CurrentState ?? PREVIEW_DEFAULT)
                : _previewState;

            // 현재 상태 표시
            if (!string.IsNullOrEmpty(currentState))
            {
                bool isDefault = currentState == PREVIEW_DEFAULT;
                var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, isDefault ? new Color(0.35f, 0.25f, 0.15f) : new Color(0.2f, 0.35f, 0.2f));

                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = isDefault ? new Color(0.85f, 0.65f, 0.35f) : ColorState }
                };
                string modeLabel = Application.isPlaying ? "Runtime" : "Preview";
                string displayName = isDefault ? "Default" : currentState;
                EditorGUI.LabelField(rect, $"[{modeLabel}] {displayName}", style);
                GUILayout.Space(2);
            }

            // 상태 전환 버튼 행
            EditorGUILayout.BeginHorizontal();

            // Default 버튼
            {
                bool isDefault = currentState == PREVIEW_DEFAULT;
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isDefault ? new Color(0.85f, 0.65f, 0.35f) : new Color(0.3f, 0.3f, 0.3f);

                var btnStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontStyle = isDefault ? FontStyle.Bold : FontStyle.Normal,
                    fixedHeight = 22
                };

                if (GUILayout.Button("Default", btnStyle, GUILayout.Width(60)))
                {
                    if (Application.isPlaying)
                    {
                        ((UIStateBinder)target).SetDefaultState();
                    }
                    else
                    {
                        ApplyPreviewState(PREVIEW_DEFAULT);
                    }
                }

                GUI.backgroundColor = prevBg;
            }

            for (int i = 0; i < names.Count; i++)
            {
                bool isActive = names[i] == currentState;
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isActive ? ColorState : new Color(0.3f, 0.3f, 0.3f);

                var btnStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                    fixedHeight = 22
                };

                if (GUILayout.Button(names[i], btnStyle))
                {
                    if (Application.isPlaying)
                    {
                        ((UIStateBinder)target).SetState(names[i]);
                    }
                    else
                    {
                        ApplyPreviewState(names[i]);
                    }
                }

                GUI.backgroundColor = prevBg;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            // Restore 버튼 (프리뷰 모드일 때만)
            if (!Application.isPlaying && !string.IsNullOrEmpty(_previewState))
            {
                // 자기 GO 비활성화 스킵 알림
                if (_previewHasSelfDeactivation)
                {
                    var warnRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(warnRect, new Color(0.4f, 0.3f, 0.15f));
                    var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1f, 0.85f, 0.4f) }
                    };
                    EditorGUI.LabelField(warnRect, "⚠ 이 상태는 자기 GO를 비활성화합니다 (프리뷰에서 스킵됨)", warnStyle);
                    GUILayout.Space(2);
                }

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.5f, 0.3f);
                if (GUILayout.Button("Restore Preview", EditorStyles.miniButton, GUILayout.Height(20)))
                {
                    RestorePreview();
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = prevBg;
            }

            GUILayout.Space(4);
        }

        private void ApplyPreviewState(string stateName)
        {
            // 이전 프리뷰가 있으면 먼저 복원
            _previewHasSelfDeactivation = false;
            if (!string.IsNullOrEmpty(_previewState))
                RestorePreview();

            _previewState = stateName;
            var selfGo = ((Component)target).gameObject;

            // 프리뷰 적용 전 현재 씬 상태 스냅샷 캡처
            CapturePreviewSnapshot();

            // Default 상태: 모든 바인딩의 Exit 바인딩 합산 적용
            ApplyAllExitBindings(selfGo);

            // Default 프리뷰면 여기서 종료 (all-exit = Default)
            if (stateName == PREVIEW_DEFAULT)
            {
                SceneView.RepaintAll();
                return;
            }

            // 대상 바인딩 검색 후 Enter 바인딩 적용
            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                var binding = _bindingsProp.GetArrayElementAtIndex(i);
                if (binding.FindPropertyRelative("stateName").stringValue == stateName)
                {
                    _previewHasSelfDeactivation = ContainsSelfInDeactivate(binding, selfGo);
                    ApplyPreviewBinding(binding, selfGo);
                    break;
                }
            }

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 모든 상태의 Exit 바인딩을 적용하여 프리뷰 기본 상태 확보.
        /// Objects 비활성화 + Visual/Text/Sprite/Alpha exit 값 적용.
        /// </summary>
        private void ApplyAllExitBindings(GameObject selfGo)
        {
            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                ApplyPreviewBinding(_bindingsProp.GetArrayElementAtIndex(i), selfGo, isExit: true);
            }
        }

        /// <summary>
        /// 바인딩의 deactivateObjects에 자기 GO가 포함되어 있는지 확인
        /// </summary>
        private static bool ContainsSelfInDeactivate(SerializedProperty binding, GameObject selfGo)
        {
            var features = (UIStateBinder.BindingFeatures)
                binding.FindPropertyRelative("features").intValue;
            if ((features & UIStateBinder.BindingFeatures.Objects) == 0) return false;

            var deactivateArr = binding.FindPropertyRelative("deactivateObjects");
            if (deactivateArr == null) return false;
            for (int i = 0; i < deactivateArr.arraySize; i++)
            {
                var go = deactivateArr.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == selfGo) return true;
            }
            return false;
        }

        // ─── Preview Snapshot ─────────────────────────────
        private void CapturePreviewSnapshot()
        {
            _snapshotObjects.Clear();
            _snapshotVisuals.Clear();
            _snapshotTexts.Clear();
            _snapshotSprites.Clear();
            _snapshotAlphas.Clear();

            // 모든 바인딩에서 참조하는 오브젝트의 현재 상태를 저장
            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                var binding = _bindingsProp.GetArrayElementAtIndex(i);
                var features = (UIStateBinder.BindingFeatures)
                    binding.FindPropertyRelative("features").intValue;

                if ((features & UIStateBinder.BindingFeatures.Objects) != 0)
                {
                    CaptureGameObjects(binding.FindPropertyRelative("activateObjects"));
                    CaptureGameObjects(binding.FindPropertyRelative("deactivateObjects"));
                    CaptureGameObjects(binding.FindPropertyRelative("exitActivateObjects"));
                    CaptureGameObjects(binding.FindPropertyRelative("exitDeactivateObjects"));
                }
                if ((features & UIStateBinder.BindingFeatures.Visual) != 0)
                {
                    CaptureGraphicSnapshots(binding.FindPropertyRelative("visualBindings"));
                    CaptureGraphicSnapshots(binding.FindPropertyRelative("exitVisualBindings"));
                }
                if ((features & UIStateBinder.BindingFeatures.Text) != 0)
                {
                    CaptureTextSnapshots(binding.FindPropertyRelative("textBindings"));
                    CaptureTextSnapshots(binding.FindPropertyRelative("exitTextBindings"));
                }
                if ((features & UIStateBinder.BindingFeatures.Sprite) != 0)
                {
                    CaptureSpriteSnapshots(binding.FindPropertyRelative("spriteBindings"));
                    CaptureSpriteSnapshots(binding.FindPropertyRelative("exitSpriteBindings"));
                }
                if ((features & UIStateBinder.BindingFeatures.Alpha) != 0)
                {
                    CaptureAlphaSnapshots(binding.FindPropertyRelative("alphaBindings"));
                    CaptureAlphaSnapshots(binding.FindPropertyRelative("exitAlphaBindings"));
                }
            }
        }

        private void CaptureGameObjects(SerializedProperty arrayProp)
        {
            if (arrayProp == null) return;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var go = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go != null) _snapshotObjects.Add((go, go.activeSelf));
            }
        }

        private void CaptureGraphicSnapshots(SerializedProperty arrayProp)
        {
            if (arrayProp == null) return;
            for (int j = 0; j < arrayProp.arraySize; j++)
            {
                var g = arrayProp.GetArrayElementAtIndex(j).FindPropertyRelative("target")
                    .objectReferenceValue as UnityEngine.UI.Graphic;
                if (g != null) _snapshotVisuals.Add((g, g.color));
            }
        }

        private void CaptureTextSnapshots(SerializedProperty arrayProp)
        {
            if (arrayProp == null) return;
            for (int j = 0; j < arrayProp.arraySize; j++)
            {
                var t = arrayProp.GetArrayElementAtIndex(j).FindPropertyRelative("target")
                    .objectReferenceValue as TMPro.TMP_Text;
                if (t != null) _snapshotTexts.Add((t, t.text));
            }
        }

        private void CaptureSpriteSnapshots(SerializedProperty arrayProp)
        {
            if (arrayProp == null) return;
            for (int j = 0; j < arrayProp.arraySize; j++)
            {
                var img = arrayProp.GetArrayElementAtIndex(j).FindPropertyRelative("target")
                    .objectReferenceValue as UnityEngine.UI.Image;
                if (img != null) _snapshotSprites.Add((img, img.sprite));
            }
        }

        private void CaptureAlphaSnapshots(SerializedProperty arrayProp)
        {
            if (arrayProp == null) return;
            for (int j = 0; j < arrayProp.arraySize; j++)
            {
                var cg = arrayProp.GetArrayElementAtIndex(j).FindPropertyRelative("target")
                    .objectReferenceValue as CanvasGroup;
                if (cg != null) _snapshotAlphas.Add((cg, cg.alpha));
            }
        }

        /// <summary>
        /// 프리뷰 바인딩 적용 (Undo 없이 직접 오브젝트 변경)
        /// </summary>
        /// <param name="binding">적용할 상태 바인딩</param>
        /// <param name="selfGo">자기 GO — deactivateObjects에서 스킵됨</param>
        /// <param name="isExit">true면 Exit 배열 적용, false면 Enter 배열 적용</param>
        private static void ApplyPreviewBinding(SerializedProperty binding, GameObject selfGo, bool isExit = false)
        {
            var features = (UIStateBinder.BindingFeatures)
                binding.FindPropertyRelative("features").intValue;

            if ((features & UIStateBinder.BindingFeatures.Objects) != 0)
            {
                string activateKey = isExit ? "exitActivateObjects" : "activateObjects";
                string deactivateKey = isExit ? "exitDeactivateObjects" : "deactivateObjects";
                SetArrayObjectsActive(binding.FindPropertyRelative(activateKey), true);
                SetArrayObjectsActive(binding.FindPropertyRelative(deactivateKey), false, selfGo);
            }

            if ((features & UIStateBinder.BindingFeatures.Visual) != 0)
            {
                var arr = binding.FindPropertyRelative(isExit ? "exitVisualBindings" : "visualBindings");
                if (arr != null)
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var elem = arr.GetArrayElementAtIndex(i);
                        var graphic = elem.FindPropertyRelative("target").objectReferenceValue
                            as UnityEngine.UI.Graphic;
                        if (graphic != null)
                            graphic.color = elem.FindPropertyRelative("color").colorValue;
                    }
            }

            if ((features & UIStateBinder.BindingFeatures.Text) != 0)
            {
                var arr = binding.FindPropertyRelative(isExit ? "exitTextBindings" : "textBindings");
                if (arr != null)
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var elem = arr.GetArrayElementAtIndex(i);
                        var tmpText = elem.FindPropertyRelative("target").objectReferenceValue
                            as TMPro.TMP_Text;
                        if (tmpText != null)
                            tmpText.text = elem.FindPropertyRelative("text").stringValue;
                    }
            }

            if ((features & UIStateBinder.BindingFeatures.Sprite) != 0)
            {
                var arr = binding.FindPropertyRelative(isExit ? "exitSpriteBindings" : "spriteBindings");
                if (arr != null)
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var elem = arr.GetArrayElementAtIndex(i);
                        var image = elem.FindPropertyRelative("target").objectReferenceValue
                            as UnityEngine.UI.Image;
                        if (image != null)
                            image.sprite = elem.FindPropertyRelative("sprite").objectReferenceValue
                                as Sprite;
                    }
            }

            if ((features & UIStateBinder.BindingFeatures.Alpha) != 0)
            {
                var arr = binding.FindPropertyRelative(isExit ? "exitAlphaBindings" : "alphaBindings");
                if (arr != null)
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var elem = arr.GetArrayElementAtIndex(i);
                        var canvasGroup = elem.FindPropertyRelative("target").objectReferenceValue
                            as CanvasGroup;
                        if (canvasGroup != null)
                            canvasGroup.alpha = elem.FindPropertyRelative("alpha").floatValue;
                    }
            }
        }

        /// <param name="skipGo">이 GO는 비활성화에서 제외 (자기 GO 보호)</param>
        private static void SetArrayObjectsActive(SerializedProperty arrayProp, bool active, GameObject skipGo = null)
        {
            if (arrayProp == null) return;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var go = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null) continue;
                // 자기 GO 비활성화 스킵 (프리뷰에서 자기 컴포넌트 비활성화 방지)
                if (!active && go == skipGo) continue;
                go.SetActive(active);
            }
        }

        private void RestorePreview()
        {
            // 스냅샷에서 원래 상태 복원
            foreach (var (go, active) in _snapshotObjects)
                if (go != null) go.SetActive(active);
            foreach (var (g, c) in _snapshotVisuals)
                if (g != null) g.color = c;
            foreach (var (t, v) in _snapshotTexts)
                if (t != null) t.text = v;
            foreach (var (img, s) in _snapshotSprites)
                if (img != null) img.sprite = s;
            foreach (var (cg, a) in _snapshotAlphas)
                if (cg != null) cg.alpha = a;

            _snapshotObjects.Clear();
            _snapshotVisuals.Clear();
            _snapshotTexts.Clear();
            _snapshotSprites.Clear();
            _snapshotAlphas.Clear();
            _previewState = null;
            _previewHasSelfDeactivation = false;
        }

        // ─── Bindings List ─────────────────────────────────
        private void DrawBindingsList()
        {
            // Repaint 시에만 rect 리스트 갱신 (MouseDrag 등 다른 이벤트에서 참조해야 하므로)
            if (Event.current.type == EventType.Repaint)
                _cardHeaderRects.Clear();
            HandleDragCancel();

            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                // 삽입 인디케이터 (카드 앞)
                if (_isDragging && _dragTargetIndex == i
                    && _dragTargetIndex != _dragSourceIndex
                    && _dragTargetIndex != _dragSourceIndex + 1)
                {
                    DrawInsertionIndicator();
                }

                DrawStateCard(i);
                GUILayout.Space(2);
            }

            // 맨 끝 삽입 인디케이터
            if (_isDragging && _dragTargetIndex == _bindingsProp.arraySize
                && _dragTargetIndex != _dragSourceIndex + 1)
            {
                DrawInsertionIndicator();
            }

            HandleDragMove();
            HandleDragDrop();
        }

        // ─── Drag Helpers ─────────────────────────────────
        private void HandleDragCancel()
        {
            if (!_isDragging) return;
            if ((Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                || Event.current.type == EventType.MouseLeaveWindow)
            {
                _isDragging = false;
                _dragSourceIndex = -1;
                _dragTargetIndex = -1;
                Event.current.Use();
                Repaint();
            }
        }

        private void HandleDragMove()
        {
            if (!_isDragging || Event.current.type != EventType.MouseDrag) return;

            float mouseY = Event.current.mousePosition.y;
            _dragTargetIndex = _cardHeaderRects.Count; // 기본: 맨 끝
            for (int i = 0; i < _cardHeaderRects.Count; i++)
            {
                if (mouseY < _cardHeaderRects[i].center.y)
                {
                    _dragTargetIndex = i;
                    break;
                }
            }
            Event.current.Use();
            Repaint();
        }

        private void HandleDragDrop()
        {
            if (!_isDragging || Event.current.type != EventType.MouseUp) return;

            if (_dragTargetIndex != _dragSourceIndex
                && _dragTargetIndex != _dragSourceIndex + 1)
            {
                int from = _dragSourceIndex;
                int to = _dragTargetIndex > _dragSourceIndex
                    ? _dragTargetIndex - 1
                    : _dragTargetIndex;
                _bindingsProp.MoveArrayElement(from, to);
                serializedObject.ApplyModifiedProperties();
            }

            _isDragging = false;
            _dragSourceIndex = -1;
            _dragTargetIndex = -1;
            Event.current.Use();
            Repaint();
        }

        private static void DrawInsertionIndicator()
        {
            var rect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, ColorHeader);
        }

        // ─── State Card ───────────────────────────────────
        private void DrawStateCard(int index)
        {
            var binding = _bindingsProp.GetArrayElementAtIndex(index);
            var nameProp = binding.FindPropertyRelative("stateName");
            var featuresProp = binding.FindPropertyRelative("features");
            string stateName = string.IsNullOrEmpty(nameProp.stringValue)
                ? $"(State {index})"
                : nameProp.stringValue;

            bool isInitial = index == 0;
            var features = (UIStateBinder.BindingFeatures)featuresProp.intValue;

            // ─ Card
            EditorGUILayout.BeginVertical(GetBoxStyle(BgCard));

            // ─ Header
            var headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            bool isDragSource = _isDragging && _dragSourceIndex == index;
            EditorGUI.DrawRect(headerRect, isDragSource ? BgHeader * 1.3f : BgHeader);

            // 카드 헤더 위치 기록 (드래그용)
            if (Event.current.type == EventType.Repaint)
            {
                while (_cardHeaderRects.Count <= index)
                    _cardHeaderRects.Add(default);
                _cardHeaderRects[index] = headerRect;
            }

            Color accentColor = isInitial ? ColorState : ColorHeader;

            // 드래그 핸들 (≡)
            var handleRect = new Rect(headerRect.x + 4, headerRect.y + 4, 16, 16);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);
            var handleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = accentColor * 0.7f }
            };
            EditorGUI.LabelField(handleRect, "≡", handleStyle);

            // 드래그 시작
            if (Event.current.type == EventType.MouseDown
                && handleRect.Contains(Event.current.mousePosition))
            {
                _isDragging = true;
                _dragSourceIndex = index;
                _dragTargetIndex = index;
                Event.current.Use();
            }

            if (!_foldouts.ContainsKey(index)) _foldouts[index] = false;
            bool foldout = _foldouts[index];

            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = accentColor } };
            EditorGUI.LabelField(
                new Rect(headerRect.x + 24, headerRect.y + 4, 16, 16),
                foldout ? "▼" : "▶", triStyle);

            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = accentColor }
            };
            string label = isInitial ? $"{stateName}  [Initial]" : stateName;

            // Del 버튼 영역 폭
            float buttonsWidth = 50;
            EditorGUI.LabelField(
                new Rect(headerRect.x + 40, headerRect.y + 2, headerRect.width - buttonsWidth - 46, headerRect.height),
                label, nameStyle);

            // 삭제 버튼
            float btnX = headerRect.xMax - buttonsWidth;
            var btnRect = new Rect(btnX, headerRect.y + 2, 44, 20);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = ColorRemove;
            if (GUI.Button(btnRect, "Del", EditorStyles.miniButton))
            {
                _bindingsProp.DeleteArrayElementAtIndex(index);
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.backgroundColor = prevBg;

            // Foldout 클릭 (핸들 영역 제외)
            var foldoutClickRect = new Rect(headerRect.x + 24, headerRect.y,
                headerRect.width - 24 - buttonsWidth, headerRect.height);
            if (Event.current.type == EventType.MouseDown
                && foldoutClickRect.Contains(Event.current.mousePosition)
                && !_isDragging)
            {
                _foldouts[index] = !_foldouts[index];
                Event.current.Use();
            }

            if (!foldout)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4);

            // ─ State Name
            EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(nameProp, new GUIContent("State Name"));
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ─ Feature Toggles
            DrawFeatureToggles(featuresProp, ref features);

            GUILayout.Space(4);

            // ─ Objects
            if ((features & UIStateBinder.BindingFeatures.Objects) != 0)
            {
                DrawSection("Objects", ColorObjects, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorObjects);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("activateObjects"),
                        new GUIContent("Activate"), true);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("deactivateObjects"),
                        new GUIContent("Deactivate"), true);
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("exitActivateObjects"),
                        new GUIContent("Activate"), true);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("exitDeactivateObjects"),
                        new GUIContent("Deactivate"), true);
                });
            }

            // ─ Animator (배열)
            if ((features & UIStateBinder.BindingFeatures.Animator) != 0)
            {
                DrawSection("Animator", ColorAnimator, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorAnimator);
                    DrawAnimatorArray(binding.FindPropertyRelative("animatorBindings"));
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    DrawAnimatorArray(binding.FindPropertyRelative("exitAnimatorBindings"));
                });
            }

            // ─ Visual (배열)
            if ((features & UIStateBinder.BindingFeatures.Visual) != 0)
            {
                DrawSection("Visual", ColorVisual, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorVisual);
                    DrawVisualArray(binding.FindPropertyRelative("visualBindings"));
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    DrawVisualArray(binding.FindPropertyRelative("exitVisualBindings"));
                });
            }

            // ─ Event (배열)
            if ((features & UIStateBinder.BindingFeatures.Event) != 0)
            {
                DrawSection("Event", ColorEvent, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorEvent);
                    DrawEventArray(binding.FindPropertyRelative("onEnterEvents"));
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    DrawEventArray(binding.FindPropertyRelative("onExitEvents"));
                });
            }

            // ─ Text
            if ((features & UIStateBinder.BindingFeatures.Text) != 0)
            {
                DrawSection("Text", ColorText, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorText);
                    DrawTextArray(binding.FindPropertyRelative("textBindings"));
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    DrawTextArray(binding.FindPropertyRelative("exitTextBindings"));
                });
            }

            // ─ Sprite
            if ((features & UIStateBinder.BindingFeatures.Sprite) != 0)
            {
                DrawSection("Sprite", ColorSprite, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorSprite);
                    DrawSpriteArray(binding.FindPropertyRelative("spriteBindings"));
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    DrawSpriteArray(binding.FindPropertyRelative("exitSpriteBindings"));
                });
            }

            // ─ Alpha
            if ((features & UIStateBinder.BindingFeatures.Alpha) != 0)
            {
                DrawSection("Alpha", ColorAlpha, () =>
                {
                    DrawSubLabel("▶ OnEnter", ColorAlpha);
                    DrawAlphaArray(binding.FindPropertyRelative("alphaBindings"));
                    GUILayout.Space(4);
                    DrawSubLabel("◀ OnExit", ColorExit);
                    DrawAlphaArray(binding.FindPropertyRelative("exitAlphaBindings"));
                });
            }

            // ─ Tween (LitMotion scale)
            if ((features & UIStateBinder.BindingFeatures.Tween) != 0)
            {
                DrawSection("Tween", ColorTween, () =>
                {
                    DrawTweenConfig(binding.FindPropertyRelative("tweenConfig"));
                    GUILayout.Space(4);
                    DrawSubLabel("▶ OnEnter Scale", ColorTween);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("targetScale"), new GUIContent("Target Scale"));
                    GUILayout.Space(2);
                    DrawSubLabel("◀ OnExit Scale", ColorExit);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("exitTargetScale"), new GUIContent("Target Scale"));
                });
            }

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        // ─── Feature Toggles (체크박스 행) ─────────────────
        private static void DrawFeatureToggles(SerializedProperty featuresProp,
            ref UIStateBinder.BindingFeatures features)
        {
            EditorGUILayout.BeginVertical(GetBoxStyle(BgSection));

            var hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = ColorMuted },
                fontStyle = FontStyle.Italic
            };
            EditorGUILayout.LabelField("Features", hintStyle);

            EditorGUILayout.BeginHorizontal();
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Objects,
                "Objects", ColorObjects);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Animator,
                "Animator", ColorAnimator);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Visual,
                "Visual", ColorVisual);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Event,
                "Event", ColorEvent);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Text,
                "Text", ColorText);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Sprite,
                "Sprite", ColorSprite);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Alpha,
                "Alpha", ColorAlpha);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Tween,
                "Tween", ColorTween);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            featuresProp.intValue = (int)features;
        }

        private static UIStateBinder.BindingFeatures DrawFeatureToggle(
            UIStateBinder.BindingFeatures current, UIStateBinder.BindingFeatures flag,
            string label, Color color)
        {
            bool on = (current & flag) != 0;

            var prevColor = GUI.contentColor;
            GUI.contentColor = on ? color : ColorMuted;

            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = on ? FontStyle.Bold : FontStyle.Normal,
                fixedHeight = 20
            };

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = on
                ? new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 1f)
                : new Color(0.2f, 0.2f, 0.2f);

            if (GUILayout.Button(on ? $"✓ {label}" : label, style))
                on = !on;

            GUI.backgroundColor = prevBg;
            GUI.contentColor = prevColor;

            return on ? (current | flag) : (current & ~flag);
        }

        // ─── Section (색상 바 + 내용) ──────────────────────
        private static void DrawSection(string title, Color color, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(GetBoxStyle(BgSection));

            // 헤더
            var headerRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(headerRect.x - 6, headerRect.y, 3, headerRect.height), color);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = color }
            };
            EditorGUI.LabelField(headerRect, title, titleStyle);

            // 내용
            drawContent();
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private static void DrawSubLabel(string text, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = color }
            };
            EditorGUILayout.LabelField(text, style);
        }

        // ─── Tween Config ────────────────────────────────
        private static void DrawTweenConfig(SerializedProperty configProp)
        {
            if (configProp == null) return;

            var durationProp = configProp.FindPropertyRelative("duration");
            var easeProp = configProp.FindPropertyRelative("ease");
            var delayProp = configProp.FindPropertyRelative("delay");
            var unscaledProp = configProp.FindPropertyRelative("useUnscaledTime");
            if (durationProp == null) return;

            EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration"));
            EditorGUILayout.PropertyField(easeProp, new GUIContent("Ease"));
            EditorGUILayout.PropertyField(delayProp, new GUIContent("Delay"));
            EditorGUILayout.PropertyField(unscaledProp, new GUIContent("Unscaled Time"));
        }

        // ─── Animator Array ──────────────────────────────
        private static void DrawAnimatorArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                var paramTypeProp = elem.FindPropertyRelative("paramType");

                EditorGUILayout.BeginHorizontal();

                // Animator 참조
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("animator"), GUIContent.none);

                // ParamType 드롭다운
                EditorGUILayout.PropertyField(
                    paramTypeProp, GUIContent.none, GUILayout.Width(60));

                // 파라미터 이름
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("paramName"),
                    GUIContent.none, GUILayout.MinWidth(60));

                // Bool이면 값 토글 표시
                if (paramTypeProp.enumValueIndex == 1) // Bool
                {
                    EditorGUILayout.PropertyField(
                        elem.FindPropertyRelative("boolValue"),
                        GUIContent.none, GUILayout.Width(16));
                }

                if (DrawSmallRemoveButton())
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawArrayAddButton(arrayProp);
        }

        // ─── Visual Array ────────────────────────────────
        private static void DrawVisualArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("target"), GUIContent.none);
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("color"),
                    GUIContent.none, GUILayout.Width(80));
                if (DrawSmallRemoveButton())
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawArrayAddButton(arrayProp, newElem =>
            {
                newElem.FindPropertyRelative("color").colorValue = Color.white;
            });
        }

        // ─── Event Array ─────────────────────────────────
        private static void DrawEventArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));

                // 헤더 + 삭제 버튼
                EditorGUILayout.BeginHorizontal();
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = ColorEvent } };
                EditorGUILayout.LabelField($"Event {i}", labelStyle);
                GUILayout.FlexibleSpace();
                if (DrawSmallRemoveButton())
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    arrayProp.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(
                    arrayProp.GetArrayElementAtIndex(i), GUIContent.none);

                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }

            DrawArrayAddButton(arrayProp);
        }

        // ─── Text Array ─────────────────────────────────
        private static void DrawTextArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("target"), GUIContent.none);
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("text"),
                    GUIContent.none, GUILayout.MinWidth(80));
                if (DrawSmallRemoveButton())
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawArrayAddButton(arrayProp);
        }

        // ─── Sprite Array ───────────────────────────────
        private static void DrawSpriteArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("target"), GUIContent.none);
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("sprite"),
                    GUIContent.none, GUILayout.Width(80));
                if (DrawSmallRemoveButton())
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawArrayAddButton(arrayProp);
        }

        // ─── Alpha Array ────────────────────────────────
        private static void DrawAlphaArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    elem.FindPropertyRelative("target"), GUIContent.none);
                var alphaProp = elem.FindPropertyRelative("alpha");
                alphaProp.floatValue = EditorGUILayout.Slider(
                    alphaProp.floatValue, 0f, 1f, GUILayout.MinWidth(80));
                if (DrawSmallRemoveButton())
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawArrayAddButton(arrayProp, newElem =>
            {
                newElem.FindPropertyRelative("alpha").floatValue = 1f;
            });
        }

        // ─── Array Helpers ───────────────────────────────
        private static bool DrawSmallRemoveButton()
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = ColorRemove;
            bool clicked = GUILayout.Button("−", EditorStyles.miniButton,
                GUILayout.Width(20), GUILayout.Height(18));
            GUI.backgroundColor = prevBg;
            return clicked;
        }

        private static void DrawArrayAddButton(SerializedProperty arrayProp,
            System.Action<SerializedProperty> onAdd = null)
        {
            GUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            var btnRect = new Rect(rect.xMax - 60, rect.y, 60, rect.height);
            if (GUI.Button(btnRect, "+ Add", EditorStyles.miniButton))
            {
                int idx = arrayProp.arraySize;
                arrayProp.InsertArrayElementAtIndex(idx);
                onAdd?.Invoke(arrayProp.GetArrayElementAtIndex(idx));
            }
        }

        // ─── Add Button ───────────────────────────────────
        private void DrawAddButton()
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = ColorAdd;
            if (GUILayout.Button("+ Add State", GUILayout.Height(26)))
            {
                _bindingsProp.InsertArrayElementAtIndex(_bindingsProp.arraySize);
                var newElem = _bindingsProp.GetArrayElementAtIndex(_bindingsProp.arraySize - 1);
                newElem.FindPropertyRelative("stateName").stringValue = $"State{_bindingsProp.arraySize}";
                newElem.FindPropertyRelative("features").intValue = 0;

                // Enter 배열 필드 초기화
                ClearArray(newElem.FindPropertyRelative("activateObjects"));
                ClearArray(newElem.FindPropertyRelative("deactivateObjects"));
                ClearArray(newElem.FindPropertyRelative("animatorBindings"));
                ClearArray(newElem.FindPropertyRelative("visualBindings"));
                ClearArray(newElem.FindPropertyRelative("onEnterEvents"));
                ClearArray(newElem.FindPropertyRelative("onExitEvents"));
                ClearArray(newElem.FindPropertyRelative("textBindings"));
                ClearArray(newElem.FindPropertyRelative("spriteBindings"));
                ClearArray(newElem.FindPropertyRelative("alphaBindings"));
                // Exit 배열 필드 초기화
                ClearArray(newElem.FindPropertyRelative("exitActivateObjects"));
                ClearArray(newElem.FindPropertyRelative("exitDeactivateObjects"));
                ClearArray(newElem.FindPropertyRelative("exitAnimatorBindings"));
                ClearArray(newElem.FindPropertyRelative("exitVisualBindings"));
                ClearArray(newElem.FindPropertyRelative("exitTextBindings"));
                ClearArray(newElem.FindPropertyRelative("exitSpriteBindings"));
                ClearArray(newElem.FindPropertyRelative("exitAlphaBindings"));

                _foldouts[_bindingsProp.arraySize - 1] = true;
            }
            GUI.backgroundColor = prevBg;
        }

        // ─── Helpers ──────────────────────────────────────
        private static void ClearArray(SerializedProperty prop)
        {
            if (prop != null)
                prop.ClearArray();
        }

        private List<string> CollectStateNames()
        {
            var names = new List<string>();
            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                var name = _bindingsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("stateName").stringValue;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            return names;
        }

        private static GUIStyle GetBoxStyle(Color bgColor)
        {
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(0, 0, 1, 1)
            };
            style.normal.background = GetOrCreateTex(bgColor);
            return style;
        }

        private static Texture2D GetOrCreateTex(Color color)
        {
            if (TexCache.TryGetValue(color, out var cached) && cached != null)
                return cached;
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            TexCache[color] = tex;
            return tex;
        }
    }
}
#endif
