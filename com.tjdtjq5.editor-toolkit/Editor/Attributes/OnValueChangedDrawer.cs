#if UNITY_EDITOR
using System;
using System.Reflection;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(OnValueChangedAttribute))]
    public class OnValueChangedDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, property, label, true);

            if (EditorGUI.EndChangeCheck())
            {
                property.serializedObject.ApplyModifiedProperties();

                var attr = (OnValueChangedAttribute)attribute;
                var target = property.serializedObject.targetObject;
                var method = target.GetType().GetMethod(attr.MethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);

                if (method != null)
                    method.Invoke(target, null);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUI.GetPropertyHeight(property, label, true);
    }
}
#endif
