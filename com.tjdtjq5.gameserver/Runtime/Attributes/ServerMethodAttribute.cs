using System;

namespace Tjdtjq5.GameServer
{
    /// <summary>[ServerLogic] 클래스 내에서 API로 노출할 메서드를 지정한다. 생략 시 모든 public 메서드가 노출됨.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerMethodAttribute : Attribute { }
}
