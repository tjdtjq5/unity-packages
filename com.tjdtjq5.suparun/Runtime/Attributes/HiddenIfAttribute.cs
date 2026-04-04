using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 어드민 페이지에서 조건 필드의 값이 일치하면 이 필드를 비활성화한다.
    /// VisibleIf의 역조건 — 조건 일치 시 회색 배경 + "—" 표시 + 편집 불가.
    /// </summary>
    /// <example>
    /// // enum 값이 None일 때 숨김 (나머지는 표시)
    /// [HiddenIf("motion_type", "None")]
    /// public float damage_multiplier;
    ///
    /// // bool 필드: is_disabled가 true이면 숨김
    /// [HiddenIf("is_disabled")]
    /// public float some_value;
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class HiddenIfAttribute : Attribute
    {
        public string ConditionField { get; }
        public string[] CompareValues { get; }

        /// <summary>bool 조건: conditionField가 true이면 비활성화.</summary>
        public HiddenIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
            CompareValues = Array.Empty<string>();
        }

        /// <summary>값 비교 조건: conditionField가 compareValues 중 하나와 일치하면 비활성화.</summary>
        public HiddenIfAttribute(string conditionField, params string[] compareValues)
        {
            ConditionField = conditionField;
            CompareValues = compareValues;
        }
    }
}
