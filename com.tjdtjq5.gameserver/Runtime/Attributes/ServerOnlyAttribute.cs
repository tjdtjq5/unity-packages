using System;

namespace Tjdtjq5.GameServer
{
    /// <summary>클라이언트 응답에서 이 필드를 제외한다.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ServerOnlyAttribute : Attribute { }
}
