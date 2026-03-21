#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 텍스트/라벨 헬퍼.</summary>
    public static partial class EditorUI
    {
        public static void DrawCellLabel(string text, float width = 0, Color? color = null,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var style = new GUIStyle(EditorStyles.label)
                { normal = { textColor = color ?? Color.white }, alignment = alignment };
            if (width > 0)
                EditorGUILayout.LabelField(text ?? "", style, GUILayout.Width(width));
            else
                EditorGUILayout.LabelField(text ?? "", style);
        }

        public static void DrawDescription(string text, Color? color = null)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                normal = { textColor = color ?? new Color(0.72f, 0.72f, 0.78f) },
                padding = new RectOffset(4, 4, 2, 2)
            };
            EditorGUILayout.LabelField(text, style, GUILayout.ExpandWidth(true));
        }

        public static void DrawSubLabel(string text)
        {
            EditorGUILayout.LabelField($"\u2500\u2500  {text}  \u2500\u2500",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.50f, 0.50f, 0.56f) } });
        }

        public static void DrawPlaceholder(string text)
        {
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 };
            style.normal.textColor = COL_MUTED;
            GUILayout.Label(text, style);
            GUILayout.FlexibleSpace();
        }
    }
}
#endif
