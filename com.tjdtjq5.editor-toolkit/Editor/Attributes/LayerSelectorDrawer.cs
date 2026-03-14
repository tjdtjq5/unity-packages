#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(LayerSelectorAttribute))]
    public class LayerSelectorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                EditorGUI.BeginProperty(position, label, property);
                property.intValue = EditorGUI.LayerField(position, label, property.intValue);
                EditorGUI.EndProperty();
            }
            else
                EditorGUI.PropertyField(position, property, label);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;
    }
}
#endif
