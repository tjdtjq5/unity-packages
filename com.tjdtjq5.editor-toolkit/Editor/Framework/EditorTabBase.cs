#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>
    /// TableTabBase / TestTabBase 공통 베이스.
    /// 색상 상수 + 섹션 폴드아웃 + 카드 드로잉 유틸.
    /// </summary>
    public abstract class EditorTabBase
    {
        // ─── Colors (공통) ───────────────────────────────
        protected static readonly Color BG_WINDOW  = new(0.15f, 0.15f, 0.19f);
        protected static readonly Color BG_SECTION = new(0.19f, 0.19f, 0.23f);
        protected static readonly Color BG_CARD    = new(0.14f, 0.14f, 0.18f);
        protected static readonly Color BG_HEADER  = new(0.11f, 0.11f, 0.14f);

        readonly Dictionary<Color, GUIStyle> _bgStyles = new();

        // ─── Abstract / Virtual ─────────────────────────
        public abstract string TabName  { get; }
        public abstract Color  TabColor { get; }

        public abstract void OnDraw();
        public virtual  void OnUpdate()  { }
        public virtual  void OnEnable()  { }
        public virtual  void OnDisable() { }

        // ─── Drawing Helpers ────────────────────────────

        protected bool DrawSectionFoldout(ref bool foldout, string title, Color color)
        {
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), color);

            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            EditorGUI.LabelField(new Rect(rect.x + 10, rect.y + 3, 16, 16), foldout ? "\u25BC" : "\u25B6", triStyle);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal   = { textColor = color }
            };
            EditorGUI.LabelField(new Rect(rect.x + 26, rect.y, rect.width - 26, rect.height), title, titleStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }
            return foldout;
        }

        protected void BeginBody()
        {
            EditorGUILayout.BeginVertical(GetBgStyle(BG_SECTION));
            GUILayout.Space(4);
        }

        protected void EndBody()
        {
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        protected void DrawStatCard(string label, string value, Color valueColor)
        {
            EditorGUILayout.BeginVertical(GetBgStyle(BG_CARD), GUILayout.MinHeight(44));
            GUILayout.Space(2);

            var lblStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                { normal = { textColor = new Color(0.55f, 0.55f, 0.62f) } };
            EditorGUILayout.LabelField(label, lblStyle);

            var valStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 15,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = valueColor }
            };
            EditorGUILayout.LabelField(value, valStyle);

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        protected void DrawSubLabel(string text)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.50f, 0.50f, 0.56f) } };
            EditorGUILayout.LabelField($"\u2500\u2500  {text}  \u2500\u2500", style);
        }

        protected bool DrawColorBtn(string text, Color color, float height = 24)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(text, GUILayout.Height(height));
            GUI.backgroundColor = prev;
            return clicked;
        }

        protected GUIStyle GetBgStyle(Color bg)
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
