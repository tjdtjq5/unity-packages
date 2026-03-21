#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 윈도우 헤더 헬퍼.</summary>
    public static partial class EditorUI
    {
        public static void DrawWindowHeader(string title, string version, Color accentColor)
        {
            var rect = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), accentColor);

            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, fixedHeight = 30 };
            style.normal.textColor = Color.white;
            EditorGUI.LabelField(new Rect(rect.x + 10, rect.y + 2, rect.width - 80, rect.height), title, style);

            if (!string.IsNullOrEmpty(version))
            {
                var verStyle = new GUIStyle(EditorStyles.miniLabel);
                verStyle.normal.textColor = COL_MUTED;
                EditorGUI.LabelField(new Rect(rect.xMax - 60, rect.y + 2, 50, rect.height), version, verStyle);
            }
        }

        public static bool DrawWindowHeaderWithGear(string title, string version, Color accentColor,
            (string name, int state)[] badges = null)
        {
            var rect = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), accentColor);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, fixedHeight = 30 };
            titleStyle.normal.textColor = Color.white;
            EditorGUI.LabelField(new Rect(rect.x + 10, rect.y + 2, 120, rect.height), title, titleStyle);

            if (badges != null)
            {
                float bx = rect.x + 130;
                var dotColors = new[] { COL_MUTED, COL_SUCCESS, COL_WARN };
                var badgeStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };

                foreach (var (name, state) in badges)
                {
                    var col = dotColors[Mathf.Clamp(state, 0, 2)];
                    badgeStyle.normal.textColor = col;
                    var content = new GUIContent($"\u25CF {name}");
                    float w = badgeStyle.CalcSize(content).x + 4;
                    EditorGUI.LabelField(new Rect(bx, rect.y + 9, w, 16), content, badgeStyle);
                    bx += w + 4;
                }
            }

            if (!string.IsNullOrEmpty(version))
            {
                var verStyle = new GUIStyle(EditorStyles.miniLabel);
                verStyle.normal.textColor = COL_MUTED;
                EditorGUI.LabelField(new Rect(rect.xMax - 80, rect.y + 2, 36, rect.height), version, verStyle);
            }

            bool gearClicked = false;
            var gearRect = new Rect(rect.xMax - 34, rect.y + 7, 20, 20);
            var gearStyle = new GUIStyle(EditorStyles.miniLabel)
                { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            gearStyle.normal.textColor = COL_MUTED;
            var gearIcon = EditorGUIUtility.IconContent("_Popup");
            GUI.Label(gearRect, gearIcon);
            EditorGUIUtility.AddCursorRect(gearRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && gearRect.Contains(Event.current.mousePosition))
            {
                gearClicked = true;
                Event.current.Use();
            }

            return gearClicked;
        }
    }
}
#endif
