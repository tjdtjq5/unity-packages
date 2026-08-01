using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// Features 창의 알림 바. 대시보드가 없어지면서 남은 유일한 IMGUI 화면이 이것뿐이라,
    /// 여기 있는 것도 알림 바 하나다(정보 박스는 대시보드와 함께 지웠다).
    /// </summary>
    public static class SupaRunUI
    {
        // ── 알림 타입 ──
        public enum NotificationType { Error, Success, Info }

        /// <summary>알림 바. Copy/닫기(✕) 버튼 포함 — 닫으면 notification이 null로 초기화된다.</summary>
        public static void DrawNotificationBar(ref string notification, NotificationType type)
        {
            if (string.IsNullOrEmpty(notification)) return;

            string label;
            switch (type)
            {
                case NotificationType.Error:   label = "✗ Error";   break;
                case NotificationType.Success: label = "✓ Success"; break;
                default:                       label = "ℹ Info";    break;
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(16)))
                EditorGUIUtility.systemCopyBuffer = notification;
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(16)))
                notification = null;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(notification))
                EditorGUILayout.LabelField(notification, EditorStyles.wordWrappedLabel);

            EditorGUILayout.EndVertical();
        }
    }
}
