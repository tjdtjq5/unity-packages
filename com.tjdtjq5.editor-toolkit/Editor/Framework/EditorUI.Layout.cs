#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 레이아웃 헬퍼.</summary>
    public static partial class EditorUI
    {
        static readonly Dictionary<Color, GUIStyle> _bgStyles = new();

        // ── 레이아웃 ──

        public static void BeginBody()
        {
            GUILayout.Space(2);
            var style = GetBgStyle(BG_SECTION);
            style.margin = new RectOffset(4, 4, 0, 0);
            style.padding = new RectOffset(10, 10, 6, 6);
            EditorGUILayout.BeginVertical(style);
        }

        public static void EndBody()
        {
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        /// <summary>가로 행 시작.</summary>
        public static void BeginRow() => EditorGUILayout.BeginHorizontal();

        /// <summary>가로 행 끝.</summary>
        public static void EndRow() => EditorGUILayout.EndHorizontal();

        /// <summary>서브 박스 시작 (helpBox 스타일).</summary>
        public static void BeginSubBox() =>
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        /// <summary>서브 박스 끝.</summary>
        public static void EndSubBox() => EditorGUILayout.EndVertical();

        /// <summary>비활성 그룹 시작.</summary>
        public static void BeginDisabled(bool disabled) =>
            EditorGUI.BeginDisabledGroup(disabled);

        /// <summary>비활성 그룹 끝.</summary>
        public static void EndDisabled() => EditorGUI.EndDisabledGroup();

        /// <summary>가운데 정렬 행 시작.</summary>
        public static void BeginCenterRow()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
        }

        /// <summary>가운데 정렬 행 끝.</summary>
        public static void EndCenterRow()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>유연 공간.</summary>
        public static void FlexSpace() => GUILayout.FlexibleSpace();

        public static void DrawWindowBackground(Rect position)
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), BG_WINDOW);
        }

        public static GUIStyle GetBgStyle(Color bg)
        {
            if (_bgStyles.TryGetValue(bg, out var style) && style?.normal?.background != null)
                return style;

            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, bg);
            tex.Apply();
            style = new GUIStyle
            {
                normal  = { background = tex },
                padding = new RectOffset(6, 6, 2, 2),
                margin  = new RectOffset(0, 0, 0, 0)
            };
            _bgStyles[bg] = style;
            return style;
        }
    }
}
#endif
