using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 어드민 페이지에서 조건 필드의 값이 일치할 때만 이 필드를 활성화한다.
    /// 조건 불일치 시 회색 배경 + "—" 표시 + 편집 불가.
    /// </summary>
    /// <example>
    /// // enum 단일 값: motion_type이 Homing일 때만 표시
    /// [VisibleIf("motion_type", "Homing")]
    /// public float turn_speed;
    ///
    /// // enum 복수 값: motion_type이 Orbit 또는 Spiral일 때 표시
    /// [VisibleIf("motion_type", "Orbit", "Spiral")]
    /// public float orbit_speed;
    ///
    /// // bool 필드: is_magnet_target이 true일 때만 표시
    /// [VisibleIf("is_magnet_target")]
    /// public float magnet_range;
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class VisibleIfAttribute : Attribute
    {
        public string ConditionField { get; }
        public string[] CompareValues { get; }

        /// <summary>bool 조건: conditionField가 true일 때 표시.</summary>
        public VisibleIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
            CompareValues = Array.Empty<string>();
        }

        /// <summary>값 비교 조건: conditionField가 compareValues 중 하나와 일치할 때 표시.</summary>
        public VisibleIfAttribute(string conditionField, params string[] compareValues)
        {
            ConditionField = conditionField;
            CompareValues = compareValues;
        }
    }
}
