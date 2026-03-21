#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 버튼 헬퍼.</summary>
    public static partial class EditorUI
    {
        public static bool DrawColorButton(string text, Color color, float height = 24)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(text, GUILayout.Height(height));
            GUI.backgroundColor = prev;
            return clicked;
        }

        public static bool DrawLinkButton(string text, Color? color = null)
        {
            var c = color ?? COL_LINK;
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(c.r, c.g, c.b, 0.3f);
            var content = new GUIContent($"{text} \u2197");
            bool clicked = GUILayout.Button(content, GUILayout.Height(22));
            GUI.backgroundColor = prev;
            return clicked;
        }

        public static bool DrawBackButton(string text = "\u2190 돌아가기")
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 12, normal = { textColor = COL_LINK }
            };

            EditorGUILayout.BeginHorizontal();
            var w = style.CalcSize(new GUIContent(text)).x + 16;
            bool clicked = GUILayout.Button(text, style, GUILayout.Width(w), GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            return clicked;
        }

        /// <summary>x 제거 버튼 (작은 닫기).</summary>
        public static bool DrawRemoveButton()
        {
            return GUILayout.Button("x", GUILayout.Width(20), GUILayout.Height(18));
        }

        /// <summary>미니 버튼.</summary>
        public static bool DrawMiniButton(string text)
        {
            return GUILayout.Button(text, EditorStyles.miniButton);
        }
    }
}
#endif
