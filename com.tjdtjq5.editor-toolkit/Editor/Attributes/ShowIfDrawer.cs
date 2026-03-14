#if UNITY_EDITOR
using System;
using System.Reflection;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public class ShowIfDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (ShouldShow(property))
                EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (ShouldShow(property))
                return EditorGUI.GetPropertyHeight(property, label, true);
            return 0f;
        }

        bool ShouldShow(SerializedProperty property)
        {
            var attr = (ShowIfAttribute)attribute;
            var target = GetTargetObject(property);
            if (target == null) return true;

            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 필드 확인
            var field = type.GetField(attr.ConditionField, flags);
            if (field != null)
            {
                var value = field.GetValue(target);
                return EvaluateCondition(attr, value);
            }

            // 프로퍼티 확인
            var prop = type.GetProperty(attr.ConditionField, flags);
            if (prop != null)
            {
                var value = prop.GetValue(target);
                return EvaluateCondition(attr, value);
            }

            // 메서드 확인 (bool 반환)
            var method = type.GetMethod(attr.ConditionField, flags, null, Type.EmptyTypes, null);
            if (method != null && method.ReturnType == typeof(bool))
            {
                var value = method.Invoke(target, null);
                return EvaluateCondition(attr, value);
            }

            return true;
        }

        static bool EvaluateCondition(ShowIfAttribute attr, object value)
        {
            if (attr.HasCompareValue)
                return Equals(value, attr.CompareValue);

            if (value is bool b)
                return b;

            return value != null;
        }

        static object GetTargetObject(SerializedProperty property)
        {
            var path = property.propertyPath;
            object obj = property.serializedObject.targetObject;

            // 중첩 프로퍼티 경로 처리 (parent.child.field → parent.child)
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "Array" && i + 1 < parts.Length - 1 && parts[i + 1].StartsWith("data["))
                {
                    // 배열 원소 접근
                    var indexStr = parts[i + 1].Replace("data[", "").TrimEnd(']');
                    if (int.TryParse(indexStr, out int index))
                    {
                        if (obj is System.Collections.IList list && index < list.Count)
                            obj = list[index];
                        else
                            return null;
                    }
                    i++; // "data[x]" 파트 건너뛰기
                    continue;
                }

                var type = obj.GetType();
                var field = type.GetField(parts[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    obj = field.GetValue(obj);
                else
                    return null;
            }

            return obj;
        }
    }
}
#endif
