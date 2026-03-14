#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(IndentAttribute))]
    public class IndentDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var prev = EditorGUI.indentLevel;
            EditorGUI.indentLevel += ((IndentAttribute)attribute).Level;
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.indentLevel = prev;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUI.GetPropertyHeight(property, label, true);
    }
}
#endif
