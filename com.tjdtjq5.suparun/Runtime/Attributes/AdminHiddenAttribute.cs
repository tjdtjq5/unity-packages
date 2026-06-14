using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 어드민 페이지의 컬럼 렌더링에서 이 필드를 숨긴다.
    /// 데이터 자체는 정상적으로 fetch/저장되며, 시각적으로만 숨겨진다.
    /// 클라이언트 응답 자체에서 필드를 제외하려면 [Hidden]을 사용한다.
    /// </summary>
    /// <example>
    /// // sort_order는 어드민 컬럼으로는 보이지 않지만 드래그 reorder API로 변경됨
    /// [AdminHidden]
    /// public int sort_order;
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class AdminHiddenAttribute : Attribute { }
}
