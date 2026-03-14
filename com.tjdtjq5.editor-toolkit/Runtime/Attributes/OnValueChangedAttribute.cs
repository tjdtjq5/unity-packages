using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 값 변경 시 지정 메서드를 호출.
    /// [OnValueChanged("OnDamageChanged")] public float damage;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class OnValueChangedAttribute : PropertyAttribute
    {
        public string MethodName { get; }
        public OnValueChangedAttribute(string methodName) { MethodName = methodName; }
    }
}
