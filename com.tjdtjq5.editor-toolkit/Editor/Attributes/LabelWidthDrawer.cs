#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(LabelWidthAttribute))]
    public class LabelWidthDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (LabelWidthAttribute)attribute;
            var prev = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = attr.Width;
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUIUtility.labelWidth = prev;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}
#endif
