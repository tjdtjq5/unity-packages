#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 카드/정보 박스 헬퍼.</summary>
    public static partial class EditorUI
    {
        /// <summary>서비스 카드 시작. 헤더 클릭으로 펼침/접기. 반환: expanded.</summary>
        public static bool BeginServiceCard(string name, Color accentColor, string status,
            int statusState, string summaryLine, ref bool expanded)
        {
            var dotColors = new[] { COL_MUTED, COL_SUCCESS, COL_WARN };
            var statusColor = dotColors[Mathf.Clamp(statusState, 0, 2)];

            GUILayout.Space(2);
            var cardStyle = GetBgStyle(BG_SECTION);
            cardStyle.margin = new RectOffset(4, 4, 0, 0);
            cardStyle.padding = new RectOffset(10, 10, 6, 6);
            EditorGUILayout.BeginVertical(cardStyle);

            // 헤더
            var headerRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(headerRect.x - 10, headerRect.y - 6, 3, headerRect.height + 12), accentColor);

            var nameStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            nameStyle.normal.textColor = accentColor;
            EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, 150, headerRect.height), name, nameStyle);

            var stStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            stStyle.normal.textColor = statusColor;
            EditorGUI.LabelField(new Rect(headerRect.xMax - 150, headerRect.y, 150, headerRect.height),
                $"\u25CF {status}", stStyle);

            EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                expanded = !expanded;
                Event.current.Use();
            }

            if (!expanded)
            {
                var sumStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
                sumStyle.normal.textColor = COL_MUTED;
                EditorGUILayout.LabelField(summaryLine ?? "", sumStyle);
            }

            return expanded;
        }

        /// <summary>서비스 카드 끝. expanded이면 [접기] 표시.</summary>
        public static void EndServiceCard(ref bool expanded)
        {
            if (expanded)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var s = new GUIStyle(EditorStyles.miniLabel);
                s.normal.textColor = COL_MUTED;
                if (GUILayout.Button("\u25B2 접기", s, GUILayout.Width(50)))
                    expanded = false;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
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
    }
}
#endif
