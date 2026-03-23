using System;

namespace Tjdtjq5.GameServer
{
    /// <summary>인증 없이 호출 가능한 API. [API]와 함께 사용.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class PublicAttribute : Attribute { }
}
