#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>알림 타입.</summary>
    internal enum NotificationType { Error, Success, Info }

    /// <summary>탭 기반 에디터 윈도우 공통 베이스.</summary>
    internal abstract class AddrXTabBase
    {
        protected string _notification;
        protected NotificationType _notificationType;

        public abstract string TabName { get; }
        public virtual Color TabColor => Color.gray;

        public abstract void OnDraw();
        public virtual void OnUpdate() { }
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
    }

    /// <summary>다회 사용되는 공용 IMGUI 헬퍼.</summary>
    internal static class AddrXGui
    {
        /// <summary>알림 바. Copy = 클립보드 복사, ✕ = 닫기.</summary>
        public static void DrawNotificationBar(ref string notification, NotificationType type)
        {
            if (string.IsNullOrEmpty(notification)) return;

            var msgType = type == NotificationType.Error ? MessageType.Error : MessageType.Info;
            var text = type == NotificationType.Success ? $"✓ {notification}" : notification;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(text, msgType);
            EditorGUILayout.BeginVertical(GUILayout.Width(42));
            if (GUILayout.Button("Copy", EditorStyles.miniButton))
                EditorGUIUtility.systemCopyBuffer = notification;
            if (GUILayout.Button("✕", EditorStyles.miniButton))
                notification = null;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>통계 카드 (제목 + 값).</summary>
        public static void DrawStatCard(string label, string value)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        /// <summary>SerializedProperty 필드. (기존 동작 보존을 위해 내부에서 Update/Apply 수행)</summary>
        public static void DrawProperty(SerializedObject so, string propertyName,
            string label = null, string tooltip = null)
        {
            so.Update();
            var prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                if (label != null)
                    EditorGUILayout.PropertyField(prop,
                        tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label));
                else
                    EditorGUILayout.PropertyField(prop);
            }
            so.ApplyModifiedProperties();
        }

        /// <summary>서비스 카드 시작. 헤더 Foldout 클릭으로 펼침/접기. 반환: expanded.</summary>
        public static bool BeginServiceCard(string name, string status, string summaryLine, ref bool expanded)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            expanded = EditorGUILayout.Foldout(expanded, $"{name} — {status} ({summaryLine})", true);
            return expanded;
        }

        /// <summary>서비스 카드 끝.</summary>
        public static void EndServiceCard()
        {
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
