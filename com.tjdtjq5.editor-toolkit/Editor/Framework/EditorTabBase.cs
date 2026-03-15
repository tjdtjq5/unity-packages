#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>
    /// 탭 기반 에디터 윈도우 공통 베이스.
    /// 색상, 폴드아웃, 카드, 알림, 테이블, 탭 바, 툴바, 리사이저블 컬럼 등.
    /// </summary>
    public abstract class EditorTabBase
    {
        // ─── Colors ─────────────────────────────────
        protected static readonly Color BG_WINDOW  = new(0.15f, 0.15f, 0.19f);
        protected static readonly Color BG_SECTION = new(0.19f, 0.19f, 0.23f);
        protected static readonly Color BG_CARD    = new(0.14f, 0.14f, 0.18f);
        protected static readonly Color BG_HEADER  = new(0.18f, 0.18f, 0.22f);

        protected static readonly Color COL_SUCCESS = new(0.30f, 0.80f, 0.40f);
        protected static readonly Color COL_WARN    = new(0.95f, 0.75f, 0.20f);
        protected static readonly Color COL_ERROR   = new(0.95f, 0.30f, 0.30f);
        protected static readonly Color COL_INFO    = new(0.40f, 0.70f, 0.95f);
        protected static readonly Color COL_MUTED   = new(0.45f, 0.45f, 0.50f);
        protected static readonly Color COL_LINK    = new(0.35f, 0.65f, 0.95f);

        readonly Dictionary<Color, GUIStyle> _bgStyles = new();

        // ─── 알림 상태 ──────────────────────────────
        protected string _notification;
        protected NotificationType _notificationType;

        protected enum NotificationType { Error, Success, Info }

        // ─── Abstract / Virtual ─────────────────────
        public abstract string TabName  { get; }
        public abstract Color  TabColor { get; }

        public abstract void OnDraw();
        public virtual  void OnUpdate()  { }
        public virtual  void OnEnable()  { }
        public virtual  void OnDisable() { }

        // ─── 알림 시스템 ────────────────────────────

        /// <summary>알림 표시 설정</summary>
        protected void ShowNotification(string message, NotificationType type)
        {
            _notification = message;
            _notificationType = type;
        }

        /// <summary>알림 박스 그리기 (Error=빨강, Success=초록, Info=파랑)</summary>
        protected void DrawNotifications()
        {
            if (string.IsNullOrEmpty(_notification)) return;

            Color bgColor, labelColor, textColor;
            string label;
            switch (_notificationType)
            {
                case NotificationType.Error:
                    bgColor = new Color(0.35f, 0.12f, 0.12f);
                    labelColor = COL_ERROR;
                    textColor = new Color(0.95f, 0.60f, 0.60f);
                    label = "Error";
                    break;
                case NotificationType.Success:
                    bgColor = new Color(0.12f, 0.28f, 0.14f);
                    labelColor = COL_SUCCESS;
                    textColor = new Color(0.60f, 0.90f, 0.65f);
                    label = "Success";
                    break;
                default:
                    bgColor = new Color(0.12f, 0.18f, 0.30f);
                    labelColor = COL_INFO;
                    textColor = new Color(0.70f, 0.85f, 0.95f);
                    label = "Info";
                    break;
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(GetBgStyle(bgColor));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = labelColor } }, GUILayout.Width(55));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(16)))
                EditorGUIUtility.systemCopyBuffer = _notification;
            if (GUILayout.Button("\u2715", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                _notification = null;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_notification))
                EditorGUILayout.LabelField(_notification, new GUIStyle(EditorStyles.wordWrappedLabel)
                    { normal = { textColor = textColor } });

            EditorGUILayout.EndVertical();
        }

        // ─── 로딩 ───────────────────────────────────

        /// <summary>로딩 스피너 표시</summary>
        protected void DrawLoading(bool isLoading, string message = "로딩 중...")
        {
            if (!isLoading) return;
            GUILayout.Space(8);
            EditorGUILayout.LabelField($"\u27F3 {message}", new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_INFO }
            });
            GUILayout.Space(8);
        }

        // ─── 테이블 유틸 ────────────────────────────

        /// <summary>테이블 헤더 라벨</summary>
        protected void DrawHeaderLabel(string text, float width = 0)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_MUTED },
                fontSize = 10
            };
            if (width > 0)
                EditorGUILayout.LabelField(text, style, GUILayout.Width(width));
            else
                EditorGUILayout.LabelField(text, style);
        }

        /// <summary>테이블 셀 라벨</summary>
        protected void DrawCellLabel(string text, float width = 0, Color? color = null)
        {
            var style = new GUIStyle(EditorStyles.label)
                { normal = { textColor = color ?? Color.white } };
            if (width > 0)
                EditorGUILayout.LabelField(text ?? "", style, GUILayout.Width(width));
            else
                EditorGUILayout.LabelField(text ?? "", style);
        }

        /// <summary>하이퍼링크 스타일 버튼</summary>
        protected bool DrawLinkButton(string text)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = COL_LINK },
                hover = { textColor = Color.white },
                alignment = TextAnchor.MiddleRight,
            };
            var content = new GUIContent($"  {text} \u2197");
            var rect = GUILayoutUtility.GetRect(content, style);

            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.yMax - 2, rect.width - 4, 1),
                new Color(COL_LINK.r, COL_LINK.g, COL_LINK.b, 0.4f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            return GUI.Button(rect, content, style);
        }

        // ─── 탭 바 ─────────────────────────────────

        int _tabEditIdx = -1;
        string _tabEditName;

        /// <summary>
        /// 스타일 탭 바. 반환값: 선택된 인덱스.
        /// onAdd: + 탭 표시, onRename: 더블클릭 이름 편집
        /// </summary>
        protected int DrawTabBar(string[] labels, int activeIdx, Color[] colors = null,
            Action onAdd = null, Action<int, string> onRename = null)
        {
            if (labels == null || labels.Length == 0)
            {
                if (onAdd != null)
                {
                    var r2 = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(r2, BG_HEADER);
                    DrawAddTab(new Rect(r2.x, r2.y, 30, r2.height), onAdd);
                }
                return 0;
            }
            if (activeIdx >= labels.Length) activeIdx = 0;

            bool hasAdd = onAdd != null;
            float addW = hasAdd ? 30f : 0f;
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);

            float tabW = (rect.width - addW) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var tr = new Rect(rect.x + tabW * i, rect.y, tabW, rect.height);
                bool active = activeIdx == i;
                Color c = colors != null && i < colors.Length ? colors[i] : TabColor;

                if (active)
                {
                    EditorGUI.DrawRect(tr, new Color(c.r, c.g, c.b, 0.15f));
                    EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 2, tr.width, 2), c);
                }

                if (_tabEditIdx == i && onRename != null)
                {
                    GUI.SetNextControlName($"tabEdit_{i}");
                    _tabEditName = EditorGUI.TextField(new Rect(tr.x + 2, tr.y + 3, tr.width - 4, tr.height - 6),
                        _tabEditName, new GUIStyle(EditorStyles.textField) { fontSize = 11, alignment = TextAnchor.MiddleCenter });

                    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                    { onRename.Invoke(i, _tabEditName); _tabEditIdx = -1; Event.current.Use(); }
                    else if (Event.current.type == EventType.MouseDown && !tr.Contains(Event.current.mousePosition))
                    { onRename.Invoke(i, _tabEditName); _tabEditIdx = -1; }
                }
                else
                {
                    var st = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 11, alignment = TextAnchor.MiddleCenter,
                        fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                        normal = { textColor = active ? c : COL_MUTED }
                    };
                    EditorGUI.LabelField(tr, labels[i], st);
                }

                if (Event.current.type == EventType.MouseDown && tr.Contains(Event.current.mousePosition))
                {
                    if (Event.current.clickCount == 2 && onRename != null)
                    { _tabEditIdx = i; _tabEditName = labels[i]; }
                    else
                    { activeIdx = i; if (_tabEditIdx != i) _tabEditIdx = -1; }
                    Event.current.Use();
                }
            }

            if (hasAdd)
                DrawAddTab(new Rect(rect.x + tabW * labels.Length, rect.y, addW, rect.height), onAdd);

            return activeIdx;
        }

        void DrawAddTab(Rect rect, Action onAdd)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            EditorGUI.LabelField(rect, "+", new GUIStyle(EditorStyles.miniLabel)
                { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = COL_MUTED } });
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            { onAdd.Invoke(); Event.current.Use(); }
        }

        // ─── 툴바 ──────────────────────────────────

        /// <summary>액션 버튼 툴바 (왼쪽: 버튼들, 오른쪽: 텍스트)</summary>
        protected void DrawActionBar(
            (string label, Color color, Action action)[] buttons,
            string rightText = null)
        {
            EditorGUILayout.BeginHorizontal();

            if (buttons != null)
            {
                foreach (var (label, color, action) in buttons)
                {
                    if (DrawColorBtn(label, color, 22))
                        action?.Invoke();
                }
            }

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(rightText))
                EditorGUILayout.LabelField(rightText, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleRight }, GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }

        // ─── 기존 유틸 ──────────────────────────────

        protected bool DrawSectionFoldout(ref bool foldout, string title, Color color)
        {
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), color);

            var triStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            EditorGUI.LabelField(new Rect(rect.x + 10, rect.y + 3, 16, 16), foldout ? "\u25BC" : "\u25B6", triStyle);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 12, normal = { textColor = color } };
            EditorGUI.LabelField(new Rect(rect.x + 26, rect.y, rect.width - 26, rect.height), title, titleStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            { foldout = !foldout; Event.current.Use(); }
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
            EditorGUILayout.LabelField(label, new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                { normal = { textColor = new Color(0.55f, 0.55f, 0.62f) } });
            EditorGUILayout.LabelField(value, new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = valueColor } });
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        protected void DrawSubLabel(string text)
        {
            EditorGUILayout.LabelField($"\u2500\u2500  {text}  \u2500\u2500",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.50f, 0.50f, 0.56f) } });
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

        // ─── 리사이저블 컬럼 ────────────────────────

        /// <summary>컬럼 정의</summary>
        protected struct ColumnDef
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

        /// <summary>
        /// Rect 기반 리사이저블 컬럼 헤더. EditorPrefs 자동 저장/복원.
        /// width=0 컬럼은 나머지 공간 사용 (flex).
        /// </summary>
        protected class ResizableColumns
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
                EditorGUI.DrawRect(r, new Color(0.18f, 0.18f, 0.22f));

                var st = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.45f, 0.45f, 0.50f) }, fontSize = 10
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
