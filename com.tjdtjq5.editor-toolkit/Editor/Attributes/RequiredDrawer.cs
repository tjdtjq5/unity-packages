#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(RequiredAttribute))]
    public class RequiredDrawer : PropertyDrawer
    {
        const float WARNING_HEIGHT = 24f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool isMissing = IsMissing(property);

            var propRect = new Rect(position.x, position.y,
                position.width, EditorGUI.GetPropertyHeight(property, label, true));

            // 누락 시 빨간 테두리 효과
            if (isMissing)
            {
                var borderRect = new Rect(propRect.x - 2, propRect.y - 1, propRect.width + 4, propRect.height + 2);
                EditorGUI.DrawRect(borderRect, new Color(0.85f, 0.20f, 0.20f, 0.15f));
            }

            EditorGUI.PropertyField(propRect, property, label, true);

            if (isMissing)
            {
                var attr = (RequiredAttribute)attribute;
                var warnRect = new Rect(position.x, propRect.yMax + 2, position.width, WARNING_HEIGHT);
                EditorGUI.HelpBox(warnRect, attr.Message, MessageType.Error);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUI.GetPropertyHeight(property, label, true);
            if (IsMissing(property))
                height += WARNING_HEIGHT + 4f;
            return height;
        }

        static bool IsMissing(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null;
                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(property.stringValue);
                case SerializedPropertyType.ExposedReference:
                    return property.exposedReferenceValue == null;
                default:
                    return false;
            }
        }
    }
}
#endif
