#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(TypeSelectorAttribute))]
    public class TypeSelectorDrawer : PropertyDrawer
    {
        Type[] _types;
        string[] _typeNames;

        void EnsureTypes()
        {
            if (_types != null) return;

            var attr = (TypeSelectorAttribute)attribute;
            var list = new List<Type>();

            foreach (var type in TypeCache.GetTypesDerivedFrom(attr.BaseType))
            {
                if (!type.IsAbstract && !type.IsInterface)
                    list.Add(type);
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            _types = list.ToArray();
            _typeNames = new string[_types.Length + 1];
            _typeNames[0] = "(None)";
            for (int i = 0; i < _types.Length; i++)
                _typeNames[i + 1] = _types[i].Name;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EnsureTypes();

            // 현재 선택된 인덱스 찾기
            int currentIndex = 0;
            string currentValue = property.stringValue;
            if (!string.IsNullOrEmpty(currentValue))
            {
                for (int i = 0; i < _types.Length; i++)
                {
                    if (_types[i].AssemblyQualifiedName == currentValue || _types[i].Name == currentValue)
                    {
                        currentIndex = i + 1;
                        break;
                    }
                }
            }

            EditorGUI.BeginProperty(position, label, property);
            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, _typeNames);

            if (newIndex != currentIndex)
            {
                property.stringValue = newIndex == 0
                    ? string.Empty
                    : _types[newIndex - 1].AssemblyQualifiedName;
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;
    }
}
#endif
