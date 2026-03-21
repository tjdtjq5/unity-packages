#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 네비게이션/탭/섹션 헬퍼.</summary>
    public static partial class EditorUI
    {
        public static int DrawTabBar(string[] labels, int activeIdx, Color[] colors,
            Color defaultColor)
        {
            if (labels == null || labels.Length == 0) return 0;
            if (activeIdx >= labels.Length) activeIdx = 0;

            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);

            float tabW = rect.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var tr = new Rect(rect.x + tabW * i, rect.y, tabW, rect.height);
                bool active = activeIdx == i;
                Color c = colors != null && i < colors.Length ? colors[i] : defaultColor;

                if (active)
                {
                    EditorGUI.DrawRect(tr, new Color(c.r, c.g, c.b, 0.15f));
                    EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 2, tr.width, 2), c);
                }

                var st = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 11, alignment = TextAnchor.MiddleCenter,
                    fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                    normal = { textColor = active ? c : COL_MUTED }
                };
                EditorGUI.LabelField(tr, labels[i], st);

                if (Event.current.type == EventType.MouseDown && tr.Contains(Event.current.mousePosition))
                {
                    activeIdx = i;
                    Event.current.Use();
                }
            }

            return activeIdx;
        }

        /// <summary>상태 요약 바. states: 0=회색(미설정), 1=초록(설정됨), 2=노랑(일부)</summary>
        public static void DrawStatusBar((string name, int state)[] items)
        {
            EditorGUILayout.BeginHorizontal(GetBgStyle(BG_HEADER));
            GUILayout.Space(8);
            var dotColors = new[] { COL_MUTED, COL_SUCCESS, COL_WARN };
            foreach (var (name, state) in items)
            {
                var col = dotColors[Mathf.Clamp(state, 0, 2)];
                var style = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
                style.normal.textColor = col;
                GUILayout.Label($"\u25CF {name}", style, GUILayout.Height(20));
                GUILayout.Space(12);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        public static void DrawStepIndicator(string[] labels, int[] stepStates)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int i = 0; i < labels.Length; i++)
            {
                Color col;
                string dot;
                switch (stepStates[i])
                {
                    case 2: col = COL_SUCCESS; dot = "\u2713"; break;
                    case 3: col = COL_WARN;    dot = "\u25B3"; break;
                    case 1: col = COL_INFO;    dot = "\u25CF"; break;
                    default: col = COL_MUTED;  dot = "\u25CB"; break;
                }

                var style = new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleCenter, normal = { textColor = col } };
                GUILayout.Label($"{dot}\n{labels[i]}", style, GUILayout.Width(70), GUILayout.Height(30));

                if (i < labels.Length - 1)
                {
                    var lineStyle = new GUIStyle(EditorStyles.miniLabel)
                        { alignment = TextAnchor.MiddleCenter, normal = { textColor = COL_MUTED } };
                    GUILayout.Label("\u2501\u2501\u2501", lineStyle, GUILayout.Width(30), GUILayout.Height(30));
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        public static void DrawActionBar(
            (string label, Color color, Action action)[] buttons,
            string rightText = null)
        {
            EditorGUILayout.BeginHorizontal();

            if (buttons != null)
            {
                foreach (var (label, color, action) in buttons)
                {
                    if (DrawColorButton(label, color, 22))
                        action?.Invoke();
                }
            }

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(rightText))
                EditorGUILayout.LabelField(rightText, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleRight }, GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }

        public static void DrawSectionHeader(string title, Color color)
        {
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1),
                new Color(color.r, color.g, color.b, 0.3f));
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 12, normal = { textColor = color } };
            EditorGUI.LabelField(new Rect(rect.x + 12, rect.y, rect.width - 12, rect.height), title, titleStyle);
        }

        public static bool DrawSectionFoldout(ref bool foldout, string title, Color color)
        {
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1),
                new Color(color.r, color.g, color.b, 0.3f));

            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            EditorGUI.LabelField(new Rect(rect.x + 12, rect.y + 4, 16, 16), foldout ? "\u25BC" : "\u25B6", triStyle);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 12, normal = { textColor = color } };
            EditorGUI.LabelField(new Rect(rect.x + 28, rect.y, rect.width - 28, rect.height), title, titleStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            { foldout = !foldout; Event.current.Use(); }
            return foldout;
        }

        /// <summary>접기/펼치기 토글 행. 반환: 클릭됨 여부.</summary>
        public static bool DrawToggleRow(string label, bool expanded, Color? color = null)
        {
            var arrow = expanded ? "v " : "> ";
            var style = new GUIStyle(EditorStyles.label)
            {
                richText = false,
                normal = { textColor = color ?? COL_INFO }
            };
            return GUILayout.Button($"  {arrow}{label}", style);
        }
    }
}
#endif
