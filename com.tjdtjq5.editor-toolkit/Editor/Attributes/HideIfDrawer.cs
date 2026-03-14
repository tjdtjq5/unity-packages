#if UNITY_EDITOR
using System;
using System.Reflection;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(HideIfAttribute))]
    public class HideIfDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!ShouldHide(property))
                EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!ShouldHide(property))
                return EditorGUI.GetPropertyHeight(property, label, true);
            return 0f;
        }

        bool ShouldHide(SerializedProperty property)
        {
            var attr = (HideIfAttribute)attribute;
            var target = GetTargetObject(property);
            if (target == null) return false;

            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var field = type.GetField(attr.ConditionField, flags);
            if (field != null) return EvaluateCondition(attr, field.GetValue(target));

            var prop = type.GetProperty(attr.ConditionField, flags);
            if (prop != null) return EvaluateCondition(attr, prop.GetValue(target));

            var method = type.GetMethod(attr.ConditionField, flags, null, Type.EmptyTypes, null);
            if (method != null && method.ReturnType == typeof(bool))
                return EvaluateCondition(attr, method.Invoke(target, null));

            return false;
        }

        static bool EvaluateCondition(HideIfAttribute attr, object value)
        {
            if (attr.HasCompareValue)
                return Equals(value, attr.CompareValue);
            if (value is bool b)
                return b;
            return value != null;
        }

        static object GetTargetObject(SerializedProperty property)
        {
            object obj = property.serializedObject.targetObject;
            var parts = property.propertyPath.Split('.');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "Array" && i + 1 < parts.Length - 1 && parts[i + 1].StartsWith("data["))
                {
                    var indexStr = parts[i + 1].Replace("data[", "").TrimEnd(']');
                    if (int.TryParse(indexStr, out int index) && obj is System.Collections.IList list && index < list.Count)
                        obj = list[index];
                    else return null;
                    i++;
                    continue;
                }
                var f = obj.GetType().GetField(parts[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) obj = f.GetValue(obj);
                else return null;
            }
            return obj;
        }
    }
}
#endif
