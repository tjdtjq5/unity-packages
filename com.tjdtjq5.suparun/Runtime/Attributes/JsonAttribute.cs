using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>이 string 필드가 JSON 배열/객체를 담고 있음을 표시. 어드민 페이지에서 JSON 에디터로 편집 가능.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class JsonAttribute : Attribute { }
}
