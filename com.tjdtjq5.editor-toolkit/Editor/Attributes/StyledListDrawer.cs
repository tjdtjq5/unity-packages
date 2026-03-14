#if UNITY_EDITOR
using System.Collections.Generic;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(StyledListAttribute))]
    public class StyledListDrawer : PropertyDrawer
    {
        // ─── 색상 (EditorTabBase 통일) ───────────────────
        static readonly Color BG_HEADER  = new(0.11f, 0.11f, 0.14f);
        static readonly Color BG_CARD    = new(0.14f, 0.14f, 0.18f);
        static readonly Color BG_SECTION = new(0.19f, 0.19f, 0.23f);
        static readonly Color COL_DELETE = new(0.85f, 0.30f, 0.30f);
        static readonly Color COL_ADD    = new(0.40f, 0.82f, 0.45f);
        static readonly Color COL_NAV    = new(0.50f, 0.50f, 0.60f);

        const float BAR_WIDTH = 4f;
        const float HEADER_HEIGHT = 24f;
        const float NAV_HEIGHT = 22f;
        const float ELEMENT_PADDING = 2f;
        const float DELETE_BTN_WIDTH = 22f;

        // 프로퍼티 경로 → 상태 캐시 (여러 리스트 동시 지원)
        static readonly Dictionary<string, ListState> _states = new();

        class ListState
        {
            public int CurrentPage;
            public bool Foldout = true;
        }

        ListState GetState(SerializedProperty property)
        {
            var key = property.serializedObject.targetObject.GetInstanceID() + ":" + property.propertyPath;
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ListState();
                _states[key] = state;
            }
            return state;
        }

        StyledListAttribute Attr => (StyledListAttribute)attribute;

        // ─── 높이 계산 ──────────────────────────────────
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isArray)
                return EditorGUI.GetPropertyHeight(property, label, true);

            var state = GetState(property);
            float height = HEADER_HEIGHT + 4f; // 헤더 + 여백

            if (!state.Foldout)
                return height;

            int count = property.arraySize;
            if (count == 0)
            {
                height += 24f; // "비어있음" 메시지
            }
            else
            {
                int startIdx, endIdx;
                GetPageRange(count, state, out startIdx, out endIdx);

                for (int i = startIdx; i < endIdx; i++)
                {
                    var element = property.GetArrayElementAtIndex(i);
                    height += EditorGUI.GetPropertyHeight(element, true) + ELEMENT_PADDING * 2 + 1;
                }
            }

            // 페이지네이션 바
            if (Attr.PageSize > 0 && count > Attr.PageSize)
                height += NAV_HEIGHT + 4f;

            height += 4f; // 하단 여백

            return height;
        }

        // ─── GUI 렌더링 ─────────────────────────────────
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!property.isArray)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            var state = GetState(property);
            var attr = Attr;
            int count = property.arraySize;
            string title = attr.Title ?? property.displayName;

            var y = position.y;

            // ─── 헤더 ───────────────────────────────
            var headerRect = new Rect(position.x, y, position.width, HEADER_HEIGHT);
            DrawHeader(headerRect, property, title, count, attr.Color, state);
            y += HEADER_HEIGHT + 2f;

            if (!state.Foldout) return;

            // ─── 본문 ───────────────────────────────
            if (count == 0)
            {
                var emptyRect = new Rect(position.x, y, position.width, 22f);
                EditorGUI.DrawRect(emptyRect, BG_CARD);
                var emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    { normal = { textColor = new Color(0.5f, 0.5f, 0.55f) } };
                EditorGUI.LabelField(emptyRect, "리스트가 비어있습니다", emptyStyle);
                y += 24f;
            }
            else
            {
                int startIdx, endIdx;
                GetPageRange(count, state, out startIdx, out endIdx);

                for (int i = startIdx; i < endIdx; i++)
                {
                    var element = property.GetArrayElementAtIndex(i);
                    float elemHeight = EditorGUI.GetPropertyHeight(element, true);
                    float rowHeight = elemHeight + ELEMENT_PADDING * 2;

                    // 교차 배경
                    var rowRect = new Rect(position.x, y, position.width, rowHeight);
                    EditorGUI.DrawRect(rowRect, (i % 2 == 0) ? BG_CARD : BG_SECTION);

                    // 구분선
                    EditorGUI.DrawRect(new Rect(position.x, y + rowHeight, position.width, 1),
                        new Color(0.25f, 0.25f, 0.30f));

                    // 프로퍼티 (삭제 버튼 공간 확보)
                    var propRect = new Rect(
                        position.x + 4,
                        y + ELEMENT_PADDING,
                        position.width - DELETE_BTN_WIDTH - 10,
                        elemHeight);

                    var propLabel = attr.ShowIndex ? new GUIContent($"[{i}]") : GUIContent.none;
                    EditorGUI.PropertyField(propRect, element, propLabel, true);

                    // 삭제 버튼
                    var delRect = new Rect(
                        position.x + position.width - DELETE_BTN_WIDTH - 2,
                        y + ELEMENT_PADDING + 1,
                        DELETE_BTN_WIDTH,
                        18f);

                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = COL_DELETE;
                    if (GUI.Button(delRect, "×"))
                    {
                        property.DeleteArrayElementAtIndex(i);
                        property.serializedObject.ApplyModifiedProperties();
                        // 페이지 보정
                        ClampPage(property.arraySize, state);
                        return;
                    }
                    GUI.backgroundColor = prevBg;

                    y += rowHeight + 1;
                }
            }

            // ─── 페이지네이션 ────────────────────────
            if (attr.PageSize > 0 && count > attr.PageSize)
            {
                y += 2f;
                var navRect = new Rect(position.x, y, position.width, NAV_HEIGHT);
                DrawPagination(navRect, count, state);
            }
        }

        // ─── 헤더 그리기 ─────────────────────────────────
        void DrawHeader(Rect rect, SerializedProperty property, string title, int count, Color color, ListState state)
        {
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, BAR_WIDTH, rect.height), color);

            // 폴드아웃 삼각형
            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            EditorGUI.LabelField(
                new Rect(rect.x + BAR_WIDTH + 4, rect.y + 2, 16, 16),
                state.Foldout ? "\u25BC" : "\u25B6", triStyle);

            // 타이틀 + 카운트
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = color }
            };
            EditorGUI.LabelField(
                new Rect(rect.x + BAR_WIDTH + 18, rect.y, rect.width - 80, rect.height),
                $"{title} ({count}개)", titleStyle);

            // [+] 추가 버튼
            var addRect = new Rect(rect.x + rect.width - 26, rect.y + 3, 24, 18);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = COL_ADD;
            if (GUI.Button(addRect, "+"))
            {
                property.InsertArrayElementAtIndex(property.arraySize);
                property.serializedObject.ApplyModifiedProperties();
                state.Foldout = true;
            }
            GUI.backgroundColor = prevBg;

            // 클릭 → 폴드아웃 토글 (버튼 영역 제외)
            var clickRect = new Rect(rect.x, rect.y, rect.width - 30, rect.height);
            if (Event.current.type == EventType.MouseDown && clickRect.Contains(Event.current.mousePosition))
            {
                state.Foldout = !state.Foldout;
                Event.current.Use();
            }
        }

        // ─── 페이지네이션 그리기 ─────────────────────────
        void DrawPagination(Rect rect, int totalCount, ListState state)
        {
            int pageSize = Attr.PageSize;
            int totalPages = Mathf.CeilToInt((float)totalCount / pageSize);

            EditorGUI.DrawRect(rect, BG_HEADER);

            float btnWidth = 30f;
            float centerX = rect.x + rect.width / 2;

            // [◀] 이전
            var prevRect = new Rect(centerX - 60, rect.y + 2, btnWidth, 18);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = COL_NAV;
            if (GUI.Button(prevRect, "\u25C0") && state.CurrentPage > 0)
                state.CurrentPage--;

            // 페이지 표시
            var pageStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            var labelRect = new Rect(centerX - 25, rect.y, 50, rect.height);
            EditorGUI.LabelField(labelRect, $"{state.CurrentPage + 1} / {totalPages}", pageStyle);

            // [▶] 다음
            var nextRect = new Rect(centerX + 30, rect.y + 2, btnWidth, 18);
            if (GUI.Button(nextRect, "\u25B6") && state.CurrentPage < totalPages - 1)
                state.CurrentPage++;

            GUI.backgroundColor = prevBg;
        }

        // ─── 유틸 ────────────────────────────────────────
        void GetPageRange(int totalCount, ListState state, out int startIdx, out int endIdx)
        {
            int pageSize = Attr.PageSize;
            if (pageSize <= 0 || totalCount <= pageSize)
            {
                startIdx = 0;
                endIdx = totalCount;
                return;
            }

            ClampPage(totalCount, state);
            startIdx = state.CurrentPage * pageSize;
            endIdx = Mathf.Min(startIdx + pageSize, totalCount);
        }

        void ClampPage(int totalCount, ListState state)
        {
            int pageSize = Attr.PageSize;
            if (pageSize <= 0) return;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalCount / pageSize));
            state.CurrentPage = Mathf.Clamp(state.CurrentPage, 0, totalPages - 1);
        }
    }
}
#endif
