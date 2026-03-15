#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Editor
{
    [CustomEditor(typeof(UIStateBinder))]
    public class UIStateBinderEditor : UnityEditor.Editor
    {
        // ─── Colors ────────────────────────────────────────
        private static readonly Color ColorHeader    = new(0.40f, 0.75f, 0.95f);
        private static readonly Color ColorState     = new(0.50f, 0.85f, 0.55f);
        private static readonly Color ColorExclusive = new(0.95f, 0.70f, 0.30f);
        private static readonly Color ColorObjects   = new(1.00f, 0.85f, 0.30f);
        private static readonly Color ColorAnimator  = new(0.82f, 0.62f, 1.00f);
        private static readonly Color ColorVisual    = new(1.00f, 0.55f, 0.65f);
        private static readonly Color ColorEvent     = new(0.40f, 0.92f, 0.92f);
        private static readonly Color ColorTween     = new(0.55f, 0.90f, 0.75f);
        private static readonly Color ColorAdd       = new(0.40f, 0.75f, 0.40f);
        private static readonly Color ColorRemove    = new(0.90f, 0.35f, 0.35f);

        private static readonly Color BgCard    = new(0.20f, 0.20f, 0.24f);
        private static readonly Color BgInner   = new(0.17f, 0.17f, 0.21f);
        private static readonly Color BgSection = new(0.22f, 0.22f, 0.27f);
        private static readonly Color BgHeader  = new(0.13f, 0.13f, 0.16f);

        // ─── State ─────────────────────────────────────────
        private SerializedProperty _initialStateProp;
        private SerializedProperty _exclusivePoolProp;
        private SerializedProperty _bindingsProp;
        private readonly Dictionary<int, bool> _foldouts = new();
        private bool _exclusivePoolFoldout = true;

        // ─── Caches ───────────────────────────────────────
        private static readonly Dictionary<Color, Texture2D> TexCache = new();
        private static readonly Dictionary<Color, GUIStyle> BoxStyleCache = new();

        private void OnEnable()
        {
            _initialStateProp = serializedObject.FindProperty("_initialState");
            _exclusivePoolProp = serializedObject.FindProperty("_exclusivePool");
            _bindingsProp = serializedObject.FindProperty("_bindings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawMainHeader();
            GUILayout.Space(4);
            DrawInitialStateDropdown();
            GUILayout.Space(6);
            DrawRuntimeState();
            DrawExclusivePool();
            GUILayout.Space(4);
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

        // ─── Initial State Dropdown ────────────────────────
        private void DrawInitialStateDropdown()
        {
            var names = CollectStateNames();

            if (names.Count == 0)
            {
                EditorGUILayout.PropertyField(_initialStateProp, new GUIContent("Initial State"));
                return;
            }

            int currentIdx = names.IndexOf(_initialStateProp.stringValue);
            if (currentIdx < 0) currentIdx = 0;

            EditorGUILayout.BeginHorizontal();
            var labelStyle = new GUIStyle(EditorStyles.label)
                { normal = { textColor = ColorState }, fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField("Initial State", labelStyle, GUILayout.Width(100));

            int newIdx = EditorGUILayout.Popup(currentIdx, names.ToArray());
            if (newIdx >= 0 && newIdx < names.Count)
                _initialStateProp.stringValue = names[newIdx];

            EditorGUILayout.EndHorizontal();
        }

        // ─── Runtime State (Play Mode) ────────────────────
        private void DrawRuntimeState()
        {
            if (!Application.isPlaying) return;

            var binder = (UIStateBinder)target;
            var current = binder.CurrentState;
            if (string.IsNullOrEmpty(current)) return;

            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.35f, 0.2f));

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ColorState }
            };
            EditorGUI.LabelField(rect, $"Current: {current}", style);
            GUILayout.Space(4);
        }

        // ─── Exclusive Pool ──────────────────────────────
        private void DrawExclusivePool()
        {
            if (_exclusivePoolProp.arraySize == 0 && !HasAnyExclusiveFeature())
            {
                // Exclusive 사용 안 하면 숨김, 추가 힌트만 표시
                var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.5f, 0.5f, 0.55f) },
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter
                };
                EditorGUILayout.LabelField("Exclusive Pool: 상태에서 Exclusive 활성화 시 표시됩니다", hintStyle);
                return;
            }

            EditorGUILayout.BeginVertical(GetBoxStyle(BgSection));

            // 헤더
            var headerRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(headerRect.x - 6, headerRect.y, 3, headerRect.height), ColorExclusive);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = ColorExclusive }
            };

            // 폴드아웃 삼각형
            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColorExclusive } };
            EditorGUI.LabelField(
                new Rect(headerRect.x, headerRect.y + 2, 16, 16),
                _exclusivePoolFoldout ? "\u25BC" : "\u25B6", triStyle);

            EditorGUI.LabelField(
                new Rect(headerRect.x + 16, headerRect.y, headerRect.width - 16, headerRect.height),
                $"Exclusive Pool ({_exclusivePoolProp.arraySize})", titleStyle);

            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                _exclusivePoolFoldout = !_exclusivePoolFoldout;
                Event.current.Use();
            }

            if (_exclusivePoolFoldout)
            {
                GUILayout.Space(2);
                for (int i = 0; i < _exclusivePoolProp.arraySize; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(
                        _exclusivePoolProp.GetArrayElementAtIndex(i), GUIContent.none);

                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = ColorRemove;
                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        _exclusivePoolProp.DeleteArrayElementAtIndex(i);
                        GUI.backgroundColor = prevBg;
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.Space(2);
                var prevBg2 = GUI.backgroundColor;
                GUI.backgroundColor = ColorAdd;
                if (GUILayout.Button("+ Add to Pool", EditorStyles.miniButton, GUILayout.Height(18)))
                {
                    _exclusivePoolProp.InsertArrayElementAtIndex(_exclusivePoolProp.arraySize);
                    _exclusivePoolProp.GetArrayElementAtIndex(_exclusivePoolProp.arraySize - 1)
                        .objectReferenceValue = null;
                }
                GUI.backgroundColor = prevBg2;
            }

            EditorGUILayout.EndVertical();
        }

        // ─── Bindings List ─────────────────────────────────
        private Dictionary<string, int> _nameCounts;

        private void DrawBindingsList()
        {
            // 중복 이름 사전 수집
            _nameCounts = new Dictionary<string, int>();
            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                var n = _bindingsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("stateName").stringValue;
                if (string.IsNullOrEmpty(n)) continue;
                _nameCounts[n] = _nameCounts.GetValueOrDefault(n) + 1;
            }

            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                DrawStateCard(i);
                GUILayout.Space(2);
            }
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

            bool isInitial = nameProp.stringValue == _initialStateProp.stringValue;
            var features = (UIStateBinder.BindingFeatures)featuresProp.intValue;

            // ─ Card
            EditorGUILayout.BeginVertical(GetBoxStyle(BgCard));

            // ─ Header: [▲▼] [▶ StateName] [Dup] [Del]
            var headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, BgHeader);

            Color accentColor = isInitial ? ColorState : ColorHeader;
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 3, headerRect.height), accentColor);

            if (!_foldouts.ContainsKey(index)) _foldouts[index] = false;
            bool foldout = _foldouts[index];

            // ▲▼ 리오더 버튼
            var arrowStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9, fixedHeight = 20, padding = new RectOffset(0,0,0,0) };
            float ax = headerRect.x + 4;
            float ay = headerRect.y + 2;

            GUI.enabled = index > 0;
            if (GUI.Button(new Rect(ax, ay, 18, 20), "\u25B2", arrowStyle))
            {
                _bindingsProp.MoveArrayElement(index, index - 1);
                SwapFoldout(index, index - 1);
                GUI.enabled = true;
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.enabled = index < _bindingsProp.arraySize - 1;
            if (GUI.Button(new Rect(ax + 18, ay, 18, 20), "\u25BC", arrowStyle))
            {
                _bindingsProp.MoveArrayElement(index, index + 1);
                SwapFoldout(index, index + 1);
                GUI.enabled = true;
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.enabled = true;

            // 삼각형 (폴드아웃)
            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = accentColor } };
            EditorGUI.LabelField(
                new Rect(ax + 40, headerRect.y + 4, 16, 16),
                foldout ? "\u25BC" : "\u25B6", triStyle);

            // 상태 이름
            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = accentColor }
            };
            string label = isInitial ? $"{stateName}  [Initial]" : stateName;
            EditorGUI.LabelField(
                new Rect(ax + 56, headerRect.y + 2, headerRect.width - 150, headerRect.height),
                label, nameStyle);

            // Dup 버튼
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = ColorAdd;
            if (GUI.Button(new Rect(headerRect.xMax - 94, ay, 38, 20), "Dup", EditorStyles.miniButton))
            {
                _bindingsProp.InsertArrayElementAtIndex(index);
                var copied = _bindingsProp.GetArrayElementAtIndex(index + 1);
                var copiedName = copied.FindPropertyRelative("stateName");
                copiedName.stringValue += "_Copy";
                _foldouts[index + 1] = true;
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndVertical();
                return;
            }

            // Del 버튼
            GUI.backgroundColor = ColorRemove;
            if (GUI.Button(new Rect(headerRect.xMax - 50, ay, 44, 20), "Del", EditorStyles.miniButton))
            {
                _bindingsProp.DeleteArrayElementAtIndex(index);
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.backgroundColor = prevBg;

            // 헤더 클릭 → 폴드아웃 토글 (버튼 영역 제외)
            var foldClickRect = new Rect(ax + 36, headerRect.y, headerRect.width - 150, headerRect.height);
            if (Event.current.type == EventType.MouseDown && foldClickRect.Contains(Event.current.mousePosition))
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

            // ─ State Name + 중복 경고
            EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(nameProp, new GUIContent("State Name"));
            if (!string.IsNullOrEmpty(nameProp.stringValue)
                && _nameCounts != null
                && _nameCounts.GetValueOrDefault(nameProp.stringValue) > 1)
            {
                EditorGUILayout.HelpBox(
                    $"'{nameProp.stringValue}' 이름이 중복됩니다! 뒤쪽 상태가 앞쪽을 덮어씁니다.",
                    MessageType.Error);
            }
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // ─ Feature Toggles
            DrawFeatureToggles(featuresProp, ref features);

            GUILayout.Space(4);

            // ─ Exclusive Show (체크박스 방식)
            if ((features & UIStateBinder.BindingFeatures.Exclusive) != 0)
            {
                DrawExclusiveShowSection(binding);
            }

            // ─ Objects (기존 호환)
            if ((features & UIStateBinder.BindingFeatures.Objects) != 0)
            {
                DrawSection("Objects", ColorObjects, () =>
                {
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("activateObjects"),
                        new GUIContent("Activate"), true);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative("deactivateObjects"),
                        new GUIContent("Deactivate"), true);
                });
            }

            // ─ Animator
            if ((features & UIStateBinder.BindingFeatures.Animator) != 0)
            {
                DrawAnimatorSection(binding);
            }

            // ─ Visual
            if ((features & UIStateBinder.BindingFeatures.Visual) != 0)
            {
                DrawVisualSection(binding);
            }

            // ─ Event (onEnter + onExit)
            if ((features & UIStateBinder.BindingFeatures.Event) != 0)
            {
                DrawSection("Events", ColorEvent, () =>
                {
                    EditorGUILayout.PropertyField(binding.FindPropertyRelative("onEnter"),
                        new GUIContent("OnEnter"));
                    GUILayout.Space(4);
                    EditorGUILayout.PropertyField(binding.FindPropertyRelative("onExit"),
                        new GUIContent("OnExit"));
                });
            }

            // ─ Tween
            if ((features & UIStateBinder.BindingFeatures.Tween) != 0)
            {
                DrawTweenSection(binding);
            }

            GUILayout.Space(2);

            // ─ Play Mode / Edit Mode 테스트 버튼
            if (Application.isPlaying)
            {
                prevBg = GUI.backgroundColor;
                GUI.backgroundColor = ColorState;
                if (GUILayout.Button($"Set State: {stateName}", GUILayout.Height(22)))
                    ((UIStateBinder)target).SetState(nameProp.stringValue);
                GUI.backgroundColor = prevBg;
            }
            else
            {
                prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.7f);
                if (GUILayout.Button($"Preview: {stateName}", GUILayout.Height(22)))
                {
                    ApplyEditModePreview(binding, features);
                }
                GUI.backgroundColor = prevBg;
            }

            EditorGUILayout.EndVertical();
        }

        // ─── Exclusive Show (체크박스로 pool에서 선택) ──────
        private void DrawExclusiveShowSection(SerializedProperty binding)
        {
            var binder = (UIStateBinder)target;
            var pool = binder.ExclusivePool;

            if (pool == null || pool.Length == 0)
            {
                DrawSection("Exclusive Show", ColorExclusive, () =>
                {
                    var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = ColorRemove },
                        fontStyle = FontStyle.Italic
                    };
                    EditorGUILayout.LabelField("Exclusive Pool이 비어있습니다. 위에서 먼저 등록하세요.", warnStyle);
                });
                return;
            }

            var showProp = binding.FindPropertyRelative("exclusiveShow");

            DrawSection("Exclusive Show", ColorExclusive, () =>
            {
                // 현재 exclusiveShow에 있는 오브젝트 수집
                var currentShows = new HashSet<int>();
                for (int i = 0; i < showProp.arraySize; i++)
                {
                    var obj = showProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (obj == null) continue;
                    for (int p = 0; p < pool.Length; p++)
                    {
                        if (pool[p] == obj)
                        {
                            currentShows.Add(p);
                            break;
                        }
                    }
                }

                bool changed = false;

                for (int p = 0; p < pool.Length; p++)
                {
                    if (pool[p] == null) continue;

                    bool wasOn = currentShows.Contains(p);
                    bool isOn = EditorGUILayout.ToggleLeft(pool[p].name, wasOn);

                    if (isOn != wasOn)
                    {
                        changed = true;
                        if (isOn)
                            currentShows.Add(p);
                        else
                            currentShows.Remove(p);
                    }
                }

                if (changed)
                {
                    // exclusiveShow 배열 재구성
                    var newShows = currentShows.OrderBy(i => i).ToList();
                    showProp.arraySize = newShows.Count;
                    for (int i = 0; i < newShows.Count; i++)
                        showProp.GetArrayElementAtIndex(i).objectReferenceValue = pool[newShows[i]];
                }
            });
        }

        // ─── Animator Section (배열 기반) ────────────────────
        private static void DrawAnimatorSection(SerializedProperty binding)
        {
            var targetsProp = binding.FindPropertyRelative("animatorTargets");
            var legacyAnimProp = binding.FindPropertyRelative("animator");
            var legacyTriggerProp = binding.FindPropertyRelative("animatorTrigger");

            bool hasNewArray = targetsProp.arraySize > 0;

            DrawSection("Animator", ColorAnimator, () =>
            {
                if (!hasNewArray && legacyAnimProp.objectReferenceValue != null)
                {
                    // 레거시 표시 + 마이그레이션 버튼
                    EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));
                    var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.7f, 0.6f, 0.4f) },
                        fontStyle = FontStyle.Italic
                    };
                    EditorGUILayout.LabelField("(레거시 단일 타겟)", hintStyle);
                    EditorGUILayout.PropertyField(legacyAnimProp, new GUIContent("Animator"));
                    EditorGUILayout.PropertyField(legacyTriggerProp, new GUIContent("Trigger"));

                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = ColorAnimator;
                    if (GUILayout.Button("배열로 전환", EditorStyles.miniButton))
                    {
                        targetsProp.InsertArrayElementAtIndex(0);
                        var elem = targetsProp.GetArrayElementAtIndex(0);
                        elem.FindPropertyRelative("animator").objectReferenceValue = legacyAnimProp.objectReferenceValue;
                        elem.FindPropertyRelative("parameterName").stringValue = legacyTriggerProp.stringValue;
                        elem.FindPropertyRelative("paramType").enumValueIndex = 0;
                        elem.FindPropertyRelative("floatValue").floatValue = 0;
                        legacyAnimProp.objectReferenceValue = null;
                        legacyTriggerProp.stringValue = "";
                    }
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndVertical();
                    return;
                }

                // 배열 항목 그리기
                for (int i = 0; i < targetsProp.arraySize; i++)
                {
                    var elem = targetsProp.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));
                    EditorGUILayout.BeginHorizontal();

                    var idxStyle = new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = ColorAnimator } };
                    EditorGUILayout.LabelField($"[{i}]", idxStyle, GUILayout.Width(24));

                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("animator"), GUIContent.none);

                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = ColorRemove;
                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        targetsProp.DeleteArrayElementAtIndex(i);
                        GUI.backgroundColor = prevBg;
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("paramType"),
                        GUIContent.none, GUILayout.Width(70));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("parameterName"),
                        GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    var paramType = (UIStateBinder.AnimatorParamType)
                        elem.FindPropertyRelative("paramType").enumValueIndex;
                    if (paramType != UIStateBinder.AnimatorParamType.Trigger)
                    {
                        string valueLabel = paramType == UIStateBinder.AnimatorParamType.Bool ? "Value (0/1)" : "Value";
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("floatValue"),
                            new GUIContent(valueLabel));
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(1);
                }

                var prevBg2 = GUI.backgroundColor;
                GUI.backgroundColor = ColorAdd;
                if (GUILayout.Button("+ Add Animator", EditorStyles.miniButton, GUILayout.Height(18)))
                {
                    targetsProp.InsertArrayElementAtIndex(targetsProp.arraySize);
                    var newElem = targetsProp.GetArrayElementAtIndex(targetsProp.arraySize - 1);
                    newElem.FindPropertyRelative("animator").objectReferenceValue = null;
                    newElem.FindPropertyRelative("parameterName").stringValue = "";
                    newElem.FindPropertyRelative("paramType").enumValueIndex = 0;
                    newElem.FindPropertyRelative("floatValue").floatValue = 0;
                }
                GUI.backgroundColor = prevBg2;
            });
        }

        // ─── Visual Section (배열 기반) ─────────────────────
        private static void DrawVisualSection(SerializedProperty binding)
        {
            var targetsProp = binding.FindPropertyRelative("visualTargets");
            var legacyImageProp = binding.FindPropertyRelative("targetImage");
            var legacyColorProp = binding.FindPropertyRelative("imageColor");

            bool hasNewArray = targetsProp.arraySize > 0;

            DrawSection("Visual", ColorVisual, () =>
            {
                if (!hasNewArray && legacyImageProp.objectReferenceValue != null)
                {
                    // 레거시 표시 + 마이그레이션 버튼
                    EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));
                    var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.7f, 0.5f, 0.5f) },
                        fontStyle = FontStyle.Italic
                    };
                    EditorGUILayout.LabelField("(레거시 단일 타겟)", hintStyle);
                    EditorGUILayout.PropertyField(legacyImageProp, new GUIContent("Target Image"));
                    EditorGUILayout.PropertyField(legacyColorProp, new GUIContent("Color"));

                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = ColorVisual;
                    if (GUILayout.Button("배열로 전환", EditorStyles.miniButton))
                    {
                        targetsProp.InsertArrayElementAtIndex(0);
                        var elem = targetsProp.GetArrayElementAtIndex(0);
                        elem.FindPropertyRelative("target").objectReferenceValue = legacyImageProp.objectReferenceValue;
                        elem.FindPropertyRelative("color").colorValue = legacyColorProp.colorValue;
                        elem.FindPropertyRelative("sprite").objectReferenceValue = null;
                        elem.FindPropertyRelative("alpha").floatValue = -1f;
                        legacyImageProp.objectReferenceValue = null;
                    }
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndVertical();
                    return;
                }

                // 배열 항목 그리기
                for (int i = 0; i < targetsProp.arraySize; i++)
                {
                    var elem = targetsProp.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical(GetBoxStyle(BgInner));

                    EditorGUILayout.BeginHorizontal();
                    var idxStyle = new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = ColorVisual } };
                    EditorGUILayout.LabelField($"[{i}]", idxStyle, GUILayout.Width(24));

                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), GUIContent.none);

                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = ColorRemove;
                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        targetsProp.DeleteArrayElementAtIndex(i);
                        GUI.backgroundColor = prevBg;
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("color"), new GUIContent("Color"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("sprite"), new GUIContent("Sprite"));

                    var alphaProp = elem.FindPropertyRelative("alpha");
                    EditorGUILayout.BeginHorizontal();
                    bool useAlpha = alphaProp.floatValue >= 0f;
                    bool newUseAlpha = EditorGUILayout.ToggleLeft("Alpha", useAlpha, GUILayout.Width(60));
                    if (newUseAlpha != useAlpha)
                        alphaProp.floatValue = newUseAlpha ? 1f : -1f;
                    if (newUseAlpha)
                        alphaProp.floatValue = EditorGUILayout.Slider(alphaProp.floatValue, 0f, 1f);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(1);
                }

                var prevBg2 = GUI.backgroundColor;
                GUI.backgroundColor = ColorAdd;
                if (GUILayout.Button("+ Add Visual", EditorStyles.miniButton, GUILayout.Height(18)))
                {
                    targetsProp.InsertArrayElementAtIndex(targetsProp.arraySize);
                    var newElem = targetsProp.GetArrayElementAtIndex(targetsProp.arraySize - 1);
                    newElem.FindPropertyRelative("target").objectReferenceValue = null;
                    newElem.FindPropertyRelative("color").colorValue = Color.white;
                    newElem.FindPropertyRelative("sprite").objectReferenceValue = null;
                    newElem.FindPropertyRelative("alpha").floatValue = -1f;
                }
                GUI.backgroundColor = prevBg2;
            });
        }

        // ─── Tween Section ──────────────────────────────────
        private static void DrawTweenSection(SerializedProperty binding)
        {
            var configProp = binding.FindPropertyRelative("tweenConfig");
            var scaleProp = binding.FindPropertyRelative("targetScale");

            DrawSection("Tween", ColorTween, () =>
            {
                EditorGUILayout.PropertyField(configProp.FindPropertyRelative("duration"),
                    new GUIContent("Duration"));
                EditorGUILayout.PropertyField(configProp.FindPropertyRelative("ease"),
                    new GUIContent("Ease"));
                EditorGUILayout.PropertyField(configProp.FindPropertyRelative("delay"),
                    new GUIContent("Delay"));
                EditorGUILayout.PropertyField(configProp.FindPropertyRelative("useUnscaledTime"),
                    new GUIContent("Unscaled Time"));

                GUILayout.Space(4);

                var scaleVal = scaleProp.vector3Value;
                bool isDefault = scaleVal == Vector3.one;
                EditorGUILayout.PropertyField(scaleProp, new GUIContent("Target Scale"));
                if (isDefault)
                {
                    var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.5f, 0.5f, 0.55f) },
                        fontStyle = FontStyle.Italic
                    };
                    EditorGUILayout.LabelField("(1,1,1) = Scale 변경 없음", hintStyle);
                }
            });
        }

        // ─── Feature Toggles (체크박스 행) ─────────────────
        private static void DrawFeatureToggles(SerializedProperty featuresProp,
            ref UIStateBinder.BindingFeatures features)
        {
            EditorGUILayout.BeginVertical(GetBoxStyle(BgSection));

            var hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.65f) },
                fontStyle = FontStyle.Italic
            };
            EditorGUILayout.LabelField("Features", hintStyle);

            EditorGUILayout.BeginHorizontal();

            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Exclusive,
                "Exclusive", ColorExclusive);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Objects,
                "Objects", ColorObjects);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Animator,
                "Animator", ColorAnimator);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Visual,
                "Visual", ColorVisual);
            features = DrawFeatureToggle(features, UIStateBinder.BindingFeatures.Event,
                "Event", ColorEvent);
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
            GUI.contentColor = on ? color : new Color(0.55f, 0.55f, 0.60f);

            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = on ? FontStyle.Bold : FontStyle.Normal,
                fixedHeight = 20
            };

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = on
                ? new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 1f)
                : new Color(0.2f, 0.2f, 0.2f);

            if (GUILayout.Button(on ? $"\u2713 {label}" : label, style))
                on = !on;

            GUI.backgroundColor = prevBg;
            GUI.contentColor = prevColor;

            return on ? (current | flag) : (current & ~flag);
        }

        // ─── Section (색상 바 + 내용) ──────────────────────
        private static void DrawSection(string title, Color color, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(GetBoxStyle(BgSection));

            var headerRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(headerRect.x - 6, headerRect.y, 3, headerRect.height), color);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = color }
            };
            EditorGUI.LabelField(headerRect, title, titleStyle);

            drawContent();
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
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
                newElem.FindPropertyRelative("features").intValue =
                    (int)UIStateBinder.BindingFeatures.Objects;
                newElem.FindPropertyRelative("imageColor").colorValue = Color.white;
                _foldouts[_bindingsProp.arraySize - 1] = true;
            }
            GUI.backgroundColor = prevBg;
        }

        // ─── Edit Mode Preview ──────────────────────────────
        private void ApplyEditModePreview(SerializedProperty binding,
            UIStateBinder.BindingFeatures features)
        {
            var binder = (UIStateBinder)target;

            // Undo 등록 (Ctrl+Z로 원복)
            var undoTargets = new List<Object> { binder };

            // Exclusive Pool GO들
            var pool = binder.ExclusivePool;
            if (pool != null)
                foreach (var go in pool)
                    if (go != null) undoTargets.Add(go);

            // activate/deactivate GO들
            AddArrayObjectsToUndo(binding.FindPropertyRelative("activateObjects"), undoTargets);
            AddArrayObjectsToUndo(binding.FindPropertyRelative("deactivateObjects"), undoTargets);
            AddArrayObjectsToUndo(binding.FindPropertyRelative("exclusiveShow"), undoTargets);

            Undo.RecordObjects(undoTargets.ToArray(), "UIStateBinder Preview");

            // Exclusive
            if ((features & UIStateBinder.BindingFeatures.Exclusive) != 0 && pool != null)
            {
                foreach (var go in pool)
                    if (go != null) go.SetActive(false);

                var showProp = binding.FindPropertyRelative("exclusiveShow");
                for (int i = 0; i < showProp.arraySize; i++)
                {
                    var go = showProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                    if (go != null) go.SetActive(true);
                }
            }

            // Objects
            if ((features & UIStateBinder.BindingFeatures.Objects) != 0)
            {
                SetActiveFromArray(binding.FindPropertyRelative("activateObjects"), true);
                SetActiveFromArray(binding.FindPropertyRelative("deactivateObjects"), false);
            }

            // Visual (즉시 적용, Tween 아님)
            if ((features & UIStateBinder.BindingFeatures.Visual) != 0)
            {
                var vtProp = binding.FindPropertyRelative("visualTargets");
                if (vtProp.arraySize > 0)
                {
                    for (int i = 0; i < vtProp.arraySize; i++)
                    {
                        var elem = vtProp.GetArrayElementAtIndex(i);
                        var graphic = elem.FindPropertyRelative("target").objectReferenceValue as UnityEngine.UI.Graphic;
                        if (graphic == null) continue;

                        Undo.RecordObject(graphic, "UIStateBinder Preview Visual");
                        var c = elem.FindPropertyRelative("color").colorValue;
                        float a = elem.FindPropertyRelative("alpha").floatValue;
                        if (a >= 0f) c.a = a;
                        graphic.color = c;

                        var sprite = elem.FindPropertyRelative("sprite").objectReferenceValue as Sprite;
                        if (sprite != null && graphic is UnityEngine.UI.Image img)
                            img.sprite = sprite;
                    }
                }
                else
                {
                    var legacyImg = binding.FindPropertyRelative("targetImage").objectReferenceValue as UnityEngine.UI.Image;
                    if (legacyImg != null)
                    {
                        Undo.RecordObject(legacyImg, "UIStateBinder Preview Visual");
                        legacyImg.color = binding.FindPropertyRelative("imageColor").colorValue;
                    }
                }
            }

            EditorUtility.SetDirty(binder);
            SceneView.RepaintAll();
        }

        private static void AddArrayObjectsToUndo(SerializedProperty arrayProp, List<Object> targets)
        {
            if (arrayProp == null) return;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var obj = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (obj != null) targets.Add(obj);
            }
        }

        private static void SetActiveFromArray(SerializedProperty arrayProp, bool active)
        {
            if (arrayProp == null) return;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var go = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go != null) go.SetActive(active);
            }
        }

        private void SwapFoldout(int a, int b)
        {
            bool fa = _foldouts.GetValueOrDefault(a);
            bool fb = _foldouts.GetValueOrDefault(b);
            _foldouts[a] = fb;
            _foldouts[b] = fa;
        }

        // ─── Helpers ──────────────────────────────────────
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

        private bool HasAnyExclusiveFeature()
        {
            for (int i = 0; i < _bindingsProp.arraySize; i++)
            {
                var f = (UIStateBinder.BindingFeatures)_bindingsProp
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("features").intValue;
                if ((f & UIStateBinder.BindingFeatures.Exclusive) != 0)
                    return true;
            }
            return false;
        }

        private static GUIStyle GetBoxStyle(Color bgColor)
        {
            if (BoxStyleCache.TryGetValue(bgColor, out var cached) && cached != null)
                return cached;

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(0, 0, 1, 1)
            };
            style.normal.background = GetOrCreateTex(bgColor);
            BoxStyleCache[bgColor] = style;
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
