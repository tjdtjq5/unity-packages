#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 테이블/리사이저블 컬럼 헬퍼.</summary>
    public static partial class EditorUI
    {
        public static void DrawHeaderLabel(string text, float width = 0,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = alignment,
                normal = { textColor = COL_MUTED },
                fontSize = 10
            };
            if (width > 0)
                EditorGUILayout.LabelField(text, style, GUILayout.Width(width));
            else
                EditorGUILayout.LabelField(text, style);
        }

        public struct ColumnDef
        {
            public string Name;
            public float DefaultWidth;
            public bool Resizable;
            public float MinWidth;

            public ColumnDef(string name, float defaultWidth = 0f, bool resizable = false, float minWidth = 40f)
            {
                Name = name; DefaultWidth = defaultWidth; Resizable = resizable; MinWidth = minWidth;
            }
        }

        public class ResizableColumns
        {
            readonly string _prefsPrefix;
            readonly ColumnDef[] _defs;
            readonly float[] _widths;
            readonly bool[] _dragging;
            readonly Action _onRepaint;

            const float DRAG_W = 6f;
            static readonly Color COL_DIV = new(0.35f, 0.35f, 0.40f);

            public int Count => _defs.Length;

            public ResizableColumns(string prefsPrefix, ColumnDef[] defs, Action onRepaint = null)
            {
                _prefsPrefix = prefsPrefix;
                _defs = defs;
                _widths = new float[defs.Length];
                _dragging = new bool[defs.Length];
                _onRepaint = onRepaint;
                LoadWidths();
            }

            public float GetWidth(int index) => _widths[index];

            public void DrawHeader()
            {
                var r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, BG_HEADER);

                var st = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
                    normal = { textColor = COL_MUTED }, fontSize = 10
                };

                float fixedTotal = 0f;
                int flexIdx = -1;
                for (int i = 0; i < _defs.Length; i++)
                {
                    if (_defs[i].DefaultWidth <= 0) flexIdx = i;
                    else fixedTotal += _widths[i];
                }
                if (flexIdx >= 0)
                    _widths[flexIdx] = Mathf.Max(r.width - fixedTotal, 40f);

                float x = r.x;
                for (int i = 0; i < _defs.Length; i++)
                {
                    float w = _widths[i];
                    EditorGUI.LabelField(new Rect(x, r.y, w, r.height), _defs[i].Name, st);
                    x += w;

                    if (i < _defs.Length - 1)
                    {
                        EditorGUI.DrawRect(new Rect(x - 1, r.y, 1, r.height), COL_DIV);
                        var dragRect = new Rect(x - DRAG_W / 2, r.y, DRAG_W, r.height);

                        if (_defs[i].Resizable && (flexIdx < 0 || i < flexIdx))
                            HandleDrag(dragRect, i, false);
                        else if (flexIdx >= 0 && i == flexIdx && i + 1 < _defs.Length && _defs[i + 1].Resizable)
                            HandleDrag(dragRect, i + 1, true);
                    }
                }

                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), COL_DIV);
            }

            void HandleDrag(Rect handle, int colIndex, bool invertDelta)
            {
                EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);
                var evt = Event.current;

                if (evt.type == EventType.MouseDown && handle.Contains(evt.mousePosition))
                { _dragging[colIndex] = true; evt.Use(); }
                else if (evt.type == EventType.MouseUp && _dragging[colIndex])
                { _dragging[colIndex] = false; SaveWidths(); evt.Use(); }
                else if (evt.type == EventType.MouseDrag && _dragging[colIndex])
                {
                    float delta = invertDelta ? -evt.delta.x : evt.delta.x;
                    _widths[colIndex] = Mathf.Max(_widths[colIndex] + delta, _defs[colIndex].MinWidth);
                    evt.Use();
                    _onRepaint?.Invoke();
                }
            }

            void SaveWidths()
            {
                for (int i = 0; i < _defs.Length; i++)
                    if (_defs[i].Resizable)
                        EditorPrefs.SetFloat($"{_prefsPrefix}_Col{i}", _widths[i]);
            }

            void LoadWidths()
            {
                for (int i = 0; i < _defs.Length; i++)
                    _widths[i] = _defs[i].Resizable
                        ? EditorPrefs.GetFloat($"{_prefsPrefix}_Col{i}", _defs[i].DefaultWidth)
                        : _defs[i].DefaultWidth;
            }
        }
    }
}
#endif
