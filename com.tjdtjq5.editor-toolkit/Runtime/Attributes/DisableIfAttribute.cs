using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 조건이 true이면 필드를 비활성화 (보이지만 편집 불가).
    /// ShowIf/HideIf와 달리 필드가 사라지지 않음.
    ///
    /// [DisableIf("isLocked")] public float lockedValue;
    /// [DisableIf("mode", MyEnum.Disabled)] public float value;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class DisableIfAttribute : PropertyAttribute
    {
        public string ConditionField { get; }
        public object CompareValue { get; }
        public bool HasCompareValue { get; }

        public DisableIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
            HasCompareValue = false;
        }

        public DisableIfAttribute(string conditionField, object compareValue)
        {
            ConditionField = conditionField;
            CompareValue = compareValue;
            HasCompareValue = true;
        }
    }
}
