using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>대시보드/Features 공용 IMGUI 헬퍼 (바닐라 IMGUI만 사용).</summary>
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

        /// <summary>설정하면?/안 하면? 정보 박스.</summary>
        public static void DrawInfoBox(string[] benefits, string[] drawbacks)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("설정하면?");
            foreach (var b in benefits)
                sb.AppendLine($"  ✓ {b}");
            sb.AppendLine();
            sb.AppendLine("안 하면?");
            foreach (var d in drawbacks)
                sb.AppendLine($"  · {d}");
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd('\n', '\r'), MessageType.Info);
        }
    }
}
