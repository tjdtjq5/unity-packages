using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 조건부 필드 표시. 조건이 false이면 Inspector에서 숨김.
    ///
    /// 사용법:
    ///   [ShowIf("isAdvanced")]                    — bool 필드
    ///   [ShowIf("mode", MyEnum.Option)]           — enum 비교
    ///   [ShowIf("IsReady")]                       — bool 반환 메서드
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string ConditionField { get; }
        public object CompareValue { get; }
        public bool HasCompareValue { get; }

        /// <summary>bool 필드/프로퍼티/메서드 기반 조건</summary>
        public ShowIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
            CompareValue = null;
            HasCompareValue = false;
        }

        /// <summary>필드 값과 비교값 일치 여부 조건 (enum 등)</summary>
        public ShowIfAttribute(string conditionField, object compareValue)
        {
            ConditionField = conditionField;
            CompareValue = compareValue;
            HasCompareValue = true;
        }
    }
}
