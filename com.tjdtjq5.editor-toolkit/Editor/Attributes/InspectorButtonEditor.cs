#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>
    /// 커스텀 어트리뷰트 통합 에디터 베이스.
    /// [InspectorButton], [BoxGroup], [StyledList] 지원.
    /// 모든 MonoBehaviour에 자동 적용됨.
    /// </summary>
    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects]
    public class InspectorButtonEditor : UnityEditor.Editor
    {
        // ─── 색상 상수 (EditorTabBase 통일) ──────────────
        static readonly Color BG_HEADER  = new(0.11f, 0.11f, 0.14f);
        static readonly Color BG_CARD    = new(0.14f, 0.14f, 0.18f);
        static readonly Color BG_SECTION = new(0.19f, 0.19f, 0.23f);
        static readonly Color COL_DELETE = new(0.85f, 0.30f, 0.30f);
        static readonly Color COL_ADD   = new(0.40f, 0.82f, 0.45f);
        static readonly Color COL_NAV   = new(0.50f, 0.50f, 0.60f);

        const float BAR_WIDTH = 4f;
        const float HEADER_HEIGHT = 22f;

        // ─── Button 수집 ─────────────────────────────────
        struct ButtonInfo
        {
            public MethodInfo Method;
            public InspectorButtonAttribute Attribute;
        }

        List<ButtonInfo> _buttons;

            // ─── StyledList 상태 (페이지/폴드아웃/드래그) ────────
        readonly Dictionary<string, ListState> _listStates = new();
        static readonly Color COL_DRAG_INSERT = new(0.30f, 0.60f, 1.0f);
        static readonly Color COL_DRAG_GHOST  = new(0.20f, 0.30f, 0.45f, 0.5f);

        class ListState
        {
            public int CurrentPage;
            public bool Foldout = true;
            public int DragFromIndex = -1;
            public int DragOverIndex = -1;
            public string DragLabel;        // 플로팅 프리뷰 텍스트
            public Rect[] RowRects;         // 행별 영역 캐시
            public string SearchQuery = ""; // 검색어
        }

        ListState GetListState(string path)
        {
            if (!_listStates.TryGetValue(path, out var s))
            {
                s = new ListState();
                _listStates[path] = s;
            }
            return s;
        }

        // ─── 초기화 ─────────────────────────────────────
        protected virtual void OnEnable()
        {
            CollectButtons();
        }

        void CollectButtons()
        {
            _buttons = new List<ButtonInfo>();
            if (target == null) return;

            var methods = target.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<InspectorButtonAttribute>();
                if (attr != null)
                    _buttons.Add(new ButtonInfo { Method = method, Attribute = attr });
            }
        }

        // ─── Inspector 렌더링 (항상 커스텀 순회) ────────────
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawAllProperties();
            serializedObject.ApplyModifiedProperties();
            DrawInspectorButtons();
        }

        void DrawAllProperties()
        {
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            string currentBoxGroup = null;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                // m_Script
                if (iterator.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iterator);
                    continue;
                }

                var fieldInfo = GetFieldInfo(iterator);

                // ─── BoxGroup 전환 감지 ─────────────
                var boxAttr = fieldInfo?.GetCustomAttribute<BoxGroupAttribute>();
                string groupName = boxAttr?.GroupName;

                if (groupName != currentBoxGroup)
                {
                    if (currentBoxGroup != null) EndBox();
                    if (groupName != null) BeginBox(boxAttr);
                    currentBoxGroup = groupName;
                }

                // ─── StyledList 감지 → 직접 렌더링 ──
                var listAttr = fieldInfo?.GetCustomAttribute<StyledListAttribute>();
                if (listAttr != null && iterator.isArray && iterator.propertyType != SerializedPropertyType.String)
                {
                    DrawStyledList(iterator, listAttr);
                    continue;
                }

                // ─── 기본 프로퍼티 ──────────────────
                EditorGUILayout.PropertyField(iterator, true);
            }

            if (currentBoxGroup != null) EndBox();
        }

        FieldInfo GetFieldInfo(SerializedProperty property)
        {
            if (target == null) return null;
            var fieldName = property.propertyPath;
            var dotIndex = fieldName.IndexOf('.');
            if (dotIndex >= 0) fieldName = fieldName.Substring(0, dotIndex);
            return target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        // ═══════════════════════════════════════════════════
        //  BoxGroup
        // ═══════════════════════════════════════════════════

        void BeginBox(BoxGroupAttribute attr)
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(GetBoxStyle());

            var headerRect = GUILayoutUtility.GetRect(0, HEADER_HEIGHT, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, BAR_WIDTH, headerRect.height), attr.Color);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = attr.Color }
            };
            EditorGUI.LabelField(
                new Rect(headerRect.x + BAR_WIDTH + 6, headerRect.y, headerRect.width - BAR_WIDTH - 6, headerRect.height),
                attr.GroupName, titleStyle);

            GUILayout.Space(2);
        }

        void EndBox()
        {
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        static GUIStyle _boxStyle;
        static GUIStyle GetBoxStyle()
        {
            if (_boxStyle?.normal?.background != null) return _boxStyle;
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, BG_CARD);
            tex.Apply();
            _boxStyle = new GUIStyle
            {
                normal = { background = tex },
                padding = new RectOffset(6, 6, 0, 2),
                margin = new RectOffset(0, 0, 0, 0),
            };
            return _boxStyle;
        }

        // ═══════════════════════════════════════════════════
        //  StyledList (Layout 기반 커스텀 렌더링)
        // ═══════════════════════════════════════════════════

        void DrawStyledList(SerializedProperty property, StyledListAttribute attr)
        {
            var state = GetListState(property.propertyPath);
            int count = property.arraySize;
            string title = attr.Title ?? property.displayName;

            // ─── 헤더 ────────────────────────────────
            GUILayout.Space(4);
            var headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, BAR_WIDTH, headerRect.height), attr.Color);

            // 폴드아웃 삼각형
            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = attr.Color } };
            EditorGUI.LabelField(new Rect(headerRect.x + BAR_WIDTH + 4, headerRect.y + 2, 16, 16),
                state.Foldout ? "\u25BC" : "\u25B6", triStyle);

            // 타이틀 + 카운트
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = attr.Color }
            };
            EditorGUI.LabelField(
                new Rect(headerRect.x + BAR_WIDTH + 18, headerRect.y, headerRect.width - 80, headerRect.height),
                $"{title} ({count}개)", titleStyle);

            // [+] 추가 버튼
            var addRect = new Rect(headerRect.x + headerRect.width - 26, headerRect.y + 3, 24, 18);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = COL_ADD;
            if (GUI.Button(addRect, "+"))
            {
                property.InsertArrayElementAtIndex(count);
                state.Foldout = true;
            }
            GUI.backgroundColor = prevBg;

            // 헤더 클릭 → 폴드아웃 토글
            var clickRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 30, headerRect.height);
            if (Event.current.type == EventType.MouseDown && clickRect.Contains(Event.current.mousePosition))
            {
                state.Foldout = !state.Foldout;
                Event.current.Use();
            }

            if (!state.Foldout) return;

            // ─── 검색 바 (Searchable 옵션) ─────────────
            bool isSearching = false;
            if (attr.Searchable && count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                var searchIcon = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = new Color(0.5f, 0.5f, 0.6f) }, alignment = TextAnchor.MiddleCenter };
                EditorGUILayout.LabelField("\uD83D\uDD0D", searchIcon, GUILayout.Width(20));
                state.SearchQuery = EditorGUILayout.TextField(state.SearchQuery, EditorStyles.toolbarSearchField);
                if (!string.IsNullOrEmpty(state.SearchQuery) && GUILayout.Button("\u00D7", GUILayout.Width(18)))
                    state.SearchQuery = "";
                EditorGUILayout.EndHorizontal();
                isSearching = !string.IsNullOrEmpty(state.SearchQuery);
            }

            // ─── 검색 필터링: 표시할 인덱스 목록 ────────
            List<int> filteredIndices = null;
            if (isSearching)
            {
                filteredIndices = new List<int>();
                string query = state.SearchQuery.ToLowerInvariant();
                for (int i = 0; i < count; i++)
                {
                    var elem = property.GetArrayElementAtIndex(i);
                    string elemLabel = GetElementLabel(elem, i).ToLowerInvariant();
                    if (elemLabel.Contains(query))
                        filteredIndices.Add(i);
                }
            }

            // ─── 본문 ────────────────────────────────
            if (count == 0)
            {
                var emptyRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(emptyRect, BG_CARD);
                var emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    { normal = { textColor = new Color(0.5f, 0.5f, 0.55f) } };
                EditorGUI.LabelField(emptyRect, "리스트가 비어있습니다", emptyStyle);
            }
            else
            {
                // 검색 중이면 필터된 인덱스 사용, 아니면 페이지 범위
                List<int> displayIndices;
                if (isSearching && filteredIndices != null)
                {
                    displayIndices = filteredIndices;
                }
                else
                {
                    int startIdx, endIdx;
                    GetPageRange(count, attr.PageSize, state, out startIdx, out endIdx);
                    displayIndices = new List<int>();
                    for (int i = startIdx; i < endIdx; i++)
                        displayIndices.Add(i);
                }

                int visibleCount = displayIndices.Count;

                // 행 영역 캐시 초기화
                if (state.RowRects == null || state.RowRects.Length != visibleCount)
                    state.RowRects = new Rect[visibleCount];

                int deleteIdx = -1;
                var evt = Event.current;
                bool isDragging = state.DragFromIndex >= 0;

                for (int di = 0; di < displayIndices.Count; di++)
                {
                    int i = displayIndices[di];
                    int vi = di; // visible index
                    var element = property.GetArrayElementAtIndex(i);
                    Color rowBg = (i % 2 == 0) ? BG_CARD : BG_SECTION;

                    // Lv.2: 삽입 라인 (행 위쪽)
                    if (isDragging && state.DragOverIndex == i && state.DragFromIndex != i
                        && state.DragFromIndex > i)
                    {
                        var insertRect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(insertRect, COL_DRAG_INSERT);
                    }

                    // Lv.1: 원본 행 반투명
                    var savedColor = GUI.color;
                    if (isDragging && state.DragFromIndex == i)
                        GUI.color = new Color(1, 1, 1, 0.3f);

                    EditorGUILayout.BeginHorizontal(GetRowStyle(rowBg));

                    // ─── 드래그 핸들 (≡) ─────────────
                    var handleRect = GUILayoutUtility.GetRect(16, 18, GUILayout.Width(16));
                    var handleStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 14,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = isDragging && state.DragFromIndex == i
                            ? new Color(0.3f, 0.5f, 0.9f)
                            : new Color(0.45f, 0.45f, 0.50f) }
                    };
                    EditorGUI.LabelField(handleRect, "\u2261", handleStyle);
                    EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);

                    // 드래그 시작
                    if (evt.type == EventType.MouseDown && handleRect.Contains(evt.mousePosition))
                    {
                        state.DragFromIndex = i;
                        state.DragOverIndex = i;
                        // 플로팅 프리뷰용 라벨 저장
                        state.DragLabel = GetElementLabel(element, i);
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        evt.Use();
                    }

                    // ─── 프로퍼티 (펼침 아이콘 없이) ──
                    EditorGUILayout.BeginVertical();

                    if (attr.ShowIndex)
                    {
                        var idxStyle = new GUIStyle(EditorStyles.miniLabel)
                            { normal = { textColor = new Color(0.55f, 0.55f, 0.62f) } };
                        EditorGUILayout.LabelField($"[{i}]", idxStyle, GUILayout.Height(14));
                    }

                    DrawElementChildren(element);

                    EditorGUILayout.EndVertical();

                    // ─── 삭제 버튼 ───────────────────
                    if (!isDragging)
                    {
                        prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = COL_DELETE;
                        if (GUILayout.Button("\u00D7", GUILayout.Width(22), GUILayout.Height(18)))
                            deleteIdx = i;
                        GUI.backgroundColor = prevBg;
                    }
                    else
                    {
                        GUILayout.Space(22);
                    }

                    EditorGUILayout.EndHorizontal();
                    GUI.color = savedColor;

                    // 행 영역 저장
                    state.RowRects[vi] = GUILayoutUtility.GetLastRect();

                    // Lv.2: 삽입 라인 (행 아래쪽)
                    if (isDragging && state.DragOverIndex == i && state.DragFromIndex != i
                        && state.DragFromIndex < i)
                    {
                        var insertRect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(insertRect, COL_DRAG_INSERT);
                    }

                    // 구분선
                    var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(lineRect, new Color(0.25f, 0.25f, 0.30f));
                }

                // ─── 드래그 중: 오버 판정 + Repaint ──
                if (isDragging)
                {
                    if (evt.type == EventType.MouseDrag)
                    {
                        for (int vi = 0; vi < visibleCount; vi++)
                        {
                            if (state.RowRects[vi].Contains(evt.mousePosition))
                            {
                                state.DragOverIndex = displayIndices[vi];
                                break;
                            }
                        }
                        evt.Use();
                    }

                    // Lv.3: 플로팅 프리뷰 (마우스 위치에 반투명 라벨)
                    if (evt.type == EventType.Repaint && state.DragLabel != null)
                    {
                        var mousePos = evt.mousePosition;
                        var ghostRect = new Rect(mousePos.x + 12, mousePos.y - 10,
                            Mathf.Min(200, EditorGUIUtility.currentViewWidth * 0.4f), 22);
                        EditorGUI.DrawRect(ghostRect, COL_DRAG_GHOST);
                        EditorGUI.DrawRect(new Rect(ghostRect.x, ghostRect.y, 3, ghostRect.height), COL_DRAG_INSERT);

                        var ghostStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = Color.white },
                            fontStyle = FontStyle.Bold,
                            padding = new RectOffset(6, 4, 2, 2)
                        };
                        EditorGUI.LabelField(ghostRect, $"\u2261 {state.DragLabel}", ghostStyle);
                    }

                    // 매 프레임 갱신
                    if (evt.type != EventType.Layout)
                        HandleUtility.Repaint();
                }

                // ─── 드래그 드롭 완료 ────────────────
                if (isDragging && evt.type == EventType.MouseUp)
                {
                    if (state.DragOverIndex >= 0 && state.DragFromIndex != state.DragOverIndex)
                        property.MoveArrayElement(state.DragFromIndex, state.DragOverIndex);
                    state.DragFromIndex = -1;
                    state.DragOverIndex = -1;
                    state.DragLabel = null;
                    GUIUtility.hotControl = 0;
                    evt.Use();
                }

                if (deleteIdx >= 0)
                {
                    property.DeleteArrayElementAtIndex(deleteIdx);
                    ClampPage(property.arraySize, attr.PageSize, state);
                }
            }

            // ─── 페이지네이션 (검색 중에는 숨김) ──────
            if (attr.PageSize > 0 && count > attr.PageSize && !isSearching)
            {
                int totalPages = Mathf.CeilToInt((float)count / attr.PageSize);

                var navRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(navRect, BG_HEADER);

                float cx = navRect.x + navRect.width / 2;

                prevBg = GUI.backgroundColor;
                GUI.backgroundColor = COL_NAV;
                if (GUI.Button(new Rect(cx - 60, navRect.y + 2, 30, 18), "\u25C0") && state.CurrentPage > 0)
                    state.CurrentPage--;

                var pageStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                EditorGUI.LabelField(new Rect(cx - 25, navRect.y, 50, navRect.height),
                    $"{state.CurrentPage + 1} / {totalPages}", pageStyle);

                if (GUI.Button(new Rect(cx + 30, navRect.y + 2, 30, 18), "\u25B6") && state.CurrentPage < totalPages - 1)
                    state.CurrentPage++;

                GUI.backgroundColor = prevBg;
            }

            GUILayout.Space(2);
        }

        /// <summary>플로팅 프리뷰에 표시할 원소 요약 라벨 생성.</summary>
        string GetElementLabel(SerializedProperty element, int index)
        {
            // 첫 번째 string/name 필드를 찾아서 표시
            if (element.hasVisibleChildren && element.propertyType == SerializedPropertyType.Generic)
            {
                var child = element.Copy();
                var end = element.GetEndProperty();
                if (child.NextVisible(true) && !SerializedProperty.EqualContents(child, end))
                {
                    if (child.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(child.stringValue))
                        return $"[{index}] {child.stringValue}";
                }
            }

            // fallback: 타입명 + 인덱스
            return $"[{index}] {element.displayName}";
        }

        /// <summary>
        /// 원소의 자식 프로퍼티를 펼침 아이콘 없이 직접 나열.
        /// 단순 타입(float, int 등)이면 그대로 표시.
        /// </summary>
        void DrawElementChildren(SerializedProperty element)
        {
            if (!element.hasVisibleChildren || element.propertyType != SerializedPropertyType.Generic)
            {
                EditorGUILayout.PropertyField(element, GUIContent.none, true);
                return;
            }

            var child = element.Copy();
            var end = element.GetEndProperty();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                EditorGUILayout.PropertyField(child, true);
            }
        }

        static readonly Dictionary<Color, GUIStyle> _rowStyles = new();
        static GUIStyle GetRowStyle(Color bg)
        {
            if (_rowStyles.TryGetValue(bg, out var s) && s?.normal?.background != null)
                return s;
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, bg);
            tex.Apply();
            s = new GUIStyle
            {
                normal = { background = tex },
                padding = new RectOffset(4, 2, 2, 2),
            };
            _rowStyles[bg] = s;
            return s;
        }

        void GetPageRange(int total, int pageSize, ListState state, out int start, out int end)
        {
            if (pageSize <= 0 || total <= pageSize) { start = 0; end = total; return; }
            ClampPage(total, pageSize, state);
            start = state.CurrentPage * pageSize;
            end = Mathf.Min(start + pageSize, total);
        }

        void ClampPage(int total, int pageSize, ListState state)
        {
            if (pageSize <= 0) return;
            int pages = Mathf.Max(1, Mathf.CeilToInt((float)total / pageSize));
            state.CurrentPage = Mathf.Clamp(state.CurrentPage, 0, pages - 1);
        }

        // ═══════════════════════════════════════════════════
        //  InspectorButton
        // ═══════════════════════════════════════════════════

        protected void DrawInspectorButtons()
        {
            if (_buttons == null || _buttons.Count == 0) return;

            EditorGUILayout.Space(4);
            var lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.36f));
            EditorGUILayout.Space(2);

            foreach (var btn in _buttons)
            {
                var label = btn.Attribute.Label ?? btn.Method.Name;
                if (btn.Attribute.HasColor)
                {
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = btn.Attribute.Color;
                    if (GUILayout.Button(label, GUILayout.Height(btn.Attribute.Height)))
                        InvokeButton(btn.Method);
                    GUI.backgroundColor = prev;
                }
                else
                {
                    if (GUILayout.Button(label, GUILayout.Height(btn.Attribute.Height)))
                        InvokeButton(btn.Method);
                }
            }
        }

        void InvokeButton(MethodInfo method)
        {
            foreach (var t in targets)
                method.Invoke(t, null);
        }
    }

    // ─── 타입별 커스텀 에디터 (선택사항) ────────
    // 특정 타입만 다르게 처리하려면:
    //   [CustomEditor(typeof(YourComponent))]
    //   public class YourComponentEditor : InspectorButtonEditor { }
}
#endif
