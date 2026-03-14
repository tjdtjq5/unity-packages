using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 조건이 true이면 Inspector에서 숨김. ShowIf의 반전.
    ///
    /// 사용법:
    ///   [HideIf("isSimple")]           — bool 필드
    ///   [HideIf("mode", MyEnum.Off)]   — enum 비교
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class HideIfAttribute : PropertyAttribute
    {
        public string ConditionField { get; }
        public object CompareValue { get; }
        public bool HasCompareValue { get; }

        public HideIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
            CompareValue = null;
            HasCompareValue = false;
        }

        public HideIfAttribute(string conditionField, object compareValue)
        {
            ConditionField = conditionField;
            CompareValue = compareValue;
            HasCompareValue = true;
        }
    }
}
