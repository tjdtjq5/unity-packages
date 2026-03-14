#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(TagSelectorAttribute))]
    public class TagSelectorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                EditorGUI.BeginProperty(position, label, property);
                property.stringValue = EditorGUI.TagField(position, label, property.stringValue);
                EditorGUI.EndProperty();
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
#endif
