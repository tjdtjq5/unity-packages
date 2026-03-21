#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>
    /// 탭 기반 에디터 윈도우 공통 베이스.
    /// 정적 유틸 메서드는 EditorUI로 이동됨.
    /// 하위 호환을 위해 모든 정적 멤버를 EditorUI로 포워딩.
    /// </summary>
    public abstract class EditorTabBase
    {
        // ═══════════════════════════════════════════════════════
        // ─── 하위 호환: 색상 포워딩
        // ═══════════════════════════════════════════════════════

        public static readonly Color BG_WINDOW  = EditorUI.BG_WINDOW;
        public static readonly Color BG_SECTION = EditorUI.BG_SECTION;
        public static readonly Color BG_CARD    = EditorUI.BG_CARD;
        public static readonly Color BG_HEADER  = EditorUI.BG_HEADER;

        public static readonly Color COL_SUCCESS = EditorUI.COL_SUCCESS;
        public static readonly Color COL_WARN    = EditorUI.COL_WARN;
        public static readonly Color COL_ERROR   = EditorUI.COL_ERROR;
        public static readonly Color COL_INFO    = EditorUI.COL_INFO;
        public static readonly Color COL_MUTED   = EditorUI.COL_MUTED;
        public static readonly Color COL_LINK    = EditorUI.COL_LINK;

        // ═══════════════════════════════════════════════════════
        // ─── 알림 타입 (EditorUI와 동일, 하위 호환용)
        // ═══════════════════════════════════════════════════════

        public enum NotificationType { Error, Success, Info }

        // ═══════════════════════════════════════════════════════
        // ─── 인스턴스 멤버
        // ═══════════════════════════════════════════════════════

        protected string _notification;
        protected NotificationType _notificationType;

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
            var editorUiType = (EditorUI.NotificationType)(int)_notificationType;
            EditorUI.DrawNotificationBar(ref _notification, editorUiType);
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

        // ═══════════════════════════════════════════════════════
        // ─── 하위 호환: 정적 메서드 포워딩
        // ═══════════════════════════════════════════════════════

        // ── Feedback ──

        public static void DrawNotificationBar(ref string notification, NotificationType type)
            => EditorUI.DrawNotificationBar(ref notification, (EditorUI.NotificationType)(int)type);

        public static void DrawLoading(bool isLoading, string message = "로딩 중...")
            => EditorUI.DrawLoading(isLoading, message);

        public static Vector2 DrawLogArea(string text, Vector2 scrollPos,
            float maxHeight = 200, Color? textColor = null)
            => EditorUI.DrawLogArea(text, scrollPos, maxHeight, textColor);

        // ── Layout ──

        public static void BeginBody() => EditorUI.BeginBody();
        public static void EndBody() => EditorUI.EndBody();
        public static void BeginRow() => EditorUI.BeginRow();
        public static void EndRow() => EditorUI.EndRow();
        public static void BeginSubBox() => EditorUI.BeginSubBox();
        public static void EndSubBox() => EditorUI.EndSubBox();
        public static void BeginDisabled(bool disabled) => EditorUI.BeginDisabled(disabled);
        public static void EndDisabled() => EditorUI.EndDisabled();

        public static void BeginCenterRow() => EditorUI.BeginCenterRow();
        public static void EndCenterRow() => EditorUI.EndCenterRow();
        public static void FlexSpace() => EditorUI.FlexSpace();

        public static void DrawWindowBackground(Rect position)
            => EditorUI.DrawWindowBackground(position);

        public static GUIStyle GetBgStyle(Color bg) => EditorUI.GetBgStyle(bg);

        // ── Text ──

        public static void DrawCellLabel(string text, float width = 0, Color? color = null,
            TextAnchor alignment = TextAnchor.MiddleLeft)
            => EditorUI.DrawCellLabel(text, width, color, alignment);

        public static void DrawDescription(string text, Color? color = null)
            => EditorUI.DrawDescription(text, color);

        public static void DrawSubLabel(string text) => EditorUI.DrawSubLabel(text);
        public static void DrawPlaceholder(string text) => EditorUI.DrawPlaceholder(text);

        // ── Button ──

        public static bool DrawColorBtn(string text, Color color, float height = 24)
            => EditorUI.DrawColorButton(text, color, height);

        public static bool DrawLinkButton(string text)
            => EditorUI.DrawLinkButton(text);

        public static bool DrawLinkBtn(string text, Color? color = null)
            => EditorUI.DrawLinkButton(text, color);

        public static bool DrawBackButton(string text = "\u2190 돌아가기")
            => EditorUI.DrawBackButton(text);

        public static bool DrawRemoveButton() => EditorUI.DrawRemoveButton();
        public static bool DrawMiniButton(string text) => EditorUI.DrawMiniButton(text);

        // ── Input ──

        public static string DrawTextField(string label, string value, string tooltip = null)
            => EditorUI.DrawTextField(label, value, tooltip);

        public static string DrawPasswordField(string label, string value, string tooltip = null)
            => EditorUI.DrawPasswordField(label, value, tooltip);

        public static int DrawPopup(string label, int selectedIndex, string[] options, string tooltip = null)
            => EditorUI.DrawPopup(label, selectedIndex, options, tooltip);

        public static void DrawProperty(SerializedObject so, string propertyName,
            string label = null, string tooltip = null)
            => EditorUI.DrawProperty(so, propertyName, label, tooltip);

        // ── Card ──

        public static bool BeginServiceCard(string name, Color accentColor, string status,
            int statusState, string summaryLine, ref bool expanded)
            => EditorUI.BeginServiceCard(name, accentColor, status, statusState, summaryLine, ref expanded);

        public static void EndServiceCard(ref bool expanded)
            => EditorUI.EndServiceCard(ref expanded);

        public static void DrawInfoBox(string[] benefits, string[] drawbacks)
            => EditorUI.DrawInfoBox(benefits, drawbacks);

        public static void DrawStatCard(string label, string value, Color valueColor)
            => EditorUI.DrawStatCard(label, value, valueColor);

        public static void DrawToolStatus(string name, bool installed, string version,
            bool loggedIn = false, string account = null)
            => EditorUI.DrawToolStatus(name, installed, version, loggedIn, account);

        // ── Nav ──

        public static int DrawTabBar(string[] labels, int activeIdx, Color[] colors,
            Color defaultColor)
            => EditorUI.DrawTabBar(labels, activeIdx, colors, defaultColor);

        public static void DrawStatusBar((string name, int state)[] items)
            => EditorUI.DrawStatusBar(items);

        public static void DrawStepIndicator(string[] labels, int[] stepStates)
            => EditorUI.DrawStepIndicator(labels, stepStates);

        public static void DrawActionBar(
            (string label, Color color, Action action)[] buttons,
            string rightText = null)
            => EditorUI.DrawActionBar(buttons, rightText);

        public static void DrawSectionHeader(string title, Color color)
            => EditorUI.DrawSectionHeader(title, color);

        public static bool DrawSectionFoldout(ref bool foldout, string title, Color color)
            => EditorUI.DrawSectionFoldout(ref foldout, title, color);

        public static bool DrawToggleRow(string label, bool expanded, Color? color = null)
            => EditorUI.DrawToggleRow(label, expanded, color);

        // ── Window ──

        public static void DrawWindowHeader(string title, string version, Color accentColor)
            => EditorUI.DrawWindowHeader(title, version, accentColor);

        public static bool DrawWindowHeaderWithGear(string title, string version, Color accentColor,
            (string name, int state)[] badges = null)
            => EditorUI.DrawWindowHeaderWithGear(title, version, accentColor, badges);

        // ── Table ──

        public static void DrawHeaderLabel(string text, float width = 0,
            TextAnchor alignment = TextAnchor.MiddleCenter)
            => EditorUI.DrawHeaderLabel(text, width, alignment);

        // ColumnDef 및 ResizableColumns는 EditorUI에 정의됨.
        // 기존 코드에서 EditorTabBase.ColumnDef 등으로 접근하는 경우를 위해 타입 별칭은 C#에서 지원하지 않으므로,
        // 기존 사용처에서 EditorUI.ColumnDef / EditorUI.ResizableColumns로 마이그레이션 필요.
    }
}
#endif
