#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 알림/로딩/로그 헬퍼.</summary>
    public static partial class EditorUI
    {
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

        public static void DrawLoading(bool isLoading, string message = "로딩 중...")
        {
            if (!isLoading) return;
            GUILayout.Space(8);
            EditorGUILayout.LabelField($"... {message}", new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_INFO }
            });
            GUILayout.Space(8);
        }

        /// <summary>로그 영역 (스크롤 + 선택 가능 텍스트).</summary>
        public static Vector2 DrawLogArea(string text, Vector2 scrollPos,
            float maxHeight = 200, Color? textColor = null)
        {
            if (string.IsNullOrEmpty(text)) return scrollPos;

            var lines = text.Split('\n').Length;
            var height = Mathf.Min(maxHeight, 14 * lines + 20);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(height));
            var style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = false,
                fontSize = 11,
                normal = { textColor = textColor ?? Color.white }
            };
            EditorGUILayout.SelectableLabel(text, style, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            return scrollPos;
        }
    }
}
#endif
