using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// string 필드에 지정 베이스 타입의 구현체를 드롭다운으로 선택.
    /// 선택하면 AssemblyQualifiedName을 저장.
    ///
    /// [TypeSelector(typeof(ISkillEffect))]
    /// public string effectTypeName;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class TypeSelectorAttribute : PropertyAttribute
    {
        public Type BaseType { get; }
        public TypeSelectorAttribute(Type baseType) { BaseType = baseType; }
    }
}
