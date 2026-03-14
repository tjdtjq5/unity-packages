using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// [SerializeReference]와 함께 사용.
    /// 인터페이스/추상 클래스의 구현체를 드롭다운으로 선택.
    ///
    /// [SerializeReference, SerializeReferenceSelector]
    /// public IEffect effect;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SerializeReferenceSelectorAttribute : PropertyAttribute { }
}
