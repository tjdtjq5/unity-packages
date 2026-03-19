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
    ///
    /// 정적 유틸 메서드는 EditorTabBase를 상속하지 않는 코드에서도 직접 호출 가능:
    ///   EditorTabBase.GetBgStyle(color)
    ///   EditorTabBase.DrawHeaderLabel("이름", 100)
    ///   EditorTabBase.DrawColorBtn("실행", EditorTabBase.COL_SUCCESS)
    /// </summary>
    public abstract class EditorTabBase
    {
        // ─── Colors (public static) ───────────────────
        public static readonly Color BG_WINDOW  = new(0.18f, 0.18f, 0.20f);
        public static readonly Color BG_SECTION = new(0.14f, 0.14f, 0.16f);
        public static readonly Color BG_CARD    = new(0.12f, 0.12f, 0.14f);
        public static readonly Color BG_HEADER  = new(0.11f, 0.11f, 0.13f);

        public static readonly Color COL_SUCCESS = new(0.30f, 0.80f, 0.40f);
        public static readonly Color COL_WARN    = new(0.95f, 0.75f, 0.20f);
        public static readonly Color COL_ERROR   = new(0.95f, 0.30f, 0.30f);
        public static readonly Color COL_INFO    = new(0.40f, 0.70f, 0.95f);
        public static readonly Color COL_MUTED   = new(0.45f, 0.45f, 0.50f);
        public static readonly Color COL_LINK    = new(0.35f, 0.65f, 0.95f);

        static readonly Dictionary<Color, GUIStyle> _bgStyles = new();

        // ─── 알림 상태 ──────────────────────────────
        protected string _notification;
        protected NotificationType _notificationType;

        public enum NotificationType { Error, Success, Info }

        // ─── Abstract / Virtual ─────────────────────
        public abstract string TabName  { get; }
        public abstract Color  TabColor { get; }

        public abstract void OnDraw();
        public virtual  void OnUpdate()  { }
        public virtual  void OnEnable()  { }
        public virtual  void OnDisable() { }

        // ─── 알림 시스템 (instance) ──────────────────

        public virtual void ShowNotification(string message, NotificationType type)
        {
            _notification = message;
            _notificationType = type;
        }

        public virtual void DrawNotifications()
        {
            if (string.IsNullOrEmpty(_notification)) return;
            DrawNotificationBar(ref _notification, _notificationType);
        }

        // ─── 알림 시스템 (static) ────────────────────

        public static void DrawNotificationBar(ref string notification, NotificationType type)
        {
            if (string.IsNullOrEmpty(notification)) return;

            Color bgColor, labelColor, textColor;
            string label;
            switch (type)
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
                EditorGUIUtility.systemCopyBuffer = notification;
            if (GUILayout.Button("\u2715", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                notification = null;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(notification))
                EditorGUILayout.LabelField(notification, new GUIStyle(EditorStyles.wordWrappedLabel)
                    { normal = { textColor = textColor } });

            EditorGUILayout.EndVertical();
        }

        // ─── 로딩 (static) ──────────────────────────

        public static void DrawLoading(bool isLoading, string message = "로딩 중...")
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

        // ─── 테이블 유틸 (static) ───────────────────

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

        public static bool DrawLinkButton(string text)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = COL_LINK },
                hover = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                stretchWidth = false,
            };
            var content = new GUIContent($"{text} \u2197");
            float w = style.CalcSize(content).x + 8;
            var rect = GUILayoutUtility.GetRect(w, 18);

            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, w - 4, 1),
                new Color(COL_LINK.r, COL_LINK.g, COL_LINK.b, 0.4f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            return GUI.Button(rect, content, style);
        }

        public static bool DrawLinkBtn(string text, Color? color = null)
        {
            var c = color ?? COL_LINK;
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(c.r, c.g, c.b, 0.3f);
            var content = new GUIContent($"{text} \u2197");
            bool clicked = GUILayout.Button(content, GUILayout.Height(22));
            GUI.backgroundColor = prev;
            return clicked;
        }

        // ─── 탭 바 (static) ─────────────────────────

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

        // ─── 탭 바 (instance — 기존 호환) ───────────

        int _tabEditIdx = -1;
        string _tabEditName;

        public virtual int DrawTabBar(string[] labels, int activeIdx, Color[] colors = null,
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

        static void DrawAddTab(Rect rect, Action onAdd)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            EditorGUI.LabelField(rect, "+", new GUIStyle(EditorStyles.miniLabel)
                { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = COL_MUTED } });
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            { onAdd.Invoke(); Event.current.Use(); }
        }

        // ─── 툴바 (static) ──────────────────────────

        public static void DrawActionBar(
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

        // ─── 섹션/카드/유틸 (static) ────────────────

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
                var dotColors = new[] { COL_MUTED, COL_SUCCESS, COL_ERROR };
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
            EditorGUI.LabelField(gearRect, "\u2699", gearStyle);
            EditorGUIUtility.AddCursorRect(gearRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && gearRect.Contains(Event.current.mousePosition))
            {
                gearClicked = true;
                Event.current.Use();
            }

            return gearClicked;
        }

        public static bool DrawBackButton(string text = "\u2190 돌아가기")
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 12, normal = { textColor = COL_LINK }
            };

            EditorGUILayout.BeginHorizontal();
            bool clicked = GUILayout.Button(text, style, GUILayout.Width(100), GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            return clicked;
        }

        public static void DrawWindowBackground(Rect position)
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), BG_WINDOW);
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

        public static void DrawInfoBox(string[] benefits, string[] drawbacks)
        {
            EditorGUILayout.BeginVertical(GetBgStyle(BG_CARD));
            GUILayout.Space(4);

            var bStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = COL_SUCCESS }, fontSize = 11 };
            var dStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = COL_MUTED }, fontSize = 11 };
            var hStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };

            hStyle.normal.textColor = COL_SUCCESS;
            GUILayout.Label("설정하면?", hStyle);
            foreach (var b in benefits)
                GUILayout.Label($"  \u2713 {b}", bStyle);

            GUILayout.Space(4);
            hStyle.normal.textColor = COL_MUTED;
            GUILayout.Label("안 하면?", hStyle);
            foreach (var d in drawbacks)
                GUILayout.Label($"  \u00B7 {d}", dStyle);

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        public static void DrawToolStatus(string name, bool installed, string version,
            bool loggedIn = false, string account = null)
        {
            EditorGUILayout.BeginHorizontal();

            if (installed)
            {
                var col = loggedIn ? COL_SUCCESS : COL_WARN;
                var verText = !string.IsNullOrEmpty(version) ? $" ({version})" : "";
                var accountText = !string.IsNullOrEmpty(account) ? $" \u2014 {account}" : "";
                var loginText = loggedIn ? $"\u2713 로그인됨{accountText}" : "\u2717 로그인 안 됨";

                var s = new GUIStyle(EditorStyles.label) { normal = { textColor = col }, fontSize = 11 };
                GUILayout.Label($"  \u2713 {name} 설치됨{verText}  |  {loginText}", s);
            }
            else
            {
                var s = new GUIStyle(EditorStyles.label) { normal = { textColor = COL_ERROR }, fontSize = 11 };
                GUILayout.Label($"  \u2717 {name} 미설치", s);
            }

            EditorGUILayout.EndHorizontal();
        }

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

        public static void DrawStatCard(string label, string value, Color valueColor)
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

        public static void DrawSubLabel(string text)
        {
            EditorGUILayout.LabelField($"\u2500\u2500  {text}  \u2500\u2500",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.50f, 0.50f, 0.56f) } });
        }

        public static bool DrawColorBtn(string text, Color color, float height = 24)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(text, GUILayout.Height(height));
            GUI.backgroundColor = prev;
            return clicked;
        }

        public static void DrawPlaceholder(string text)
        {
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 };
            style.normal.textColor = COL_MUTED;
            GUILayout.Label(text, style);
            GUILayout.FlexibleSpace();
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

        // ─── 리사이저블 컬럼 ────────────────────────

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
