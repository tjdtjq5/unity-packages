using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>[Service] 클래스 내에서 API로 노출할 메서드를 지정한다.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class APIAttribute : Attribute { }
}
