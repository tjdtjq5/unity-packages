#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 입력 필드 헬퍼.</summary>
    public static partial class EditorUI
    {
        /// <summary>텍스트 입력 필드.</summary>
        public static string DrawTextField(string label, string value, string tooltip = null)
        {
            return EditorGUILayout.TextField(
                tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label),
                value);
        }

        /// <summary>비밀번호 입력 필드.</summary>
        public static string DrawPasswordField(string label, string value, string tooltip = null)
        {
            return EditorGUILayout.PasswordField(
                tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label),
                value);
        }

        /// <summary>드롭다운 선택.</summary>
        public static int DrawPopup(string label, int selectedIndex, string[] options, string tooltip = null)
        {
            return EditorGUILayout.Popup(
                tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label),
                selectedIndex, options);
        }

        /// <summary>SerializedProperty 필드.</summary>
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
    }
}
#endif
