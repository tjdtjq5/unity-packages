#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(SerializeReferenceSelectorAttribute))]
    public class SerializeReferenceSelectorDrawer : PropertyDrawer
    {
        // 베이스 타입별 캐시
        static readonly Dictionary<string, TypeInfo> _typeCache = new();

        struct TypeInfo
        {
            public Type[] Types;
            public string[] Names;
        }

        TypeInfo GetTypes(SerializedProperty property)
        {
            // managedReferenceFieldTypename: "Assembly FullTypeName"
            string fieldTypeName = property.managedReferenceFieldTypename;
            if (string.IsNullOrEmpty(fieldTypeName))
                return default;

            if (_typeCache.TryGetValue(fieldTypeName, out var cached))
                return cached;

            // 베이스 타입 파싱
            var parts = fieldTypeName.Split(' ');
            if (parts.Length < 2) return default;

            string assemblyName = parts[0];
            string typeName = parts[1];

            Type baseType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == assemblyName)
                {
                    baseType = asm.GetType(typeName);
                    break;
                }
            }

            if (baseType == null) return default;

            // 구현체 수집
            var list = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (!type.IsAbstract && !type.IsInterface && !type.IsGenericType)
                    list.Add(type);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            var info = new TypeInfo
            {
                Types = list.ToArray(),
                Names = new string[list.Count + 1]
            };
            info.Names[0] = "(None)";
            for (int i = 0; i < list.Count; i++)
                info.Names[i + 1] = list[i].Name;

            _typeCache[fieldTypeName] = info;
            return info;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var typeInfo = GetTypes(property);
            if (typeInfo.Types == null || typeInfo.Types.Length == 0)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            // 현재 타입 인덱스
            int currentIndex = 0;
            var currentObj = property.managedReferenceValue;
            if (currentObj != null)
            {
                var currentType = currentObj.GetType();
                for (int i = 0; i < typeInfo.Types.Length; i++)
                {
                    if (typeInfo.Types[i] == currentType)
                    {
                        currentIndex = i + 1;
                        break;
                    }
                }
            }

            // 타입 선택 드롭다운 (1줄)
            var dropdownRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            int newIndex = EditorGUI.Popup(dropdownRect, label.text, currentIndex, typeInfo.Names);

            // 타입 변경
            if (newIndex != currentIndex)
            {
                if (newIndex == 0)
                {
                    property.managedReferenceValue = null;
                }
                else
                {
                    var newType = typeInfo.Types[newIndex - 1];
                    property.managedReferenceValue = Activator.CreateInstance(newType);
                }
                property.serializedObject.ApplyModifiedProperties();
            }

            // 선택된 타입의 필드 그리기
            if (property.managedReferenceValue != null)
            {
                float yOffset = EditorGUIUtility.singleLineHeight + 2;
                var childRect = new Rect(position.x, position.y + yOffset,
                    position.width, position.height - yOffset);

                // 자식 프로퍼티 순회
                var child = property.Copy();
                var end = property.GetEndProperty();
                bool enterChildren = true;
                float y = childRect.y;

                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    enterChildren = false;
                    float h = EditorGUI.GetPropertyHeight(child, true);
                    var r = new Rect(childRect.x + 16, y, childRect.width - 16, h);
                    EditorGUI.PropertyField(r, child, true);
                    y += h + 2;
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight; // 드롭다운

            if (property.managedReferenceValue != null)
            {
                var child = property.Copy();
                var end = property.GetEndProperty();
                bool enterChildren = true;

                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    enterChildren = false;
                    height += EditorGUI.GetPropertyHeight(child, true) + 2;
                }
            }

            return height;
        }
    }
}
#endif
